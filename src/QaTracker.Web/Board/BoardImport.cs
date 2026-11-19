using System.Text;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Admin;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Board;

/// <summary>One story / bug / task the file will create, as read and checked.</summary>
public sealed record BoardImportItem(
    int Row,
    BoardItemType Type,
    string Title,
    string? Body,
    string? AssigneeId,
    string? AssigneeName,
    BoardColumn Column,
    bool Done,
    DefectTemplate? Template,
    string? ExpectedResults,
    string? ActualResults);

/// <summary>A story or bug with the tasks listed under it in the file.</summary>
public sealed record BoardImportGroup(BoardImportItem Item, IReadOnlyList<BoardImportItem> Tasks);

/// <summary>What an import file would create, and anything wrong with it.</summary>
public sealed record BoardImportPlan(IReadOnlyList<BoardImportGroup> Groups, IReadOnlyList<ImportIssue> Issues)
{
    public IEnumerable<ImportIssue> Errors => Issues.Where(i => i.Severity == ImportSeverity.Error);

    public IEnumerable<ImportIssue> Warnings => Issues.Where(i => i.Severity == ImportSeverity.Warning);

    public bool CanImport => !Errors.Any() && Groups.Count > 0;

    public int Stories => Groups.Count(g => g.Item.Type == BoardItemType.Story);

    public int Bugs => Groups.Count(g => g.Item.Type == BoardItemType.Bug);

    public int Tasks => Groups.Sum(g => g.Tasks.Count);

    public int Total => Groups.Count + Tasks;
}

/// <summary>
/// The board's CSV importer. It only ever creates: every row becomes a new story, bug or task —
/// nothing is matched to or updates what's already on the board. A Task row belongs to the
/// nearest Story or Bug row above it. People are matched to the project's team by email or name.
/// Nothing is created while the file has errors, and imported items don't notify their assignees.
/// </summary>
public sealed class BoardImportService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    BoardService boards,
    SystemSettingsService settings)
{
    public const string TypeColumn = "Type";
    public const string TitleColumn = "Title";

    /// <summary>Every column the importer reads, in the template's order.</summary>
    public static readonly IReadOnlyList<string> Columns =
        [TypeColumn, TitleColumn, "Description", "Assigned to", "Status", "Template", "Expected results", "Actual results"];

    /// <summary>The downloadable example: a story with tasks, an Advanced bug with a task, and a Simple bug.</summary>
    public static string Template()
    {
        var rows = new[]
        {
            Columns.ToArray(),
            ["Story", "Save a card for next time",
                "Customers can tick 'Save this card' when they pay, and pick a saved card next time.\nOnly the CVC is asked for again.",
                "", "In progress", "", "", ""],
            ["Task", "Store the payment provider's card token", "", "", "Complete", "", "", ""],
            ["Task", "Saved cards list at the payment step", "", "", "Active", "", "", ""],
            ["Bug", "Promo code total ignores the discount", "1. Apply WELCOME10 to a £60 basket\n2. Go to the payment step",
                "", "New", "Advanced", "The total shows £54.00 plus delivery", "The total shows £60.00"],
            ["Task", "Recalculate the total when a promo code is applied", "", "", "Active", "", "", ""],
            ["Bug", "Typo on the basket page", "\"Continue shoping\" should read \"Continue shopping\".", "", "", "Simple", "", ""],
        };
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(string.Join(',', row.Select(Csv.Field))).Append('\n');
        }
        return text.ToString();
    }

    public async Task<BoardImportPlan> BuildPlanAsync(Guid projectId, string csv, CancellationToken ct = default)
    {
        var issues = new List<ImportIssue>();
        var groups = new List<(BoardImportItem Item, List<BoardImportItem> Tasks)>();
        var rows = Csv.Parse(csv ?? "");
        if (rows.Count == 0)
        {
            return Fail("The file is empty.");
        }

        var header = rows[0].Select(Key).ToList();
        int Col(string name) => header.IndexOf(Key(name));
        var missing = new[] { TypeColumn, TitleColumn }.Where(c => Col(c) < 0).ToList();
        if (missing.Count > 0)
        {
            return Fail($"The file is missing the {string.Join(" and ", missing)} column{(missing.Count == 1 ? "" : "s")}. " +
                        "Download the template to see the expected format.");
        }
        foreach (var unknown in rows[0].Where(h => h.Trim().Length > 0 && !Columns.Any(c => Key(c) == Key(h))))
        {
            issues.Add(new(ImportSeverity.Warning, $"The column “{unknown.Trim()}” isn't one the importer reads, so it's ignored."));
        }

        var team = await TeamAsync(projectId, ct);
        var defaultTemplate = await DefaultTemplateAsync(projectId, ct);

        for (var r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            var rowNumber = r + 1; // as a spreadsheet numbers it, header = row 1
            string Cell(string name) => Col(name) is var i and >= 0 && i < cells.Length ? cells[i].Trim() : "";
            if (cells.All(c => string.IsNullOrWhiteSpace(c)))
            {
                continue;
            }

            void Error(string message) => issues.Add(new(ImportSeverity.Error, message, rowNumber));
            void Warn(string message) => issues.Add(new(ImportSeverity.Warning, message, rowNumber));

            var type = ParseType(Cell(TypeColumn));
            if (type is null)
            {
                Error(Cell(TypeColumn).Length == 0
                    ? "Type is empty — use Story, Bug or Task."
                    : $"“{Cell(TypeColumn)}” isn't a type — use Story, Bug or Task.");
                continue;
            }

            var title = Cell(TitleColumn);
            if (title.Length == 0)
            {
                Error("Title is empty.");
            }
            else if (title.Length > BoardItem.MaxTitleLength)
            {
                Error($"Title is longer than {BoardItem.MaxTitleLength} characters.");
            }

            // Who: a team member, by email or name.
            string? assigneeId = null, assigneeName = null;
            var who = Cell("Assigned to");
            if (who.Length > 0)
            {
                var match = team.FirstOrDefault(m => string.Equals(m.Email, who, StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(m.Name, who, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    Error($"“{who}” isn't on this project's team — use a team member's email or name, or leave it empty.");
                }
                else
                {
                    (assigneeId, assigneeName) = (match.Id, match.Name);
                }
            }

            // Where: a column for stories and bugs, Active / Complete for tasks.
            var status = Cell("Status");
            var column = BoardColumn.New;
            var done = false;
            if (type == BoardItemType.Task)
            {
                if (ParseTaskState(status) is { } state) done = state;
                else Error($"“{status}” isn't a task status — use Active or Complete, or leave it empty.");
            }
            else if (ParseColumn(status) is { } c)
            {
                column = c;
            }
            else
            {
                Error($"“{status}” isn't a column — use New, In progress, Testing or Closed, or leave it empty.");
            }

            // Bugs are written up like defects; stories and tasks have just a description.
            DefectTemplate? template = null;
            string? expected = null, actual = null;
            var templateText = Cell("Template");
            if (type == BoardItemType.Bug)
            {
                if (templateText.Length == 0) template = defaultTemplate;
                else if (Enum.TryParse<DefectTemplate>(templateText, ignoreCase: true, out var t) && Enum.IsDefined(t)) template = t;
                else Error($"“{templateText}” isn't a template — use Simple or Advanced, or leave it empty for the project's default.");
                expected = Blank(Cell("Expected results"));
                actual = Blank(Cell("Actual results"));
            }
            else if (templateText.Length > 0 || Cell("Expected results").Length > 0 || Cell("Actual results").Length > 0)
            {
                Warn($"Template, Expected results and Actual results are only for bugs, so they're ignored for this {(type == BoardItemType.Task ? "task" : "story")}.");
            }

            var item = new BoardImportItem(rowNumber, type.Value, title, Blank(Cell("Description")), assigneeId, assigneeName,
                column, done, template, expected, actual);
            if (type == BoardItemType.Task)
            {
                if (groups.Count == 0)
                {
                    Error("A task needs a Story or Bug row above it to belong to.");
                }
                else
                {
                    groups[^1].Tasks.Add(item);
                }
            }
            else
            {
                groups.Add((item, []));
            }
        }

        if (groups.Count == 0 && !issues.Any(i => i.Severity == ImportSeverity.Error))
        {
            issues.Add(new(ImportSeverity.Error, "The file has no stories or bugs to import."));
        }

        return new BoardImportPlan(groups.Select(g => new BoardImportGroup(g.Item, g.Tasks)).ToList(), issues);

        static BoardImportPlan Fail(string message) => new([], [new ImportIssue(ImportSeverity.Error, message)]);
    }

    /// <summary>Creates everything in the plan, in file order (tasks in their listed order). Returns how many items.</summary>
    public async Task<int> ApplyAsync(Guid projectId, BoardImportPlan plan, string actorId, CancellationToken ct = default)
    {
        if (!plan.CanImport)
        {
            return 0;
        }

        var created = 0;
        foreach (var group in plan.Groups)
        {
            var parent = await boards.CreateAsync(projectId, group.Item.Type, Input(group.Item), actorId,
                options: new BoardCreateOptions(group.Item.Column, Imported: true), ct: ct);
            created++;
            foreach (var task in group.Tasks)
            {
                await boards.CreateAsync(projectId, BoardItemType.Task, Input(task), actorId, parentId: parent.Id,
                    options: new BoardCreateOptions(Done: task.Done, Imported: true), ct: ct);
                created++;
            }
        }
        return created;

        static BoardItemInput Input(BoardImportItem i) =>
            new(i.Title, i.Body, i.AssigneeId, i.Template, i.ExpectedResults, i.ActualResults);
    }

    private sealed record Member(string Id, string Name, string? Email);

    private async Task<IReadOnlyList<Member>> TeamAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Projects.Where(p => p.Id == projectId)
                .SelectMany(p => p.Members)
                .Where(u => u.DeletedUtc == null)
                .Select(u => new { u.Id, u.FullName, u.UserName, u.Email })
                .ToListAsync(ct))
            .Select(u => new Member(u.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.UserName ?? u.Id : u.FullName!, u.Email))
            .ToList();
    }

    private async Task<DefectTemplate> DefaultTemplateAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var projectDefault = await db.Projects.Where(p => p.Id == projectId).Select(p => p.DefaultDefectTemplate).FirstOrDefaultAsync(ct);
        return DefectTemplates.Resolve(projectDefault, await settings.GetDefaultDefectTemplateAsync(ct));
    }

    /// <summary>"Assigned to" / "assigned  TO" / " Assigned to " all match.</summary>
    private static string Key(string header) =>
        string.Join(' ', header.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? Blank(string text) => text.Length == 0 ? null : text;

    private static string Squash(string text) => new string(text.Where(char.IsLetter).ToArray()).ToLowerInvariant();

    public static BoardItemType? ParseType(string text) => Squash(text) switch
    {
        "story" or "userstory" => BoardItemType.Story,
        "bug" => BoardItemType.Bug,
        "task" => BoardItemType.Task,
        _ => null,
    };

    /// <summary>Empty is New; "In progress", "in-progress" and "InProgress" all read the same.</summary>
    public static BoardColumn? ParseColumn(string text) => Squash(text) switch
    {
        "" or "new" => BoardColumn.New,
        "inprogress" => BoardColumn.InProgress,
        "testing" => BoardColumn.Testing,
        "closed" or "done" => BoardColumn.Closed,
        _ => null,
    };

    /// <summary>Empty is Active (false); Complete / Done is true.</summary>
    public static bool? ParseTaskState(string text) => Squash(text) switch
    {
        "" or "active" or "open" => false,
        "complete" or "completed" or "done" => true,
        _ => null,
    };
}
