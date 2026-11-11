namespace QaTracker.Web.TestCases;

/// <summary>How a line differs between two versions of a block of text.</summary>
public enum LineChange
{
    Same,
    Removed,
    Added,
}

/// <summary>One line of a <see cref="LineDiff"/>.</summary>
public sealed record DiffLine(LineChange Change, string Text);

/// <summary>
/// Line-by-line diff (longest common subsequence) — used by the import preview to show what an
/// update will do to a test case's steps. Inputs are small (a case's steps), so the quadratic
/// table is fine.
/// </summary>
public static class LineDiff
{
    public static IReadOnlyList<DiffLine> Compute(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // lcs[i, j] = length of the LCS of before[i..] and after[j..].
        var lcs = new int[before.Count + 1, after.Count + 1];
        for (var i = before.Count - 1; i >= 0; i--)
        {
            for (var j = after.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = before[i] == after[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        int a = 0, b = 0;
        while (a < before.Count && b < after.Count)
        {
            if (before[a] == after[b])
            {
                result.Add(new(LineChange.Same, before[a]));
                a++;
                b++;
            }
            else if (lcs[a + 1, b] >= lcs[a, b + 1])
            {
                result.Add(new(LineChange.Removed, before[a++]));
            }
            else
            {
                result.Add(new(LineChange.Added, after[b++]));
            }
        }

        while (a < before.Count) result.Add(new(LineChange.Removed, before[a++]));
        while (b < after.Count) result.Add(new(LineChange.Added, after[b++]));
        return result;
    }

    /// <summary>A block of steps as comparable lines: line endings unified, each line trimmed,
    /// blank lines dropped — so re-importing an export doesn't flag whitespace as a change.</summary>
    public static IReadOnlyList<string> Lines(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
}
