using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for <see cref="ProfileVerificationFileSetService"/>.
/// </summary>
public sealed class ProfileVerificationFileSetServiceTests : IDisposable
{
    private const string InstallManifestId = "1.104.test.gameinstallation.zerohour";
    private const string ClientManifestId = "1.104.test.gameclient.zerohour";
    private const string ModManifestId = "1.0.test.mod.supermod";
    private const string AddonManifestId = "1.0.test.addon.hotkeys";
    private const string ValidHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly string _tempRoot;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<ICasService> _casMock;
    private readonly ProfileVerificationFileSetService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileVerificationFileSetServiceTests"/> class.
    /// </summary>
    public ProfileVerificationFileSetServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_VerifySetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("not found"));
        _casMock = new Mock<ICasService>();
        _service = new ProfileVerificationFileSetService(_manifestPoolMock.Object, _casMock.Object);
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
    /// Verifies that installation and client manifest files form the allowed base set.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithBaseManifests_ReturnsAllowedBasePaths()
    {
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash),
            ("Data/Scripts/Scripts.ini", null, ValidHash)));
        RegisterManifest(CreateManifest(
            ClientManifestId,
            ContentType.GameClient,
            ("game.dat", null, ValidHash)));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ClientManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.NotNull(result.AllowedBaseRelativePaths);
        Assert.Equal(3, result.AllowedBaseRelativePaths.Count);
        Assert.Contains("INIZH.big", result.AllowedBaseRelativePaths);
        Assert.True(result.IsComplete);
        Assert.Empty(result.OverlayModPaths);
    }

    /// <summary>
    /// Verifies that unavailable base manifests fail closed instead of degrading to an unfiltered scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithMissingBaseManifests_MarksIncomplete()
    {
        var profile = CreateProfile("1.0.test.gameclient.unknown", "1.0.test.gameinstallation.unknown");

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.Null(result.AllowedBaseRelativePaths);
        Assert.False(result.IsComplete);
    }

    /// <summary>
    /// Verifies that a missing enabled manifest marks the set incomplete even when the client manifest resolves.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithMissingEnabledManifest_MarksIncomplete()
    {
        RegisterManifest(CreateManifest(
            ClientManifestId,
            ContentType.GameClient,
            ("game.dat", null, ValidHash)));
        var profile = CreateProfile(ClientManifestId, "1.0.test.mod.unknown");

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.False(result.IsComplete);
    }

    /// <summary>
    /// Verifies that an unpooled client ID stays best-effort even when it appears in the
    /// enabled IDs (profile creation always inserts it there), keeping the unfiltered scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithUnpooledClientIdInEnabledIds_StaysComplete()
    {
        const string unpooledClientId = "1.20260821.thesuperhackers.gameclient.zerohour";
        var profile = CreateProfile(unpooledClientId, unpooledClientId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.Null(result.AllowedBaseRelativePaths);
        Assert.True(result.IsComplete);
    }

    /// <summary>
    /// Verifies that loose INI files from enabled overlay manifests join the allowed set so
    /// workspace-materialized rules participate in the scoped CRC instead of verifying as retail.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithOverlayLooseIni_IncludesItInAllowedBasePaths()
    {
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash)));
        RegisterManifest(CreateManifest(
            ModManifestId,
            ContentType.Mod,
            ("Data/INI/ExtraMod.ini", null, ValidHash)));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ModManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.NotNull(result.AllowedBaseRelativePaths);
        Assert.Contains("INIZH.big", result.AllowedBaseRelativePaths);
        Assert.Contains("Data/INI/ExtraMod.ini", result.AllowedBaseRelativePaths);
        Assert.True(result.IsComplete);
    }

    /// <summary>
    /// Verifies that an overlay whose variants match nothing on this host marks the set incomplete.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithUnmatchedVariantOverlay_MarksIncomplete()
    {
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash)));
        var modManifest = CreateManifest(
            ModManifestId,
            ContentType.Mod,
            ("ModFiles.big", null, ValidHash));
        modManifest.Variants.Add(new ArtifactVariant
        {
            RuntimeIdentifiers = ["nonexistent-rid"],
            Files =
            [
                new ManifestFile { RelativePath = "ModFiles.big", Hash = ValidHash },
            ],
        });
        RegisterManifest(modManifest);
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ModManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.False(result.IsComplete);
    }

    /// <summary>
    /// Verifies that base paths come from the resolved variant rather than the flat file list.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithVariantManifest_UsesResolvedVariantFiles()
    {
        var installManifest = CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("IgnoredFlat.txt", null, ValidHash));
        installManifest.Variants.Add(new ArtifactVariant
        {
            Files =
            [
                new ManifestFile { RelativePath = "INIZH.big", Hash = ValidHash },
            ],
        });
        RegisterManifest(installManifest);
        RegisterManifest(CreateManifest(
            ClientManifestId,
            ContentType.GameClient,
            ("game.dat", null, ValidHash)));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ClientManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.NotNull(result.AllowedBaseRelativePaths);
        Assert.Contains("INIZH.big", result.AllowedBaseRelativePaths);
        Assert.DoesNotContain("IgnoredFlat.txt", result.AllowedBaseRelativePaths);
        Assert.True(result.IsComplete);
    }

    /// <summary>
    /// Verifies that overlay archives resolve through absolute source paths and CAS hashes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithOverlays_ResolvesLocalArchives()
    {
        var localBig = Path.Combine(_tempRoot, "LocalMod.big");
        await File.WriteAllTextAsync(localBig, "fake big");
        var casBig = Path.Combine(_tempRoot, "casblob");
        await File.WriteAllTextAsync(casBig, "fake cas big");
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash)));
        RegisterManifest(CreateManifest(
            ModManifestId,
            ContentType.Mod,
            ("ModFiles.big", localBig, ValidHash),
            ("CasMod.big", null, ValidHash)));
        _casMock
            .Setup(c => c.GetContentPathAsync(ValidHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(casBig));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ModManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.OverlayModPaths.Count);
        Assert.Contains(localBig, result.OverlayModPaths);
        Assert.Contains(casBig, result.OverlayModPaths);
    }

    /// <summary>
    /// Verifies that unresolvable overlay archives mark the set incomplete.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithMissingOverlay_MarksIncomplete()
    {
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash)));
        RegisterManifest(CreateManifest(
            ModManifestId,
            ContentType.Mod,
            ("Missing.big", null, ValidHash)));
        _casMock
            .Setup(c => c.GetContentPathAsync(ValidHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not found"));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ModManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.False(result.IsComplete);
        Assert.Empty(result.OverlayModPaths);
    }

    /// <summary>
    /// Verifies that non-archive overlay files are skipped without affecting completeness.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithNonArchiveOverlay_SkipsWithoutIncomplete()
    {
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash)));
        RegisterManifest(CreateManifest(
            AddonManifestId,
            ContentType.Addon,
            ("readme.txt", null, ValidHash),
            ("extra.big.BAK", null, ValidHash)));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, AddonManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.True(result.IsComplete);
        Assert.Empty(result.OverlayModPaths);
    }

    /// <summary>
    /// Verifies that overlays mount in ascending content priority so higher-priority mods win ties.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetVerificationFileSetAsync_WithMultipleOverlays_OrdersByAscendingPriority()
    {
        var addonBig = Path.Combine(_tempRoot, "AddonMod.big");
        await File.WriteAllTextAsync(addonBig, "fake addon big");
        var modBig = Path.Combine(_tempRoot, "RealMod.big");
        await File.WriteAllTextAsync(modBig, "fake mod big");
        RegisterManifest(CreateManifest(
            InstallManifestId,
            ContentType.GameInstallation,
            ("INIZH.big", null, ValidHash)));
        RegisterManifest(CreateManifest(
            AddonManifestId,
            ContentType.Addon,
            ("AddonMod.big", addonBig, ValidHash)));
        RegisterManifest(CreateManifest(
            ModManifestId,
            ContentType.Mod,
            ("RealMod.big", modBig, ValidHash)));
        var profile = CreateProfile(ClientManifestId, InstallManifestId, ModManifestId, AddonManifestId);

        var result = await _service.GetVerificationFileSetAsync(profile);

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { addonBig, modBig }, result.OverlayModPaths);
    }

    private static GameProfile CreateProfile(string clientId, params string[] enabledIds)
    {
        return new GameProfile
        {
            Id = "test-profile",
            Name = "Test Profile",
            GameClient = new GameClient
            {
                Id = clientId,
                Name = "Test Client",
                PublisherType = "test",
                Version = "1.04",
                GameType = GameType.ZeroHour,
            },
            EnabledContentIds = enabledIds.ToList(),
        };
    }

    private static ContentManifest CreateManifest(string id, ContentType contentType, params (string RelativePath, string? SourcePath, string Hash)[] files)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = id,
            ContentType = contentType,
            Files = files.Select(f => new ManifestFile
            {
                RelativePath = f.RelativePath,
                SourcePath = f.SourcePath,
                Hash = f.Hash,
            }).ToList(),
        };
    }

    private void RegisterManifest(ContentManifest manifest)
    {
        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.Is<ManifestId>(m => m.Value == manifest.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));
    }
}
