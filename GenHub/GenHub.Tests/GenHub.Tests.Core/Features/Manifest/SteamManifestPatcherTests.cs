using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Tests for <see cref="SteamManifestPatcher"/> launch-mode patching.
/// </summary>
public class SteamManifestPatcherTests : IDisposable
{
    private readonly string _manifestsDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamManifestPatcherTests"/> class.
    /// </summary>
    public SteamManifestPatcherTests()
    {
        _manifestsDirectory = Path.Combine(Path.GetTempPath(), $"genhub-patcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_manifestsDirectory);
    }

    /// <summary>
    /// Steam mode launches through the stub, so the patcher declares the stub-to-engine
    /// relationship alongside the entry switch.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PatchManifestAsync_SteamModeWithGameDat_DeclaresEngineRelationshipAsync()
    {
        const string manifestId = "1.104.steam.gameclient.zerohour";
        WriteManifest(manifestId, withGameDat: true);

        await PatchAsync(manifestId, useSteamLaunch: true);

        var patched = ReadManifest(manifestId);
        Assert.NotNull(patched.LaunchRelationship);
        Assert.Equal(GameClientConstants.GameProcessName, patched.LaunchRelationship!.ProcessName);
    }

    /// <summary>
    /// Standalone mode launches the engine directly, so any stub relationship is cleared.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PatchManifestAsync_StandaloneMode_ClearsRelationshipAsync()
    {
        const string manifestId = "1.104.steam.gameclient.zerohour";
        WriteManifest(manifestId, withGameDat: true);

        await PatchAsync(manifestId, useSteamLaunch: true);
        await PatchAsync(manifestId, useSteamLaunch: false);

        var patched = ReadManifest(manifestId);
        Assert.Null(patched.LaunchRelationship);
    }

    /// <summary>
    /// A lone stub with no engine beside it is a direct launch: no relationship is declared.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PatchManifestAsync_SteamModeWithoutGameDat_DeclaresNoRelationshipAsync()
    {
        const string manifestId = "1.104.steam.gameclient.zerohour";
        WriteManifest(manifestId, withGameDat: false);

        await PatchAsync(manifestId, useSteamLaunch: true);

        var patched = ReadManifest(manifestId);
        Assert.Null(patched.LaunchRelationship);
    }

    /// <summary>
    /// For a variant manifest, Steam mode declares the relationship on the host variant,
    /// which is where the launcher reads it, and leaves the root and foreign variant alone.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PatchManifestAsync_SteamModeVariantManifest_DeclaresRelationshipOnHostVariantAsync()
    {
        const string manifestId = "1.104.steam.gameclient.zerohour";
        WriteVariantManifest(manifestId, hostRelationship: null);

        await PatchAsync(manifestId, useSteamLaunch: true);

        var patched = ReadManifest(manifestId);
        Assert.Null(patched.LaunchRelationship);
        Assert.Null(patched.Variants[0].LaunchRelationship);
        Assert.Equal(GameClientConstants.GameProcessName, patched.Variants[1].LaunchRelationship?.ProcessName);
        Assert.Equal(GameClientConstants.GameProcessName, ManifestVariantResolver.ResolveLaunchRelationship(patched)?.ProcessName);
    }

    /// <summary>
    /// For a variant manifest, standalone mode clears the host variant's relationship and
    /// any stale root relationship left by earlier patching.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PatchManifestAsync_StandaloneModeVariantManifest_ClearsHostVariantRelationshipAsync()
    {
        const string manifestId = "1.104.steam.gameclient.zerohour";
        WriteVariantManifest(
            manifestId,
            new LaunchRelationship { ProcessName = GameClientConstants.GameProcessName },
            new LaunchRelationship { ProcessName = GameClientConstants.GameProcessName });

        await PatchAsync(manifestId, useSteamLaunch: false);

        var patched = ReadManifest(manifestId);
        Assert.Null(patched.LaunchRelationship);
        Assert.Null(patched.Variants[1].LaunchRelationship);
        Assert.Null(ManifestVariantResolver.ResolveLaunchRelationship(patched));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_manifestsDirectory))
        {
            Directory.Delete(_manifestsDirectory, recursive: true);
        }
    }

    private static ContentManifest CreateManifest(string manifestId, bool withGameDat)
    {
        var files = new List<ManifestFile>
        {
            new() { RelativePath = GameClientConstants.GeneralsExecutable },
        };
        if (withGameDat)
        {
            files.Add(new ManifestFile { RelativePath = GameClientConstants.SteamGameDatExecutable });
        }

        return new ContentManifest
        {
            Id = manifestId,
            Name = "Zero Hour",
            Version = "1.04",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Files = files,
        };
    }

    private void WriteManifest(string manifestId, bool withGameDat)
    {
        var json = JsonSerializer.Serialize(CreateManifest(manifestId, withGameDat));
        File.WriteAllText(Path.Combine(_manifestsDirectory, $"{manifestId}.manifest.json"), json);
    }

    private void WriteVariantManifest(string manifestId, LaunchRelationship? hostRelationship, LaunchRelationship? rootRelationship = null)
    {
        var manifest = VariantManifestFixture.Create(
            [
                new() { RelativePath = GameClientConstants.GeneralsExecutable },
                new() { RelativePath = GameClientConstants.SteamGameDatExecutable },
            ],
            [new() { RelativePath = GameClientConstants.GeneralsExecutable }]);
        manifest.Id = manifestId;
        manifest.Variants[1].LaunchRelationship = hostRelationship;
        manifest.LaunchRelationship = rootRelationship;
        var json = JsonSerializer.Serialize(manifest);
        File.WriteAllText(Path.Combine(_manifestsDirectory, $"{manifestId}.manifest.json"), json);
    }

    private ContentManifest ReadManifest(string manifestId)
    {
        var json = File.ReadAllText(Path.Combine(_manifestsDirectory, $"{manifestId}.manifest.json"));
        return JsonSerializer.Deserialize<ContentManifest>(json)!;
    }

    private async Task PatchAsync(string manifestId, bool useSteamLaunch)
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(x => x.GetManifestsPath()).Returns(_manifestsDirectory);
        var patcher = new SteamManifestPatcher(
            NullLogger<SteamManifestPatcher>.Instance,
            configuration.Object);

        await patcher.PatchManifestAsync(manifestId, useSteamLaunch);
    }
}
