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
        projectId = projects.CreateAsync("Proj", null, [], "qa-1").GetAwaiter().GetResult().Id;
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
        new(summary, null, null, null, DefectSeverity.Medium, assignee);

    [Fact]
    public async Task Creating_a_defect_notifies_only_dev_team_members_of_the_project()
    {
        await defects.CreateAsync(projectId, Input("New bug"), "qa-1");

        var devNotifications = await sut.ListActiveAsync("dev-1");
        Assert.Equal("New defect D-1: New bug", Assert.Single(devNotifications).Message);
        Assert.Empty(await sut.ListActiveAsync("dev-2")); // not a team member
        Assert.Empty(await sut.ListActiveAsync("qa-1")); // QA, not a dev recipient
    }

    [Fact]
    public async Task Assigning_a_defect_to_a_qa_notifies_them_but_not_a_dev_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.SetAssigneeAsync(defect.Id, "qa-1");
        Assert.Single(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("assigned to you"));

        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        Assert.DoesNotContain(await sut.ListActiveAsync("dev-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Creating_a_defect_already_assigned_to_a_qa_also_notifies_the_assignment()
    {
        await defects.CreateAsync(projectId, Input(assignee: "qa-1"), "dev-1");

        var notifications = await sut.ListActiveAsync("qa-1");
        Assert.Contains(notifications, n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Reassigning_via_UpdateAsync_also_notifies_the_new_qa_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "dev-1");

        await defects.UpdateAsync(defect.Id, Input(assignee: "qa-1"));

        Assert.Contains(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Moving_back_to_not_fixed_notifies_the_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);

        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed);

        var notification = Assert.Single(await sut.ListActiveAsync("dev-1"), n => n.Message.Contains("Not fixed"));
        Assert.Equal(projectId, notification.ProjectId);
        Assert.Equal("Proj", notification.ProjectName);
        Assert.Equal(defect.Id, notification.DefectId);
    }

    [Fact]
    public async Task Setting_status_to_the_same_value_does_not_notify_again()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);
        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed);
        var before = (await sut.ListActiveAsync("dev-1")).Count;

        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed); // already Not fixed

        Assert.Equal(before, (await sut.ListActiveAsync("dev-1")).Count);
    }

    [Fact]
    public async Task Moving_to_ready_to_check_notifies_a_qa_assignee_but_not_a_dev_assignee()
    {
        var toQa = await defects.CreateAsync(projectId, Input("For QA"), "qa-1");
        await defects.SetAssigneeAsync(toQa.Id, "qa-1");
        await defects.SetStatusAsync(toQa.Id, DefectStatus.ToCheck);
        Assert.Contains(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("ready to check"));

        var toDev = await defects.CreateAsync(projectId, Input("For dev"), "qa-1");
        await defects.SetAssigneeAsync(toDev.Id, "dev-1");
        await defects.SetStatusAsync(toDev.Id, DefectStatus.ToCheck);
        Assert.DoesNotContain(await sut.ListActiveAsync("dev-1"), n => n.Message.Contains("ready to check"));
    }

    [Fact]
    public async Task Assigning_a_defect_to_yourself_does_not_notify_you()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.SetAssigneeAsync(defect.Id, "qa-1", actingUserId: "qa-1");

        Assert.DoesNotContain(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Assigning_a_defect_to_another_qa_still_notifies_them()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");

        await defects.SetAssigneeAsync(defect.Id, "qa-1", actingUserId: "qa-2");

        Assert.Contains(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("assigned to you"));
    }

    [Fact]
    public async Task Moving_your_own_defect_back_to_not_fixed_does_not_notify_you()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);

        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed, actingUserId: "dev-1");

        Assert.DoesNotContain(await sut.ListActiveAsync("dev-1"), n => n.Message.Contains("Not fixed"));
    }

    [Fact]
    public async Task Marking_your_own_defect_ready_to_check_does_not_notify_you()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "qa-1", actingUserId: "qa-2");

        await defects.SetStatusAsync(defect.Id, DefectStatus.ToCheck, actingUserId: "qa-1");

        Assert.DoesNotContain(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("ready to check"));
    }

    [Fact]
    public async Task DismissAllAsync_clears_every_active_notification_and_is_idempotent()
    {
        await defects.CreateAsync(projectId, Input("one"), "qa-1"); // both notify dev-1
        await defects.CreateAsync(projectId, Input("two"), "qa-1");
        Assert.Equal(2, await sut.CountActiveAsync("dev-1"));

        Assert.Equal(2, await sut.DismissAllAsync("dev-1"));
        Assert.Empty(await sut.ListActiveAsync("dev-1"));
        Assert.Equal(0, await sut.DismissAllAsync("dev-1"));
    }

    [Fact]
    public async Task Comments_notify_the_dev_assignee_but_not_the_comments_own_author()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "dev-1");

        await defects.AddCommentAsync(defect.Id, "dev-1", "I'll take a look"); // self — no notification
        Assert.DoesNotContain(await sut.ListActiveAsync("dev-1"), n => n.Message.Contains("New comment"));

        await defects.AddCommentAsync(defect.Id, "qa-1", "Any update?");
        Assert.Contains(await sut.ListActiveAsync("dev-1"), n => n.Message.Contains("New comment"));
    }

    [Fact]
    public async Task Comments_do_not_notify_a_qa_assignee()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1");
        await defects.SetAssigneeAsync(defect.Id, "qa-1");

        await defects.AddCommentAsync(defect.Id, "dev-1", "Fixed, please verify");

        Assert.DoesNotContain(await sut.ListActiveAsync("qa-1"), n => n.Message.Contains("New comment"));
    }

    [Fact]
    public async Task ListActiveAsync_orders_newest_first_and_excludes_dismissed()
    {
        var defect = await defects.CreateAsync(projectId, Input(), "qa-1"); // notifies dev-1
        time.Advance(TimeSpan.FromMinutes(5));
        await defects.AddCommentAsync(defect.Id, "qa-1", "ping"); // no assignee yet, no notification
        await defects.SetAssigneeAsync(defect.Id, "dev-1");
        time.Advance(TimeSpan.FromMinutes(5));
        await defects.SetStatusAsync(defect.Id, DefectStatus.Fixing);
        await defects.SetStatusAsync(defect.Id, DefectStatus.NotFixed); // notifies dev-1 again, later

        var active = await sut.ListActiveAsync("dev-1");
        Assert.Equal(2, active.Count);
        Assert.True(active[0].CreatedUtc > active[1].CreatedUtc);
    }

    [Fact]
    public async Task DismissAsync_removes_it_from_the_active_list_and_is_idempotent()
    {
        await defects.CreateAsync(projectId, Input(), "qa-1");
        var notification = Assert.Single(await sut.ListActiveAsync("dev-1"));

        await sut.DismissAsync(notification.Id, "dev-1");
        Assert.Empty(await sut.ListActiveAsync("dev-1"));

        await sut.DismissAsync(notification.Id, "dev-1"); // already dismissed — no-op, no throw
        Assert.Empty(await sut.ListActiveAsync("dev-1"));
    }

    [Fact]
    public async Task DismissAsync_ignores_a_notification_that_does_not_belong_to_the_caller()
    {
        await defects.CreateAsync(projectId, Input(), "qa-1");
        var notification = Assert.Single(await sut.ListActiveAsync("dev-1"));

        await sut.DismissAsync(notification.Id, "dev-2"); // wrong user

        Assert.Single(await sut.ListActiveAsync("dev-1"));
    }

    [Fact]
    public async Task CountActiveAsync_matches_the_active_list_size()
    {
        await defects.CreateAsync(projectId, Input(), "qa-1");

        Assert.Equal(1, await sut.CountActiveAsync("dev-1"));
        Assert.Equal(0, await sut.CountActiveAsync("dev-2"));
    }

    public void Dispose() => connection.Dispose();
}
