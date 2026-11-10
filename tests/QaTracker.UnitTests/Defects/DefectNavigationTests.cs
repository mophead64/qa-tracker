using System.Globalization;
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

namespace QaTracker.UnitTests.Defects;

/// <summary>Previous / Next through the defects assigned to someone that still need work.</summary>
public sealed class DefectNavigationTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-11-10T10:00:00Z", CultureInfo.InvariantCulture));
    private readonly DefectService sut;
    private readonly Guid projectId;

    public DefectNavigationTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        factory = new TestDbContextFactory(options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "me", UserName = "me@test.local", FullName = "Me" });
            db.Users.Add(new ApplicationUser { Id = "other", UserName = "other@test.local", FullName = "Other" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        var notifications = new NotificationService(factory, time);
        var projects = new ProjectService(factory, time, attachments, notifications);
        projectId = projects.CreateAsync("Proj", null, null, [], "me").GetAwaiter().GetResult().Id;
        sut = new DefectService(factory, time, projects, attachments, notifications);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private async Task<Defect> RaiseAsync(string summary, DefectSeverity severity, string? assignee, DefectStatus status = DefectStatus.NotFixed)
    {
        var defect = await sut.CreateAsync(projectId, new DefectInput(summary, null, null, null, assignee), "me", severity);
        if (status != DefectStatus.NotFixed)
        {
            await sut.SetStatusAsync(defect.Id, status, "me");
        }
        return defect;
    }

    [Fact]
    public async Task Steps_through_my_open_defects_most_severe_first()
    {
        var low = await RaiseAsync("low", DefectSeverity.Low, "me");
        var critical = await RaiseAsync("critical", DefectSeverity.Critical, "me", DefectStatus.ToCheck);
        var high = await RaiseAsync("high", DefectSeverity.High, "me", DefectStatus.Fixing);

        var middle = await sut.GetAssignedNavigationAsync(projectId, high.Id, "me");

        Assert.Equal((critical.Id, low.Id), (middle.Previous?.Id, middle.Next?.Id));
        Assert.Equal((3, 2), (middle.Count, middle.Position!.Value));
        Assert.Equal((1, 1, 1), (middle.NotFixed, middle.Fixing, middle.ToCheck));

        var first = await sut.GetAssignedNavigationAsync(projectId, critical.Id, "me");
        Assert.Null(first.Previous);
        var last = await sut.GetAssignedNavigationAsync(projectId, low.Id, "me");
        Assert.Null(last.Next);
    }

    [Fact]
    public async Task Skips_closed_defects_and_other_peoples()
    {
        var mine = await RaiseAsync("mine", DefectSeverity.Critical, "me");
        await RaiseAsync("theirs", DefectSeverity.High, "other");
        await RaiseAsync("unassigned", DefectSeverity.High, null);
        await RaiseAsync("fixed", DefectSeverity.High, "me", DefectStatus.Fixed);
        await RaiseAsync("not a defect", DefectSeverity.High, "me", DefectStatus.NotADefect);
        var alsoMine = await RaiseAsync("also mine", DefectSeverity.Low, "me");

        var nav = await sut.GetAssignedNavigationAsync(projectId, mine.Id, "me");

        Assert.Equal(alsoMine.Id, nav.Next?.Id);
        Assert.Equal(2, nav.Count);
    }

    [Fact]
    public async Task From_a_defect_outside_the_queue_steps_to_the_nearest_either_side()
    {
        var critical = await RaiseAsync("critical", DefectSeverity.Critical, "me");
        var theirs = await RaiseAsync("theirs", DefectSeverity.Medium, "other");
        var low = await RaiseAsync("low", DefectSeverity.Low, "me");

        var nav = await sut.GetAssignedNavigationAsync(projectId, theirs.Id, "me");

        Assert.Equal((critical.Id, low.Id), (nav.Previous?.Id, nav.Next?.Id));
        Assert.Null(nav.Position);
        Assert.Equal(2, nav.Count);
    }

    [Fact]
    public async Task Nothing_assigned_means_no_steps()
    {
        var theirs = await RaiseAsync("theirs", DefectSeverity.Medium, "other");

        var nav = await sut.GetAssignedNavigationAsync(projectId, theirs.Id, "me");

        Assert.Equal((null, null, 0), (nav.Previous, nav.Next, nav.Count));
        Assert.Same(DefectNavigation.Empty, await sut.GetAssignedNavigationAsync(projectId, Guid.NewGuid(), "me"));
    }

    public void Dispose() => connection.Dispose();
}
