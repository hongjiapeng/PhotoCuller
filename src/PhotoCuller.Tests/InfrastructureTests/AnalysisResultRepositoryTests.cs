using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;
using PhotoCuller.Infrastructure.Repositories;

namespace PhotoCuller.Tests.InfrastructureTests;

public sealed class AnalysisResultRepositoryTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"PhotoCullerResults_{Guid.NewGuid():N}");
    private DbContextOptions<PhotoCullerDbContext> _options = null!;
    private readonly PhotoGroup _previousGroup = new() { Name = "previous group", PhotoCount = 1 };
    private readonly PhotoItem _previous = new()
    {
        FilePath = "previous.jpg", FileName = "previous.jpg",
        RecommendationStatus = RecommendationStatus.ManualKeep, ScoringSignature = "previous-score",
    };

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _options = new DbContextOptionsBuilder<PhotoCullerDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "results.db")};Pooling=False").Options;
        await using var db = new PhotoCullerDbContext(_options);
        await db.Database.MigrateAsync();
        _previous.GroupId = _previousGroup.Id;
        _previousGroup.RecommendedPhotoId = _previous.Id;
        db.PhotoGroups.Add(_previousGroup);
        db.Photos.Add(_previous.CreateSnapshot());
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ReplaceAsync_Success_PublishesGroupsAndPhotosAndRetainsTrash()
    {
        var trash = new PhotoItem { FilePath = "removed.jpg", FileName = "removed.jpg", IsMovedToTrash = true };
        trash.GroupId = _previousGroup.Id;
        await using (var db = new PhotoCullerDbContext(_options))
        {
            db.Photos.Add(trash); await db.SaveChangesAsync();
        }
        var group = new PhotoGroup { Name = "new group", PhotoCount = 1 };
        var current = new PhotoItem { FilePath = "current.jpg", FileName = "current.jpg", GroupId = group.Id,
            RecommendationStatus = RecommendationStatus.ManualKeep, ScoringSignature = "current-score" };
        group.RecommendedPhotoId = current.Id;
        group.Photos.Add(current);

        await new AnalysisResultRepository(_options).ReplaceAsync([current], [group]);

        await using var verify = new PhotoCullerDbContext(_options);
        var photos = await verify.Photos.OrderBy(p => p.FileName).ToListAsync();
        photos.Select(p => p.FilePath).Should().Equal("current.jpg", "removed.jpg");
        photos[0].GroupId.Should().Be(group.Id);
        photos[0].RecommendationStatus.Should().Be(RecommendationStatus.ManualKeep);
        photos[0].ScoringSignature.Should().Be("current-score");
        photos[1].IsMovedToTrash.Should().BeTrue();
        photos[1].GroupId.Should().BeNull();
        (await verify.PhotoGroups.SingleAsync()).RecommendedPhotoId.Should().Be(current.Id);
        group.Photos.Should().ContainSingle().Which.Should().BeSameAs(current);
    }

    [Fact]
    public async Task ReplaceAsync_InsertFailure_RollsBackPreviousSnapshot()
    {
        var first = new PhotoItem { FilePath = "duplicate.jpg", FileName = "first" };
        var second = new PhotoItem { FilePath = "duplicate.jpg", FileName = "second" };

        var act = () => new AnalysisResultRepository(_options).ReplaceAsync([first, second], []);

        await act.Should().ThrowAsync<DbUpdateException>();
        await AssertPreviousSnapshotAsync();
    }

    [Fact]
    public async Task ReplaceAsync_CancellationAfterDelete_RollsBackPreviousSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var options = new DbContextOptionsBuilder<PhotoCullerDbContext>(_options)
            .AddInterceptors(new CancelAfterDelete(cancellation)).Options;

        var act = () => new AnalysisResultRepository(options).ReplaceAsync([], [], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertPreviousSnapshotAsync();
    }

    [Fact]
    public async Task ReplaceAsync_PreCancelled_DoesNotChangePreviousSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => new AnalysisResultRepository(_options).ReplaceAsync([], [], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertPreviousSnapshotAsync();
    }

    [Fact]
    public async Task ReplaceAsync_EmptySuccessfulScan_RemovesStaleActivePhotos()
    {
        await new AnalysisResultRepository(_options).ReplaceAsync([], []);

        await using var verify = new PhotoCullerDbContext(_options);
        (await verify.Photos.CountAsync()).Should().Be(0);
        (await verify.PhotoGroups.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateRecommendationAsync_ManualDecision_SurvivesNewContextAndSnapshotReplacement()
    {
        await using (var db = new PhotoCullerDbContext(_options))
        {
            var repository = new PhotoRepository(db, NullLogger<PhotoRepository>.Instance);
            await repository.UpdateRecommendationAsync(_previous.Id, RecommendationStatus.ManualDelete);
        }
        PhotoItem stored;
        await using (var db = new PhotoCullerDbContext(_options)) stored = await db.Photos.SingleAsync();
        stored.RecommendationStatus.Should().Be(RecommendationStatus.ManualDelete);
        await new AnalysisResultRepository(_options).ReplaceAsync([stored], [_previousGroup]);

        await using var verify = new PhotoCullerDbContext(_options);
        (await verify.Photos.SingleAsync()).RecommendationStatus.Should().Be(RecommendationStatus.ManualDelete);
    }

    private async Task AssertPreviousSnapshotAsync()
    {
        await using var verify = new PhotoCullerDbContext(_options);
        var persisted = await verify.Photos.SingleAsync();
        persisted.Id.Should().Be(_previous.Id);
        persisted.FilePath.Should().Be("previous.jpg");
        persisted.RecommendationStatus.Should().Be(RecommendationStatus.ManualKeep);
        persisted.ScoringSignature.Should().Be("previous-score");
        persisted.GroupId.Should().Be(_previousGroup.Id);
        var group = await verify.PhotoGroups.SingleAsync();
        group.Name.Should().Be("previous group");
        group.RecommendedPhotoId.Should().Be(_previous.Id);
    }

    private sealed class CancelAfterDelete(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"Photos\"", StringComparison.Ordinal))
                cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
