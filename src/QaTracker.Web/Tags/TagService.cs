using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Data;
using QaTracker.Web.Projects;

namespace QaTracker.Web.Tags;

/// <summary>A project a tag is on, for the tags page's "where it's used".</summary>
public sealed record TagProjectRef(Guid Id, string Name, ProjectStatus Status);

/// <summary>A tag with the projects that use it, by name.</summary>
public sealed record TagUsage(Guid Id, string Name, TagColour Colour, IReadOnlyList<TagProjectRef> Projects)
{
    public int ProjectCount => Projects.Count;

    /// <summary>Of <see cref="Projects"/>, how many are Active (in flight).</summary>
    public int ActiveCount => Projects.Count(p => p.Status == ProjectStatus.InFlight);
}

/// <summary>Outcome of a tag write: the problem to show, or null on success.</summary>
public sealed record TagResult(string? Error, Guid? TagId = null)
{
    public bool Succeeded => Error is null;

    public static TagResult Fail(string error) => new(error);
}

/// <summary>
/// System-wide project tags: listing them with where they're used, putting them on and taking
/// them off projects (creating a tag by name on first use), and renaming / recolouring /
/// deleting them from System settings. Every change to a project's tags lands in that project's
/// history — including renames and deletes made from System settings.
/// </summary>
public sealed class TagService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>Every tag, by name, with the projects it's on (by name).</summary>
    public async Task<IReadOnlyList<TagUsage>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tags = await db.Tags
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Colour,
                Projects = t.Projects.Select(p => new TagProjectRef(p.Id, p.Name, p.Status)).ToList(),
            })
            .ToListAsync(ct);

        return tags
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagUsage(t.Id, t.Name, t.Colour,
                t.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    /// <summary>
    /// Puts the tag called <paramref name="name"/> on a project, creating it in
    /// <paramref name="newTagColour"/> when no tag by that name exists yet (an existing tag keeps its
    /// own colour). Already on the project: a no-op success.
    /// </summary>
    public async Task<TagResult> AddToProjectAsync(
        Guid projectId, string? name, string? actorId, TagColour newTagColour = TagColour.Gray, CancellationToken ct = default)
    {
        var cleaned = Tag.Clean(name);
        if (Tag.Validate(cleaned) is { } error)
        {
            return TagResult.Fail(error);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
        {
            return TagResult.Fail("That project no longer exists.");
        }

        var normalized = Tag.Normalize(cleaned);
        var now = timeProvider.GetUtcNow();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalized, ct);
        if (tag is null)
        {
            tag = new Tag
            {
                Id = Guid.NewGuid(), Name = cleaned, NormalizedName = normalized, Colour = newTagColour, CreatedUtc = now,
            };
            db.Tags.Add(tag);
        }
        else if (project.Tags.Any(t => t.Id == tag.Id))
        {
            return new TagResult(null, tag.Id);
        }

        project.Tags.Add(tag);
        project.UpdatedUtc = now;
        ProjectHistory.Record(db, projectId, ProjectEventKind.TagAdded, actorId, now, toValue: tag.Name, subjectId: tag.Id);
        await db.SaveChangesAsync(ct);
        return new TagResult(null, tag.Id);
    }

    /// <summary>Takes a tag off a project (the tag itself stays). Idempotent.</summary>
    public async Task RemoveFromProjectAsync(Guid projectId, Guid tagId, string? actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == projectId, ct);
        var tag = project?.Tags.FirstOrDefault(t => t.Id == tagId);
        if (project is null || tag is null)
        {
            return;
        }

        project.Tags.Remove(tag);
        project.UpdatedUtc = timeProvider.GetUtcNow();
        ProjectHistory.Record(db, projectId, ProjectEventKind.TagRemoved, actorId, project.UpdatedUtc,
            fromValue: tag.Name, subjectId: tag.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates a tag that isn't on any project yet (System settings).</summary>
    public async Task<TagResult> CreateAsync(string? name, TagColour colour, CancellationToken ct = default)
    {
        var cleaned = Tag.Clean(name);
        if (Tag.Validate(cleaned) is { } error)
        {
            return TagResult.Fail(error);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalized = Tag.Normalize(cleaned);
        if (await db.Tags.AnyAsync(t => t.NormalizedName == normalized, ct))
        {
            return TagResult.Fail($"There's already a tag called \"{cleaned}\".");
        }

        var tag = new Tag
        {
            Id = Guid.NewGuid(), Name = cleaned, NormalizedName = normalized, Colour = colour,
            CreatedUtc = timeProvider.GetUtcNow(),
        };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return new TagResult(null, tag.Id);
    }

    /// <summary>
    /// Renames and/or recolours a tag. A rename is recorded in the history of every project the
    /// tag is on; a colour change isn't (it's presentation only).
    /// </summary>
    public async Task<TagResult> UpdateAsync(Guid tagId, string? name, TagColour colour, string? actorId, CancellationToken ct = default)
    {
        var cleaned = Tag.Clean(name);
        if (Tag.Validate(cleaned) is { } error)
        {
            return TagResult.Fail(error);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.Include(t => t.Projects).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null)
        {
            return TagResult.Fail("That tag no longer exists.");
        }

        var normalized = Tag.Normalize(cleaned);
        if (normalized != tag.NormalizedName && await db.Tags.AnyAsync(t => t.NormalizedName == normalized, ct))
        {
            return TagResult.Fail($"There's already a tag called \"{cleaned}\".");
        }

        if (cleaned != tag.Name)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var project in tag.Projects)
            {
                ProjectHistory.Record(db, project.Id, ProjectEventKind.TagRenamed, actorId, now,
                    fromValue: tag.Name, toValue: cleaned, subjectId: tag.Id);
            }
            tag.Name = cleaned;
            tag.NormalizedName = normalized;
        }

        tag.Colour = colour;
        await db.SaveChangesAsync(ct);
        return new TagResult(null, tag.Id);
    }

    /// <summary>Deletes a tag, taking it off every project — recorded in each one's history.</summary>
    public async Task DeleteAsync(Guid tagId, string? actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.Include(t => t.Projects).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var project in tag.Projects)
        {
            ProjectHistory.Record(db, project.Id, ProjectEventKind.TagDeleted, actorId, now,
                fromValue: tag.Name, subjectId: tag.Id);
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
    }
}
