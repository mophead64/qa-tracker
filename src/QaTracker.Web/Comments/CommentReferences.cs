using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Comments;

/// <summary>
/// "#D-3" / "#T-12" in a comment: a link to that defect or test case in the comment's project.
/// The text is stored as typed — like @-mentions, references are resolved when the comment is
/// shown, so a renamed item shows its current title and a deleted one is left as plain text.
/// </summary>
public static partial class CommentReference
{
    /// <summary>"#D-3" / "#t-12" not glued to a word before it ("abc#D-1") or digits/letters after it ("#D-1x").</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}_#&])#([DT])-([0-9]{1,9})(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase)]
    public static partial Regex Pattern();

    /// <summary>The canonical key for a reference: "D-3" / "T-12".</summary>
    public static string Key(char kind, int number) => $"{char.ToUpperInvariant(kind)}-{number}";

    /// <summary>The distinct references in <paramref name="bodies"/>, as (kind 'D'/'T', number).</summary>
    public static IEnumerable<(char Kind, int Number)> Find(IEnumerable<string?> bodies) =>
        bodies.Where(b => !string.IsNullOrEmpty(b))
            .SelectMany(b => Pattern().Matches(b!))
            .Select(m => (Kind: char.ToUpperInvariant(m.Groups[1].Value[0]), Ok: int.TryParse(m.Groups[2].ValueSpan, out var n), Number: n))
            .Where(r => r.Ok && r.Number > 0)
            .Select(r => (r.Kind, r.Number))
            .Distinct();
}

/// <summary>A resolved reference: where "#D-3" links to and its tooltip ("D-3 Login broken · Fixing").</summary>
public sealed record ResolvedReference(string Key, string Href, string Title);

/// <summary>The references a set of comments can link, keyed by <see cref="CommentReference.Key"/>.</summary>
public sealed class CommentReferenceMap
{
    public static readonly CommentReferenceMap Empty = new([]);

    private readonly Dictionary<string, ResolvedReference> byKey;

    public CommentReferenceMap(IEnumerable<ResolvedReference> references) =>
        byKey = references.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

    public int Count => byKey.Count;

    public ResolvedReference? Find(string key) => byKey.GetValueOrDefault(key);
}

/// <summary>One entry in the comment box's "#" picker (comment-references endpoint; property names match mention.js).</summary>
public sealed record ReferenceOption(string Ref, string Kind, string Title, string Status);

public sealed class CommentReferenceService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    /// <summary>
    /// Every defect and test case in the project, for the "#" picker: defects newest first, then
    /// test cases in ID order.
    /// </summary>
    public async Task<IReadOnlyList<ReferenceOption>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var defects = await db.Defects.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.Number > 0)
            .OrderByDescending(d => d.Number)
            .Select(d => new { d.Number, d.Summary, d.Status })
            .ToListAsync(ct);
        var cases = await db.TestCases.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.Number > 0)
            .OrderBy(t => t.Number)
            .Select(t => new { t.Number, t.Scenario, t.Result })
            .ToListAsync(ct);

        return defects
            .Select(d => new ReferenceOption(DefectDisplay.Ref(d.Number), "defect", d.Summary, DefectDisplay.StatusLabel(d.Status)))
            .Concat(cases.Select(t => new ReferenceOption(TestCaseDisplay.CaseRef(t.Number), "test case", t.Scenario,
                TestCaseDisplay.ResultLabel(t.Result))))
            .ToList();
    }

    /// <summary>Looks up the defects and test cases <paramref name="bodies"/> refer to, in this project only.</summary>
    public async Task<CommentReferenceMap> ResolveAsync(Guid projectId, IEnumerable<string?> bodies, CancellationToken ct = default)
    {
        var found = CommentReference.Find(bodies).ToList();
        if (found.Count == 0)
        {
            return CommentReferenceMap.Empty;
        }

        var defectNumbers = found.Where(r => r.Kind == 'D').Select(r => r.Number).ToList();
        var caseNumbers = found.Where(r => r.Kind == 'T').Select(r => r.Number).ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var resolved = new List<ResolvedReference>();
        if (defectNumbers.Count > 0)
        {
            var defects = await db.Defects.AsNoTracking()
                .Where(d => d.ProjectId == projectId && defectNumbers.Contains(d.Number))
                .Select(d => new { d.Id, d.Number, d.Summary, d.Status })
                .ToListAsync(ct);
            resolved.AddRange(defects.Select(d => new ResolvedReference(
                DefectDisplay.Ref(d.Number), AppUrls.Defect(projectId, d.Id),
                $"{DefectDisplay.Ref(d.Number)} {d.Summary} · {DefectDisplay.StatusLabel(d.Status)}")));
        }

        if (caseNumbers.Count > 0)
        {
            var cases = await db.TestCases.AsNoTracking()
                .Where(t => t.ProjectId == projectId && caseNumbers.Contains(t.Number))
                .Select(t => new { t.Id, t.Number, t.Scenario, t.Result })
                .ToListAsync(ct);
            resolved.AddRange(cases.Select(t => new ResolvedReference(
                TestCaseDisplay.CaseRef(t.Number), AppUrls.TestCase(projectId, t.Id),
                $"{TestCaseDisplay.CaseRef(t.Number)} {t.Scenario} · {TestCaseDisplay.ResultLabel(t.Result)}")));
        }

        return new CommentReferenceMap(resolved);
    }
}

public static class CommentReferenceEndpoints
{
    /// <summary>
    /// GET /projects/{p}/comment-references — the "#" picker's list (fetched by mention.js the first
    /// time "#" is typed). Everyone signed in can read every project, so no extra policy.
    /// </summary>
    public static IEndpointRouteBuilder MapCommentReferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/projects/{projectId:guid}/comment-references", async (
            Guid projectId, HttpContext http, CommentReferenceService references, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Json(await references.ListAsync(projectId, ct));
        }).RequireAuthorization();
        return endpoints;
    }
}
