using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Infrastructure.Database;
using PhotoCuller.Infrastructure.Repositories;

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

        return services;
    }
}
