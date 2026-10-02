using Avalonia.Media.Imaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// Resolves interface image references to bitmaps, searching the document folder
/// and optional linked asset folders. Sprite names without a path or extension
/// refer to archived game art and intentionally resolve to no bitmap.
/// </summary>
public sealed class RmlImageResolver(TextureBitmapService bitmapService, ILogger<RmlImageResolver> logger)
{
    private static readonly string[] ProbeExtensions = [".tga", ".dds", ".png", ".jpg", ".jpeg", ".bmp"];

    private readonly object _sync = new();
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _searchFolders = [];

    /// <summary>
    /// Sets the additional folders searched for interface images.
    /// </summary>
    /// <param name="folders">The folders in search order.</param>
    public void SetSearchFolders(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        lock (_sync)
        {
            _searchFolders = folders;
            ClearCacheLocked();
        }
    }

    /// <summary>
    /// Clears the resolved bitmap cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_sync)
        {
            ClearCacheLocked();
        }
    }

    /// <summary>
    /// Resolves an image reference to a bitmap. A null result is the documented
    /// degraded-preview signal: the preview renders a placeholder instead of failing.
    /// </summary>
    /// <param name="reference">The raw src or background reference.</param>
    /// <param name="documentDirectory">The document folder searched first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bitmap, or null when the reference cannot be resolved.</returns>
    public async Task<Bitmap?> ResolveAsync(string? reference, string? documentDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(reference) || IsExternalReference(reference))
        {
            return null;
        }

        reference = reference.Trim().Trim('"', '\'');
        var key = $"{documentDirectory}::{reference}";
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var path = FindCandidate(reference, documentDirectory);
        Bitmap? bitmap = null;
        if (path != null)
        {
            bitmap = await LoadBitmapAsync(path, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                if (!ReferenceEquals(existing, bitmap))
                {
                    bitmap?.Dispose();
                }

                return existing;
            }

            _cache[key] = bitmap;
        }

        return bitmap;
    }

    private static bool IsExternalReference(string reference)
    {
        return reference.StartsWith(RmlConstants.ExternalSchemes.HttpPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(RmlConstants.ExternalSchemes.HttpsPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(RmlConstants.ExternalSchemes.DataPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpriteReference(string reference)
    {
        return reference.IndexOf('/') < 0 && reference.IndexOf('\\') < 0 && Path.GetExtension(reference).Length == 0;
    }

    private static bool IsWithinFolders(string path, List<string> folders)
    {
        foreach (var folder in folders)
        {
            if (PathHelper.IsPathWithinDirectory(folder, path))
            {
                return true;
            }
        }

        return false;
    }

    private void ClearCacheLocked()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap?.Dispose();
        }

        _cache.Clear();
    }

    private string? FindCandidate(string reference, string? documentDirectory)
    {
        if (IsSpriteReference(reference))
        {
            return null;
        }

        var folders = new List<string>();
        if (!string.IsNullOrEmpty(documentDirectory))
        {
            folders.Add(documentDirectory);
        }

        lock (_sync)
        {
            folders.AddRange(_searchFolders);
        }

        if (Path.IsPathFullyQualified(reference))
        {
            return File.Exists(reference) && IsWithinFolders(reference, folders) ? reference : null;
        }

        foreach (var folder in folders)
        {
            string candidate = string.Empty;
            try
            {
                candidate = Path.Combine(folder, reference);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!PathHelper.IsPathWithinDirectory(folder, candidate))
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (Path.GetExtension(reference).Length == 0)
            {
                foreach (var extension in ProbeExtensions)
                {
                    var probed = candidate + extension;
                    if (File.Exists(probed) && PathHelper.IsPathWithinDirectory(folder, probed))
                    {
                        return probed;
                    }
                }
            }
        }

        return null;
    }

    private async Task<Bitmap?> LoadBitmapAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var decoded = await bitmapService.LoadDecodedAsync(path, cancellationToken).ConfigureAwait(false);
            if (!decoded.Success || decoded.Data == null)
            {
                logger.LogDebug("Interface image {Path} did not decode: {Error}", path, decoded.FirstError);
                return null;
            }

            var bitmap = bitmapService.ToBitmap(decoded.Data);
            if (!bitmap.Success || bitmap.Data == null)
            {
                logger.LogDebug("Interface image {Path} did not convert: {Error}", path, bitmap.FirstError);
                return null;
            }

            return bitmap.Data;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Interface image {Path} could not be read", path);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Interface image {Path} is not accessible", path);
            return null;
        }
    }
}
