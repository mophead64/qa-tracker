using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace QaTracker.Web.Components.Shared;

/// <summary>
/// A posted comment's text, with each "@Name" of a project team member shown as a green tag —
/// the same look as the tag in the comment box while typing (mention.js). Mentions aren't
/// stored on the comment, so this matches on the team's current names using the same rule as
/// the picker: whitespace or start before the "@", no letter/digit straight after the name.
/// A plain component (no markup) so no template whitespace leaks into the pre-wrap text.
/// </summary>
public sealed class CommentBody : ComponentBase
{
    // Tailwind scans this file for these class names (Components/Shared/*.cs in tailwind.config.js).
    private const string TagClass =
        "rounded bg-brand-100 font-medium text-brand-800 ring-1 ring-brand-300 dark:bg-brand-700/40 dark:text-brand-100 dark:ring-brand-600";

    [Parameter, EditorRequired]
    public string Body { get; set; } = "";

    /// <summary>Display names of everyone on the project's team (the current user included).</summary>
    [Parameter]
    public IEnumerable<string> Names { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var seq = 0;
        foreach (var (text, isTag) in Segments(Body, Names))
        {
            if (isTag)
            {
                builder.OpenElement(seq++, "span");
                builder.AddAttribute(seq++, "class", TagClass);
                builder.AddContent(seq++, text);
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(seq++, text);
            }
        }
    }

    /// <summary>Splits <paramref name="body"/> into plain and tagged ("@Name") runs; the runs
    /// concatenate back to exactly <paramref name="body"/>.</summary>
    public static IEnumerable<(string Text, bool IsTag)> Segments(string body, IEnumerable<string> teamNames)
    {
        var names = teamNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()
            .OrderByDescending(n => n.Length).Select(Regex.Escape).ToList();
        if (names.Count == 0)
        {
            yield return (body, false);
            yield break;
        }

        var re = new Regex(@"(?<![^\s])@(?:" + string.Join("|", names) + @")(?![\p{L}\p{N}])", RegexOptions.IgnoreCase);
        var last = 0;
        foreach (Match m in re.Matches(body))
        {
            if (m.Index > last)
            {
                yield return (body[last..m.Index], false);
            }

            yield return (m.Value, true);
            last = m.Index + m.Length;
        }

        if (last < body.Length)
        {
            yield return (body[last..], false);
        }
    }
}
