using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QaTracker.UnitTests.Attachments;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;

namespace QaTracker.UnitTests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-03T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly FakeFileStorage storage = new();
    private readonly AttachmentService attachments;

    public ProjectServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        db.Users.AddRange(
            new ApplicationUser { Id = "user-1", UserName = "qa", Email = "qa@test.local" },
            new ApplicationUser { Id = "user-2", UserName = "dev", Email = "dev@test.local" });
        db.SaveChanges();

        attachments = new AttachmentService(factory, storage, time, NullLogger<AttachmentService>.Instance);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private NotificationService Notifications() => new(factory, time);

    private ProjectService CreateSut() => new(factory, time, attachments, Notifications());

    [Fact]
    public async Task CreateAsync_sets_defaults()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync(
            "  Acme portal  ", "  Rapid portal rebuild  ", "  some notes  ", [], "user-1");

        Assert.Equal("Acme portal", project.Name);
        Assert.Equal("Rapid portal rebuild", project.Brief);
        Assert.Equal("some notes", project.Notes);
        Assert.Equal(ProjectStatus.NotStarted, project.Status);
        Assert.Equal("user-1", project.CreatedById);
        Assert.Equal(time.GetUtcNow(), project.CreatedUtc);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
    }

    [Fact]
    public async Task CreateAsync_blank_notes_stored_as_null()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync("P", "   ", "   ", [], "user-1");

        Assert.Null(project.Brief);
        Assert.Null(project.Notes);
    }

    [Fact]
    public async Task CreateAsync_collapses_brief_to_one_line()
    {
        var sut = CreateSut();

        var project = await sut.CreateAsync("P", "One line\r\n  with a pasted break", null, [], "user-1");

        Assert.Equal("One line with a pasted break", project.Brief);
    }

    [Fact]
    public async Task CreateAsync_keeps_links_in_order_and_drops_empty_rows()
    {
        var sut = CreateSut();

        var created = await sut.CreateAsync("P", null, null,
            [new("Repo", "https://example.test/repo"), new("", ""), new("Staging", "https://staging.test")],
            "user-1");

        var project = await sut.GetAsync(created.Id);

        Assert.NotNull(project);
        Assert.Collection(project!.Links,
            l => Assert.Equal(("Repo", 0), (l.Label, l.SortOrder)),
            l => Assert.Equal(("Staging", 1), (l.Label, l.SortOrder)));
    }

    [Fact]
    public async Task UpdateAsync_replaces_links_and_touches_timestamp()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [new("Old", "https://old.test")], "user-1");

        time.Advance(TimeSpan.FromHours(2));
        await sut.UpdateAsync(created.Id, "Renamed", null, "notes",
            [new("New", "https://new.test")]);

        var project = await sut.GetAsync(created.Id);

        Assert.NotNull(project);
        Assert.Equal("Renamed", project!.Name);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
        Assert.Equal("New", Assert.Single(project.Links).Label);
    }

    [Fact]
    public async Task UpdateDefaultDefectTemplateAsync_sets_and_clears_the_project_default()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("Templated", null, null, [], "user-1");
        Assert.Null((await sut.GetAsync(created.Id))!.DefaultDefectTemplate);

        await sut.UpdateDefaultDefectTemplateAsync(created.Id, QaTracker.Web.Defects.DefectTemplate.Simple);
        Assert.Equal(QaTracker.Web.Defects.DefectTemplate.Simple, (await sut.GetAsync(created.Id))!.DefaultDefectTemplate);

        await sut.UpdateDefaultDefectTemplateAsync(created.Id, null);
        Assert.Null((await sut.GetAsync(created.Id))!.DefaultDefectTemplate);
    }

    [Fact]
    public async Task UpdateScheduleAsync_stores_the_window_and_estimates()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        time.Advance(TimeSpan.FromHours(1));
        await sut.UpdateScheduleAsync(created.Id, new ProjectSchedule(
            new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 11), false, true, 10.5m, null, 30));

        var project = await sut.GetAsync(created.Id);
        Assert.Equal(new DateOnly(2026, 11, 9), project!.TestingStartDate);
        Assert.Equal(new DateOnly(2026, 11, 11), project.TestingEndDate);
        Assert.False(project.TestingStartsMidday);
        Assert.True(project.TestingEndsMidday);
        Assert.Equal(10.5m, project.EstimatedDevDays);
        Assert.Null(project.EstimatedQaDays);
        Assert.Equal(30, project.QaEffortPercent);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
        Assert.Equal("2.5 days", QaEffort.Label(project, 25)!.Text);
    }

    [Fact]
    public async Task UpdateScheduleAsync_drops_midday_flags_without_dates_and_can_clear_everything()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.UpdateScheduleAsync(created.Id, new ProjectSchedule(
            new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), true, false, 4m, 2m, null));

        await sut.UpdateScheduleAsync(created.Id, ProjectSchedule.Empty with { StartsMidday = true, EndsMidday = true });

        var project = await sut.GetAsync(created.Id);
        Assert.Null(project!.TestingStartDate);
        Assert.Null(project.TestingEndDate);
        Assert.False(project.TestingStartsMidday);
        Assert.False(project.TestingEndsMidday);
        Assert.Null(project.EstimatedDevDays);
        Assert.Null(project.EstimatedQaDays);
        Assert.Null(QaEffort.Label(project, 25));
    }

    // In ProjectOrdering order: Active, Upcoming ("Soon"), Not Started ("Fresh"), Completed, Cancelled.
    [Theory]
    [InlineData(ProjectListFilter.ActiveAndUpcoming, new[] { "Active", "Soon", "Fresh" })]
    [InlineData(ProjectListFilter.Active, new[] { "Active" })]
    [InlineData(ProjectListFilter.All, new[] { "Active", "Soon", "Fresh", "Done", "Dropped" })]
    public async Task ListForSwitcherAsync_follows_the_users_project_list_filter(ProjectListFilter filter, string[] expected)
    {
        var sut = CreateSut();
        var active = await sut.CreateAsync("Active", null, null, [], "user-1");
        await sut.CreateAsync("Fresh", null, null, [], "user-1");
        var soon = await sut.CreateAsync("Soon", null, null, [], "user-1");
        var done = await sut.CreateAsync("Done", null, null, [], "user-1");
        var dropped = await sut.CreateAsync("Dropped", null, null, [], "user-1");
        await sut.MarkInFlightAsync(active.Id);
        await sut.SetStatusAsync(soon.Id, ProjectStatus.Upcoming);
        await sut.SetStatusAsync(done.Id, ProjectStatus.Complete);
        await sut.SetStatusAsync(dropped.Id, ProjectStatus.Cancelled);
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(u => u.Id == "user-1")).ProjectListFilter = filter;
            await db.SaveChangesAsync();
        }

        var listed = await sut.ListForSwitcherAsync("user-1");

        Assert.Equal(expected, listed.Select(p => p.Name)); // ordered by name
    }

    [Fact]
    public async Task ListForSwitcherAsync_defaults_to_active_and_not_started_for_an_unknown_user()
    {
        var sut = CreateSut();
        await sut.CreateAsync("Fresh", null, null, [], "user-1");
        var done = await sut.CreateAsync("Done", null, null, [], "user-1");
        await sut.SetStatusAsync(done.Id, ProjectStatus.Complete);

        Assert.Equal(["Fresh"], (await sut.ListForSwitcherAsync(null)).Select(p => p.Name));
    }

    [Theory]
    [InlineData(ProjectStatus.NotStarted, ProjectStatus.InFlight)]
    [InlineData(ProjectStatus.Upcoming, ProjectStatus.InFlight)]
    [InlineData(ProjectStatus.InFlight, ProjectStatus.InFlight)]
    [InlineData(ProjectStatus.Complete, ProjectStatus.Complete)]
    [InlineData(ProjectStatus.Cancelled, ProjectStatus.Cancelled)]
    public async Task MarkInFlightAsync_only_promotes_projects_where_work_hasnt_started(ProjectStatus from, ProjectStatus expected)
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.SetStatusAsync(created.Id, from);

        await sut.MarkInFlightAsync(created.Id);

        Assert.Equal(expected, (await sut.GetAsync(created.Id))!.Status);
    }

    private static readonly ProjectSchedule Window = new(
        new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20), false, false, null, null, null);

    [Fact]
    public async Task Setting_testing_dates_makes_a_not_started_project_upcoming_and_clearing_them_undoes_it()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        // Estimates alone don't count — it's the dates that make it upcoming.
        await sut.UpdateScheduleAsync(created.Id, ProjectSchedule.Empty with { EstimatedDevDays = 5m }, "user-1");
        Assert.Equal(ProjectStatus.NotStarted, (await sut.GetAsync(created.Id))!.Status);

        await sut.UpdateScheduleAsync(created.Id, Window, "user-1");
        Assert.Equal(ProjectStatus.Upcoming, (await sut.GetAsync(created.Id))!.Status);

        await sut.UpdateScheduleAsync(created.Id, ProjectSchedule.Empty, "user-2");
        Assert.Equal(ProjectStatus.NotStarted, (await sut.GetAsync(created.Id))!.Status);

        await using var db = factory.CreateDbContext();
        var changes = (await db.ProjectEvents.Where(e => e.Kind == ProjectEventKind.StatusChanged).ToListAsync())
            .OrderBy(e => e.OccurredUtc).ThenBy(e => e.ToValue == "NotStarted")
            .Select(e => (e.FromValue, e.ToValue, e.ActorId))
            .ToList();
        Assert.Equal(
            [("NotStarted", "Upcoming", (string?)"user-1"), ("Upcoming", "NotStarted", "user-2")],
            changes);
    }

    [Theory]
    [InlineData(ProjectStatus.InFlight)]
    [InlineData(ProjectStatus.Complete)]
    [InlineData(ProjectStatus.Cancelled)]
    public async Task Testing_dates_leave_other_statuses_alone(ProjectStatus status)
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.SetStatusAsync(created.Id, status);

        await sut.UpdateScheduleAsync(created.Id, Window);
        Assert.Equal(status, (await sut.GetAsync(created.Id))!.Status);
        await sut.UpdateScheduleAsync(created.Id, ProjectSchedule.Empty);
        Assert.Equal(status, (await sut.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task SetStatusAsync_sets_any_status_and_touches_timestamp()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        time.Advance(TimeSpan.FromHours(1));
        await sut.SetStatusAsync(created.Id, ProjectStatus.Complete);
        var done = await sut.GetAsync(created.Id);
        Assert.Equal(ProjectStatus.Complete, done!.Status);
        Assert.Equal(time.GetUtcNow(), done.UpdatedUtc);

        // Unlike MarkInFlightAsync, this can move a project back out of a terminal state.
        await sut.SetStatusAsync(created.Id, ProjectStatus.InFlight);
        Assert.Equal(ProjectStatus.InFlight, (await sut.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task DeleteAsync_removes_project_and_links()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [new("Repo", "https://example.test")], "user-1");

        await sut.DeleteAsync(created.Id);

        Assert.Null(await sut.GetAsync(created.Id));
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.ProjectLinks);
    }

    [Fact]
    public async Task AddMembersAsync_adds_users_and_is_idempotent()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.AddMembersAsync(created.Id, ["user-1", "user-2"]);
        await sut.AddMembersAsync(created.Id, ["user-1"]); // already there — no-op

        var project = await sut.GetAsync(created.Id);
        Assert.Equal(
            new[] { "user-1", "user-2" }.ToHashSet(),
            project!.Members.Select(m => m.Id).ToHashSet());
    }

    [Fact]
    public async Task AddMembersAsync_skips_viewers()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new ApplicationUser { Id = "viewer", UserName = "viewer", Email = "viewer@test.local" });
            db.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole(Roles.Viewer) { Id = "role-viewer", NormalizedName = "VIEWER" });
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = "viewer", RoleId = "role-viewer" });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.AddMembersAsync(created.Id, ["user-2", "viewer"]);

        Assert.Equal(["user-2"], (await sut.GetAsync(created.Id))!.Members.Select(m => m.Id));
    }

    [Fact]
    public async Task RemoveMemberAsync_removes_one_user_and_leaves_the_rest()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.AddMembersAsync(created.Id, ["user-1", "user-2"]);

        await sut.RemoveMemberAsync(created.Id, "user-1");
        await sut.RemoveMemberAsync(created.Id, "user-1"); // gone already — no-op

        var project = await sut.GetAsync(created.Id);
        Assert.Equal("user-2", Assert.Single(project!.Members).Id);
    }

    [Fact]
    public async Task UpdateAsync_leaves_the_team_untouched()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.AddMembersAsync(created.Id, ["user-1"]);

        await sut.UpdateAsync(created.Id, "Renamed", null, null, []);

        var project = await sut.GetAsync(created.Id);
        Assert.Equal("user-1", Assert.Single(project!.Members).Id);
    }

    [Fact]
    public async Task ListForUserAsync_returns_only_projects_the_user_is_a_member_of()
    {
        var sut = CreateSut();
        var mine = await sut.CreateAsync("Mine", null, null, [], "user-1");
        await sut.AddMembersAsync(mine.Id, ["user-1"]);
        var notMine = await sut.CreateAsync("Not mine", null, null, [], "user-1");
        await sut.AddMembersAsync(notMine.Id, ["user-2"]);

        var result = await sut.ListForUserAsync("user-1");

        Assert.Equal(mine.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task AddLinkAsync_appends_after_the_existing_links()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null,
            [new ProjectLinkInput("Repo", "https://example.test/repo"), new ProjectLinkInput("Spec", "https://example.test/spec")],
            "user-1");
        time.Advance(TimeSpan.FromMinutes(5));

        await sut.AddLinkAsync(created.Id, "  Staging  ", "  https://staging.example.test  ");

        var project = await sut.GetAsync(created.Id);
        Assert.Equal(["Repo", "Spec", "Staging"], project!.Links.Select(l => l.Label));
        Assert.Equal("https://staging.example.test", project.Links[^1].Url);
        Assert.Equal(time.GetUtcNow(), project.UpdatedUtc);
    }

    [Fact]
    public async Task AddLinkAsync_on_a_project_with_no_links_starts_the_order_at_zero()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.AddLinkAsync(created.Id, "Repo", "https://example.test/repo");

        Assert.Equal(0, Assert.Single((await sut.GetAsync(created.Id))!.Links).SortOrder);
    }

    [Theory]
    [InlineData("Repo", "https://example.test", true)]
    [InlineData("Repo", "http://example.test", true)]
    [InlineData("Email", "mailto:qa@example.test", true)]
    [InlineData("", "https://example.test", false)]
    [InlineData("Repo", "", false)]
    [InlineData("Repo", "example.test", false)]
    [InlineData("Repo", "ftp://example.test", false)]
    [InlineData("Repo", "javascript:alert(1)", false)]
    public void ProjectLink_Validate_accepts_only_labelled_http_https_and_mailto_links(string label, string url, bool valid)
    {
        Assert.Equal(valid, ProjectLink.Validate(label, url) is null);
    }

    [Fact]
    public void ProjectLink_Validate_enforces_the_column_lengths()
    {
        Assert.NotNull(ProjectLink.Validate(new string('a', ProjectLink.MaxLabelLength + 1), "https://example.test"));
        Assert.NotNull(ProjectLink.Validate("Repo", "https://example.test/" + new string('a', ProjectLink.MaxUrlLength)));
        Assert.Null(ProjectLink.Validate(new string('a', ProjectLink.MaxLabelLength), "https://example.test"));
    }

    [Fact]
    public async Task Comments_add_list_in_order_and_delete()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("P", null, null, [], "user-1");

        await sut.AddCommentAsync(project.Id, "user-1", "  first  ");
        time.Advance(TimeSpan.FromMinutes(1));
        var dropId = await sut.AddCommentAsync(project.Id, "user-2", "second");

        var comments = await sut.ListCommentsAsync(project.Id);
        Assert.Equal(["first", "second"], comments.Select(c => c.Body));
        Assert.Equal(["qa", "dev"], comments.Select(c => c.AuthorName));

        await sut.DeleteCommentAsync(dropId);
        Assert.Equal("first", Assert.Single(await sut.ListCommentsAsync(project.Id)).Body);
    }

    [Fact]
    public async Task Comments_belong_to_their_own_project()
    {
        var sut = CreateSut();
        var a = await sut.CreateAsync("A", null, null, [], "user-1");
        var b = await sut.CreateAsync("B", null, null, [], "user-1");

        await sut.AddCommentAsync(a.Id, "user-1", "on A");

        Assert.Single(await sut.ListCommentsAsync(a.Id));
        Assert.Empty(await sut.ListCommentsAsync(b.Id));
    }

    [Fact]
    public async Task Mentioning_a_team_member_notifies_them_with_a_link_to_the_dashboard()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("Acme", null, null, [], "user-1");
        await sut.AddMembersAsync(project.Id, ["user-1", "user-2"], "user-1");

        await sut.AddCommentAsync(project.Id, "user-1", "Over to you @dev", ["user-2"]);

        // user-2 also heard they'd been added to the team; the mention is the one to check.
        var notification = Assert.Single(await Notifications().ListAsync("user-2"), n => n.Message.Contains("mentioned"));
        Assert.Equal("qa mentioned you in a comment on the project dashboard", notification.Message);
        Assert.Equal("Acme", notification.ProjectName);
        Assert.Equal($"projects/{project.Id}#comments", notification.Path);
        Assert.Empty(await Notifications().ListAsync("user-1"));
    }

    [Fact]
    public async Task Mentioning_someone_off_the_team_notifies_nobody()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("Acme", null, null, [], "user-1");
        await sut.AddMembersAsync(project.Id, ["user-1"]);

        await sut.AddCommentAsync(project.Id, "user-1", "Over to you @dev", ["user-2"]);

        Assert.Empty(await Notifications().ListAsync("user-2"));
    }

    [Fact]
    public async Task Deleting_a_comment_removes_its_files_from_storage()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("P", null, null, [], "user-1");
        var commentId = await sut.AddCommentAsync(project.Id, "user-1", "see attached");
        await attachments.UploadAsync(AttachmentOwner.ProjectComment, commentId, new MemoryStream([1, 2, 3]),
            "shot.png", "image/png", 3, null, "user-1");

        var comment = Assert.Single(await sut.ListCommentsAsync(project.Id));
        Assert.Equal("shot.png", Assert.Single(comment.Attachments).FileName);
        Assert.Empty(await attachments.ListForProjectAsync(project.Id));

        await sut.DeleteCommentAsync(commentId);

        Assert.Equal(0, storage.Count);
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task Deleting_the_project_removes_its_comments_their_files_and_mention_notifications()
    {
        var sut = CreateSut();
        var project = await sut.CreateAsync("P", null, null, [], "user-1");
        await sut.AddMembersAsync(project.Id, ["user-1", "user-2"]);
        var commentId = await sut.AddCommentAsync(project.Id, "user-1", "hi @dev", ["user-2"]);
        await attachments.UploadAsync(AttachmentOwner.ProjectComment, commentId, new MemoryStream([1]),
            "a.txt", "text/plain", 1, null, "user-1");

        await sut.DeleteAsync(project.Id);

        Assert.Equal(0, storage.Count);
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.ProjectComments);
        Assert.Empty(db.Attachments);
        Assert.Empty(db.Notifications);
    }

    public void Dispose() => connection.Dispose();
}
