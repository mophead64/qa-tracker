using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Defects;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;
using QaTracker.Web.Tags;

namespace QaTracker.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectLink> ProjectLinks => Set<ProjectLink>();

    public DbSet<ProjectComment> ProjectComments => Set<ProjectComment>();

    public DbSet<ProjectEvent> ProjectEvents => Set<ProjectEvent>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<TestScope> TestScopes => Set<TestScope>();

    public DbSet<TestCase> TestCases => Set<TestCase>();

    public DbSet<TestCaseComment> TestCaseComments => Set<TestCaseComment>();

    public DbSet<TestCaseEvent> TestCaseEvents => Set<TestCaseEvent>();

    public DbSet<Defect> Defects => Set<Defect>();

    public DbSet<DefectComment> DefectComments => Set<DefectComment>();

    public DbSet<DefectEvent> DefectEvents => Set<DefectEvent>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<CommentReaction> CommentReactions => Set<CommentReaction>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Instance-wide runtime configuration: exactly one row, seeded here so a fresh
        // database starts with developers allowed to manage every area.
        builder.Entity<SystemSettings>(entity =>
        {
            entity.Property(s => s.DefaultDefectTemplate)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.HasData(new SystemSettings { Id = Data.SystemSettings.SingletonId });
        });

        builder.Entity<ApplicationUser>()
            .Property(u => u.Theme)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Entity<Project>(entity =>
        {
            entity.Property(p => p.DefaultDefectTemplate)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.Property(p => p.Status)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(p => p.Name);

            // Day figures are in half-day steps, well under 10,000.
            entity.Property(p => p.EstimatedDevDays).HasPrecision(6, 1);
            entity.Property(p => p.EstimatedQaDays).HasPrecision(6, 1);

            entity.HasOne(p => p.CreatedBy)
                .WithMany()
                .HasForeignKey(p => p.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.Links)
                .WithOne(l => l.Project)
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Project team — plain many-to-many, no reverse nav on ApplicationUser
            // since membership is always queried from the Project side.
            entity.HasMany(p => p.Members)
                .WithMany()
                .UsingEntity(j => j.ToTable("ProjectMembers"));

            // System-wide tags — deleting either side drops the link only.
            entity.HasMany(p => p.Tags)
                .WithMany(t => t.Projects)
                .UsingEntity(j => j.ToTable("ProjectTags"));
        });

        builder.Entity<Tag>(entity =>
        {
            entity.Property(t => t.Colour)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(t => t.NormalizedName).IsUnique();
        });

        builder.Entity<TestScope>(entity =>
        {
            entity.Property(s => s.Kind)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(s => s.ProjectId);

            // S-1, S-2, … per project.
            entity.HasIndex(s => new { s.ProjectId, s.Number }).IsUnique();

            entity.HasOne(s => s.Project)
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.CreatedBy)
                .WithMany()
                .HasForeignKey(s => s.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(s => s.Cases)
                .WithOne(tc => tc.TestScope)
                .HasForeignKey(tc => tc.TestScopeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TestCase>(entity =>
        {
            entity.Property(tc => tc.Result)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(tc => tc.TestScopeId);

            // T-1, T-2, … unique across a project's scopes. ProjectId is a plain copy of the scope's
            // (no relationship — the scope's cascade already covers deletes).
            entity.HasIndex(tc => new { tc.ProjectId, tc.Number }).IsUnique();

            entity.HasOne(tc => tc.CreatedBy)
                .WithMany()
                .HasForeignKey(tc => tc.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(tc => tc.TestedBy)
                .WithMany()
                .HasForeignKey(tc => tc.TestedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProjectEvent>(entity =>
        {
            entity.Property(e => e.Kind)
                .HasConversion<string>()
                .HasMaxLength(32);

            // The activity feed reads a project's newest entries first.
            entity.HasIndex(e => new { e.ProjectId, e.OccurredUtc });

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // As for test case history: deleting a user keeps the entry, actor goes null.
            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DefectEvent>(entity =>
        {
            entity.Property(e => e.Kind)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => e.DefectId);

            // The project activity feed reads newest-first across every defect.
            entity.HasIndex(e => e.OccurredUtc);

            entity.HasOne(e => e.Defect)
                .WithMany()
                .HasForeignKey(e => e.DefectId)
                .OnDelete(DeleteBehavior.Cascade);

            // As for test case history: deleting a user keeps the entry, actor goes null.
            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TestCaseEvent>(entity =>
        {
            entity.Property(e => e.Kind)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => e.TestCaseId);

            // The project activity feed reads newest-first across every test case.
            entity.HasIndex(e => e.OccurredUtc);

            entity.HasOne(e => e.TestCase)
                .WithMany()
                .HasForeignKey(e => e.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unlike authored content, history shouldn't stop a user being deleted: the entry
            // stays and its actor goes null ("a deleted user").
            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TestCaseComment>(entity =>
        {
            entity.HasIndex(c => c.TestCaseId);

            entity.HasOne(c => c.TestCase)
                .WithMany()
                .HasForeignKey(c => c.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Defect>(entity =>
        {
            // No HasDefaultValue: Simple is the enum's CLR default, so EF would treat it as
            // "unset" and write the database default instead. Existing rows get 'Advanced' from
            // the migration.
            entity.Property(d => d.Template)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.Property(d => d.Status)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.Property(d => d.Severity)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(d => d.ProjectId);
            entity.HasIndex(d => new { d.ProjectId, d.Number }).IsUnique();

            entity.HasOne(d => d.Project)
                .WithMany()
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // A defect links to zero or more test cases; the defect outlives any of them.
            entity.HasMany(d => d.TestCases)
                .WithMany(tc => tc.Defects)
                .UsingEntity(j => j.ToTable("DefectTestCases"));

            entity.HasOne(d => d.CreatedBy)
                .WithMany()
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.AssignedTo)
                .WithMany()
                .HasForeignKey(d => d.AssignedToId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.FixedBy)
                .WithMany()
                .HasForeignKey(d => d.FixedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.TestedBy)
                .WithMany()
                .HasForeignKey(d => d.TestedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProjectComment>(entity =>
        {
            entity.HasIndex(c => c.ProjectId);

            entity.HasOne(c => c.Project)
                .WithMany()
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DefectComment>(entity =>
        {
            entity.HasIndex(c => c.DefectId);

            entity.HasOne(c => c.Defect)
                .WithMany()
                .HasForeignKey(c => c.DefectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasIndex(a => a.ProjectId);
            entity.HasIndex(a => a.TestCaseId);
            entity.HasIndex(a => a.DefectId);
            entity.HasIndex(a => a.TestCaseCommentId);
            entity.HasIndex(a => a.DefectCommentId);
            entity.HasIndex(a => a.ProjectCommentId);

            entity.HasOne(a => a.Project)
                .WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.TestCase)
                .WithMany()
                .HasForeignKey(a => a.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Defect)
                .WithMany()
                .HasForeignKey(a => a.DefectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.TestCaseComment)
                .WithMany(c => c.Attachments)
                .HasForeignKey(a => a.TestCaseCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.DefectComment)
                .WithMany(c => c.Attachments)
                .HasForeignKey(a => a.DefectCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.ProjectComment)
                .WithMany(c => c.Attachments)
                .HasForeignKey(a => a.ProjectCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.UploadedBy)
                .WithMany()
                .HasForeignKey(a => a.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CommentReaction>(entity =>
        {
            entity.Property(r => r.Reaction)
                .HasConversion<string>()
                .HasMaxLength(16);

            // One reaction per user per comment. Rows on other kinds of comment leave the
            // column null, and NULLs never collide in a unique index.
            entity.HasIndex(r => new { r.ProjectCommentId, r.UserId }).IsUnique();
            entity.HasIndex(r => new { r.TestCaseCommentId, r.UserId }).IsUnique();
            entity.HasIndex(r => new { r.DefectCommentId, r.UserId }).IsUnique();

            entity.HasOne(r => r.ProjectComment)
                .WithMany(c => c.Reactions)
                .HasForeignKey(r => r.ProjectCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.TestCaseComment)
                .WithMany(c => c.Reactions)
                .HasForeignKey(r => r.TestCaseCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.DefectComment)
                .WithMany(c => c.Reactions)
                .HasForeignKey(r => r.DefectCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Like notifications, a reaction isn't content worth keeping without its user —
            // deleting the user takes their reactions rather than blocking the delete.
            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(entity =>
        {
            entity.HasIndex(n => new { n.UserId, n.ReadUtc });

            entity.HasOne(n => n.Defect)
                .WithMany()
                .HasForeignKey(n => n.DefectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(n => n.TestCase)
                .WithMany()
                .HasForeignKey(n => n.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(n => n.Project)
                .WithMany()
                .HasForeignKey(n => n.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unlike other user FKs (CreatedById, AssignedToId, ...), notifications carry no
            // content of their own — they're just a per-recipient inbox — so deleting the
            // recipient should take their notifications with them rather than block the delete.
            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
