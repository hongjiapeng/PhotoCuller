using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PhotoCuller.Infrastructure.Database;

/// <summary>
/// Design-time factory used exclusively by 'dotnet ef migrations add'.
/// Not used at runtime — the real DbContext is registered via DI.
/// </summary>
internal sealed class PhotoCullerDbContextFactory
    : IDesignTimeDbContextFactory<PhotoCullerDbContext>
{
    public PhotoCullerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PhotoCullerDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new PhotoCullerDbContext(options);
    }
}
