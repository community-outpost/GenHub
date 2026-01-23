using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Extensions.GameInstallations;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.Manifest;

/// <summary>
/// Provides ContentManifest instances by retrieving them from CAS, embedded resources,
/// or generating them dynamically.
/// </summary>
public class ManifestProvider(ILogger<ManifestProvider> logger, IContentManifestPool manifestPool, IManifestIdService? manifestIdService = null, IContentManifestBuilder? manifestBuilder = null, ManifestProviderOptions? options = null) : IManifestProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<ManifestProvider> logger = logger ?? NullLogger<ManifestProvider>.Instance;
    private readonly IContentManifestPool manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly IManifestIdService manifestIdService = manifestIdService ?? throw new ArgumentNullException(nameof(manifestIdService));
    private readonly IContentManifestBuilder manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
    private readonly ManifestProviderOptions options = options ?? new ManifestProviderOptions();

    /// <summary>
    /// Gets or generates a manifest for the specified <see cref="GameClient"/>.
    /// </summary>
    /// <param name="gameClient">The game client to locate a manifest for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The manifest if found or generated; otherwise <c>null</c>.</returns>
    public async Task<ContentManifest?> GetManifestAsync(GameClient gameClient, CancellationToken cancellationToken = default)
    {
        // 1. Try CAS first (only if gameClient.Id is a valid manifest id)
        try
        {
            var tryId = ManifestId.Create(gameClient.Id);
            var casResult = await manifestPool.GetManifestAsync(tryId, cancellationToken);
            if (casResult.Success && casResult.Data != null)
            {
                // Validate cached manifest security and ensure the manifest id matches the requested id
                ValidateCachedManifest(casResult.Data, gameClient.Id);
                return casResult.Data;
            }
        }
        catch (ArgumentException)
        {
            // Not a valid manifest id - skip CAS lookup for this id
        }

        // 2. Try embedded resources
        var manifestName = $"GenHub.Manifests.{gameClient.Id}.json";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(manifestName);
        if (stream != null)
        {
            try
            {
                var manifest = await JsonSerializer.DeserializeAsync<ContentManifest>(stream, _jsonOptions, cancellationToken);
                if (manifest != null)
                {
                    // Validate security of parsed manifest
                    ValidateManifestSecurity(manifest);

                    // Ensure manifest ID matches the requested id
                    if (!string.Equals(manifest.Id.Value, gameClient.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ManifestValidationException(gameClient.Id, $"Manifest ID mismatch: expected '{gameClient.Id}' but manifest contains '{manifest.Id.Value}'");
                    }

                    // Determine a sensible source directory for embedded manifests when possible.
                    // For embedded gameClient manifests we prefer the working directory or executable's directory.
                    string? embeddedSourceDir = null;
                    try
                    {
                        embeddedSourceDir = !string.IsNullOrEmpty(gameClient.WorkingDirectory)
                            ? gameClient.WorkingDirectory
                            : (!string.IsNullOrEmpty(gameClient.ExecutablePath) ? Path.GetDirectoryName(gameClient.ExecutablePath) : null);
                    }
                    catch
                    {
                        embeddedSourceDir = null;
                    }

                    var addResult = await manifestPool.AddManifestAsync(manifest, embeddedSourceDir ?? string.Empty, null, cancellationToken);
                    if (addResult?.Success == true)
                    {
                        return manifest;
                    }

                    logger.LogWarning("Embedded manifest {Id} parsed but failed to add to pool: {Errors}", manifest.Id, string.Join(", ", addResult?.Errors ?? []));

                    return manifest;
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to parse embedded manifest {ManifestName}", manifestName);
                throw new ManifestValidationException(gameClient.Id, $"JSON parsing failed: {ex.Message}", ex);
            }
        }

        // 3. Generate fallback manifest (optional)
        if (options.GenerateFallbackManifests)
        {
            logger.LogInformation("Generating fallback manifest for GameClient {Id}", gameClient.Id);

            var gameVersionInt = int.TryParse(gameClient.Version, out var parsedVersion) ? parsedVersion : 0;
            var generated = manifestBuilder
                .WithBasicInfo("EA Games", gameClient.Name ?? GameClientConstants.UnknownVersion, gameVersionInt)
                .WithContentType(ContentType.GameClient, gameClient.GameType)
                .WithPublisher("EA Games", "https://www.ea.com")
                .WithMetadata($"Generated manifest for {gameClient.Name}")
                .AddFile(new ManifestFile
                {
                    RelativePath = Path.GetFileName(gameClient.ExecutablePath),
                    SourceType = ContentSourceType.GameInstallation,
                    IsExecutable = true,
                    IsRequired = true,
                })
                .AddRequiredDirectories("Data", "Maps")
                .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy)
                .Build();

            // Validate ID before adding to pool
            ManifestIdValidator.EnsureValid(generated.Id.Value);

            // Determine a sensible source directory for the generated manifest.
            // Prefer the working directory if present, otherwise fall back to the directory
            // containing the configured executable path.
            string? gameDir;
            try
            {
                gameDir = !string.IsNullOrEmpty(gameClient.WorkingDirectory)
                    ? gameClient.WorkingDirectory
                    : (!string.IsNullOrEmpty(gameClient.ExecutablePath) ? Path.GetDirectoryName(gameClient.ExecutablePath) : null);
            }
            catch
            {
                gameDir = null;
            }

            var addRes = await manifestPool.AddManifestAsync(generated, gameDir ?? string.Empty, null, cancellationToken);
            if (addRes?.Success != true)
            {
                logger.LogWarning("Failed to add generated manifest {Id} to pool: {Errors}", generated.Id, string.Join(", ", addRes?.Errors ?? []));
            }

            return generated;
        }

        return null;
    }

    /// <summary>
    /// Gets or generates a manifest for a <see cref="GameInstallation"/>.
    /// </summary>
    /// <param name="installation">The installation to get a manifest for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The manifest if found or generated; otherwise null.</returns>
    public async Task<ContentManifest?> GetManifestAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        // Prefer a deterministic manifest id for installations so tests and embedded resources can
        // reference stable ids instead of runtime GUIDs. Generate using ManifestIdGenerator.
        var tempInstallForId = new GameInstallation(installation.InstallationPath, installation.InstallationType, null);

        var gameType = installation.HasZeroHour ? GameType.ZeroHour : GameType.Generals;

        // Use appropriate manifest version for generated installation manifests
        var manifestVersion = gameType == GameType.ZeroHour
            ? ManifestConstants.ZeroHourManifestVersion
            : ManifestConstants.GeneralsManifestVersion;

        var deterministicId = ManifestIdGenerator.GenerateGameInstallationId(tempInstallForId, gameType, manifestVersion);

        // Try CAS using deterministic id
        var casResult = await manifestPool.GetManifestAsync(ManifestId.Create(deterministicId), cancellationToken);
        if (casResult.Success && casResult.Data != null)
        {
            // Validate cached manifest; expect the deterministic id
            ValidateCachedManifest(casResult.Data, deterministicId);
            return casResult.Data;
        }

        // 2. Try embedded (deterministic id)
        var manifestName = $"GenHub.Manifests.{deterministicId}.json";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(manifestName);
        if (stream != null)
        {
            try
            {
                var manifest = await JsonSerializer.DeserializeAsync<ContentManifest>(stream, _jsonOptions, cancellationToken);
                if (manifest != null)
                {
                    // For embedded installation manifests, provide the installation path as source when available.
                    var addRes = await manifestPool.AddManifestAsync(manifest, installation.InstallationPath ?? string.Empty, null, cancellationToken);
                    if (addRes?.Success != true)
                        logger.LogWarning("Failed to add embedded installation manifest {Id} to pool: {Errors}", manifest.Id, string.Join(", ", addRes?.Errors ?? []));
                    return manifest;
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to parse embedded manifest {ManifestName}", manifestName);
                throw new ManifestValidationException(deterministicId, $"JSON parsing failed: {ex.Message}", ex);
            }
        }

        // 3. Generate fallback (optional)
        if (options.GenerateFallbackManifests)
        {
            logger.LogInformation("Generating fallback manifest for installation {Id}", installation.Id);

            // Determine the correct source path based on the game type
            var manifestGameType = installation.HasZeroHour ? GameType.ZeroHour : GameType.Generals;
            var sourcePath = manifestGameType == GameType.ZeroHour
                ? (!string.IsNullOrEmpty(installation.ZeroHourPath) ? installation.ZeroHourPath : installation.InstallationPath)
                : (!string.IsNullOrEmpty(installation.GeneralsPath) ? installation.GeneralsPath : installation.InstallationPath);

            var publisherName = installation.InstallationType.GetDisplayName();

            var builder = manifestBuilder
                .WithBasicInfo(installation.InstallationType, manifestGameType, manifestVersion)
                .WithContentType(ContentType.GameInstallation, manifestGameType)
                .WithPublisher(publisherName, string.Empty)
                .WithMetadata($"Generated manifest for {manifestGameType} at {sourcePath}")
                .AddRequiredDirectories("Data", "Maps")
                .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy);

            // Currently, AddFilesFromDirectoryAsync will skip hash computation for ContentSourceType.GameInstallation
            // to dramatically improve scan performance. This is acceptable because:
            // 1. Future implementation will use CSV-based authority from GitHub
            // 2. CSV will contain file lists specific to EA/Steam installation types and languages
            // 3. Users don't modify game installation files, so integrity checking via hashes is unnecessary
            // 4. Hash computation for thousands of files takes significant time during game scanning
            //
            // The CSV authority system will be implemented in a future PR and will:
            // - Download CSV from GitHub based on installation type (EA/Steam), language, and version
            // - Generate manifest directly from CSV without filesystem scanning
            // - Only scan filesystem to verify installation completeness
            //
            // For now: Manifest files will have Hash=null for GameInstallation source type
            if (!string.IsNullOrEmpty(sourcePath) && Directory.Exists(sourcePath))
            {
                await builder.AddFilesFromDirectoryAsync(sourcePath, ContentSourceType.GameInstallation);
            }

            var generated = builder.Build();

            // Validate ID before adding to pool
            ManifestIdValidator.EnsureValid(generated.Id.Value);
            var addRes2 = await manifestPool.AddManifestAsync(generated, sourcePath ?? string.Empty, null, cancellationToken);
            if (addRes2?.Success != true)
            {
                logger.LogWarning("Failed to add generated installation manifest {Id} to pool: {Errors}", generated.Id, string.Join(", ", addRes2?.Errors ?? []));
            }

            return generated;
        }

        // If fallback generation is disabled (e.g., tests), return null to preserve legacy behavior
        return null;
    }

    private static void ValidateManifestSecurity(ContentManifest manifest)
    {
        // Ensure no file entries contain path traversal patterns
        if (manifest.Files != null)
        {
            foreach (var f in manifest.Files)
            {
                if (!string.IsNullOrEmpty(f.RelativePath) && (f.RelativePath.Contains("..") || f.RelativePath.Contains("/../") || f.RelativePath.Contains("\\..\\")))
                {
                    throw new ManifestSecurityException(manifest.Id.Value, $"Path traversal detected in file '{f.RelativePath}'");
                }
            }
        }
    }

    private static void ValidateCachedManifest(ContentManifest manifest, string expectedId)
    {
        // Run the same security validations as for embedded manifests
        ValidateManifestSecurity(manifest);

        if (!string.Equals(manifest.Id.Value, expectedId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ManifestValidationException(expectedId, $"Manifest ID mismatch: expected '{expectedId}' but manifest contains '{manifest.Id.Value}'");
        }
    }
}
