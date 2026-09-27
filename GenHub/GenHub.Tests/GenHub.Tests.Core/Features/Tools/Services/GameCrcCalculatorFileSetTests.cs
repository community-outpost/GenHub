using GenHub.Core.Models.Enums;
using GenHub.Core.Services.Tools.Checksum;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for profile file-set scoping in <see cref="GameCrcCalculatorService"/>.
/// </summary>
public sealed class GameCrcCalculatorFileSetTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameCrcCalculatorFileSetTests"/> class.
    /// </summary>
    public GameCrcCalculatorFileSetTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_CalcSetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that the legacy cache key shape is preserved when file-set scoping is unused.
    /// </summary>
    [Fact]
    public void BuildIniCacheKey_WithoutFileSet_KeepsLegacyShape()
    {
        var key = GameCrcCalculatorService.BuildIniCacheKey(_tempRoot, GameType.ZeroHour);

        Assert.Equal($"{_tempRoot}|ZeroHour||", key);
    }

    /// <summary>
    /// Verifies that distinct file sets produce distinct cache keys while set order is normalized.
    /// </summary>
    [Fact]
    public void BuildIniCacheKey_WithFileSets_SeparatesKeys()
    {
        var vanilla = GameCrcCalculatorService.BuildIniCacheKey(_tempRoot, GameType.ZeroHour, allowedBaseRelativePaths: ["INIZH.big"]);
        var reordered = GameCrcCalculatorService.BuildIniCacheKey(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["PatchZH.big", "INIZH.big"]);
        var sorted = GameCrcCalculatorService.BuildIniCacheKey(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["INIZH.big", "PatchZH.big"]);
        var withOverlay = GameCrcCalculatorService.BuildIniCacheKey(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["INIZH.big"],
            overlayModPaths: ["C:\\mods\\Extra.big"]);

        Assert.NotEqual(vanilla, reordered);
        Assert.Equal(sorted, reordered);
        Assert.NotEqual(vanilla, withOverlay);
    }

    /// <summary>
    /// Verifies that an explicitly empty allow-list gets its own cache key instead of reusing the legacy unrestricted key.
    /// </summary>
    [Fact]
    public void BuildIniCacheKey_WithEmptyAllowList_DiffersFromLegacyKey()
    {
        var legacy = GameCrcCalculatorService.BuildIniCacheKey(_tempRoot, GameType.ZeroHour);
        var empty = GameCrcCalculatorService.BuildIniCacheKey(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: []);

        Assert.NotEqual(legacy, empty);
    }

    /// <summary>
    /// Verifies that foreign base archives are excluded from the calculated CRC when scoped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CalculateIniCrcAsync_WithAllowList_IgnoresForeignArchives()
    {
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Retail.big"),
            ("Data\\INI\\GameData.ini", "GameData retail body"));
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Foreign.big"),
            ("Data\\INI\\AIData.ini", "AIData foreign body"));

        var service = new GameCrcCalculatorService();
        var scoped = await service.CalculateIniCrcAsync(_tempRoot, GameType.ZeroHour, allowedBaseRelativePaths: ["Retail.big"]);
        var unscoped = await service.CalculateIniCrcAsync(_tempRoot, GameType.ZeroHour);

        Assert.True(scoped.Success);
        Assert.True(unscoped.Success);
        Assert.NotEqual(unscoped.Data, scoped.Data);
    }

    /// <summary>
    /// Verifies that an allow-listed loose INI participates in the calculated CRC.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CalculateIniCrcAsync_WithAllowListedLooseIni_IncludesLooseRules()
    {
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Retail.big"),
            ("Data\\INI\\GameData.ini", "GameData retail body"));
        var looseDir = Path.Combine(_tempRoot, "Data", "INI");
        Directory.CreateDirectory(looseDir);
        await File.WriteAllTextAsync(Path.Combine(looseDir, "AIData.ini"), "AIData loose body");

        var service = new GameCrcCalculatorService();
        var baseOnly = await service.CalculateIniCrcAsync(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["Retail.big"]);
        var unioned = await service.CalculateIniCrcAsync(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["Retail.big", "Data/INI/AIData.ini"]);

        Assert.True(baseOnly.Success);
        Assert.True(unioned.Success);
        Assert.NotEqual(baseOnly.Data, unioned.Data);
    }

    /// <summary>
    /// Verifies that overlay mod archives feed the calculated CRC.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CalculateIniCrcAsync_WithOverlayMod_IncludesOverlayIni()
    {
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Retail.big"),
            ("Data\\INI\\GameData.ini", "GameData retail body"));
        var overlay = Path.Combine(_tempRoot, "Overlay.big");
        BigArchiveFixture.Write(overlay, ("Data\\INI\\AIData.ini", "AIData overlay body"));

        var service = new GameCrcCalculatorService();
        var plain = await service.CalculateIniCrcAsync(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["Retail.big"]);
        var modded = await service.CalculateIniCrcAsync(
            _tempRoot,
            GameType.ZeroHour,
            allowedBaseRelativePaths: ["Retail.big"],
            overlayModPaths: [overlay]);

        Assert.True(plain.Success);
        Assert.True(modded.Success);
        Assert.NotEqual(plain.Data, modded.Data);
    }
}
