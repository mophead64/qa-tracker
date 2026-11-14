using Microsoft.AspNetCore.Components;
using QaTracker.Web.Components.Shared;

namespace QaTracker.UnitTests.Components;

/// <summary>Comments shown in the History modals: excerpts, links to their page of the thread, and merge order.</summary>
public sealed class CommentHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private static HistoryEntry Event(string kind, int minutes) =>
        new(Guid.NewGuid(), kind, "QA", T0.AddMinutes(minutes), "", builder => { });

    [Fact]
    public void Excerpts_are_one_line_and_cut_at_a_word()
    {
        Assert.Equal("Broken on Safari and Firefox", CommentHistory.Excerpt("  Broken on Safari\n\nand   Firefox "));

        var longBody = string.Join(" ", Enumerable.Repeat("word", 60));
        var excerpt = CommentHistory.Excerpt(longBody);
        Assert.EndsWith("word…", excerpt);
        Assert.True(excerpt.Length <= 141);
    }

    [Fact]
    public void Each_comment_links_to_its_page_of_the_thread()
    {
        var comments = Enumerable.Range(0, 7)
            .Select(i => new CommentHistory.Item(Guid.NewGuid(), "Ann", T0.AddMinutes(i), $"c{i}", 0))
            .ToList();

        var entries = CommentHistory.Entries(comments, "projects/p/defects/d").ToList();

        Assert.All(entries, e => Assert.Equal("Commented", e.Kind));
        Assert.Equal(comments.Select(c => c.Id), entries.Select(e => e.Id));
        Assert.Equal(T0.AddMinutes(6), entries[6].OccurredUtc);
    }

    [Fact]
    public void Merged_entries_are_newest_first_with_events_ahead_of_comments_at_the_same_instant()
    {
        var status = Event("StatusChanged", 5);
        var assignee = Event("AssigneeChanged", 5);
        var created = Event("Created", 0);
        var comment = new HistoryEntry(Guid.NewGuid(), "Commented", "Ann", T0.AddMinutes(5), "", builder => { });
        var later = new HistoryEntry(Guid.NewGuid(), "Commented", "Ann", T0.AddMinutes(9), "", builder => { });

        var merged = CommentHistory.Merge([status, assignee, created], [comment, later]);

        Assert.Equal([later, status, assignee, comment, created], merged);
    }
}
