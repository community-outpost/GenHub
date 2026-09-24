using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Persists remote content artwork (icons, covers) to local per-manifest storage so the
/// offline library renders without a connection, and purges it when content is deleted.
/// </summary>
/// <param name="configurationProvider">Optional configuration provider supplying the application data path.</param>
/// <param name="httpClientFactory">Factory for artwork download clients.</param>
/// <param name="logger">Logger for recording diagnostic and error events.</param>
public sealed class ContentArtworkService(
    IConfigurationProviderService? configurationProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<ContentArtworkService> logger) : IContentArtworkService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".gif",
        ".bmp",
        ".ico",
    };

    private string? _resolvedArtworkRoot;

    private string ArtworkRoot
    {
        get
        {
            if (_resolvedArtworkRoot != null)
            {
                return _resolvedArtworkRoot;
            }

            _resolvedArtworkRoot = ResolveArtworkRoot();
            return _resolvedArtworkRoot;
        }
    }

    /// <inheritdoc />
    public string? GetLocalArtworkPath(string manifestId, ContentArtworkKind kind)
    {
        try
        {
            var directory = GetManifestArtworkDirectory(manifestId);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            return Directory.EnumerateFiles(directory, $"{GetSlotName(kind)}.*").FirstOrDefault();
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to probe local artwork for {ManifestId}", manifestId);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to probe local artwork for {ManifestId}", manifestId);
            return null;
        }
        catch (ArgumentException ex)
        {
            logger.LogDebug(ex, "Failed to probe local artwork for {ManifestId}", manifestId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> PrefetchArtworkAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var failures = new List<string>();
        using var httpClient = httpClientFactory.CreateClient();
        await PersistSlotAsync(manifest, ContentArtworkKind.Icon, manifest.Metadata.IconUrl, httpClient, failures, cancellationToken);
        await PersistSlotAsync(manifest, ContentArtworkKind.Cover, manifest.Metadata.CoverUrl, httpClient, failures, cancellationToken);

        if (failures.Count > 0)
        {
            return OperationResult<bool>.CreateFailure(failures);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> PurgeArtworkAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = GetManifestArtworkDirectory(manifestId);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
                logger.LogDebug("Purged local artwork for {ManifestId}", manifestId);
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (IOException ex)
        {
            return PurgeFailure(manifestId, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return PurgeFailure(manifestId, ex);
        }
        catch (ArgumentException ex)
        {
            return PurgeFailure(manifestId, ex);
        }
    }

    private static string GetSlotName(ContentArtworkKind kind) => kind == ContentArtworkKind.Icon ? "icon" : "cover";

    private static string SanitizeManifestId(string manifestId) =>
        string.Concat(manifestId.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static string ResolveExtension(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.LocalPath);
            if (ImageExtensions.Contains(extension))
            {
                return extension.ToLowerInvariant();
            }
        }

        return ".png";
    }

    private Task<OperationResult<bool>> PurgeFailure(string manifestId, Exception ex)
    {
        logger.LogWarning(ex, "Failed to purge local artwork for {ManifestId}", manifestId);
        return Task.FromResult(OperationResult<bool>.CreateFailure($"Could not remove artwork for '{manifestId}': {ex.Message}"));
    }

    private async Task PersistSlotAsync(
        ContentManifest manifest,
        ContentArtworkKind kind,
        string? remoteUrl,
        HttpClient httpClient,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        if (GetLocalArtworkPath(manifest.Id.Value, kind) != null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(remoteUrl) || !ImageCacheService.IsSafeRemoteUrl(remoteUrl, out _))
        {
            return;
        }

        var directory = GetManifestArtworkDirectory(manifest.Id.Value);
        if (string.IsNullOrEmpty(directory))
        {
            failures.Add($"Artwork storage unavailable for '{manifest.Id.Value}'.");
            return;
        }

        string? tempPath = null;
        try
        {
            var bytes = await DownloadArtworkBytesAsync(httpClient, remoteUrl, cancellationToken);
            if (bytes == null)
            {
                failures.Add($"Artwork download rejected for '{manifest.Id.Value}' ({kind}).");
                return;
            }

            Directory.CreateDirectory(directory);
            var targetPath = Path.Combine(directory, GetSlotName(kind) + ResolveExtension(remoteUrl));
            tempPath = Path.Combine(directory, $"{ContentArtworkConstants.TempFilePrefix}{Guid.NewGuid():N}");

            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);
            tempPath = null;
            logger.LogDebug("Persisted {Kind} artwork for {ManifestId}", kind, manifest.Id.Value);
        }
        catch (HttpRequestException ex)
        {
            PersistFailure(manifest, kind, failures, ex);
        }
        catch (IOException ex)
        {
            PersistFailure(manifest, kind, failures, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            PersistFailure(manifest, kind, failures, ex);
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not clean up temporary artwork file {TempPath}", tempPath);
                }
            }
        }
    }

    private void PersistFailure(ContentManifest manifest, ContentArtworkKind kind, List<string> failures, Exception ex)
    {
        logger.LogDebug(ex, "Failed to persist {Kind} artwork for {ManifestId}", kind, manifest.Id.Value);
        failures.Add($"Could not persist {kind} artwork for '{manifest.Id.Value}': {ex.Message}");
    }

    private async Task<byte[]?> DownloadArtworkBytesAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength > ImageCacheConstants.MaxImageDownloadSizeBytes)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                if (buffer.Length > ImageCacheConstants.MaxImageDownloadSizeBytes)
                {
                    return null;
                }
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Artwork download timed out: {Url}", url);
            return null;
        }
    }

    private string GetManifestArtworkDirectory(string manifestId)
    {
        var root = ArtworkRoot;
        if (string.IsNullOrEmpty(root))
        {
            return string.Empty;
        }

        return Path.Combine(root, SanitizeManifestId(manifestId));
    }

    private string ResolveArtworkRoot()
    {
        try
        {
            var appDataPath = configurationProvider?.GetApplicationDataPath();
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                appDataPath = AppDataPathHelper.GetDataRoot();
            }

            return Path.Combine(appDataPath, ContentArtworkConstants.ArtworkDirectoryName);
        }
        catch (IOException ex)
        {
            return DisableArtwork(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return DisableArtwork(ex);
        }
        catch (ArgumentException ex)
        {
            return DisableArtwork(ex);
        }
    }

    private string DisableArtwork(Exception ex)
    {
        logger.LogWarning(ex, "Failed to resolve artwork storage directory. Artwork persistence is disabled.");
        return string.Empty;
    }
}
