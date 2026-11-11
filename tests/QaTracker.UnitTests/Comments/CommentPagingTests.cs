using QaTracker.Web.Comments;

namespace QaTracker.UnitTests.Comments;

public sealed class CommentPagingTests
{
    private static IReadOnlyList<int> Comments(int count) => Enumerable.Range(1, count).ToList();

    [Fact]
    public void A_page_holds_at_most_five_comments()
    {
        Assert.Equal(5, CommentPaging.PageSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void Without_a_valid_page_the_newest_page_is_shown(string? requested)
    {
        var page = CommentPaging.Slice(Comments(12), requested);

        Assert.Equal(3, page.Current);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal([11, 12], page.Items);
        Assert.False(page.IsExplicit);
        Assert.Null(page.ReturnPage);
        Assert.Equal("", page.ReturnQuery);
    }

    [Fact]
    public void A_requested_page_shows_that_slice_in_posting_order()
    {
        var page = CommentPaging.Slice(Comments(12), "2");

        Assert.Equal(2, page.Current);
        Assert.Equal([6, 7, 8, 9, 10], page.Items);
        Assert.True(page.IsExplicit);
        Assert.Equal(2, page.ReturnPage);
        Assert.Equal("?commentsPage=2", page.ReturnQuery);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-4", 1)]
    [InlineData("9", 3)] // e.g. the last page emptied by a delete
    public void Out_of_range_pages_are_clamped(string requested, int expected)
    {
        Assert.Equal(expected, CommentPaging.Slice(Comments(12), requested).Current);
    }

    [Fact]
    public void An_exact_multiple_of_the_page_size_has_no_empty_trailing_page()
    {
        var page = CommentPaging.Slice(Comments(10), null);

        Assert.Equal(2, page.TotalPages);
        Assert.Equal([6, 7, 8, 9, 10], page.Items);
    }

    [Fact]
    public void No_comments_is_a_single_empty_page()
    {
        var page = CommentPaging.Slice(Comments(0), "3");

        Assert.Equal(1, page.Current);
        Assert.Equal(1, page.TotalPages);
        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData("4", "?commentsPage=4")]
    [InlineData("0", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("x", "")]
    public void QueryFrom_only_carries_a_positive_page_number(string? formValue, string expected)
    {
        Assert.Equal(expected, CommentPaging.QueryFrom(formValue));
    }
}
