using QaTracker.Web.Data;

namespace QaTracker.Web.Notifications;

/// <summary>
/// Something someone else does that can notify a user — each one switchable on the Settings
/// page ("Notify me when someone…"). Values are bit positions in the user's stored choices
/// (<see cref="ApplicationUser.NotificationsOn"/> / <see cref="ApplicationUser.NotificationsOff"/>),
/// so never renumber them; add new ones at the end.
/// </summary>
public enum NotificationTrigger
{
    DefectRaisedOnMyProject = 0,
    DefectAssignedToMe = 1,
    DefectUnassignedFromMe = 2,
    DefectStatusChanged = 3,
    DefectCommented = 4,
    DefectFileAdded = 5,
    DefectEdited = 6,
    TestCaseCommented = 7,
    TestCaseResultChanged = 8,
    Mentioned = 9,
    CommentReaction = 10,
    AddedToProjectTeam = 11,
    RemovedFromProjectTeam = 12,
}

/// <summary>One switch on the Settings page: its trigger, the area it's grouped under and its wording.</summary>
public sealed record NotificationTriggerInfo(NotificationTrigger Trigger, string Area, string Label);

/// <summary>
/// The catalogue of <see cref="NotificationTrigger"/>s — areas, wording and defaults — and how a
/// user's stored choices resolve against those defaults. A user only stores what they've changed,
/// so a trigger added later starts at its default for everyone.
/// </summary>
public static class NotificationTriggers
{
    public const string DefectsArea = "Defects";
    public const string TestCasesArea = "Test cases";
    public const string CommentsArea = "Comments";
    public const string ProjectsArea = "Projects";

    /// <summary>Every trigger, grouped by area in Settings order. Labels finish "Notify me when someone…".</summary>
    public static readonly IReadOnlyList<NotificationTriggerInfo> All =
    [
        new(NotificationTrigger.DefectAssignedToMe, DefectsArea, "assigns a defect to me"),
        new(NotificationTrigger.DefectUnassignedFromMe, DefectsArea, "takes me off a defect"),
        new(NotificationTrigger.DefectStatusChanged, DefectsArea, "changes the status of my defect"),
        new(NotificationTrigger.DefectCommented, DefectsArea, "comments on my defect"),
        new(NotificationTrigger.DefectFileAdded, DefectsArea, "adds a file to my defect"),
        new(NotificationTrigger.DefectEdited, DefectsArea, "edits my defect's details or severity"),
        new(NotificationTrigger.DefectRaisedOnMyProject, DefectsArea, "raises a defect on a project I'm on"),
        new(NotificationTrigger.TestCaseResultChanged, TestCasesArea, "changes the result of a test case I tested"),
        new(NotificationTrigger.TestCaseCommented, TestCasesArea, "comments on a test case I tested"),
        new(NotificationTrigger.Mentioned, CommentsArea, "mentions me in a comment"),
        new(NotificationTrigger.CommentReaction, CommentsArea, "reacts to my comment"),
        new(NotificationTrigger.AddedToProjectTeam, ProjectsArea, "adds me to a project's team"),
        new(NotificationTrigger.RemovedFromProjectTeam, ProjectsArea, "removes me from a project's team"),
    ];

    /// <summary>
    /// The triggers that can ever reach someone with <paramref name="role"/>, for the Settings page.
    /// A <see cref="Roles.Viewer"/> is never on a team, assigned a defect or able to record a result,
    /// so only being @-mentioned and reactions to their own comments can notify them.
    /// </summary>
    public static IReadOnlyList<NotificationTriggerInfo> ForRole(string? role) => role == Roles.Viewer
        ? All.Where(t => t.Trigger is NotificationTrigger.Mentioned or NotificationTrigger.CommentReaction).ToList()
        : All;

    /// <summary>
    /// The stored masks after saving the Settings form as someone with <paramref name="role"/>: the
    /// triggers they were shown take the ticked boxes; the rest keep their current state, so a role
    /// change later doesn't find them all switched off.
    /// </summary>
    public static (long On, long Off) Save(IEnumerable<NotificationTrigger> ticked, string? role, long on, long off)
    {
        var isDeveloper = role == Roles.Dev;
        var shown = ForRole(role).Select(t => t.Trigger).ToHashSet();
        var wanted = ticked.Where(shown.Contains)
            .Concat(All.Select(t => t.Trigger).Where(t => !shown.Contains(t) && Wants(t, on, off, isDeveloper)));
        return Store(wanted, isDeveloper);
    }

    /// <summary>A short note under an area's heading on the Settings page.</summary>
    public static string? AreaHint(string area) => area switch
    {
        DefectsArea => "\"My defect\" is one assigned to you.",
        TestCasesArea => "One you last set a result on.",
        _ => null,
    };

    /// <summary>
    /// What a trigger does for someone who hasn't chosen: everything is on, except a new defect on
    /// a project you're on, which only developers hear about (they fix them; QAs raise them).
    /// </summary>
    public static bool DefaultFor(NotificationTrigger trigger, bool isDeveloper) => trigger switch
    {
        NotificationTrigger.DefectRaisedOnMyProject => isDeveloper,
        _ => true,
    };

    public static long Bit(NotificationTrigger trigger) => 1L << (int)trigger;

    /// <summary>Whether a user with these stored choices wants this trigger.</summary>
    public static bool Wants(NotificationTrigger trigger, long on, long off, bool isDeveloper)
    {
        var bit = Bit(trigger);
        if ((off & bit) != 0)
        {
            return false;
        }

        return (on & bit) != 0 || DefaultFor(trigger, isDeveloper);
    }

    /// <summary>
    /// The stored (on, off) masks for a set of wanted triggers: only choices that differ from the
    /// default are kept, so defaults (including ones changed later) still apply to the rest.
    /// </summary>
    public static (long On, long Off) Store(IEnumerable<NotificationTrigger> wanted, bool isDeveloper)
    {
        var set = wanted.ToHashSet();
        long on = 0, off = 0;
        foreach (var info in All)
        {
            var want = set.Contains(info.Trigger);
            if (want == DefaultFor(info.Trigger, isDeveloper))
            {
                continue;
            }

            if (want)
            {
                on |= Bit(info.Trigger);
            }
            else
            {
                off |= Bit(info.Trigger);
            }
        }

        return (on, off);
    }
}
