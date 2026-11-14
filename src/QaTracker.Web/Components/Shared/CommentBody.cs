using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using QaTracker.Web.Comments;

namespace QaTracker.Web.Components.Shared;

/// <summary>
/// A posted comment's text, with each "@Name" of a project team member shown as a green tag —
/// the same look as the tag in the comment box while typing (mention.js). Mentions aren't
/// stored on the comment, so this matches on the team's current names using the same rule as
/// the picker: whitespace or start before the "@", no letter/digit straight after the name.
/// "#D-3" / "#T-12" references to items in <see cref="References"/> become links to them (see
/// <see cref="CommentReference"/>); unknown ones stay plain text.
/// A plain component (no markup) so no template whitespace leaks into the pre-wrap text.
/// </summary>
public sealed class CommentBody : ComponentBase
{
    // Tailwind scans this file for these class names (Components/Shared/*.cs in tailwind.config.js).
    /// <summary>The green tag look for an "@Name" — also handed to attachment-preview.js (via the preview
    /// dialog's data-mention-class) so a comment shown in the preview tags its mentions the same way.</summary>
    public const string TagClass =
        "rounded bg-brand-100 font-medium text-brand-800 ring-1 ring-brand-300 dark:bg-brand-700/40 dark:text-brand-100 dark:ring-brand-600";

    /// <summary>The look of a "#D-3" link — also handed to attachment-preview.js (data-reference-class).</summary>
    public const string ReferenceClass = "font-mono font-medium";

    [Parameter, EditorRequired]
    public string Body { get; set; } = "";

    /// <summary>The defects and test cases this page's comments refer to (CommentReferenceService.ResolveAsync).</summary>
    [Parameter]
    public CommentReferenceMap References { get; set; } = CommentReferenceMap.Empty;

    /// <summary>Display names of everyone on the project's team (the current user included).</summary>
    [Parameter]
    public IEnumerable<string> Names { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        foreach (var run in Runs(Body, Names, References))
        {
            if (run.Reference is { } reference)
            {
                builder.OpenElement(0, "a");
                builder.AddAttribute(1, "href", reference.Href);
                builder.AddAttribute(2, "class", ReferenceClass);
                builder.AddAttribute(3, "title", reference.Title);
                builder.AddAttribute(4, "data-comment-ref", reference.Key);
                builder.AddContent(5, run.Text);
                builder.CloseElement();
            }
            else if (run.IsMention)
            {
                builder.OpenElement(6, "span");
                builder.AddAttribute(7, "class", TagClass);
                builder.AddContent(8, run.Text);
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(9, run.Text);
            }
        }
    }

    /// <summary>
    /// <see cref="Segments"/>, with each "#D-3" / "#T-12" in the plain runs that <paramref name="references"/>
    /// knows split out as a reference run. The runs still concatenate back to exactly <paramref name="body"/>.
    /// </summary>
    public static IEnumerable<CommentRun> Runs(string body, IEnumerable<string> teamNames, CommentReferenceMap references)
    {
        foreach (var (text, isTag) in Segments(body, teamNames))
        {
            if (isTag || references.Count == 0)
            {
                yield return new CommentRun(text, isTag);
                continue;
            }

            var last = 0;
            foreach (Match m in CommentReference.Pattern().Matches(text))
            {
                var key = CommentReference.Key(m.Groups[1].Value[0], int.TryParse(m.Groups[2].ValueSpan, out var n) ? n : 0);
                if (references.Find(key) is not { } reference)
                {
                    continue;
                }

                if (m.Index > last)
                {
                    yield return new CommentRun(text[last..m.Index], false);
                }

                yield return new CommentRun(m.Value, false, reference);
                last = m.Index + m.Length;
            }

            if (last < text.Length)
            {
                yield return new CommentRun(text[last..], false);
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

/// <summary>A run of a comment's text: plain, an "@Name" mention, or a "#D-3" link.</summary>
public sealed record CommentRun(string Text, bool IsMention, ResolvedReference? Reference = null);
