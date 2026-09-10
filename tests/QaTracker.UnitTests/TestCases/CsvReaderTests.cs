using QaTracker.Web.TestCases;

namespace QaTracker.UnitTests.TestCases;

public sealed class CsvReaderTests
{
    [Fact]
    public void Parse_empty_input_returns_no_rows()
    {
        Assert.Empty(Csv.Parse(string.Empty));
    }

    [Fact]
    public void Parse_splits_simple_rows_and_fields()
    {
        var rows = Csv.Parse("a,b,c\n1,2,3");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void Parse_handles_quoted_commas_and_doubled_quotes()
    {
        var rows = Csv.Parse("\"a, b\",\"she said \"\"hi\"\"\"");

        Assert.Single(rows);
        Assert.Equal(["a, b", "she said \"hi\""], rows[0]);
    }

    [Fact]
    public void Parse_keeps_newlines_inside_quoted_fields()
    {
        var rows = Csv.Parse("\"line one\nline two\",next");

        Assert.Single(rows);
        Assert.Equal("line one\nline two", rows[0][0]);
        Assert.Equal("next", rows[0][1]);
    }

    [Fact]
    public void Parse_treats_crlf_as_a_row_break()
    {
        var rows = Csv.Parse("a,b\r\nc,d\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["c", "d"], rows[1]);
    }

    [Fact]
    public void Parse_ignores_a_trailing_newline()
    {
        Assert.Equal(2, Csv.Parse("a\nb\n").Count);
    }

    [Fact]
    public void Parse_strips_a_leading_bom()
    {
        var rows = Csv.Parse("\uFEFFa,b");

        Assert.Equal("a", rows[0][0]);
    }

    [Fact]
    public void Parse_preserves_empty_trailing_field()
    {
        var rows = Csv.Parse("a,b,");

        Assert.Equal(["a", "b", ""], rows[0]);
    }

    [Fact]
    public void Parse_allows_ragged_rows()
    {
        var rows = Csv.Parse("a,b,c\nx\ny,z");

        Assert.Single(rows[1]);
        Assert.Equal(2, rows[2].Length);
    }
}
