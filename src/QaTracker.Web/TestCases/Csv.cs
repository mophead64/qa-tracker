using System.Text;

namespace QaTracker.Web.TestCases;

/// <summary>
/// Minimal RFC 4180 CSV helpers, shared by the test-case export
/// (<see cref="TestCaseEndpoints"/>) and the importer. <see cref="Parse"/> reads;
/// <see cref="Field"/> escapes a single value for writing.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Splits CSV text into records of fields. Handles quoted fields, <c>""</c> escapes,
    /// embedded commas / newlines, CRLF or LF line endings, a trailing newline, and a
    /// leading UTF-8 BOM. Records may have differing field counts (the caller decides what
    /// that means).
    /// </summary>
    public static IReadOnlyList<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text))
        {
            return rows;
        }

        if (text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;
        var pending = false; // some content (a field separator, a char, or an open quote) seen on this record

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    pending = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    pending = true;
                    break;
                case '\r':
                    break; // swallow; the following \n (or end of text) closes the record
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. record]);
                    record.Clear();
                    pending = false;
                    break;
                default:
                    field.Append(c);
                    pending = true;
                    break;
            }
        }

        if (pending || field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            rows.Add([.. record]);
        }

        return rows;
    }

    /// <summary>Escapes one value for a CSV cell, quoting only when it has to.</summary>
    public static string Field(string value)
    {
        var needsQuoting = value.AsSpan().IndexOfAny("\",\r\n") >= 0;
        var escaped = value.Replace("\"", "\"\"");
        return needsQuoting ? $"\"{escaped}\"" : escaped;
    }
}
