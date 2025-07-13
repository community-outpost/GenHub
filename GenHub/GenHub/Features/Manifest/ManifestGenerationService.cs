using System;
using GenHub.Core;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenHub.Features.Manifest;

/// <summary>
/// High-level service for generating different types of content manifests.
/// </summary>
public class ManifestGenerationService(ILogger<ManifestGenerationService> logger, IServiceProvider serviceProvider) : IManifestGenerationService
{
    /// <summary>
    /// Static JSON serializer options for manifest serialization.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<ManifestGenerationService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc/>
    public async Task<IContentManifestBuilder> CreateBaseGameManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        string version)
    {
        _logger.LogInformation(
            "Creating base game manifest for {GameType} {InstallationType} v{Version}",
            gameType,
            installationType,
            version);

        var builder = CreateBuilder()
            .WithBasicInfo(
                $"{gameType}_{version}_{installationType}",
                $"{gameType} {version}",
                version)
            .WithContentType(ContentType.BaseGame, gameType)
            .WithPublisher(
                "EA Games",
                "https://www.ea.com",
                "https://help.ea.com",
                "support@ea.com")
            .WithMetadata($"Base game installation of {gameType} version {version} from {installationType}")
            .AddRequiredDirectories("Data", "Maps")
            .WithInstallationInstructions(WorkspaceStrategy.FullSymlink);

        // Add all game files
        await builder.AddFilesFromDirectoryAsync(gameInstallationPath, ManifestFileSourceType.LinkFromBase);

        return builder;
    }

    /// <inheritdoc/>
    public async Task<IContentManifestBuilder> CreateModManifestAsync(
        string modDirectory,
        string modId,
        string modName,
        string modVersion,
        GameType targetGame,
        params string[] baseGameDependencies)
    {
        _logger.LogInformation("Creating mod manifest for {ModName} v{ModVersion}", modName, modVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(modId, modName, modVersion)
            .WithContentType(ContentType.Mod, targetGame)
            .WithMetadata($"Total conversion mod: {modName}")
            .WithInstallationInstructions(WorkspaceStrategy.HybridSymlink);

        // Add dependencies on base game versions
        foreach (var baseGameId in baseGameDependencies)
        {
            builder.AddDependency(baseGameId, $"Base Game {baseGameId}", isRequired: true, dependencyType: ContentType.BaseGame);
        }

        // Add mod files with copy strategy (since they're unique)
        await builder.AddFilesFromDirectoryAsync(modDirectory, ManifestFileSourceType.CopyUnique);

        return builder;
    }

    /// <inheritdoc/>
    public async Task<IContentManifestBuilder> CreateAddonManifestAsync(
        string addonDirectory,
        string addonId,
        string addonName,
        string addonVersion,
        GameType targetGame)
    {
        _logger.LogInformation("Creating addon manifest for {AddonName} v{AddonVersion}", addonName, addonVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(addonId, addonName, addonVersion)
            .WithContentType(ContentType.Addon, targetGame)
            .WithMetadata($"Utility addon: {addonName}")
            .WithInstallationInstructions(WorkspaceStrategy.FullCopy);

        // Addons typically have executable files
        await builder.AddFilesFromDirectoryAsync(addonDirectory, ManifestFileSourceType.CopyUnique, "*", true);

        // Common addon directories
        builder.AddRequiredDirectories("Tools", "Utilities");

        return builder;
    }

    /// <inheritdoc/>
    public async Task<IContentManifestBuilder> CreatePatchManifestAsync(
        string patchDirectory,
        string patchId,
        string patchName,
        string patchVersion,
        string targetContent,
        string targetVersion)
    {
        _logger.LogInformation("Creating patch manifest for {PatchName} v{PatchVersion}", patchName, patchVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(patchId, patchName, patchVersion)
            .WithContentType(ContentType.Patch, GameType.Generals) // Patches can target any game
            .WithMetadata($"Patch for {targetContent}: {patchName}")
            .WithInstallationInstructions(WorkspaceStrategy.FullCopy);

        // Add dependency on the target content
        builder.AddDependency(targetContent, targetContent, targetVersion, targetVersion, true, ContentType.Mod);

        // Patches typically override specific files
        await builder.AddFilesFromDirectoryAsync(patchDirectory, ManifestFileSourceType.CopyUnique);

        return builder;
    }

    /// <inheritdoc/>
    public async Task<IContentManifestBuilder> CreateStandaloneGameManifestAsync(
        string gameDirectory,
        string gameId,
        string gameName,
        string gameVersion,
        string executablePath)
    {
        _logger.LogInformation("Creating standalone game manifest for {GameName} v{GameVersion}", gameName, gameVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(gameId, gameName, gameVersion)
            .WithContentType(ContentType.StandaloneVersion, GameType.Generals)
            .WithMetadata($"Standalone game version: {gameName}")
            .WithInstallationInstructions(WorkspaceStrategy.FullCopy);

        // Add all game files
        await builder.AddFilesFromDirectoryAsync(gameDirectory, ManifestFileSourceType.CopyUnique);

        // Mark the main executable
        await builder.AddFileAsync(executablePath, ManifestFileSourceType.CopyUnique, string.Empty, true);

        return builder;
    }

    /// <inheritdoc/>
    public async Task SaveManifestAsync(GameManifest manifest, string outputPath)
    {
        _logger.LogInformation("Saving manifest {ManifestId} to {OutputPath}", manifest.Id, outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);

        _logger.LogInformation("Manifest saved successfully to {OutputPath}", outputPath);
    }

    /// <summary>
    /// Creates a new manifest builder instance.
    /// </summary>
    /// <returns>The manifest builder.</returns>
    private IContentManifestBuilder CreateBuilder()
    {
        return (IContentManifestBuilder?)_serviceProvider.GetService(typeof(IContentManifestBuilder))
               ?? throw new InvalidOperationException("IContentManifestBuilder service not registered");
    }
}
