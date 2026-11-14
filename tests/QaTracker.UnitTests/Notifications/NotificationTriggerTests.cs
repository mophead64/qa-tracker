using QaTracker.Web.Data;
using QaTracker.Web.Notifications;

namespace QaTracker.UnitTests.Notifications;

/// <summary>Which notification switches each role sees, and saving them without disturbing the rest.</summary>
public sealed class NotificationTriggerTests
{
    [Fact]
    public void Viewers_only_see_mentions_and_reactions_to_their_comments()
    {
        Assert.Equal([NotificationTrigger.Mentioned, NotificationTrigger.CommentReaction],
            NotificationTriggers.ForRole(Roles.Viewer).Select(t => t.Trigger));
        Assert.Equal(NotificationTriggers.All.Count, NotificationTriggers.ForRole(Roles.Dev).Count);
        Assert.Equal(NotificationTriggers.All.Count, NotificationTriggers.ForRole(Roles.QA).Count);
    }

    [Fact]
    public void Saving_as_a_viewer_leaves_the_switches_they_were_not_shown_alone()
    {
        // Previously chose to hear about new defects (off by default for a QA-or-viewer) and not
        // about being assigned a defect.
        var on = NotificationTriggers.Bit(NotificationTrigger.DefectRaisedOnMyProject);
        var off = NotificationTriggers.Bit(NotificationTrigger.DefectAssignedToMe);

        // Tick mentions, untick reactions (the two switches a viewer sees).
        var (newOn, newOff) = NotificationTriggers.Save([NotificationTrigger.Mentioned], Roles.Viewer, on, off);

        Assert.True(NotificationTriggers.Wants(NotificationTrigger.Mentioned, newOn, newOff, false));
        Assert.False(NotificationTriggers.Wants(NotificationTrigger.CommentReaction, newOn, newOff, false));
        Assert.True(NotificationTriggers.Wants(NotificationTrigger.DefectRaisedOnMyProject, newOn, newOff, false));
        Assert.False(NotificationTriggers.Wants(NotificationTrigger.DefectAssignedToMe, newOn, newOff, false));
    }
}
