using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="SampleProjectService"/>.
/// </summary>
public sealed class SampleProjectServiceTests : IDisposable
{
    private readonly Mock<IDownloadService> _mockDownloadService;
    private readonly Mock<IStringTableConversionService> _mockStringTableService;
    private readonly Mock<ILogger<SampleProjectService>> _mockLogger;
    private readonly Mock<ILogger<CompressedImageToTgaConverter>> _mockConverterLogger;
    private readonly SampleProjectService _service;
    private readonly string _tempDirectory;

    public SampleProjectServiceTests()
    {
        _mockDownloadService = new Mock<IDownloadService>();
        _mockStringTableService = new Mock<IStringTableConversionService>();
        _mockLogger = new Mock<ILogger<SampleProjectService>>();
        _mockConverterLogger = new Mock<ILogger<CompressedImageToTgaConverter>>();

        var imageConverter = new CompressedImageToTgaConverter(_mockConverterLogger.Object);

        _service = new SampleProjectService(
            _mockDownloadService.Object,
            imageConverter,
            _mockLogger.Object,
            _mockStringTableService.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_SampleServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Theory]
    [InlineData("GeneralsGamePatch2", true)]
    [InlineData("ImprovedMenus", true)]
    [InlineData("LemonControlBar", true)]
    [InlineData("LeikezeHotkeys", true)]
    [InlineData("Hotkeys", true)]
    [InlineData("CustomIcons", true)]
    [InlineData(@"C:\Projects\GeneralsGamePatch2\GeneralsGamePatch2.mbproj", true)]
    [InlineData("/home/user/Samples/ImprovedMenus/ImprovedMenus.mbproj", true)]
    [InlineData(@"C:\Projects\Hotkeys\Hotkeys.mbproj", true)]
    [InlineData("BasicMod", false)]
    [InlineData("BalancePatch", false)]
    [InlineData("TextureOverhaul", false)]
    [InlineData("UnknownCustomMod", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSampleProject_ValidatesKnownSampleNamesAndPaths(string projectPath, bool expected)
    {
        // Act
        var result = _service.IsSampleProject(projectPath);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void HasSampleAssets_WhenDirectoryDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_tempDirectory, "DoesNotExist");

        // Act
        var result = _service.HasSampleAssets(nonExistentDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenGameFilesEditedIsEmpty_ReturnsFalse()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "EmptyProject");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited");
        Directory.CreateDirectory(editedDir);

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenOnlyReadmeExists_ReturnsFalse()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "ReadmeOnlyProject");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited");
        Directory.CreateDirectory(editedDir);
        File.WriteAllText(Path.Combine(editedDir, "README.md"), "# Sample placeholder");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenAuthenticFilesExist_ReturnsTrue()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "PopulatedProject");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited", "Data", "INI");
        Directory.CreateDirectory(editedDir);
        File.WriteAllText(Path.Combine(editedDir, "GameData.ini"), "[GameData]\nWindowed = Yes");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasSampleAssets_WhenLemonControlBarMissingControlBarProTxt_ReturnsFalse()
    {
        // Arrange: per-generation layout predating ControlBarPro.txt acquisition.
        var projectDir = Path.Combine(_tempDirectory, "LemonControlBar");
        var editedDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        var artDir = Path.Combine(editedDir, "Gen1080", "Art");
        var wndDir = Path.Combine(editedDir, "Res1080p", "Window");
        var dataDir = Path.Combine(editedDir, "Gen1080", "Data");
        Directory.CreateDirectory(artDir);
        Directory.CreateDirectory(wndDir);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(artDir, "texture.dds"), "dds");
        File.WriteAllText(Path.Combine(wndDir, "MainMenu.wnd"), "wnd");
        File.WriteAllText(Path.Combine(dataDir, "GameData.ini"), "ini");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenLemonControlBarHasLegacyMergedLayout_ReturnsFalse()
    {
        // Arrange: stale cache with merged Art/Window/Data roots predating the
        // per-generation layout must trigger re-acquisition even with all files present.
        var projectDir = Path.Combine(_tempDirectory, "LemonControlBar");
        var editedDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        var artDir = Path.Combine(editedDir, "Art");
        var wndDir = Path.Combine(editedDir, "Window");
        var dataDir = Path.Combine(editedDir, "Data");
        Directory.CreateDirectory(artDir);
        Directory.CreateDirectory(wndDir);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(artDir, "texture.dds"), "dds");
        File.WriteAllText(Path.Combine(wndDir, "MainMenu.wnd"), "wnd");
        File.WriteAllText(Path.Combine(dataDir, "GameData.ini"), "ini");
        File.WriteAllText(Path.Combine(editedDir, ModBuilderConstants.ControlBarProTxtFileName), "txt");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenLemonControlBarAssetsComplete_ReturnsTrue()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "LemonControlBar");
        var editedDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        var artDir = Path.Combine(editedDir, "Gen1080", "Art");
        var wndDir = Path.Combine(editedDir, "Res1080p", "Window");
        var dataDir = Path.Combine(editedDir, "Gen1080", "Data");
        Directory.CreateDirectory(artDir);
        Directory.CreateDirectory(wndDir);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(artDir, "texture.dds"), "dds");
        File.WriteAllText(Path.Combine(wndDir, "MainMenu.wnd"), "wnd");
        File.WriteAllText(Path.Combine(dataDir, "GameData.ini"), "ini");
        File.WriteAllText(Path.Combine(editedDir, ModBuilderConstants.ControlBarProTxtFileName), "txt");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void FindZhEnglishCsf_WhenRussianVariantPresent_PrefersEnglish()
    {
        // Arrange: the Russian ZH table shares ZH and English path tokens.
        var stagingDir = Path.Combine(_tempDirectory, "hlei");
        var ruCsf = Path.Combine(stagingDir, "ZH", "BIG RU", "Data", "English", "generals.csf");
        var enCsf = Path.Combine(stagingDir, "ZH", "BIG EN", "Data", "English", "generals.csf");
        var csfFiles = new List<string> { ruCsf, enCsf };

        // Act
        var result = SampleProjectService.FindZhEnglishCsf(csfFiles, stagingDir);

        // Assert
        result.Should().Be(enCsf);
    }

    [Fact]
    public void FindGeneralsEnglishCsf_WhenMultipleVariantsPresent_SelectsGeneralsEnglish()
    {
        // Arrange
        var stagingDir = Path.Combine(_tempDirectory, "hlei");
        var zhEnCsf = Path.Combine(stagingDir, "ZH", "BIG EN", "Data", "English", "generals.csf");
        var genEnCsf = Path.Combine(stagingDir, "CCG", "BIG EN", "Data", "English", "generals.csf");
        var zhDeCsf = Path.Combine(stagingDir, "ZH", "BIG DE", "Data", "English", "generals.csf");
        var csfFiles = new List<string> { zhEnCsf, genEnCsf, zhDeCsf };

        // Act
        var result = SampleProjectService.FindGeneralsEnglishCsf(csfFiles, stagingDir);

        // Assert
        result.Should().Be(genEnCsf);
    }

    [Fact]
    public void FindGermanCsf_WhenMultipleVariantsPresent_SelectsGerman()
    {
        // Arrange
        var stagingDir = Path.Combine(_tempDirectory, "hlei");
        var zhEnCsf = Path.Combine(stagingDir, "ZH", "BIG EN", "Data", "English", "generals.csf");
        var zhDeCsf = Path.Combine(stagingDir, "ZH", "BIG DE", "Data", "English", "generals.csf");
        var csfFiles = new List<string> { zhEnCsf, zhDeCsf };

        // Act
        var result = SampleProjectService.FindGermanCsf(csfFiles, stagingDir);

        // Assert
        result.Should().Be(zhDeCsf);
    }

    [Theory]
    [InlineData("340_ControlBarProLemonEditionArt1080ZH.big", SampleProjectService.LemonBigRole.Art)]
    [InlineData("340_ControlBarProLemonEditionArt2160ZH.big", SampleProjectService.LemonBigRole.Art)]
    [InlineData("340_ControlBarProLemonEditionData1080ZH.big", SampleProjectService.LemonBigRole.Data)]
    [InlineData("340_ControlBarProLemonEditionData2160ZH.big", SampleProjectService.LemonBigRole.Data)]
    [InlineData("340_ControlBarProLemonEditionZH.big", SampleProjectService.LemonBigRole.Base)]
    [InlineData("340_ControlBarProLemonEdition1080ZH.big", SampleProjectService.LemonBigRole.Resolution)]
    [InlineData("340_ControlBarProLemonEdition720ZH.big", null)]
    public void ClassifyLemonBig_RoutesArchivesByRole(string fileName, SampleProjectService.LemonBigRole? expected)
    {
        // Arrange
        var spec = new SampleProjectService.LemonResolutionSpec(
            "1080p",
            "https://example.invalid/1080.zip",
            "1080.zip",
            "340_ControlBarProLemonEdition1080ZH.big",
            "res-sha",
            "Gen1080",
            "art-sha",
            "data-sha",
            true);

        // Act
        var result = SampleProjectService.ClassifyLemonBig(fileName, spec);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_WithUnknownProject_ReturnsFailure()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "InvalidProject");
        Directory.CreateDirectory(projectDir);

        // Act
        var result = await _service.EnsureSampleAssetsAsync(projectDir, "InvalidProjectName");

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Unknown sample project");
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_WhenAssetsAlreadyExist_ReturnsSuccessImmediately()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "AlreadyPopulated");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited", "Data", "INI");
        Directory.CreateDirectory(editedDir);
        File.WriteAllText(Path.Combine(editedDir, "GameData.ini"), "[GameData]\nWindowed = Yes");

        // Act
        var result = await _service.EnsureSampleAssetsAsync(projectDir, "GeneralsGamePatch2");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
        _mockDownloadService.Verify(
            d => d.DownloadFileAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<GenHub.Core.Models.Common.DownloadProgress>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_WhenAcquisitionFails_PreservesPreExistingUserFiles()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "GeneralsGamePatch2_UserEdits");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited");
        Directory.CreateDirectory(editedDir);
        var customUserFile = Path.Combine(editedDir, "CustomUserScript.txt");
        File.WriteAllText(customUserFile, "User's valuable mod data");

        _mockDownloadService
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<GenHub.Core.Models.Common.DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Simulated network download failure"));

        // Act
        var result = await _service.EnsureSampleAssetsAsync(projectDir, "GeneralsGamePatch2");

        // Assert
        result.Success.Should().BeFalse();
        File.Exists(customUserFile).Should().BeTrue("pre-existing user files must not be deleted on download failure");
        File.ReadAllText(customUserFile).Should().Be("User's valuable mod data");
    }

    [Fact]
    public async Task SelectVerifiedBigFilesAsync_WithMixedBigs_ReturnsOnlyVerifiedAndDeletesCachedPackage()
    {
        // Arrange: one pristine archive and one repacked archive with a different hash
        var pristinePath = Path.Combine(_tempDirectory, "a_pristine.big");
        var tamperedPath = Path.Combine(_tempDirectory, "b_tampered.big");
        await File.WriteAllTextAsync(pristinePath, "PRISTINE_BIG_CONTENTS");
        await File.WriteAllTextAsync(tamperedPath, "TAMPERED_BIG_CONTENTS");

        var expectedSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PRISTINE_BIG_CONTENTS"))).ToLowerInvariant();

        var cachedZipPath = Path.Combine(_tempDirectory, "variant.zip");
        await File.WriteAllTextAsync(cachedZipPath, "CACHED_PACKAGE");

        // Act
        var result = await _service.SelectVerifiedBigFilesAsync(
            new[] { pristinePath, tamperedPath },
            expectedSha256,
            "Test Variant",
            cachedZipPath,
            CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(pristinePath);
        File.Exists(cachedZipPath).Should().BeFalse("a package containing unverified archives must be re-downloaded");
    }

    [Fact]
    public async Task SelectVerifiedBigFilesAsync_WhenAllBigsVerified_ReturnsAllAndKeepsCachedPackage()
    {
        // Arrange
        var bigPath = Path.Combine(_tempDirectory, "only.big");
        await File.WriteAllTextAsync(bigPath, "VERIFIED_CONTENTS");
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("VERIFIED_CONTENTS"))).ToLowerInvariant();

        var cachedZipPath = Path.Combine(_tempDirectory, "clean.zip");
        await File.WriteAllTextAsync(cachedZipPath, "CACHED_PACKAGE");

        // Act
        var result = await _service.SelectVerifiedBigFilesAsync(
            new[] { bigPath },
            expectedSha256,
            "Test Variant",
            cachedZipPath,
            CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().Be(bigPath);
        File.Exists(cachedZipPath).Should().BeTrue();
    }
}
