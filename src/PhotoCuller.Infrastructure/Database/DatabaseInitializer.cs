using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PhotoCuller.Infrastructure.Database;

/// <summary>
/// Applies EF Core migrations and configures SQLite PRAGMAs on startup.
/// Called once from the IHost startup pipeline; safe to call multiple times.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly PhotoCullerDbContext _db;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        PhotoCullerDbContext db,
        ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing database...");

        // Apply any pending EF Core migrations (creates DB file on first run)
        await _db.Database.MigrateAsync(ct);

        // Apply SQLite performance PRAGMAs via raw connection
        await ApplyPragmasAsync(ct);

        _logger.LogInformation("Database initialized successfully.");
    }

    private async Task ApplyPragmasAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();

            // WAL mode: concurrent readers don't block the writer
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            await cmd.ExecuteNonQueryAsync(ct);

            // NORMAL sync: safe durability without fsync on every write
            cmd.CommandText = "PRAGMA synchronous=NORMAL;";
            await cmd.ExecuteNonQueryAsync(ct);

            // 64 MB page cache (default is ~2 MB)
            cmd.CommandText = "PRAGMA cache_size=-65536;";
            await cmd.ExecuteNonQueryAsync(ct);

            // Store temp tables in memory
            cmd.CommandText = "PRAGMA temp_store=MEMORY;";
            await cmd.ExecuteNonQueryAsync(ct);

            // Enable foreign key enforcement (off by default in SQLite)
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogDebug("SQLite PRAGMAs applied (WAL, NORMAL sync, 64 MB cache).");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
