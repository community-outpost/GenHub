using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="HotkeyPackageService"/>.
/// </summary>
public class HotkeyPackageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILocalContentService> _mockLocalContent;
    private readonly Mock<ITechTreeService> _mockTechTree;
    private readonly Mock<IIconOverlayService> _mockIconOverlay;
    private readonly HotkeyPackageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyPackageServiceTests"/> class.
    /// </summary>
    public HotkeyPackageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GenHub_Pack_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLocalContent = new Mock<ILocalContentService>();
        _mockTechTree = new Mock<ITechTreeService>();
        _mockIconOverlay = new Mock<IIconOverlayService>();

        _service = new HotkeyPackageService(
            _mockTechTree.Object,
            _mockIconOverlay.Object,
            _mockLocalContent.Object,
            NullLogger<HotkeyPackageService>.Instance);
    }

    /// <summary>
    /// Disposes resources used during tests.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Verifies that CreateHotkeysAddonAsync creates a .big package and invokes CreateLocalContentManifestAsync with Addon content type.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_GeneratesBigFileAndRegistersManifest()
    {
        var profile = new HotkeyProfile
        {
            Name = "My Hotkeys",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = false,
        };
        profile.KeyMappings["CONTROLBAR:ConstructAmericaDozer"] = 'D';

        var expectedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.hotkeys-test"),
            Name = "Custom Hotkeys: My Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(expectedManifest));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(ContentType.Addon, result.Data.ContentType);

        _mockLocalContent.Verify(
            l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }
}
