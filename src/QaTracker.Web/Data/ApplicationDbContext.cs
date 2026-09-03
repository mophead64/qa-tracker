using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Projects;

namespace QaTracker.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectLink> ProjectLinks => Set<ProjectLink>();

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
    }
}
