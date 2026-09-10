using System.Text;
using QaTracker.Web.Defects;

namespace QaTracker.Web.TestCases;

/// <summary>
/// Writes a project's test plan as CSV in the exact shape the importer accepts
/// (see <see cref="TestCaseImportParser"/>), so an export can be bulk-edited in a spreadsheet
/// and re-imported. One row per step line / defect link; the scope, type and scenario are
/// repeated on the first row of each case and left blank on its continuation rows.
/// </summary>
public static class TestCaseCsv
{
    /// <summary>Column header shared by the export, the importer and the blank template.</summary>
    public const string Header = "Test Case Scope,Test Case Type,Test Case Scenario,Steps,DefectLink";

    public static string Export(
        IReadOnlyList<TestScope> scopes,
        IReadOnlyDictionary<Guid, IReadOnlyList<int>> defectNumbersByCase)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(defectNumbersByCase);

        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');

        foreach (var scope in scopes)
        {
            var typeLabel = KindLabel(scope.Kind);

            foreach (var @case in scope.Cases)
            {
                var steps = SplitSteps(@case.Steps);
                var defectRefs = defectNumbersByCase.TryGetValue(@case.Id, out var numbers)
                    ? numbers.Select(DefectDisplay.Ref).ToList()
                    : [];

                var rowCount = Math.Max(1, Math.Max(steps.Count, defectRefs.Count));
                for (var i = 0; i < rowCount; i++)
                {
                    sb.Append(Csv.Field(i == 0 ? scope.Name : string.Empty)).Append(',')
                      .Append(Csv.Field(i == 0 ? typeLabel : string.Empty)).Append(',')
                      .Append(Csv.Field(i == 0 ? @case.Scenario : string.Empty)).Append(',')
                      .Append(Csv.Field(i < steps.Count ? steps[i] : string.Empty)).Append(',')
                      .Append(Csv.Field(i < defectRefs.Count ? defectRefs[i] : string.Empty))
                      .Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Groups the project's defects by the test case they're linked to, as
    /// <see cref="Export"/> expects. Defect numbers are ascending.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<int>>> DefectNumbersByCaseAsync(
        DefectService defects, Guid projectId, CancellationToken ct = default)
    {
        var byCase = new Dictionary<Guid, List<int>>();

        foreach (var defect in await defects.ListForProjectAsync(projectId, ct))
        {
            foreach (var testCase in defect.TestCases)
            {
                if (!byCase.TryGetValue(testCase.Id, out var list))
                {
                    list = byCase[testCase.Id] = [];
                }

                list.Add(defect.Number);
            }
        }

        return byCase.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<int>)kv.Value.OrderBy(n => n).ToList());
    }

    // Match the spelling the blank template uses ("Non-Functional"); the importer accepts
    // any casing / spacing anyway.
    private static string KindLabel(TestCaseKind kind) =>
        kind == TestCaseKind.NonFunctional ? "Non-Functional" : "Functional";

    private static List<string> SplitSteps(string? steps) =>
        string.IsNullOrWhiteSpace(steps)
            ? []
            : steps.Replace("\r\n", "\n").Split('\n').Select(s => s.TrimEnd()).ToList();
}
