using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Board;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Board;

/// <summary>The board's CSV importer: create-only, tasks under the story or bug above them, checked before anything is added.</summary>
public sealed class BoardImportServiceTests : IDisposable
{
    private const string Header = "Type,Title,Description,Assigned to,Status,Template,Expected results,Actual results\n";

    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-19T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly BoardService board;
    private readonly BoardImportService import;
    private readonly Guid projectId;

    public BoardImportServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new Factory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", Email = "qa@test.local", FullName = "QA One" });
            db.Users.Add(new ApplicationUser { Id = "dev", UserName = "dev@test.local", Email = "dev@test.local", FullName = "Dev One" });
            db.Users.Add(new ApplicationUser { Id = "outsider", UserName = "out@test.local", Email = "out@test.local", FullName = "Not On Team" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        projects = new ProjectService(factory, time, attachments, notifications);
        board = new BoardService(factory, new BoardProviders(new LocalBoardProvider(factory, time)), attachments, notifications, time);
        var settings = new SystemSettingsService(factory, new MemoryCache(new MemoryCacheOptions()), new ConfigurationBuilder().Build());
        import = new BoardImportService(factory, board, settings);
        projectId = projects.CreateAsync("Proj", null, null, [], "qa").GetAwaiter().GetResult().Id;
        projects.AddMembersAsync(projectId, ["qa", "dev"]).GetAwaiter().GetResult();
        board.SetModeAsync(projectId, BoardMode.Local).GetAwaiter().GetResult();
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task The_downloadable_template_imports_cleanly()
    {
        var plan = await import.BuildPlanAsync(projectId, BoardImportService.Template());

        Assert.True(plan.CanImport, string.Join("; ", plan.Issues.Select(i => i.Message)));
        Assert.Empty(plan.Issues);
        Assert.Equal((1, 2, 3), (plan.Stories, plan.Bugs, plan.Tasks));
        var story = plan.Groups[0];
        Assert.Equal(BoardColumn.InProgress, story.Item.Column);
        Assert.Equal([true, false], story.Tasks.Select(t => t.Done));
        Assert.Contains("\n", story.Item.Body); // a multi-line description survives the quoting
        Assert.Equal(DefectTemplate.Advanced, plan.Groups[1].Item.Template);
        Assert.Equal("The total shows £60.00", plan.Groups[1].Item.ActualResults);
        Assert.Equal(DefectTemplate.Simple, plan.Groups[2].Item.Template);
        Assert.Empty(plan.Groups[2].Tasks);
    }

    [Fact]
    public async Task Importing_creates_every_item_where_the_file_puts_it_without_notifying_anyone()
    {
        var csv = Header +
            "Story,Wallets,Apple and Google Pay,dev@test.local,Testing,,,\n" +
            "task,Register the merchant ID,,Dev One,complete,,,\n" +
            "Task,Google Pay button,,,,,,\n" +
            "Bug,Total wrong,1. Apply a code,,In-Progress,,Discounted,Full price\n";

        var plan = await import.BuildPlanAsync(projectId, csv);
        Assert.True(plan.CanImport, string.Join("; ", plan.Issues.Select(i => i.Message)));
        Assert.Equal(4, await import.ApplyAsync(projectId, plan, "qa"));

        var items = await board.ListAsync(projectId);
        var story = Assert.Single(items, i => i.Type == BoardItemType.Story);
        Assert.Equal((BoardColumn.Testing, "dev"), (story.Column, story.AssignedToId));
        var tasks = (await board.GetAsync(projectId, story.Id))!.Tasks;
        Assert.Equal(["Register the merchant ID", "Google Pay button"], tasks.Select(t => t.Title));
        Assert.Equal([true, false], tasks.Select(t => t.Done));
        Assert.Equal("dev", tasks[0].AssignedToId);

        // The bug takes the project's defect template (the system default here), with its results.
        var bug = Assert.Single(items, i => i.Type == BoardItemType.Bug);
        Assert.Equal((BoardColumn.InProgress, DefectTemplates.SystemFallback, "Full price"), (bug.Column, bug.Template, bug.ActualResults));

        // History says it was imported (and never "moved" or "completed"); nobody is notified.
        var history = await board.ListHistoryAsync(story.Id);
        Assert.Contains(history, e => e.Kind == BoardEventKind.Created && e.ToValue == LocalBoardProvider.CreatedByImport);
        Assert.DoesNotContain(history, e => e.Kind == BoardEventKind.Moved);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.Notifications.AnyAsync(n => n.BoardItemId != null));
    }

    [Fact]
    public async Task Problems_are_reported_by_row_and_block_the_import()
    {
        var csv = Header +
            "Task,Orphan task,,,,,,\n" +           // row 2: no story above
            "Epic,Big thing,,,,,,\n" +              // row 3: unknown type
            "Story,,,,,,,\n" +                      // row 4: no title
            $"Story,{new string('x', 201)},,,,,,\n" + // row 5: too long
            "Story,Fine story,,out@test.local,,,,\n" + // row 6: not on the team
            "Bug,A bug,,,Blocked,Fancy,,\n" +       // row 7: unknown column and template
            "Task,A task,,,Halfway,,,\n" +          // row 8: unknown task state
            ",,,,,,,\n";                            // blank rows are skipped

        var plan = await import.BuildPlanAsync(projectId, csv);

        Assert.False(plan.CanImport);
        Assert.Equal([2, 3, 4, 5, 6, 7, 7, 8], plan.Errors.Select(e => e.Row!.Value).Order());
        Assert.Contains(plan.Errors, e => e.Row == 6 && e.Message.Contains("isn't on this project's team"));
        Assert.Equal(0, await import.ApplyAsync(projectId, plan, "qa"));
        Assert.Empty(await board.ListAsync(projectId));
    }

    [Fact]
    public async Task Bug_only_fields_on_other_rows_and_unknown_columns_are_warnings()
    {
        var csv = "Type,Title,Template,Expected results,Priority\n" +
                  "Story,Wallets,Advanced,,High\n";

        var plan = await import.BuildPlanAsync(projectId, csv);

        Assert.True(plan.CanImport);
        Assert.Contains(plan.Warnings, w => w.Row is null && w.Message.Contains("Priority"));
        Assert.Contains(plan.Warnings, w => w.Row == 2 && w.Message.Contains("only for bugs"));
        Assert.Null(plan.Groups[0].Item.Template);
    }

    [Fact]
    public async Task A_new_bug_without_a_template_uses_the_projects_defect_template()
    {
        await projects.UpdateDefaultDefectTemplateAsync(projectId, DefectTemplate.Simple);

        var plan = await import.BuildPlanAsync(projectId, Header + "Bug,Typo,,,,,,\n");

        Assert.Equal(DefectTemplate.Simple, plan.Groups[0].Item.Template);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Type,Description\nStory,x\n")]
    [InlineData("Type,Title\n")]
    public async Task Empty_files_or_missing_columns_explain_themselves(string csv)
    {
        var plan = await import.BuildPlanAsync(projectId, csv);

        Assert.False(plan.CanImport);
        Assert.Single(plan.Errors);
    }
}
