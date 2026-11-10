using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class LineDiffTests
{
    private static string Render(IEnumerable<DiffLine> diff) =>
        string.Join("|", diff.Select(d => d.Change switch
        {
            LineChange.Removed => "-" + d.Text,
            LineChange.Added => "+" + d.Text,
            _ => " " + d.Text,
        }));

    [Fact]
    public void Identical_text_is_all_same() =>
        Assert.Equal(" a| b", Render(LineDiff.Compute(["a", "b"], ["a", "b"])));

    [Fact]
    public void Insertions_and_removals_keep_the_common_lines()
    {
        var diff = LineDiff.Compute(["open", "type password", "submit"], ["open", "type email", "type password", "click sign in"]);

        Assert.Equal(" open|+type email| type password|-submit|+click sign in", Render(diff));
    }

    [Fact]
    public void Empty_sides()
    {
        Assert.Equal("+a", Render(LineDiff.Compute([], ["a"])));
        Assert.Equal("-a", Render(LineDiff.Compute(["a"], [])));
        Assert.Empty(LineDiff.Compute([], []));
    }

    [Fact]
    public void Lines_unifies_endings_trims_and_drops_blanks() =>
        Assert.Equal(["open", "submit"], LineDiff.Lines("  open \r\n\r\n\tsubmit\r"));

    [Fact]
    public void Lines_of_nothing_is_empty() => Assert.Empty(LineDiff.Lines(null));
}
