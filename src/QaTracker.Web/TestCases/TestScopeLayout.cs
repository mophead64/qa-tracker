namespace QaTracker.Web.TestCases;

/// <summary>How the test cases page lays out its scopes. Stored per user, like
/// <see cref="TestCaseListFilter"/>, so it follows them across projects and logins.</summary>
public enum TestScopeLayout
{
    /// <summary>Every scope as a panel listing its test cases. The default.</summary>
    Expanded = 0,

    /// <summary>One compact row per scope — name, kind and result tally only.</summary>
    Collapsed = 1,
}
