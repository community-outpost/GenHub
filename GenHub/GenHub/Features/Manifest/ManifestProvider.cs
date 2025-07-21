using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Provides GameManifest instances by retrieving them from cache or loading from embedded resources, with comprehensive error handling and security validation.
/// </summary>
public class ManifestProvider(ILogger<ManifestProvider> logger, IManifestCache manifestCache) : IManifestProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<ManifestProvider> _logger = logger;
    private readonly IManifestCache _manifestCache = manifestCache;

    /// <inheritdoc/>
    public async Task<GameManifest?> GetManifestAsync(GameVersion gameVersion, CancellationToken cancellationToken = default)
    {
        // First check cache
        var cachedManifest = _manifestCache.GetManifest(gameVersion.Id);
        if (cachedManifest != null)
        {
            if (cachedManifest.Id != gameVersion.Id)
            {
                throw new ManifestValidationException(
                    gameVersion.Id,
                    $"Manifest ID mismatch. Expected '{gameVersion.Id}', got '{cachedManifest.Id}'");
            }

            return cachedManifest;
        }

        // Load from embedded resources as fallback
        var manifestName = $"GenHub.Manifests.{gameVersion.Id}.json";
        var assembly = Assembly.GetExecutingAssembly();
        await using var stream = assembly.GetManifestResourceStream(manifestName);

        if (stream == null)
        {
            _logger.LogWarning(
                "Manifest for GameVersion '{VersionName}' ({VersionId}) not found. Looked for: {ManifestName}",
                gameVersion.Name,
                gameVersion.Id,
                manifestName);
            return null;
        }

        try
        {
            var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(stream, _jsonOptions, cancellationToken);
            if (manifest == null)
            {
                throw new ManifestValidationException(
                    gameVersion.Id,
                    "Manifest deserialization returned null");
            }

            if (manifest.Id != gameVersion.Id)
            {
                throw new ManifestValidationException(
                    gameVersion.Id,
                    $"Manifest ID mismatch. Expected '{gameVersion.Id}', got '{manifest.Id}'");
            }

            // Validate security
            ValidateManifestSecurity(manifest);

            // Cache the manifest for future use
            _manifestCache.AddOrUpdateManifest(manifest);

            return manifest;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse manifest file: {ManifestName}", manifestName);
            throw new ManifestValidationException(
                gameVersion.Id,
                $"JSON parsing failed: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<GameManifest?> GetManifestAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        string gameType = installation.HasGenerals && !installation.HasZeroHour ? "Generals" :
                          installation.HasZeroHour ? "ZeroHour" : "Unknown";
        var manifestId = $"{installation.InstallationType}.{gameType}";

        // First check cache
        var cachedManifest = _manifestCache.GetManifest(manifestId);
        if (cachedManifest != null)
        {
            return cachedManifest;
        }

        var manifestName = $"GenHub.Manifests.{manifestId}.json";
        _logger.LogDebug("Attempting to load manifest for installation: {ManifestName}", manifestName);

        var assembly = Assembly.GetExecutingAssembly();
        await using var stream = assembly.GetManifestResourceStream(manifestName);

        if (stream == null)
        {
            _logger.LogWarning(
                "Manifest for installation type '{Type}' and game '{Game}' not found. Looked for: {ManifestName}",
                installation.InstallationType,
                gameType,
                manifestName);
            return null;
        }

        try
        {
            var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(stream, _jsonOptions, cancellationToken);
            if (manifest == null)
            {
                throw new ManifestValidationException(
                    manifestId,
                    "Manifest deserialization returned null");
            }

            // Validate security
            ValidateManifestSecurity(manifest);

            // Cache the manifest for future use
            _manifestCache.AddOrUpdateManifest(manifest);

            return manifest;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse manifest file: {ManifestName}", manifestName);
            throw new ManifestValidationException(
                manifestId,
                $"JSON parsing failed: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Validates manifest security to prevent path traversal and other security issues.
    /// </summary>
    private static void ValidateManifestSecurity(GameManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            if (Path.IsPathRooted(file.RelativePath))
            {
                throw new ManifestSecurityException(
                    manifest.Id,
                    $"Absolute path not allowed: {file.RelativePath}");
            }

            if (file.RelativePath.Contains(".."))
            {
                throw new ManifestSecurityException(
                    manifest.Id,
                    $"Path traversal not allowed: {file.RelativePath}");
            }

            if (string.IsNullOrWhiteSpace(file.RelativePath))
            {
                throw new ManifestSecurityException(
                    manifest.Id,
                    "Empty file path not allowed");
            }
        }

        // Validate required directories
        foreach (var directory in manifest.RequiredDirectories)
        {
            if (Path.IsPathRooted(directory) || directory.Contains(".."))
            {
                throw new ManifestSecurityException(
                    manifest.Id,
                    $"Invalid required directory path: {directory}");
            }
        }
    }
}
