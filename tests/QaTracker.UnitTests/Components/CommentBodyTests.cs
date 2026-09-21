using QaTracker.Web.Components.Shared;

namespace QaTracker.UnitTests.Components;

public sealed class CommentBodyTests
{
    private static List<(string Text, bool IsTag)> Split(string body, params string[] names) =>
        CommentBody.Segments(body, names).ToList();

    private static string[] Tags(string body, params string[] names) =>
        Split(body, names).Where(s => s.IsTag).Select(s => s.Text).ToArray();

    [Fact]
    public void With_no_team_names_the_body_is_returned_untouched()
    {
        var segments = Split("hi @Ann");

        Assert.Equal([("hi @Ann", false)], segments);
    }

    [Fact]
    public void Blank_names_are_ignored()
    {
        Assert.Equal([("hi @Ann", false)], Split("hi @Ann", "", "  "));
    }

    [Fact]
    public void A_mention_is_split_out_as_a_tag_between_plain_runs()
    {
        var segments = Split("hey @Ann Lee, look", "Ann Lee");

        Assert.Equal([("hey ", false), ("@Ann Lee", true), (", look", false)], segments);
    }

    [Fact]
    public void Segments_always_concatenate_back_to_the_original_body()
    {
        const string body = "@Ann first, then @Bob and @Ann again";

        Assert.Equal(body, string.Concat(Split(body, "Ann", "Bob").Select(s => s.Text)));
    }

    [Fact]
    public void The_longest_name_wins_when_one_name_is_a_prefix_of_another()
    {
        Assert.Equal(["@Anna"], Tags("ping @Anna", "Ann", "Anna"));
        Assert.Equal(["@Ann"], Tags("ping @Ann", "Ann", "Anna"));
    }

    [Fact]
    public void A_name_followed_by_a_letter_or_digit_is_not_a_mention()
    {
        Assert.Empty(Tags("@Annabel and @Ann2", "Ann"));
    }

    [Fact]
    public void A_name_followed_by_punctuation_is_still_a_mention()
    {
        Assert.Equal(["@Ann", "@Ann"], Tags("@Ann, thanks @Ann.", "Ann"));
    }

    [Fact]
    public void An_at_sign_glued_to_the_previous_word_is_not_a_mention()
    {
        Assert.Empty(Tags("mail bob@Ann", "Ann"));
    }

    [Fact]
    public void A_mention_at_the_start_or_after_a_newline_is_recognised()
    {
        Assert.Equal(["@Ann", "@Ann"], Tags("@Ann\n@Ann", "Ann"));
    }

    [Fact]
    public void Matching_is_case_insensitive_and_keeps_the_text_as_typed()
    {
        Assert.Equal(["@ANN"], Tags("hi @ANN", "Ann"));
    }

    [Fact]
    public void Regex_metacharacters_in_names_are_matched_literally()
    {
        Assert.Equal(["@O'Brien (QA)"], Tags("cc @O'Brien (QA) please", "O'Brien (QA)"));
        Assert.Empty(Tags("cc @Xbrien", "X.brien"));
    }

    [Fact]
    public void A_body_that_is_only_a_mention_yields_one_tag()
    {
        Assert.Equal([("@Ann", true)], Split("@Ann", "Ann"));
    }
}
