using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
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
