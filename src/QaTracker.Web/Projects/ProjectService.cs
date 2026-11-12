using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Data;
using QaTracker.Web.Notifications;

namespace QaTracker.Web.Projects;

/// <summary>Fields for one custom link when creating or updating a project.</summary>
public sealed record ProjectLinkInput(string Label, string Url);

/// <summary>A comment on a project's dashboard, with its author's display name resolved.</summary>
public sealed record ProjectCommentView(
    Guid Id, string AuthorId, string AuthorName, string Body, DateTimeOffset CreatedUtc,
    IReadOnlyList<CommentAttachmentView> Attachments, IReadOnlyList<CommentReactionView> Reactions);

/// <summary>
/// Reads and writes <see cref="Project"/> aggregates. Uses a context factory so each
/// call gets a short-lived <see cref="ApplicationDbContext"/> — Blazor Server keeps a
/// component's services alive for the whole circuit, and a single shared context there
/// throws on concurrent renders.
/// </summary>
public sealed class ProjectService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    AttachmentService attachments,
    NotificationService notifications)
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The projects the top-bar switcher offers <paramref name="userId"/>: the same statuses as
    /// the filter they've picked on the All Projects page (<see cref="ProjectListFilter"/>) —
    /// only Active, Active and Not Started (the default), or everything including Completed.
    /// </summary>
    public async Task<IReadOnlyList<Project>> ListForSwitcherAsync(string? userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var filter = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => (ProjectListFilter?)u.ProjectListFilter)
            .FirstOrDefaultAsync(ct) ?? ProjectListFilter.ActiveAndUpcoming;

        var projects = db.Projects.AsNoTracking();
        projects = filter switch
        {
            ProjectListFilter.Active => projects.Where(p => p.Status == ProjectStatus.InFlight),
            ProjectListFilter.All => projects,
            _ => projects.Where(p => p.Status != ProjectStatus.Complete),
        };

        return await projects.OrderBy(p => p.Name).ToListAsync(ct);
    }

    /// <summary>Loads a project with its links and team members ordered, or null if it does not exist.</summary>
    public async Task<Project?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Links)
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        project?.Links.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        project?.Members.Sort((a, b) => string.Compare(
            a.FullName ?? a.UserName, b.FullName ?? b.UserName, StringComparison.OrdinalIgnoreCase));
        return project;
    }

    /// <summary>Projects the given user is a team member of, ordered by name.</summary>
    public async Task<IReadOnlyList<Project>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects
            .AsNoTracking()
            .Where(p => p.Members.Any(m => m.Id == userId))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.AnyAsync(p => p.Id == id, ct);
    }

    public async Task<Project> CreateAsync(
        string name,
        string? brief,
        string? notes,
        IReadOnlyList<ProjectLinkInput> links,
        string createdById,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Brief = NormalizeBrief(brief),
            Notes = NormalizeNotes(notes),
            Status = ProjectStatus.NotStarted,
            CreatedById = createdById,
            CreatedUtc = now,
            UpdatedUtc = now,
            Links = BuildLinks(links),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Projects.Add(project);
        ProjectHistory.Record(db, project.Id, ProjectEventKind.Created, createdById, now);
        await db.SaveChangesAsync(ct);
        return project;
    }

    /// <summary>Updates name, brief, notes and replaces the link set. Status is managed
    /// separately from the dashboard (see <see cref="SetStatusAsync"/>); the team too (see
    /// <see cref="AddMembersAsync"/> / <see cref="RemoveMemberAsync"/>). Returns what changed
    /// ("name", "links" …) for <see cref="RecordEditedAsync"/>.</summary>
    public async Task<IReadOnlyList<string>> UpdateAsync(
        Guid id,
        string name,
        string? brief,
        string? notes,
        IReadOnlyList<ProjectLinkInput> links,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Project {id} not found.");

        var changed = new List<string>();
        var newName = name.Trim();
        var newBrief = NormalizeBrief(brief);
        var newNotes = NormalizeNotes(notes);
        if (project.Name != newName) changed.Add("name");
        if (project.Brief != newBrief) changed.Add("brief");
        if (project.Notes != newNotes) changed.Add("notes");

        project.Name = newName;
        project.Brief = newBrief;
        project.Notes = newNotes;
        project.UpdatedUtc = timeProvider.GetUtcNow();

        // Replace the link set wholesale — simplest correct behaviour for a handful of links.
        var replacement = BuildLinks(links);
        var current = await db.ProjectLinks.AsNoTracking()
            .Where(l => l.ProjectId == id)
            .OrderBy(l => l.SortOrder)
            .Select(l => new { l.Label, l.Url })
            .ToListAsync(ct);
        if (!current.SequenceEqual(replacement.Select(l => new { l.Label, l.Url })))
        {
            changed.Add("links");
        }

        await db.ProjectLinks.Where(l => l.ProjectId == id).ExecuteDeleteAsync(ct);
        replacement.ForEach(l => l.ProjectId = id);
        db.ProjectLinks.AddRange(replacement);

        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Adds one entry to a project's history on its own (for changes made elsewhere,
    /// e.g. a test case import, that don't share a context with a project write).</summary>
    public async Task RecordAsync(Guid projectId, ProjectEventKind kind, string? actorId,
        string? fromValue = null, string? toValue = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        ProjectHistory.Record(db, projectId, kind, actorId, timeProvider.GetUtcNow(), fromValue, toValue);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records one "edited" entry in the project's history for a save of the edit form, which
    /// updates details, schedule and defect template in separate calls — each returns what it
    /// changed, and the form records them together. Nothing is recorded when nothing changed.
    /// </summary>
    public async Task RecordEditedAsync(Guid id, IReadOnlyList<string> changedFields, string? actorId, CancellationToken ct = default)
    {
        if (changedFields.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        ProjectHistory.Record(db, id, ProjectEventKind.Edited, actorId, timeProvider.GetUtcNow(),
            toValue: Defects.DefectHistory.DescribeEditedFields(changedFields));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sets a project's testing window and effort estimates. The caller validates first
    /// (<see cref="QaEffort.Validate"/>); midday flags are dropped when there are no dates.
    /// </summary>
    public async Task<IReadOnlyList<string>> UpdateScheduleAsync(Guid id, ProjectSchedule schedule, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Project {id} not found.");
        var before = ProjectSchedule.From(project);

        var hasWindow = schedule.TestingStart is not null && schedule.TestingEnd is not null;
        project.TestingStartDate = hasWindow ? schedule.TestingStart : null;
        project.TestingEndDate = hasWindow ? schedule.TestingEnd : null;
        project.TestingStartsMidday = hasWindow && schedule.StartsMidday;
        project.TestingEndsMidday = hasWindow && schedule.EndsMidday;
        project.EstimatedDevDays = schedule.EstimatedDevDays;
        project.EstimatedQaDays = schedule.EstimatedQaDays;
        project.QaEffortPercent = schedule.QaPercent;
        project.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return ProjectSchedule.From(project) == before ? [] : ["testing schedule"];
    }

    /// <summary>Sets the template new defects in this project start with; null = use the system default.</summary>
    public async Task<IReadOnlyList<string>> UpdateDefaultDefectTemplateAsync(Guid id, Defects.DefectTemplate? template, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Project {id} not found.");

        var next = template is { } t && Enum.IsDefined(t) ? t : (Defects.DefectTemplate?)null;
        var changed = project.DefaultDefectTemplate != next;
        project.DefaultDefectTemplate = next;
        project.UpdatedUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return changed ? ["default defect template"] : [];
    }

    /// <summary>Adds users to a project's team. Idempotent — ids already on the team, or
    /// unknown, are ignored.</summary>
    public async Task AddMembersAsync(Guid projectId, IReadOnlyList<string> userIds, string? actorId = null, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
        {
            return;
        }

        var existing = project.Members.Select(m => m.Id).ToHashSet();
        var toAdd = await db.Users
            .Where(u => userIds.Contains(u.Id) && !existing.Contains(u.Id))
            .ToListAsync(ct);
        if (toAdd.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var user in toAdd)
        {
            project.Members.Add(user);
            ProjectHistory.Record(db, projectId, ProjectEventKind.MemberAdded, actorId, now,
                toValue: ProjectHistory.DisplayName(user));
        }

        project.UpdatedUtc = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes one user from a project's team. Idempotent.</summary>
    public async Task RemoveMemberAsync(Guid projectId, string userId, string? actorId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        var member = project?.Members.FirstOrDefault(m => m.Id == userId);
        if (project is null || member is null)
        {
            return;
        }

        project.Members.Remove(member);
        project.UpdatedUtc = timeProvider.GetUtcNow();
        ProjectHistory.Record(db, projectId, ProjectEventKind.MemberRemoved, actorId, project.UpdatedUtc,
            fromValue: ProjectHistory.DisplayName(member));
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await attachments.PurgeForProjectAsync(id, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>Appends one link after the project's existing ones (the dashboard's quick
    /// "Add link"). The caller validates it first (<see cref="ProjectLink.Validate"/>).</summary>
    public async Task AddLinkAsync(Guid projectId, string label, string url, string? actorId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var nextOrder = await db.ProjectLinks
            .Where(l => l.ProjectId == projectId)
            .Select(l => (int?)l.SortOrder)
            .MaxAsync(ct) + 1 ?? 0;

        db.ProjectLinks.Add(new ProjectLink
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Label = label.Trim(),
            Url = url.Trim(),
            SortOrder = nextOrder,
        });
        project.UpdatedUtc = timeProvider.GetUtcNow();
        ProjectHistory.Record(db, projectId, ProjectEventKind.Edited, actorId, project.UpdatedUtc, toValue: "links");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Promotes a project from <see cref="ProjectStatus.NotStarted"/> to
    /// <see cref="ProjectStatus.InFlight"/>. Called when the first item is created in a
    /// project; a no-op once work has started or finished.
    /// </summary>
    public async Task MarkInFlightAsync(Guid id, string? actorId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null || project.Status != ProjectStatus.NotStarted)
        {
            return;
        }

        project.Status = ProjectStatus.InFlight;
        project.UpdatedUtc = timeProvider.GetUtcNow();
        ProjectHistory.Record(db, id, ProjectEventKind.StatusChanged, actorId, project.UpdatedUtc,
            nameof(ProjectStatus.NotStarted), nameof(ProjectStatus.InFlight));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets a project's lifecycle status directly (the dashboard status dropdown).</summary>
    public async Task SetStatusAsync(Guid id, ProjectStatus status, string? actorId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Project {id} not found.");

        if (project.Status != status)
        {
            ProjectHistory.Record(db, id, ProjectEventKind.StatusChanged, actorId, timeProvider.GetUtcNow(),
                project.Status.ToString(), status.ToString());
        }

        project.Status = status;
        project.UpdatedUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectCommentView>> ListCommentsAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var comments = await db.ProjectComments
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .Include(c => c.Author)
            .Include(c => c.Attachments)
            .Include(c => c.Reactions).ThenInclude(r => r.User)
            .AsSplitQuery()
            .ToListAsync(ct);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .Select(c => new ProjectCommentView(
                c.Id, c.AuthorId, DisplayName(c.Author), c.Body.Trim(), c.CreatedUtc,
                c.Attachments.OrderBy(a => a.SortOrder).Select(CommentAttachmentView.From).ToList(),
                CommentReactionView.ListFrom(c.Reactions)))
            .ToList();
    }

    /// <summary>Adds a comment and notifies anyone @-mentioned in it (see
    /// <see cref="NotificationService.NotifyMentionedAsync"/>).</summary>
    public async Task<Guid> AddCommentAsync(Guid projectId, string authorId, string body,
        IReadOnlyCollection<string>? mentionedUserIds = null, CancellationToken ct = default)
    {
        var comment = new ProjectComment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedUtc = timeProvider.GetUtcNow(),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ProjectComments.Add(comment);
        await db.SaveChangesAsync(ct);

        await notifications.NotifyMentionedAsync(projectId, null, null, authorId, comment.Body, mentionedUserIds, ct);

        return comment.Id;
    }

    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        await attachments.PurgeForCommentAsync(AttachmentOwner.ProjectComment, commentId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.ProjectComments.Where(c => c.Id == commentId).ExecuteDeleteAsync(ct);
    }

    private static string DisplayName(ApplicationUser? user)
    {
        if (user is null)
        {
            return "Unknown";
        }

        return !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.UserName ?? "Unknown";
    }

    private static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>Collapses the brief to a single trimmed line (any run of whitespace,
    /// including newlines pasted in, becomes one space) and caps it at the column length.</summary>
    private static string? NormalizeBrief(string? brief)
    {
        if (string.IsNullOrWhiteSpace(brief))
        {
            return null;
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(brief.Trim(), @"\s+", " ");
        return collapsed.Length > 280 ? collapsed[..280] : collapsed;
    }

    private static List<ProjectLink> BuildLinks(IReadOnlyList<ProjectLinkInput> links)
    {
        var result = new List<ProjectLink>();
        var order = 0;
        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link.Label) || string.IsNullOrWhiteSpace(link.Url))
            {
                continue;
            }

            result.Add(new ProjectLink
            {
                Id = Guid.NewGuid(),
                Label = link.Label.Trim(),
                Url = link.Url.Trim(),
                SortOrder = order++,
            });
        }

        return result;
    }
}
