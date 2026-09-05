using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;

namespace QaTracker.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectLink> ProjectLinks => Set<ProjectLink>();

    public DbSet<TestScope> TestScopes => Set<TestScope>();

    public DbSet<TestCase> TestCases => Set<TestCase>();

    public DbSet<TestCaseComment> TestCaseComments => Set<TestCaseComment>();

    public DbSet<Defect> Defects => Set<Defect>();

    public DbSet<DefectEvidence> DefectEvidence => Set<DefectEvidence>();

    public DbSet<DefectComment> DefectComments => Set<DefectComment>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .Property(u => u.Theme)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Status)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(p => p.Name);

            entity.HasOne(p => p.CreatedBy)
                .WithMany()
                .HasForeignKey(p => p.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.Links)
                .WithOne(l => l.Project)
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TestScope>(entity =>
        {
            entity.Property(s => s.Kind)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.HasIndex(s => s.ProjectId);

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

            entity.HasOne(tc => tc.CreatedBy)
                .WithMany()
                .HasForeignKey(tc => tc.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
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

            entity.HasMany(d => d.Evidence)
                .WithOne(e => e.Defect)
                .HasForeignKey(e => e.DefectId)
                .OnDelete(DeleteBehavior.Cascade);
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

            entity.HasOne(a => a.UploadedBy)
                .WithMany()
                .HasForeignKey(a => a.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
