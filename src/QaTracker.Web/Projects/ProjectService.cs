using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Data;

namespace QaTracker.Web.Projects;

/// <summary>Fields for one custom link when creating or updating a project.</summary>
public sealed record ProjectLinkInput(string Label, string Url);

/// <summary>
/// Reads and writes <see cref="Project"/> aggregates. Uses a context factory so each
/// call gets a short-lived <see cref="ApplicationDbContext"/> — Blazor Server keeps a
/// component's services alive for the whole circuit, and a single shared context there
/// throws on concurrent renders.
/// </summary>
public sealed class ProjectService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TimeProvider timeProvider,
    AttachmentService attachments)
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    /// <summary>Loads a project with its links ordered, or null if it does not exist.</summary>
    public async Task<Project?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Links)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        project?.Links.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return project;
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Projects.AnyAsync(p => p.Id == id, ct);
    }

    public async Task<Project> CreateAsync(
        string name,
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
            Notes = NormalizeNotes(notes),
            Status = ProjectStatus.NotStarted,
            CreatedById = createdById,
            CreatedUtc = now,
            UpdatedUtc = now,
            Links = BuildLinks(links),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    /// <summary>Updates name, notes, status and replaces the link set.</summary>
    public async Task UpdateAsync(
        Guid id,
        string name,
        string? notes,
        ProjectStatus status,
        IReadOnlyList<ProjectLinkInput> links,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Project {id} not found.");

        project.Name = name.Trim();
        project.Notes = NormalizeNotes(notes);
        project.Status = status;
        project.UpdatedUtc = timeProvider.GetUtcNow();

        // Replace the link set wholesale — simplest correct behaviour for a handful of links.
        await db.ProjectLinks.Where(l => l.ProjectId == id).ExecuteDeleteAsync(ct);
        var replacement = BuildLinks(links);
        replacement.ForEach(l => l.ProjectId = id);
        db.ProjectLinks.AddRange(replacement);

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await attachments.PurgeForProjectAsync(id, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Promotes a project from <see cref="ProjectStatus.NotStarted"/> to
    /// <see cref="ProjectStatus.InFlight"/>. Called when the first item is created in a
    /// project (test cases arrive in Phase 3); a no-op once work has started or finished.
    /// </summary>
    public async Task MarkInFlightAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null || project.Status != ProjectStatus.NotStarted)
        {
            return;
        }

        project.Status = ProjectStatus.InFlight;
        project.UpdatedUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

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
