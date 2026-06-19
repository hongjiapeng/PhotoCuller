using Microsoft.EntityFrameworkCore;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database.Configurations;

namespace PhotoCuller.Infrastructure.Database;

/// <summary>
/// Main EF Core DbContext for PhotoCuller.
/// Default tracking behaviour: NoTracking (read-heavy workload).
/// Use .AsTracking() or explicit Update() for writes.
/// </summary>
public sealed class PhotoCullerDbContext : DbContext
{
    public PhotoCullerDbContext(DbContextOptions<PhotoCullerDbContext> options)
        : base(options)
    {
        // Read-heavy: most queries are display-only. Opt-in tracking with
        // .AsTracking() or dbContext.Update(entity) for writes.
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public DbSet<PhotoItem> Photos => Set<PhotoItem>();
    public DbSet<PhotoGroup> PhotoGroups => Set<PhotoGroup>();
    public DbSet<AiModelProfile> AiModelProfiles => Set<AiModelProfile>();
    public DbSet<FileOperationLog> FileOperationLogs => Set<FileOperationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PhotoItemConfiguration());
        modelBuilder.ApplyConfiguration(new PhotoGroupConfiguration());
        modelBuilder.ApplyConfiguration(new AiModelProfileConfiguration());
        modelBuilder.ApplyConfiguration(new FileOperationLogConfiguration());
    }
}
