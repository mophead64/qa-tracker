using System.Globalization;
using Microsoft.AspNetCore.Identity;
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

namespace QaTracker.UnitTests.Notifications;

/// <summary>
/// Exercises <see cref="NotificationService"/> through the real trigger points wired up in
/// <see cref="DefectService"/> — the most faithful way to verify the feature,
/// since the service never creates notifications on its own initiative.
/// </summary>
public sealed class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-09-05T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly DefectService defects;
    private readonly NotificationService sut;
    private readonly Guid projectId;

    public NotificationServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        factory = new TestDbContextFactory(options);

        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Roles.AddRange(
                new IdentityRole { Id = Roles.QA, Name = Roles.QA, NormalizedName = Roles.QA.ToUpperInvariant() },
                new IdentityRole { Id = Roles.Dev, Name = Roles.Dev, NormalizedName = Roles.Dev.ToUpperInvariant() });
            db.Users.AddRange(
                new ApplicationUser { Id = "qa-1", UserName = "qa1@test.local", Email = "qa1@test.local" },
                new ApplicationUser { Id = "qa-2", UserName = "qa2@test.local", Email = "qa2@test.local" },
                new ApplicationUser { Id = "dev-1", UserName = "dev1@test.local", Email = "dev1@test.local" },
                new ApplicationUser { Id = "dev-2", UserName = "dev2@test.local", Email = "dev2@test.local" });
            db.SaveChanges();
            db.UserRoles.AddRange(
                new IdentityUserRole<string> { UserId = "qa-1", RoleId = Roles.QA },
                new IdentityUserRole<string> { UserId = "qa-2", RoleId = Roles.QA },
                new IdentityUserRole<string> { UserId = "dev-1", RoleId = Roles.Dev },
                new IdentityUserRole<string> { UserId = "dev-2", RoleId = Roles.Dev });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        projects = new ProjectService(factory, time, attachments);
        // dev-2 is a Dev but NOT on the project team — only dev-1 should hear about new defects.
        projectId = projects.CreateAsync("Proj", null, null, [], "qa-1").GetAwaiter().GetResult().Id;
        projects.AddMembersAsync(projectId, ["qa-1", "qa-2", "dev-1"]).GetAwaiter().GetResult();
        sut = new NotificationService(factory, time);
        defects = new DefectService(factory, time, projects, attachments, sut);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private static DefectInput Input(string summary = "Modal never opens", string? assignee = null) =>
        new(summary, null, null, null, assignee);

    [Fact]
    public async Task Creating_a_defect_notifies_only_dev_team_members_of_the_project()
    {
        await defects.CreateAsync(projectId, Input("New bug"), "qa-1");

        var devNotifications = await sut.ListAsync("dev-1");
        Assert.Equal("New defect D-1: New bug", Assert.Single(devNotifications).Message);
        Assert.Empty(await sut.ListAsync("dev-2")); // not a team member
        Assert.Empty(await sut.ListAsync("qa-1")); // QA, not a dev recipient
    }

    [Fact]
    public async Task Assigning_a_defect_to_a_qa_notifies_them_but_not_a_dev_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.SetAssigneeAsync(defect.Id, "qa-1");
        Assert.Single(await sut.ListAsync("qa-1"), n => n.Message.Contains("assigned to you"));

        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        Assert.DoesNotContain(await sut.ListAsync("dev-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Creating_a_defect_already_assigned_to_a_qa_also_notifies_the_assignment()
    {
        await defects.CreateAsync(projectId, Input(assignee: "qa-1"), "dev-1");

        var notifications = await sut.ListAsync("qa-1");
        Assert.Contains(notifications, n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Reassigning_via_UpdateAsync_also_notifies_the_new_qa_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "dev-1");

        await defects.UpdateAsync(defect.Id, Input(assignee: "qa-1"));

        Assert.Contains(await sut.ListAsync("qa-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Moving_back_to_not_fixed_notifies_the_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);

        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed);

        var notification = Assert.Single(await sut.ListAsync("dev-1"), n => n.Message.Contains("Not fixed"));
        Assert.Equal(projectId, notification.ProjectId);
        Assert.Equal("Proj", notification.ProjectName);
        Assert.Equal($"projects/{projectId}/defects/{defect.Id}", notification.Path);
    }

    [Fact]
    public async Task Setting_status_to_the_same_value_does_not_notify_again()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);
        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed);
        var before = (await sut.ListAsync("dev-1")).Count;

        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed); // already Not fixed

        Assert.Equal(before, (await sut.ListAsync("dev-1")).Count);
    }

    [Fact]
    public async Task Moving_to_ready_to_check_notifies_a_qa_assignee_but_not_a_dev_assignee()
    {
        var toQa = await defects.CreateAsync(projectId, Input("For QA"), "qa-1");
        await defects.SetAssigneeAsync(toQa.Id, "qa-1");
        await defects.SetStatusAsync(toQa.Id, DefectStatus.ToCheck);
        Assert.Contains(await sut.ListAsync("qa-1"), n => n.Message.Contains("ready to check"));

        var toDev = await defects.CreateAsync(projectId, Input("For dev"), "qa-1");
        await defects.SetAssigneeAsync(toDev.Id, "dev-1");
        await defects.SetStatusAsync(toDev.Id, DefectStatus.ToCheck);
        Assert.DoesNotContain(await sut.ListAsync("dev-1"), n => n.Message.Contains("ready to check"));
    }

    [Fact]
    public async Task Assigning_a_defect_to_yourself_does_not_notify_you()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.SetAssigneeAsync(defect.Id, "qa-1", actingUserId: "qa-1");

        Assert.DoesNotContain(await sut.ListAsync("qa-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Assigning_a_defect_to_another_qa_still_notifies_them()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.SetAssigneeAsync(defect.Id, "qa-1", actingUserId: "qa-2");

        Assert.Contains(await sut.ListAsync("qa-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Moving_your_own_defect_back_to_not_fixed_does_not_notify_you()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);

        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed, actingUserId: "dev-1");

        Assert.DoesNotContain(await sut.ListAsync("dev-1"), n => n.Message.Contains("Not fixed"));
    }

    [Fact]
    public async Task Marking_your_own_defect_ready_to_check_does_not_notify_you()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "qa-1", actingUserId: "qa-2");

        await defects.SetStatusAsync(defect.Id, DefectStatus.ToCheck, actingUserId: "qa-1");

        Assert.DoesNotContain(await sut.ListAsync("qa-1"), n => n.Message.Contains("ready to check"));
    }

    [Fact]
    public async Task Marking_a_defect_fixed_hands_it_to_a_project_qa_who_is_notified()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.StartFixingAsync(defect.Id, "dev-1");

        var qaId = await defects.MarkFixedAsync(defect.Id, "dev-1");

        Assert.Contains(qaId, new[] { "qa-1", "qa-2" });
        var reloaded = await defects.GetAsync(defect.Id);
        Assert.Equal(DefectStatus.ToCheck, reloaded!.Status);
        Assert.Equal(qaId, reloaded.AssignedToId);
        Assert.Equal("dev-1", reloaded.FixedById);
        Assert.Contains(await sut.ListAsync(qaId!), n => n.Message.Contains("ready to check"));
    }

    [Fact]
    public async Task Rejecting_a_fix_sends_it_back_to_the_developer_who_is_alerted()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.StartFixingAsync(defect.Id, "dev-1");
        await defects.MarkFixedAsync(defect.Id, "dev-1"); // -> ToCheck, FixedById = dev-1

        await defects.RejectFixAsync(defect.Id, "qa-1");

        var reloaded = await defects.GetAsync(defect.Id);
        Assert.Equal(DefectStatus.NotFixed, reloaded!.Status);
        Assert.Equal("dev-1", reloaded.AssignedToId);
        Assert.Contains(await sut.ListAsync("dev-1"), n => n.Message.Contains("Not fixed"));
    }

    [Fact]
    public async Task ClearAllAsync_deletes_every_notification_and_is_idempotent()
    {
        await defects.CreateAsync(projectId, Input("one"), "qa-1"); // both notify dev-1
        await defects.CreateAsync(projectId, Input("two"), "qa-1");
        Assert.Equal(2, await sut.CountUnreadAsync("dev-1"));

        Assert.Equal(2, await sut.ClearAllAsync("dev-1"));
        Assert.Empty(await sut.ListAsync("dev-1"));
        Assert.Equal(0, await sut.CountUnreadAsync("dev-1"));
        Assert.Equal(0, await sut.ClearAllAsync("dev-1"));
    }

    [Fact]
    public async Task ClearAsync_deletes_one_notification_and_is_idempotent()
    {
        await defects.CreateAsync(projectId, Input("one"), "qa-1"); // both notify dev-1
        await defects.CreateAsync(projectId, Input("two"), "qa-1");
        var toClear = (await sut.ListAsync("dev-1")).Single(n => n.Message.Contains("one"));

        await sut.ClearAsync(toClear.Id, "dev-1");

        var remaining = await sut.ListAsync("dev-1");
        Assert.Single(remaining);
        Assert.Contains("two", remaining[0].Message);

        await sut.ClearAsync(toClear.Id, "dev-1"); // already gone — no-op, no throw
        Assert.Single(await sut.ListAsync("dev-1"));
    }

    [Fact]
    public async Task ClearAsync_ignores_a_notification_that_does_not_belong_to_the_caller()
    {
        await defects.CreateAsync(projectId, Input(), "qa-1");
        var notification = Assert.Single(await sut.ListAsync("dev-1"));

        await sut.ClearAsync(notification.Id, "dev-2"); // wrong user

        Assert.Single(await sut.ListAsync("dev-1"));
    }

    [Fact]
    public async Task MarkAllReadAsync_zeroes_the_unread_count_without_removing_notifications()
    {
        await defects.CreateAsync(projectId, Input("one"), "qa-1"); // both notify dev-1
        await defects.CreateAsync(projectId, Input("two"), "qa-1");
        Assert.Equal(2, await sut.CountUnreadAsync("dev-1"));

        Assert.Equal(2, await sut.MarkAllReadAsync("dev-1"));
        Assert.Equal(0, await sut.CountUnreadAsync("dev-1"));
        Assert.Equal(2, (await sut.ListAsync("dev-1")).Count); // still there, just read

        Assert.Equal(0, await sut.MarkAllReadAsync("dev-1")); // idempotent — nothing left unread
    }

    [Fact]
    public async Task Comments_notify_the_dev_assignee_but_not_the_comments_own_author()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");

        await defects.AddCommentAsync(defect.Id, "dev-1", "I'll take a look"); // self — no notification
        Assert.DoesNotContain(await sut.ListAsync("dev-1"), n => n.Message.Contains("New comment"));

        await defects.AddCommentAsync(defect.Id, "qa-1", "Any update?");
        Assert.Contains(await sut.ListAsync("dev-1"), n => n.Message.Contains("New comment"));
    }

    [Fact]
    public async Task Comments_do_not_notify_a_qa_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "qa-1");

        await defects.AddCommentAsync(defect.Id, "dev-1", "Fixed, please verify");

        Assert.DoesNotContain(await sut.ListAsync("qa-1"), n => n.Message.Contains("New comment"));
    }

    [Fact]
    public async Task Mentioning_several_team_members_notifies_each_but_not_the_author()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await sut.MarkAllReadAsync("dev-1");

        await defects.AddCommentAsync(
            defect.Id, "qa-1", "@dev1@test.local and @qa2@test.local and @qa1@test.local please look",
            ["dev-1", "qa-2", "qa-1"]);

        Assert.Contains(await sut.ListAsync("dev-1"), n => n.Message == "qa1@test.local mentioned you in a comment on D-1: Modal never opens");
        Assert.Contains(await sut.ListAsync("qa-2"), n => n.Message.Contains("mentioned you"));
        Assert.DoesNotContain(await sut.ListAsync("qa-1"), n => n.Message.Contains("mentioned you"));
    }

    [Fact]
    public async Task Mentions_of_non_team_members_or_names_no_longer_in_the_text_are_ignored()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.AddCommentAsync(defect.Id, "qa-1", "@dev2@test.local and @dev1 removed", ["dev-2", "dev-1"]);

        Assert.DoesNotContain(await sut.ListAsync("dev-2"), n => n.Message.Contains("mentioned you")); // not on the team
        Assert.DoesNotContain(await sut.ListAsync("dev-1"), n => n.Message.Contains("mentioned you")); // "@dev1@test.local" absent
    }

    [Fact]
    public async Task A_mentioned_assignee_gets_the_mention_instead_of_a_second_new_comment_notification()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");

        await defects.AddCommentAsync(defect.Id, "qa-1", "@dev1@test.local ping", ["dev-1"]);

        var all = await sut.ListAsync("dev-1");
        Assert.Single(all, n => n.Message.Contains("mentioned you"));
        Assert.DoesNotContain(all, n => n.Message.Contains("New comment"));
    }

    [Fact]
    public async Task Mentioning_someone_in_a_test_case_comment_links_the_notification_to_the_test_case()
    {
        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var scope = await new TestScopeService(factory, time, projects, attachments)
            .CreateAsync(projectId, TestCaseKind.Functional, "Auth", "qa-1");
        var cases = new TestCaseService(factory, time, attachments, sut);
        var tc = await cases.CreateAsync(scope.Id, new TestCaseInput("Login works", null), "qa-1");

        await cases.AddCommentAsync(tc.Id, "qa-1", "@dev1@test.local fails on Safari", ["dev-1"]);

        var notification = Assert.Single(await sut.ListAsync("dev-1"), n => n.Message.Contains("mentioned you"));
        Assert.Equal($"projects/{projectId}/test-cases/scopes/{scope.Id}/cases/{tc.Id}", notification.Path);
        Assert.Contains("Login works", notification.Message);
    }

    [Fact]
    public async Task ListAsync_orders_newest_first()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1"); // notifies dev-1
        time.Advance(TimeSpan.FromMinutes(5));
        await defects.AddCommentAsync(defect.Id, "qa-1", "ping"); // no assignee yet, no notification
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        time.Advance(TimeSpan.FromMinutes(5));
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);
        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed); // notifies dev-1 again, later

        var all = await sut.ListAsync("dev-1");
        Assert.Equal(2, all.Count);
        Assert.True(all[0].CreatedUtc > all[1].CreatedUtc);
    }

    [Fact]
    public async Task CountUnreadAsync_matches_the_list_size_before_anything_is_read()
    {
        await defects.CreateAsync(projectId, Input(), "qa-1");

        Assert.Equal(1, await sut.CountUnreadAsync("dev-1"));
        Assert.Equal(0, await sut.CountUnreadAsync("dev-2"));
    }

    public void Dispose() => connection.Dispose();
}
