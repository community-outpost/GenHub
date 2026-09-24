using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using GenHub.Tests.Core.Features.Tools.WndEditor.TestSupport;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Acceptance;

/// <summary>
/// Acceptance criteria tests for the 3 core WND Editor issues:
/// 1. Asset loading resolves Zero Hour over Generals (Expansion tier beats BaseGame loose/archive fallback;
///    expansion archives beat same-tier base archives for shared paths, names, and string tables),
///    and MainMenuRuler does not inject Generals MainMenuBackdrop behind Zero Hour screens.
/// 2. Border/window bounds expand virtual screen width/height so widescreen borders are full-screen, not corner-boxed.
/// 3. ModBuilder sample project (ElTioRata / ImprovedMenus) loose assets in GameFilesEdited are discovered
///    and prioritized at Mod tier over base game assets.
/// </summary>
public sealed class WndEditorAcceptanceCriteriaTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndEditorAcceptanceCriteriaTests"/> class.
    /// </summary>
    public WndEditorAcceptanceCriteriaTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_Acceptance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Acceptance Criterion 1A:
    /// In a dual-game setup (Zero Hour active target + Generals base fallback),
    /// Zero Hour archives (Expansion tier) must strictly take precedence over Generals loose files (BaseGame tier).
    /// </summary>
    [Fact]
    public void Criterion1A_ZeroHourArchive_TakesPrecedenceOverGeneralsLooseFile()
    {
        // Arrange
        var zhRoot = Path.Combine(_tempRoot, "ZeroHour");
        var genRoot = Path.Combine(_tempRoot, "Generals");
        Directory.CreateDirectory(zhRoot);
        Directory.CreateDirectory(genRoot);

        // Generals base game has a loose Data\English\Generals.csf
        var genLooseCsfDir = Path.Combine(genRoot, "Data", "English");
        Directory.CreateDirectory(genLooseCsfDir);
        var genCsfPath = Path.Combine(genLooseCsfDir, "Generals.csf");
        var genBytes = Encoding.UTF8.GetBytes("Generals Base Fallback String Table");
        File.WriteAllBytes(genCsfPath, genBytes);

        // Zero Hour has EnglishZH.big containing Data\English\Generals.csf
        var zhCsfBytes = Encoding.UTF8.GetBytes("Zero Hour Expansion String Table");
        var zhBigPath = Path.Combine(zhRoot, "EnglishZH.big");
        WndTestAssets.CreateBigArchive(zhBigPath, ("Data\\English\\Generals.csf", zhCsfBytes));

        // Create VFS with Zero Hour as active target and Generals as base fallback
        var vfs = new SageVirtualFileSystem(
            zhRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);
        vfs.AddBaseFallback(genRoot);

        // Act
        var resolvedBytes = vfs.Read("Data\\English\\Generals.csf");
        var resolvedTier = vfs.GetFileTier("Data\\English\\Generals.csf");

        // Assert: Zero Hour expansion data MUST win over Generals loose fallback
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().Equal(zhCsfBytes);
        resolvedTier.Should().Be(SageFileTier.Expansion);
    }

    /// <summary>
    /// Acceptance Criterion 1B:
    /// Menus utilizing MainMenuRuler (like ChallengeLoadScreen or LAN lobbies) must NOT have
    /// Generals MainMenuBackdrop forcibly injected behind them as an underlay.
    /// </summary>
    [Fact]
    public void Criterion1B_MainMenuRuler_DoesNotForceGeneralsMainMenuBackdropUnderlay()
    {
        // Arrange: window referencing MainMenuRuler
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.ControlTypes.User,
        };
        window.SetProperty(WndConstants.PropertyKeys.Name, "ChallengeLoadScreen.wnd:Border");

        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        entries[0] = new WndDrawDataEntry("MainMenuRuler", WndRgbaColor.Undefined, WndRgbaColor.Undefined);

        var drawDataSet = new WndDrawDataSet(entries);
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, drawDataSet.ToString());

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert: SingleImage is MainMenuRuler, but UnderlayImage must be null (no Generals tank backdrop!)
        plan.SingleImage.Should().Be("MainMenuRuler");
        plan.UnderlayImage.Should().BeNull();
        plan.ReferencedImages.Should().NotContain("MainMenuBackdrop");
    }

    /// <summary>
    /// Acceptance Criterion 2:
    /// When a window or overlay ruler defines widescreen dimensions (e.g. 1920x1080),
    /// the virtual screen dimensions expand to the full width/height so the border is full-screen,
    /// rather than being confined to an 800x600 corner.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Criterion2_WidescreenWindowCoordinates_ExpandVirtualScreenDimensions()
    {
        // Arrange: a document whose window boundaries extend to 1920x1080 despite default creation resolution
        const string widescreenDocument =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 1920 1080, CREATIONRESOLUTION: 800 600;\n" +
            "  NAME = \"Menu.wnd:Parent\";\n" +
            "END\n";

        var mockGameInstallService = new Mock<IGameInstallationService>();
        mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));

        var documentService = new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>());
        var assetService = new WndEditorAssetService(
            Mock.Of<IWndImageAssetService>(),
            Mock.Of<IWndStringTableService>());
        var viewModel = new WndEditorViewModel(
            documentService,
            Mock.Of<INotificationService>(),
            Mock.Of<ILocalizationService>(),
            Mock.Of<IDialogService>(),
            mockGameInstallService.Object,
            assetService,
            Mock.Of<IWndTextureImportService>(),
            Mock.Of<IChallengeMedalService>(),
            Mock.Of<ILogger<WndEditorViewModel>>());

        // Act: load document into ViewModel
        var loaded = await viewModel.LoadFromTextAsync(widescreenDocument, null);

        // Assert: ViewModel VirtualScreenWidth and VirtualScreenHeight expand to widescreen dimensions
        loaded.Should().BeTrue();
        viewModel.VirtualScreenWidth.Should().Be(1920);
        viewModel.VirtualScreenHeight.Should().Be(1080);
    }

    /// <summary>
    /// Acceptance Criterion 3A:
    /// In a ModBuilder project with GameFilesEdited (e.g. ElTioRata / ImprovedMenus),
    /// loose textures and mapped images in GameFilesEdited are discovered and resolved at Mod tier,
    /// overriding any base game or expansion assets.
    /// </summary>
    [Fact]
    public void Criterion3A_ModBuilderSampleProject_DiscoversAndPrioritizesModAssets()
    {
        // Arrange: create mock ModBuilder sample project structure
        var projectDir = Path.Combine(_tempRoot, "ImprovedMenus");
        var gameFilesEdited = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        var texturesDir = Path.Combine(gameFilesEdited, "Data", "English", "Art", "Textures");
        var mappedImagesDir = Path.Combine(gameFilesEdited, "Data", "INI", "MappedImages", "HandCreated");
        Directory.CreateDirectory(texturesDir);
        Directory.CreateDirectory(mappedImagesDir);

        // Create loose mod texture (1920x1080 MainMenuRulerUserInterface.tga)
        var modTexturePath = Path.Combine(texturesDir, "mainmenuruleruserinterface.tga");
        byte[] modTextureBytes = [0x54, 0x47, 0x41, 0x01, 0x02, 0x03];
        File.WriteAllBytes(modTexturePath, modTextureBytes);

        // Create loose mod mapped image INI
        var modIniPath = Path.Combine(mappedImagesDir, "HandCreatedMappedImages.INI");
        File.WriteAllText(modIniPath, "MappedImage MainMenuRuler\n  Texture = mainmenuruleruserinterface.tga\nEnd\n");

        // Base game has an older/smaller version of the texture in an archive
        var baseDir = Path.Combine(_tempRoot, "BaseGame");
        Directory.CreateDirectory(baseDir);
        var baseBigPath = Path.Combine(baseDir, "Textures.big");
        byte[] baseTextureBytes = [0x54, 0x47, 0x41, 0x00, 0x00, 0x00];
        WndTestAssets.CreateBigArchive(baseBigPath, ("Data\\English\\Art\\Textures\\mainmenuruleruserinterface.tga", baseTextureBytes));

        // Create VFS with base game and add the mod project
        var vfs = new SageVirtualFileSystem(
            baseDir,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);

        vfs.AddMod(gameFilesEdited);

        // Act 1: Read the texture
        var readBytes = vfs.Read("Data\\English\\Art\\Textures\\mainmenuruleruserinterface.tga");
        var readTier = vfs.GetFileTier("Data\\English\\Art\\Textures\\mainmenuruleruserinterface.tga");

        // Act 2: Read loose file by filename alone
        var modLooseByName = vfs.TryReadModLooseFileByName("mainmenuruleruserinterface.tga");

        // Act 3: Enumerate INI files under Data\INI\MappedImages
        var iniFiles = vfs.FilesUnder("Data\\INI\\MappedImages");

        // Assert: Mod assets win at Mod tier
        readBytes.Should().NotBeNull();
        readBytes.Should().Equal(modTextureBytes);
        readTier.Should().Be(SageFileTier.Mod);

        modLooseByName.Should().NotBeNull();
        modLooseByName.Should().Equal(modTextureBytes);

        iniFiles.Should().NotBeEmpty();
        iniFiles.Should().Contain(f => f.EndsWith("HandCreatedMappedImages.INI", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Acceptance Criterion 1C:
    /// Inside one Zero Hour installation, expansion archives override same-path base archives
    /// per GenHub deliberate preview precedence policy (later-mounted-wins within the same tier,
    /// a conscious GenHub preview deviation from the retail engine init path).
    /// A mapped image and texture shared by INI.big (red) and INIZH.big (blue) must resolve
    /// to the expansion (blue) pixels end to end through the image asset service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Criterion1C_ZeroHourRoot_SamePathArtResolvesToZeroHourPixels()
    {
        // Arrange: base and expansion archives share one MappedImages INI path and one texture path
        var zhRoot = Path.Combine(_tempRoot, "ZeroHourPixels");
        Directory.CreateDirectory(zhRoot);
        var redPng = WndTestAssets.CreateSolidPng(MagickColors.Red);
        var bluePng = WndTestAssets.CreateSolidPng(MagickColors.Blue);
        const string iniPath = "Data\\INI\\MappedImages\\HandCreated\\Shared.ini";
        const string iniText = "MappedImage SharedImage\n  Texture = shared.tga\n  Coords = Left:0 Top:0 Right:1 Bottom:1\nEnd\n";
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "INI.big"),
            (iniPath, Encoding.UTF8.GetBytes(iniText)),
            ("shared.tga", redPng));
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "INIZH.big"),
            (iniPath, Encoding.UTF8.GetBytes(iniText)),
            ("shared.tga", bluePng));

        var service = new WndImageAssetService(Mock.Of<ILogger<WndImageAssetService>>());

        // Act
        var result = await service.GetImagesAsync(["SharedImage"], zhRoot, null, null, null, true, CancellationToken.None);

        // Assert: the resolved pixels are the expansion texture, not the base one
        result.Success.Should().BeTrue();
        var sharedImages = result.Data;
        sharedImages.Should().NotBeNull().And.ContainKey("SharedImage");
        WndTestAssets.ComparePngs(sharedImages!["SharedImage"], bluePng).Should().Be(0);
        WndTestAssets.ComparePngs(sharedImages!["SharedImage"], redPng).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Acceptance Criterion 1D:
    /// When base and expansion archives define the same mapped image name in different INI files,
    /// the expansion (later-mounted) definition wins per GenHub deliberate preview precedence policy
    /// even when its INI path sorts alphabetically earlier than the base definition path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Criterion1D_SameNameMappedImage_LaterMountedArchiveWinsDespitePathOrder()
    {
        // Arrange: expansion definition in an alphabetically-early path, base definition in a late path
        var zhRoot = Path.Combine(_tempRoot, "ZeroHourNameOrder");
        Directory.CreateDirectory(zhRoot);
        var redPng = WndTestAssets.CreateSolidPng(MagickColors.Red);
        var bluePng = WndTestAssets.CreateSolidPng(MagickColors.Blue);
        const string baseIniText = "MappedImage OrderImage\n  Texture = orderbase.tga\n  Coords = Left:0 Top:0 Right:1 Bottom:1\nEnd\n";
        const string zhIniText = "MappedImage OrderImage\n  Texture = orderzh.tga\n  Coords = Left:0 Top:0 Right:1 Bottom:1\nEnd\n";
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "A_Base.big"),
            ("Data\\INI\\MappedImages\\HandCreated\\Z_Late.ini", Encoding.UTF8.GetBytes(baseIniText)),
            ("orderbase.tga", redPng));
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "Z_Expansion.big"),
            ("Data\\INI\\MappedImages\\HandCreated\\A_Early.ini", Encoding.UTF8.GetBytes(zhIniText)),
            ("orderzh.tga", bluePng));

        var service = new WndImageAssetService(Mock.Of<ILogger<WndImageAssetService>>());

        // Act
        var result = await service.GetImagesAsync(["OrderImage"], zhRoot, null, null, null, true, CancellationToken.None);

        // Assert: the expansion definition (blue texture) wins over the base definition (red texture)
        result.Success.Should().BeTrue();
        var orderImages = result.Data;
        orderImages.Should().NotBeNull().And.ContainKey("OrderImage");
        WndTestAssets.ComparePngs(orderImages!["OrderImage"], bluePng).Should().Be(0);
        WndTestAssets.ComparePngs(orderImages!["OrderImage"], redPng).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Acceptance Criterion 1E:
    /// The shared string table path (Data\English\Generals.csf) resolves to the expansion
    /// archive content inside one Zero Hour installation, so Zero Hour labels load instead
    /// of showing raw GUI: labels.
    /// </summary>
    [Fact]
    public void Criterion1E_StringTable_SamePathResolvesToZeroHour()
    {
        // Arrange: base and expansion archives share the string table path
        var zhRoot = Path.Combine(_tempRoot, "ZeroHourStrings");
        Directory.CreateDirectory(zhRoot);
        var baseCsf = Encoding.UTF8.GetBytes("generals-base-strings");
        var zhCsf = Encoding.UTF8.GetBytes("zerohour-expansion-strings");
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "English.big"),
            ("Data\\English\\Generals.csf", baseCsf));
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "EnglishZH.big"),
            ("Data\\English\\Generals.csf", zhCsf));

        var vfs = new SageVirtualFileSystem(
            zhRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);

        // Act
        var resolvedBytes = vfs.Read("Data\\English\\Generals.csf");

        // Assert
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().Equal(zhCsf);
    }

    /// <summary>
    /// Acceptance Criterion 1F:
    /// ArchiveRankWeight exceeds MaxTextureSizeScoreBonus so that a larger texture size hint in an earlier-mounted
    /// archive cannot invert archive precedence. The later-mounted archive definition wins even when the earlier
    /// archive provides a large texture (e.g. 2048x2048) and the later archive provides a small texture (e.g. 32x32).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Criterion1F_LaterMountedArchiveWins_EvenWhenEarlierArchiveHasLargerTexture()
    {
        // Arrange
        var zhRoot = Path.Combine(_tempRoot, "ZeroHourSizeWeight");
        Directory.CreateDirectory(zhRoot);
        var largeRedPng = WndTestAssets.CreateSolidPng(MagickColors.Red, 2048, 2048);
        var smallBluePng = WndTestAssets.CreateSolidPng(MagickColors.Blue, 32, 32);
        const string baseIniText = "MappedImage TiedImage\n  Texture = tiedbase.tga\n  Coords = Left:0 Top:0 Right:2048 Bottom:2048\nEnd\n";
        const string zhIniText = "MappedImage TiedImage\n  Texture = tiedzh.tga\n  Coords = Left:0 Top:0 Right:32 Bottom:32\nEnd\n";
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "A_Base.big"),
            ("Data\\INI\\MappedImages\\HandCreated\\Base.ini", Encoding.UTF8.GetBytes(baseIniText)),
            ("tiedbase.tga", largeRedPng));
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "Z_Expansion.big"),
            ("Data\\INI\\MappedImages\\HandCreated\\Zh.ini", Encoding.UTF8.GetBytes(zhIniText)),
            ("tiedzh.tga", smallBluePng));

        var service = new WndImageAssetService(Mock.Of<ILogger<WndImageAssetService>>());

        // Act
        var result = await service.GetImagesAsync(["TiedImage"], zhRoot, null, null, null, true, CancellationToken.None);

        // Assert: the expansion definition (small blue texture) wins over the base definition (large red texture)
        result.Success.Should().BeTrue();
        var tiedImages = result.Data;
        tiedImages.Should().NotBeNull().And.ContainKey("TiedImage");
        WndTestAssets.ComparePngs(tiedImages!["TiedImage"], smallBluePng).Should().Be(0);
        WndTestAssets.ComparePngs(tiedImages!["TiedImage"], largeRedPng).Should().BeGreaterThan(0);
    }
}
