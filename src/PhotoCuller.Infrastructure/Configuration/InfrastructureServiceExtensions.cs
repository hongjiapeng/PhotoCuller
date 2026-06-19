using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Infrastructure.Database;
using PhotoCuller.Infrastructure.Hashing;
using PhotoCuller.Infrastructure.Repositories;
using PhotoCuller.Infrastructure.Scanning;
using PhotoCuller.Infrastructure.ThumbnailCache;

namespace PhotoCuller.Infrastructure.Configuration;

/// <summary>
/// IServiceCollection extension methods for PhotoCuller.Infrastructure.
/// Called from App.xaml.cs to keep the startup file clean.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers the SQLite DbContext, all repositories, and the
    /// DatabaseInitializer. Call once during application startup.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string databasePath)
    {
        // DbContext — SQLite, scoped lifetime
        services.AddDbContext<PhotoCullerDbContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath};Mode=ReadWriteCreate")
                   .UseLoggerFactory(LoggerFactory.Create(b =>
                       b.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",
                           LogLevel.Warning)));
        });

        // Database initializer (run once on startup)
        services.AddTransient<DatabaseInitializer>();

        // Repositories
        services.AddScoped<IPhotoRepository, PhotoRepository>();
        services.AddScoped<IPhotoGroupRepository, PhotoGroupRepository>();
        services.AddScoped<IAiModelProfileRepository, AiModelProfileRepository>();
        services.AddScoped<IFileOperationLogRepository, FileOperationLogRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        // Scanning
        services.AddSingleton<IScanProgressService, ScanProgressService>();
        services.AddTransient<IPhotoMetadataReader, PhotoMetadataReader>();
        services.AddTransient<IImageDecoder, ImageDecoder>();
        services.AddTransient<IPhotoScanner, PhotoScanner>();

        // Thumbnail cache
        services.AddSingleton<IThumbnailService, ThumbnailService>();

        // Hashing
        services.AddTransient<IHashService, HashService>();
        services.AddTransient<IPerceptualHashService, PerceptualHashService>();
        services.AddTransient<ISimilarityService, SimilarityService>();

        return services;
    }
}
