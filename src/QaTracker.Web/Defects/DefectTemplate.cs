namespace QaTracker.Web.Defects;

/// <summary>Which fields a defect uses. Picked per defect on its form; new defects start from
/// the project's default, which falls back to the system default (see <see cref="DefectTemplates.Resolve"/>).</summary>
public enum DefectTemplate
{
    /// <summary>Just a description (stored in <see cref="Defect.ReproSteps"/>); no expected / actual results.</summary>
    Simple = 0,

    /// <summary>Repro steps, expected results and actual results.</summary>
    Advanced = 1,
}

public static class DefectTemplates
{
    /// <summary>What a defect starts with when nobody has chosen: the original, all-fields layout.</summary>
    public const DefectTemplate SystemFallback = DefectTemplate.Advanced;

    public static readonly IReadOnlyList<DefectTemplate> All = [DefectTemplate.Simple, DefectTemplate.Advanced];

    /// <summary>The project's own default when it has one, otherwise the system default.</summary>
    public static DefectTemplate Resolve(DefectTemplate? projectDefault, DefectTemplate systemDefault) =>
        projectDefault ?? systemDefault;

    public static string Label(DefectTemplate template) => template switch
    {
        DefectTemplate.Simple => "Simple",
        _ => "Advanced",
    };

    // Worded so neither repeats a form field's label ("Description", "Repro steps", "Expected
    // results", "Actual results") — the option text is part of its label, and label lookups match
    // substrings.
    public static string Description(DefectTemplate template) => template switch
    {
        DefectTemplate.Simple => "A single free-text box",
        _ => "Separate steps, expected and actual fields",
    };
}
