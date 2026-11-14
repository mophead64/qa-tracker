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
using QaTracker.Web.Tags;

namespace QaTracker.UnitTests.Projects;

/// <summary>System-wide project tags: reuse by name, usage counts, and the history they leave.</summary>
public sealed class TagServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<ApplicationDbContext> factory;
    private readonly FakeTimeProvider time = new(
        DateTimeOffset.Parse("2026-11-14T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly ProjectService projects;
    private readonly TagService sut;
    private readonly Guid alpha;
    private readonly Guid beta;

    public TagServiceTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        factory = new TestDbContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = "qa", UserName = "qa@test.local", FullName = "QA One" });
            db.SaveChanges();
        }

        var attachments = new AttachmentService(factory, new FakeFileStorage(), time, NullLogger<AttachmentService>.Instance);
        projects = new ProjectService(factory, time, attachments, new NotificationService(factory, time));
        sut = new TagService(factory, time);
        alpha = projects.CreateAsync("Alpha", null, null, [], "qa").GetAwaiter().GetResult().Id;
        beta = projects.CreateAsync("Beta", null, null, [], "qa").GetAwaiter().GetResult().Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => connection.Dispose();

    private List<ProjectEvent> TagEvents(Guid projectId)
    {
        using var db = factory.CreateDbContext();
        return db.ProjectEvents
            .Where(e => e.ProjectId == projectId && e.Kind != ProjectEventKind.Created)
            .AsEnumerable()
            .OrderBy(e => e.OccurredUtc)
            .ToList();
    }

    [Fact]
    public async Task A_name_typed_on_one_project_reuses_the_existing_tag_ignoring_case_and_spacing()
    {
        var first = await sut.AddToProjectAsync(alpha, "  React  Native ", "qa");
        var second = await sut.AddToProjectAsync(beta, "react native", "qa");

        Assert.True(first.Succeeded);
        Assert.Equal(first.TagId, second.TagId);

        var tag = Assert.Single(await sut.ListAsync());
        Assert.Equal("React Native", tag.Name);
        Assert.Equal(TagColour.Gray, tag.Colour);
        Assert.Equal(["Alpha", "Beta"], tag.Projects.Select(p => p.Name));
        Assert.Equal(["React Native"], (await projects.GetAsync(alpha))!.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task A_new_tag_takes_the_picked_colour_but_an_existing_one_keeps_its_own()
    {
        await sut.AddToProjectAsync(alpha, "Mobile", "qa", TagColour.Purple);
        await sut.AddToProjectAsync(beta, "mobile", "qa", TagColour.Red);

        Assert.Equal(TagColour.Purple, Assert.Single(await sut.ListAsync()).Colour);
    }

    [Fact]
    public async Task Adding_a_tag_twice_is_a_no_op_and_bad_names_are_rejected()
    {
        await sut.AddToProjectAsync(alpha, "API", "qa");
        await sut.AddToProjectAsync(alpha, "api", "qa");

        Assert.Single(TagEvents(alpha));
        Assert.Equal("A tag needs a name.", (await sut.AddToProjectAsync(alpha, "   ", "qa")).Error);
        Assert.Contains("at most", (await sut.AddToProjectAsync(alpha, new string('x', Tag.MaxNameLength + 1), "qa")).Error);
    }

    [Fact]
    public async Task Usage_counts_say_how_many_projects_and_how_many_are_active()
    {
        await projects.SetStatusAsync(beta, ProjectStatus.InFlight, "qa");
        await sut.AddToProjectAsync(alpha, "Team A", "qa");
        await sut.AddToProjectAsync(beta, "Team A", "qa");
        await sut.CreateAsync("Unused", TagColour.Blue);

        var tags = await sut.ListAsync();
        Assert.Equal(["Team A", "Unused"], tags.Select(t => t.Name));
        Assert.Equal((2, 1), (tags[0].ProjectCount, tags[0].ActiveCount));
        Assert.Equal(0, tags[1].ProjectCount);
        Assert.Equal(TagColour.Blue, tags[1].Colour);
    }

    [Fact]
    public async Task Creating_or_renaming_onto_an_existing_name_is_refused()
    {
        await sut.CreateAsync("Mobile", TagColour.Gray);
        var web = await sut.CreateAsync("Web", TagColour.Gray);

        Assert.False((await sut.CreateAsync("MOBILE", TagColour.Red)).Succeeded);
        Assert.Contains("already a tag", (await sut.UpdateAsync(web.TagId!.Value, "mobile", TagColour.Gray, "qa")).Error);

        // Changing only the case of its own name is fine.
        Assert.True((await sut.UpdateAsync(web.TagId!.Value, "WEB", TagColour.Gray, "qa")).Succeeded);
    }

    [Fact]
    public async Task Every_tag_change_is_recorded_in_the_projects_history()
    {
        var tagId = (await sut.AddToProjectAsync(alpha, "Legacy", "qa")).TagId!.Value;
        await sut.AddToProjectAsync(beta, "Legacy", "qa");

        time.Advance(TimeSpan.FromMinutes(1));
        await sut.UpdateAsync(tagId, "Legacy", TagColour.Red, "qa");   // colour only: no entry
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.UpdateAsync(tagId, "Old stack", TagColour.Red, "qa");
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.RemoveFromProjectAsync(beta, tagId, "qa");
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.DeleteAsync(tagId, "qa");

        Assert.Equal(
            [
                (ProjectEventKind.TagAdded, null, "Legacy"),
                (ProjectEventKind.TagRenamed, "Legacy", "Old stack"),
                (ProjectEventKind.TagDeleted, "Old stack", null),
            ],
            TagEvents(alpha).Select(e => (e.Kind, e.FromValue, e.ToValue)));
        Assert.Equal(
            [
                (ProjectEventKind.TagAdded, null, "Legacy"),
                (ProjectEventKind.TagRenamed, "Legacy", "Old stack"),
                (ProjectEventKind.TagRemoved, "Old stack", null),
            ],
            TagEvents(beta).Select(e => (e.Kind, e.FromValue, e.ToValue)));

        Assert.Empty(await sut.ListAsync());
        Assert.Empty((await projects.GetAsync(alpha))!.Tags);
    }

    [Fact]
    public async Task The_activity_feed_shows_tags_in_their_current_colour_and_gray_once_deleted()
    {
        var activity = new ProjectActivityService(factory);
        var red = (await sut.CreateAsync("Hot", TagColour.Red)).TagId!.Value;
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.AddToProjectAsync(alpha, "Hot", "qa");
        time.Advance(TimeSpan.FromMinutes(1));
        await sut.AddToProjectAsync(alpha, "Plain", "qa");

        async Task<Dictionary<string, TagColour?>> Colours() =>
            (await activity.ListAsync(alpha, ActivityFilter.Project, null, 20)).Items
                .OfType<ProjectActivity>()
                .Where(p => p.Event.Kind == ProjectEventKind.TagAdded)
                .ToDictionary(p => p.Event.ToValue!, p => p.Event.TagColour);

        Assert.Equal(new Dictionary<string, TagColour?> { ["Hot"] = TagColour.Red, ["Plain"] = TagColour.Gray }, await Colours());

        // Recolouring shows straight away; a deleted tag has no colour left.
        await sut.UpdateAsync(red, "Hot", TagColour.Purple, "qa");
        Assert.Equal(TagColour.Purple, (await Colours())["Hot"]);
        await sut.DeleteAsync(red, "qa");
        Assert.Null((await Colours())["Hot"]);
    }

    [Fact]
    public async Task Deleting_a_project_leaves_its_tags_for_the_others()
    {
        await sut.AddToProjectAsync(alpha, "Shared", "qa");
        await sut.AddToProjectAsync(beta, "Shared", "qa");

        await projects.DeleteAsync(alpha);

        Assert.Equal(["Beta"], Assert.Single(await sut.ListAsync()).Projects.Select(p => p.Name));
    }
}
