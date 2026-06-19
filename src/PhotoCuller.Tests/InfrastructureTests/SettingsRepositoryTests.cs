using Microsoft.Extensions.Logging.Abstractions;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Repositories;

namespace PhotoCuller.Tests.InfrastructureTests;

/// <summary>
/// Tests for SettingsRepository using a temp directory so the test
/// does not pollute %LocalAppData%.
/// </summary>
public sealed class SettingsRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly SettingsRepository _sut;

    public SettingsRepositoryTests()
    {
        _tempDir     = Path.Combine(Path.GetTempPath(), $"PCullerSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");

        // Use the internal overload that accepts a custom directory
        _sut = new SettingsRepository(
            NullLogger<SettingsRepository>.Instance,
            _tempDir);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_FileNotExist_ReturnsDefaults()
    {
        var s = await _sut.LoadAsync();
        s.Should().NotBeNull();
        s.Theme.Should().Be("Dark");
        s.ScoringWeights.IsValid().Should().BeTrue();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesValues()
    {
        var original = new AppSettings
        {
            Theme                   = "Light",
            Language                = "en-US",
            MaxFileSizeMb           = 200,
            ToDeleteFolderName      = "MyTrash",
            PreserveFolderStructure = false,
            EnableCloudAi           = true,
        };

        await _sut.SaveAsync(original);
        var loaded = await _sut.LoadAsync();

        loaded.Theme.Should().Be("Light");
        loaded.Language.Should().Be("en-US");
        loaded.MaxFileSizeMb.Should().Be(200);
        loaded.ToDeleteFolderName.Should().Be("MyTrash");
        loaded.PreserveFolderStructure.Should().BeFalse();
        loaded.EnableCloudAi.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_IsAtomic_TempFileCleanedUp()
    {
        await _sut.SaveAsync(new AppSettings());

        var tmp = _settingsPath + ".tmp";
        File.Exists(tmp).Should().BeFalse("tmp file should be renamed away");
        File.Exists(_settingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_MultipleTimes_LastValueWins()
    {
        await _sut.SaveAsync(new AppSettings { Theme = "Dark" });
        await _sut.SaveAsync(new AppSettings { Theme = "Light" });

        var loaded = await _sut.LoadAsync();
        loaded.Theme.Should().Be("Light");
    }

    [Fact]
    public async Task ConcurrentSaves_DoNotCorruptFile()
    {
        var tasks = Enumerable.Range(0, 5)
            .Select(i => _sut.SaveAsync(new AppSettings { MaxFileSizeMb = i * 100 }));

        await Task.WhenAll(tasks);

        var loaded = await _sut.LoadAsync();
        loaded.Should().NotBeNull();
    }
}
