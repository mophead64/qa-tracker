using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.Defects;

/// <summary>The transition history behind the defect timeline.</summary>
public sealed class DefectHistoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-11-10T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly AttachmentService attachments;
    private readonly DefectService sut;
    private readonly TestCaseService cases;
    private readonly Guid projectId;
    private readonly Guid testCaseId;

    public DefectHistoryTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", FullName = "QA One" });
            db.Users.Add(new ApplicationUser { Id = "dev", UserName = "dev@test.local", FullName = "Dev One" });
            db.Users.Add(new ApplicationUser { Id = "dev2", UserName = "dev2@test.local", FullName = "Dev Two" });
            db.SaveChanges();
        }

        attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        var projects = new ProjectService(factory, time, attachments, notifications);
        projectId = projects.CreateAsync("Proj", null, null, [], "qa").GetAwaiter().GetResult().Id;
        var scopes = new TestScopeService(factory, time, projects, attachments);
        var scopeId = scopes.CreateAsync(projectId, TestCaseKind.Functional, "Auth", "qa").GetAwaiter().GetResult().Id;
        cases = new TestCaseService(factory, time, attachments, notifications);
        testCaseId = cases.CreateAsync(scopeId, new TestCaseInput("Signs in", null), "qa").GetAwaiter().GetResult().Id;
        sut = new DefectService(factory, time, projects, attachments, notifications);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private Task<Defect> RaiseAsync(string? assignee = null) =>
        sut.CreateAsync(projectId, new DefectInput("Modal never opens", "Click", null, null, assignee), "qa");

    private async Task<DefectEventView> LatestAsync(Guid defectId) => (await sut.ListHistoryAsync(defectId))[0];

    [Fact]
    public async Task Raising_records_creation_and_any_initial_assignee()
    {
        var defect = await RaiseAsync(assignee: "dev");

        var history = await sut.ListHistoryAsync(defect.Id);

        Assert.Equal([DefectEventKind.AssigneeChanged, DefectEventKind.Created], history.Select(e => e.Kind));
        Assert.Equal((null, "Dev One", "QA One"), (history[0].FromValue, history[0].ToValue, history[0].ActorName));
        Assert.Equal("QA One", history[1].ActorName);
    }

    [Fact]
    public async Task Status_changes_record_from_and_to()
    {
        var defect = await RaiseAsync();
        time.Advance(TimeSpan.FromMinutes(1));

        await sut.SetStatusAsync(defect.Id, DefectStatus.NotADefect, "qa");

        var e = await LatestAsync(defect.Id);
        Assert.Equal(DefectEventKind.StatusChanged, e.Kind);
        Assert.Equal((DefectStatus.NotFixed, DefectStatus.NotADefect), (e.FromStatus!.Value, e.ToStatus!.Value));
    }

    [Fact]
    public async Task Unchanged_values_record_nothing()
    {
        var defect = await RaiseAsync(assignee: "dev");

        await sut.SetStatusAsync(defect.Id, DefectStatus.NotFixed, "qa");
        await sut.SetSeverityAsync(defect.Id, DefectSeverity.Medium, "qa");
        await sut.SetAssigneeAsync(defect.Id, "dev", "qa");
        await sut.UpdateAsync(defect.Id, new DefectInput("  Modal never opens ", "Click", null, null, "dev"), "qa");

        Assert.Equal(2, (await sut.ListHistoryAsync(defect.Id)).Count); // created + initial assignee
    }

    [Fact]
    public async Task Severity_change_is_recorded_with_its_actor()
    {
        var defect = await RaiseAsync();
        time.Advance(TimeSpan.FromMinutes(1));

        await sut.SetSeverityAsync(defect.Id, DefectSeverity.Critical, "qa");

        var e = await LatestAsync(defect.Id);
        Assert.Equal((DefectEventKind.SeverityChanged, "QA One"), (e.Kind, e.ActorName));
        Assert.Equal((DefectSeverity.Medium, DefectSeverity.Critical), (e.FromSeverity!.Value, e.ToSeverity!.Value));
    }

    [Fact]
    public async Task Reassigning_records_both_names()
    {
        var defect = await RaiseAsync(assignee: "dev");
        time.Advance(TimeSpan.FromMinutes(1));

        await sut.SetAssigneeAsync(defect.Id, "dev2", "qa");
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.SetAssigneeAsync(defect.Id, null, "qa");

        var history = await sut.ListHistoryAsync(defect.Id);
        Assert.Equal(("Dev Two", null), (history[0].FromValue, history[0].ToValue));
        Assert.Equal(("Dev One", "Dev Two"), (history[1].FromValue, history[1].ToValue));
    }

    [Fact]
    public async Task Editing_names_the_fields_that_changed()
    {
        var defect = await RaiseAsync();
        time.Advance(TimeSpan.FromMinutes(1));

        await sut.UpdateAsync(defect.Id, new DefectInput("Modal never opens on Safari", "Click", "Opens", "Nothing", null), "dev");

        var e = await LatestAsync(defect.Id);
        Assert.Equal(DefectEventKind.Edited, e.Kind);
        Assert.Equal("summary, expected results and actual results", e.ToValue);
        Assert.Equal("Dev One", e.ActorName);
    }

    [Fact]
    public async Task Fix_workflow_records_each_transition_and_handover()
    {
        var defect = await RaiseAsync();

        time.Advance(TimeSpan.FromMinutes(1));
        await sut.StartFixingAsync(defect.Id, "dev");
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.MarkFixedAsync(defect.Id, "dev");      // no project QA, so it goes unassigned
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.RejectFixAsync(defect.Id, "qa");       // back to the fixer
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.VerifyFixedAsync(defect.Id, "qa");

        var history = (await sut.ListHistoryAsync(defect.Id)).Reverse().ToList();
        string Line(DefectEventView e) => e.Kind == DefectEventKind.StatusChanged
            ? $"{e.ActorName}: {e.FromValue}->{e.ToValue}"
            : $"{e.ActorName}: assignee {e.FromValue ?? "-"}->{e.ToValue ?? "-"}";

        Assert.Equal(
        [
            "QA One: created",
            // Oldest first here; within one action the newest-first view puts the status change
            // above the assignment it caused, so reversed the assignment comes first.
            "Dev One: assignee -->Dev One", "Dev One: NotFixed->Fixing",
            "Dev One: assignee Dev One->-", "Dev One: Fixing->ToCheck",
            "QA One: assignee -->Dev One", "QA One: ToCheck->NotFixed",
            "QA One: assignee Dev One->-", "QA One: NotFixed->Fixed",
        ], history.Select(e => e.Kind == DefectEventKind.Created ? $"{e.ActorName}: created" : Line(e)));
    }

    [Fact]
    public async Task Linking_a_test_case_shows_in_both_histories()
    {
        var defect = await RaiseAsync();
        time.Advance(TimeSpan.FromMinutes(1));

        await sut.LinkTestCaseAsync(defect.Id, testCaseId, "qa");
        await sut.LinkTestCaseAsync(defect.Id, testCaseId, "qa"); // idempotent: no second entry
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.UnlinkTestCaseAsync(defect.Id, testCaseId, "dev");

        var defectHistory = await sut.ListHistoryAsync(defect.Id);
        Assert.Equal(
            [DefectEventKind.TestCaseUnlinked, DefectEventKind.TestCaseLinked, DefectEventKind.Created],
            defectHistory.Select(e => e.Kind));
        Assert.Equal("T-1 Signs in", defectHistory[1].ToValue);

        var caseHistory = await cases.ListHistoryAsync(testCaseId);
        Assert.Equal((TestCaseEventKind.DefectUnlinked, "D-1 Modal never opens", "Dev One"),
            (caseHistory[0].Kind, caseHistory[0].FromValue, caseHistory[0].ActorName));
        Assert.Equal((TestCaseEventKind.DefectLinked, "D-1 Modal never opens"), (caseHistory[1].Kind, caseHistory[1].ToValue));
    }

    [Fact]
    public async Task Defect_files_are_recorded()
    {
        var defect = await RaiseAsync();
        time.Advance(TimeSpan.FromMinutes(1));
        var file = await attachments.UploadAsync(AttachmentOwner.Defect, defect.Id,
            new MemoryStream(Encoding.UTF8.GetBytes("x")), "screenshot.png", "image/png", 1, null, "qa");
        time.Advance(TimeSpan.FromMinutes(1));
        await attachments.DeleteAsync(file.Id, "dev");

        var history = await sut.ListHistoryAsync(defect.Id);
        Assert.Equal((DefectEventKind.FileRemoved, "screenshot.png", "Dev One"), (history[0].Kind, history[0].FromValue, history[0].ActorName));
        Assert.Equal((DefectEventKind.FileAdded, "screenshot.png", "QA One"), (history[1].Kind, history[1].ToValue, history[1].ActorName));
    }

    [Fact]
    public async Task Deleting_the_defect_deletes_its_history()
    {
        var defect = await RaiseAsync();
        await sut.SetStatusAsync(defect.Id, DefectStatus.Fixing, "qa");

        await sut.DeleteAsync(defect.Id);

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.DefectEvents);
    }

    [Fact]
    public async Task Creating_sets_severity_and_template()
    {
        var defect = await sut.CreateAsync(projectId,
            new DefectInput("Typo", "On the home page", null, null, null, DefectTemplate.Simple), "qa", DefectSeverity.Low);

        // Round-trip through the database: Simple is the enum's CLR default, so this also guards
        // against a column default overriding it on insert.
        var reloaded = await sut.GetAsync(defect.Id);
        Assert.Equal((DefectSeverity.Low, DefectTemplate.Simple), (reloaded!.Severity, reloaded.Template));
    }

    [Fact]
    public async Task Defaults_are_medium_and_advanced()
    {
        var defect = await RaiseAsync();

        var reloaded = await sut.GetAsync(defect.Id);
        Assert.Equal((DefectSeverity.Medium, DefectTemplate.Advanced), (reloaded!.Severity, reloaded.Template));
    }

    [Fact]
    public async Task Switching_template_is_recorded_and_keeps_the_other_fields()
    {
        var defect = await sut.CreateAsync(projectId, new DefectInput("Bug", "Steps", "Works", "Breaks", null), "qa");
        time.Advance(TimeSpan.FromMinutes(1));

        await sut.UpdateAsync(defect.Id, new DefectInput("Bug", "Steps", "Works", "Breaks", null, DefectTemplate.Simple), "dev");

        var e = await LatestAsync(defect.Id);
        Assert.Equal((DefectEventKind.TemplateChanged, "Advanced", "Simple", "Dev One"), (e.Kind, e.FromValue, e.ToValue, e.ActorName));
        var reloaded = await sut.GetAsync(defect.Id);
        Assert.Equal((DefectTemplate.Simple, "Works", "Breaks"), (reloaded!.Template, reloaded.ExpectedResults, reloaded.ActualResults));
    }

    [Theory]
    [InlineData(null, DefectTemplate.Advanced, DefectTemplate.Advanced)]
    [InlineData(null, DefectTemplate.Simple, DefectTemplate.Simple)]
    [InlineData(DefectTemplate.Simple, DefectTemplate.Advanced, DefectTemplate.Simple)]
    [InlineData(DefectTemplate.Advanced, DefectTemplate.Simple, DefectTemplate.Advanced)]
    public void A_project_default_wins_over_the_system_default(DefectTemplate? project, DefectTemplate system, DefectTemplate expected) =>
        Assert.Equal(expected, DefectTemplates.Resolve(project, system));

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "summary" }, "summary")]
    [InlineData(new[] { "summary", "repro steps" }, "summary and repro steps")]
    [InlineData(new[] { "summary", "repro steps", "actual results" }, "summary, repro steps and actual results")]
    public void Edited_fields_read_as_a_list(string[] fields, string? expected) =>
        Assert.Equal(expected, DefectHistory.DescribeEditedFields(fields));

    public void Dispose() => connection.Dispose();
}
