namespace GenHub.Tests.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="HDIconsFix"/>.
/// </summary>
public class HDIconsFixTests : IDisposable
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<ILogger<HDIconsFix>> _loggerMock = new();
    private readonly string _testDir;
    private readonly HDIconsFix _fix;

    /// <summary>
    /// Initializes a new instance of the <see cref="HDIconsFixTests"/> class.
    /// </summary>
    public HDIconsFixTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"HDIconsFixTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var markerPath = Path.Combine(_testDir, "HDIconsFix.done");
        _fix = new HDIconsFix(_httpClientFactoryMock.Object, _loggerMock.Object, markerPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    /// <summary>
    /// Verifies properties return expected defaults.
    /// </summary>
    [Fact]
    public void Properties_ReturnExpectedDefaults()
    {
        Assert.Equal("HDIconsFix", _fix.Id);
        Assert.Equal("HD Icons (Addon)", _fix.Title);
        Assert.Equal(ActionSetConstants.Categories.QualityOfLife, _fix.Category);
        Assert.False(_fix.IsCoreFix);
        Assert.False(_fix.IsCrucialFix);
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns true when either game component is present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsApplicableAsync_WhenGeneralsOrZeroHourPresent_ReturnsTrueAsync()
    {
        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = _testDir,
        };

        var result = await _fix.IsApplicableAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns true when HD icon files exist in the installation directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsAppliedAsync_WhenIconsExist_ReturnsTrueAsync()
    {
        var genDir = Path.Combine(_testDir, "Generals");
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(genDir);
        Directory.CreateDirectory(zhDir);

        File.WriteAllText(Path.Combine(genDir, "GeneralsHD.ico"), "icon");
        File.WriteAllText(Path.Combine(zhDir, "GeneralsZHHD.ico"), "icon");

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = genDir,
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.IsAppliedAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns false when HD icon files are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsAppliedAsync_WhenIconsMissing_ReturnsFalseAsync()
    {
        var genDir = Path.Combine(_testDir, "Generals");
        Directory.CreateDirectory(genDir);

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = genDir,
        };

        var result = await _fix.IsAppliedAsync(installation);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that UndoAsync deletes existing HD icon files and returns success.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenIconsExist_DeletesFilesAndReturnsSuccessAsync()
    {
        var genDir = Path.Combine(_testDir, "Generals");
        Directory.CreateDirectory(genDir);
        var iconPath = Path.Combine(genDir, "GeneralsHD.ico");
        File.WriteAllText(iconPath, "icon");

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = genDir,
        };

        var result = await _fix.UndoAsync(installation);

        Assert.True(result.Success);
        Assert.False(File.Exists(iconPath));
    }

    /// <summary>
    /// Verifies that ValidateArchiveContents returns false when archive is empty.
    /// </summary>
    [Fact]
    public void ValidateArchiveContents_WhenArchiveEmpty_ReturnsFalse()
    {
        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = _testDir,
        };

        var (isValid, errorMessage) = HDIconsFix.ValidateArchiveContents(new HashSet<string>(), installation);

        Assert.False(isValid);
        Assert.Equal("HD icons archive contains no valid files.", errorMessage);
    }

    /// <summary>
    /// Verifies that ValidateArchiveContents returns false when Generals icon is missing.
    /// </summary>
    [Fact]
    public void ValidateArchiveContents_WhenGeneralsInstalledAndMissingIcon_ReturnsFalse()
    {
        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = _testDir,
        };

        var archiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unrecognized.ico" };
        var (isValid, errorMessage) = HDIconsFix.ValidateArchiveContents(archiveFiles, installation);

        Assert.False(isValid);
        Assert.Equal("HD icons package does not contain a recognized icon for Generals.", errorMessage);
    }

    /// <summary>
    /// Verifies that ValidateArchiveContents returns false when Zero Hour icon is missing.
    /// </summary>
    [Fact]
    public void ValidateArchiveContents_WhenZeroHourInstalledAndMissingIcon_ReturnsFalse()
    {
        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = _testDir,
        };

        var archiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unrecognized.ico" };
        var (isValid, errorMessage) = HDIconsFix.ValidateArchiveContents(archiveFiles, installation);

        Assert.False(isValid);
        Assert.Equal("HD icons package does not contain a recognized icon for Zero Hour.", errorMessage);
    }

    /// <summary>
    /// Verifies that ValidateArchiveContents returns true when required icons are present.
    /// </summary>
    [Fact]
    public void ValidateArchiveContents_WhenAllRequiredIconsPresent_ReturnsTrue()
    {
        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = _testDir,
            HasZeroHour = true,
            ZeroHourPath = _testDir,
        };

        var archiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GeneralsHD.ico",
            "GeneralsZHHD.ico",
        };
        var (isValid, errorMessage) = HDIconsFix.ValidateArchiveContents(archiveFiles, installation);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }
}
