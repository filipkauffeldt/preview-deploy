using Microsoft.EntityFrameworkCore;

namespace PreviewDeploy.Server.Data;

public sealed class PreviewDeployDbContext(DbContextOptions<PreviewDeployDbContext> options)
    : DbContext(options)
{
    public DbSet<App> Apps => Set<App>();
    public DbSet<DeployEvent> DeployEvents => Set<DeployEvent>();
    public DbSet<Deployment> Deployments => Set<Deployment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<App>(entity =>
        {
            entity.ToTable("apps");
            entity.HasIndex(a => new { a.Owner, a.Repo }).IsUnique();
            entity.Property(a => a.Owner).HasMaxLength(200);
            entity.Property(a => a.Repo).HasMaxLength(200);
        });

        modelBuilder.Entity<DeployEvent>(entity =>
        {
            entity.ToTable("deploy_events");
            entity.Property(e => e.Sha).HasMaxLength(40);
            entity.Property(e => e.Action).HasMaxLength(20);
            entity.HasOne(e => e.App)
                .WithMany(a => a.Events)
                .HasForeignKey(e => e.AppId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Deployment>(entity =>
        {
            entity.ToTable("deployments");
            entity.Property(d => d.Sha).HasMaxLength(40);
            entity.Property(d => d.Status).HasMaxLength(30);
            entity.Property(d => d.Url).HasMaxLength(500);
            entity.HasOne(d => d.App)
                .WithMany(a => a.Deployments)
                .HasForeignKey(d => d.AppId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
