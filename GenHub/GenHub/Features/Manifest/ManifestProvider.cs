using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.Manifest;

/// <summary>
/// Provides ContentManifest instances by retrieving them from CAS, embedded resources,
/// or generating them dynamically.
/// </summary>
public class ManifestProvider : IManifestProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<ManifestProvider> _logger;
    private readonly IContentManifestPool _manifestPool;
    private readonly IManifestIdService _manifestIdService;
    private readonly ManifestProviderOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestProvider"/> class using an <see cref="IContentManifestPool"/>.
    /// </summary>
    /// <param name="logger">Logger instance used for diagnostic messages.</param>
    /// <param name="manifestPool">Pool used to store and retrieve manifests.</param>
    /// <param name="manifestIdService">The manifest ID service.</param>
    /// <param name="options">Optional provider behaviour options.</param>
    public ManifestProvider(ILogger<ManifestProvider> logger, IContentManifestPool manifestPool, IManifestIdService? manifestIdService = null, ManifestProviderOptions? options = null)
    {
        _logger = logger ?? NullLogger<ManifestProvider>.Instance;
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _manifestIdService = manifestIdService ?? throw new ArgumentNullException(nameof(manifestIdService));
        _options = options ?? new ManifestProviderOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestProvider"/> class by adapting an <see cref="IManifestCache"/> to an <see cref="IContentManifestPool"/>.
    /// </summary>
    /// <param name="logger">Logger instance used for diagnostic messages.</param>
    /// <param name="cache">Legacy manifest cache instance to adapt.</param>
    /// <param name="manifestIdService">The manifest ID service.</param>
    /// <param name="options">Optional provider behaviour options.</param>
    public ManifestProvider(ILogger<ManifestProvider> logger, IManifestCache cache, IManifestIdService? manifestIdService = null, ManifestProviderOptions? options = null)
        : this(logger, new ManifestCacheAdapter(cache), manifestIdService, options)
    {
    }

    /// <summary>
    /// Gets or generates a manifest for the specified <see cref="GameVersion"/>.
    /// </summary>
    /// <param name="gameVersion">The game version to locate a manifest for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The manifest if found or generated; otherwise <c>null</c>.</returns>
    public async Task<ContentManifest?> GetManifestAsync(GameVersion gameVersion, CancellationToken cancellationToken = default)
    {
        // 1. Try CAS first (only if gameVersion.Id is a valid manifest id)
        try
        {
            var tryId = ManifestId.Create(gameVersion.Id);
            var casResult = await _manifestPool.GetManifestAsync(tryId, cancellationToken);
            if (casResult.Success && casResult.Data != null)
            {
                // Validate cached manifest security and ensure the manifest id matches the requested id
                ValidateCachedManifest(casResult.Data, gameVersion.Id);
                return casResult.Data;
            }
        }
        catch (ArgumentException)
        {
            // Not a valid manifest id - skip CAS lookup for this id
        }

        // 2. Try embedded resources
        var manifestName = $"GenHub.Manifests.{gameVersion.Id}.json";
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
                    if (!string.Equals(manifest.Id.Value, gameVersion.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ManifestValidationException(gameVersion.Id, $"Manifest ID mismatch: expected '{gameVersion.Id}' but manifest contains '{manifest.Id.Value}'");
                    }

                    // Determine a sensible source directory for embedded manifests when possible.
                    // For embedded gameVersion manifests we prefer the working directory or executable's directory.
                    string? embeddedSourceDir = null;
                    try
                    {
                        embeddedSourceDir = !string.IsNullOrEmpty(gameVersion.WorkingDirectory)
                            ? gameVersion.WorkingDirectory
                            : (!string.IsNullOrEmpty(gameVersion.ExecutablePath) ? Path.GetDirectoryName(gameVersion.ExecutablePath) : null);
                    }
                    catch
                    {
                        embeddedSourceDir = null;
                    }

                    var addResult = await _manifestPool.AddManifestAsync(manifest, embeddedSourceDir ?? string.Empty, cancellationToken);
                    if (addResult?.Success == true)
                    {
                        return manifest;
                    }

                    _logger.LogWarning("Embedded manifest {Id} parsed but failed to add to pool: {Errors}", manifest.Id, addResult?.AllErrors ?? "unknown");

                    return manifest;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse embedded manifest {ManifestName}", manifestName);
                throw new ManifestValidationException(gameVersion.Id, $"JSON parsing failed: {ex.Message}", ex);
            }
        }

        // 3. Generate fallback manifest (optional)
        if (_options.GenerateFallbackManifests)
        {
            _logger.LogInformation("Generating fallback manifest for GameVersion {Id}", gameVersion.Id);

            var builder = new ContentManifestBuilder(Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentManifestBuilder>.Instance, null!, _manifestIdService);
            var gameVersionInt = int.TryParse(gameVersion.Version, out var parsedVersion) ? parsedVersion : 0;
            var generated = builder
                .WithBasicInfo("EA Games", gameVersion.Name ?? "Unknown", gameVersionInt)
                .WithContentType(ContentType.GameClient, gameVersion.GameType)
                .WithPublisher("EA Games", "https://www.ea.com")
                .WithMetadata($"Generated manifest for {gameVersion.Name}")
                .AddFile(new ManifestFile
                {
                    RelativePath = Path.GetFileName(gameVersion.ExecutablePath),
                    SourceType = ContentSourceType.GameInstallation,
                    IsExecutable = true,
                    IsRequired = true,
                })
                .AddRequiredDirectories("Data", "Maps")
                .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink)
                .Build();

            // Validate ID before adding to pool
            ManifestIdValidator.EnsureValid(generated.Id.Value);

            // Determine a sensible source directory for the generated manifest.
            // Prefer the working directory if present, otherwise fall back to the directory
            // containing the configured executable path.
            string? gameDir = null;
            try
            {
                gameDir = !string.IsNullOrEmpty(gameVersion.WorkingDirectory)
                    ? gameVersion.WorkingDirectory
                    : (!string.IsNullOrEmpty(gameVersion.ExecutablePath) ? Path.GetDirectoryName(gameVersion.ExecutablePath) : null);
            }
            catch
            {
                gameDir = null;
            }

            var addRes = await _manifestPool.AddManifestAsync(generated, gameDir ?? string.Empty, cancellationToken);
            if (addRes?.Success != true)
                _logger.LogWarning("Failed to add generated manifest {Id} to pool: {Errors}", generated.Id, addRes?.AllErrors ?? "unknown");

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
        var tempInstallForId = new GameInstallation(string.Empty, installation.InstallationType);
        tempInstallForId.HasZeroHour = installation.HasZeroHour;
        var versionForId = installation.AvailableVersions?.Count > 0 ? installation.AvailableVersions[0].Version : "1.0";
        var gameType = installation.HasZeroHour ? GameType.ZeroHour : GameType.Generals;
        var userVersion = int.TryParse(versionForId, out var parsedVersion) ? parsedVersion : 0;
        var deterministicId = ManifestIdGenerator.GenerateGameInstallationId(tempInstallForId, gameType, userVersion);

        // Try CAS using deterministic id
        var casResult = await _manifestPool.GetManifestAsync(ManifestId.Create(deterministicId), cancellationToken);
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
                    var addRes = await _manifestPool.AddManifestAsync(manifest, installation.InstallationPath ?? string.Empty, cancellationToken);
                    if (addRes?.Success != true)
                        _logger.LogWarning("Failed to add embedded installation manifest {Id} to pool: {Errors}", manifest.Id, addRes?.AllErrors ?? "unknown");
                    return manifest;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse embedded manifest {ManifestName}", manifestName);
                throw new ManifestValidationException(deterministicId, $"JSON parsing failed: {ex.Message}", ex);
            }
        }

        // 3. Generate fallback (optional)
        if (_options.GenerateFallbackManifests)
        {
            _logger.LogInformation("Generating fallback manifest for installation {Id}", installation.Id);

            var builder = new ContentManifestBuilder(Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentManifestBuilder>.Instance, null!, _manifestIdService);
            var generated = builder
                .WithBasicInfo(installation.InstallationType, installation.HasZeroHour ? GameType.ZeroHour : GameType.Generals, userVersion)
            .WithContentType(ContentType.GameInstallation, installation.HasZeroHour ? GameType.ZeroHour : GameType.Generals)
            .WithPublisher(installation.InstallationType.ToString(), string.Empty)
            .WithMetadata($"Generated manifest for installation at {installation.InstallationPath}")
            .AddRequiredDirectories("Data", "Maps")
            .WithInstallationInstructions(WorkspaceStrategy.FullSymlink)
            .Build();

            // Validate ID before adding to pool
            ManifestIdValidator.EnsureValid(generated.Id);
            var addRes2 = await _manifestPool.AddManifestAsync(generated, installation.InstallationPath ?? string.Empty, cancellationToken);
            if (addRes2?.Success != true)
                _logger.LogWarning("Failed to add generated installation manifest {Id} to pool: {Errors}", generated.Id, addRes2?.AllErrors ?? "unknown");
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

    // Adapter to bridge the legacy IManifestCache interface to the newer IContentManifestPool.
    private sealed class ManifestCacheAdapter : IContentManifestPool
    {
        private readonly IManifestCache _cache;

        public ManifestCacheAdapter(IManifestCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
        {
            _cache.AddOrUpdateManifest(manifest);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }

        public Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default)
        {
            // Legacy cache does not persist source directories, so we just add the manifest
            _cache.AddOrUpdateManifest(manifest);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }

        public Task<OperationResult<ContentManifest?>> GetManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        {
            var m = _cache.GetManifest(manifestId.Value);
            return Task.FromResult(OperationResult<ContentManifest?>.CreateSuccess(m));
        }

        public Task<OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>> GetAllManifestsAsync(CancellationToken cancellationToken = default)
        {
            var all = _cache.GetAllManifests();
            return Task.FromResult(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess(all));
        }

        public Task<OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>> SearchManifestsAsync(Core.Models.Content.ContentSearchQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess(Array.Empty<ContentManifest>()));
        }

        public Task<OperationResult<bool>> RemoveManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        {
            // No-op for legacy cache - just return success
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }

        public Task<OperationResult<bool>> IsManifestAcquiredAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        {
            var m = _cache.GetManifest(manifestId.Value);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(m != null));
        }

        public Task<OperationResult<string?>> GetContentDirectoryAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<string?>.CreateSuccess(null));
        }
    }
}

/// <summary>
/// Options to control <see cref="ManifestProvider"/> behaviour.
/// </summary>
public sealed class ManifestProviderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether fallback manifests should be generated when none are found in CAS or embedded resources.
    /// </summary>
    public bool GenerateFallbackManifests { get; set; } = false;
}
