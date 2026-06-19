using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.FileSystem;

namespace PhotoCuller.Tests.InfrastructureTests;

public sealed class FileMoveServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFileOperationLogRepository _logRepo;
    private readonly FileMoveService _sut;

    public FileMoveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PhotoCullerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _logRepo = Substitute.For<IFileOperationLogRepository>();
        _sut     = new FileMoveService(_logRepo, NullLogger<FileMoveService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveToTrash_MovesFile_AndLogsSuccess()
    {
        // Arrange
        var srcPath  = Path.Combine(_tempDir, "photo.jpg");
        var trashDir = Path.Combine(_tempDir, "trash");
        await File.WriteAllBytesAsync(srcPath, [0xFF, 0xD8, 0xFF]); // minimal JPEG header

        var photo = new PhotoItem { FilePath = srcPath, FileName = "photo.jpg" };

        // Act
        var log = await _sut.MoveToTrashAsync(photo, trashDir, preserveStructure: false);

        // Assert
        log.Status.Should().Be(OperationStatus.Success);
        log.SourcePath.Should().Be(srcPath);
        log.IsUndoable.Should().BeTrue();

        File.Exists(srcPath).Should().BeFalse("source should be moved");
        File.Exists(log.TargetPath).Should().BeTrue("target should exist");

        photo.IsMovedToTrash.Should().BeTrue();
        photo.RecommendationStatus.Should().Be(RecommendationStatus.ToDelete);

        await _logRepo.Received(1).AddAsync(log, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveToTrash_FileNotFound_LogsFailure()
    {
        var photo = new PhotoItem
        {
            FilePath = Path.Combine(_tempDir, "missing.jpg"),
            FileName = "missing.jpg",
        };

        var log = await _sut.MoveToTrashAsync(photo, _tempDir);

        log.Status.Should().Be(OperationStatus.Failed);
        log.ErrorMessage.Should().NotBeNullOrEmpty();
        photo.IsMovedToTrash.Should().BeFalse();
    }

    // ── Collision handling ────────────────────────────────────────────────────

    [Fact]
    public async Task MoveToTrash_FileNameCollision_AppendsSuffix()
    {
        var trashDir = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(trashDir);

        // Put an existing file at the destination
        var existing = Path.Combine(trashDir, "photo.jpg");
        await File.WriteAllBytesAsync(existing, [0x01]);

        // Source file
        var srcPath = Path.Combine(_tempDir, "photo.jpg");
        await File.WriteAllBytesAsync(srcPath, [0xFF, 0xD8]);

        var photo = new PhotoItem { FilePath = srcPath, FileName = "photo.jpg" };
        var log   = await _sut.MoveToTrashAsync(photo, trashDir, preserveStructure: false);

        log.Status.Should().Be(OperationStatus.Success);
        log.TargetPath.Should().EndWith("photo(1).jpg");
        File.Exists(log.TargetPath).Should().BeTrue();
        // Original destination still intact
        File.Exists(existing).Should().BeTrue();
    }

    // ── Batch ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveManyToTrash_AllSucceed_ReturnsAllLogs()
    {
        var trashDir = Path.Combine(_tempDir, "trash");
        var photos   = new List<PhotoItem>();

        for (int i = 0; i < 3; i++)
        {
            var path = Path.Combine(_tempDir, $"photo{i}.jpg");
            await File.WriteAllBytesAsync(path, [0xFF]);
            photos.Add(new PhotoItem { FilePath = path, FileName = $"photo{i}.jpg" });
        }

        var logs = await _sut.MoveManyToTrashAsync(photos, trashDir,
            preserveStructure: false);

        logs.Should().HaveCount(3);
        logs.Should().AllSatisfy(l => l.Status.Should().Be(OperationStatus.Success));
    }

    [Fact]
    public async Task MoveManyToTrash_ProgressReported()
    {
        var trashDir = Path.Combine(_tempDir, "trash");
        var reported = new List<int>();

        var photos = new List<PhotoItem>();
        for (int i = 0; i < 2; i++)
        {
            var path = Path.Combine(_tempDir, $"p{i}.jpg");
            await File.WriteAllBytesAsync(path, [0xFF]);
            photos.Add(new PhotoItem { FilePath = path, FileName = $"p{i}.jpg" });
        }

        await _sut.MoveManyToTrashAsync(photos, trashDir,
            preserveStructure: false,
            progress: new Progress<int>(n => reported.Add(n)));

        // Give Progress<T> callback time to fire
        await Task.Delay(50);
        reported.Should().BeEquivalentTo([1, 2]);
    }
}
