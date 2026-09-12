using System;
using System.IO;
using Avalonia.Platform;
using GenHub.Core.Constants;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Helper for loading bundled GenHotkeys assets from Avalonia resources or development file system fallbacks.
/// </summary>
internal static class GenHotkeysAssetLoader
{
    private const string GenHubFolder = "GenHub";
    private const string AssetsFolder = "Assets";
    private const string GenHotkeysFolder = "GenHotkeys";

    /// <summary>
    /// Attempts to open an asset stream for a relative path under Assets/GenHotkeys.
    /// </summary>
    /// <param name="relativePath">The relative path to the asset (e.g. "Presets/CommandMap.ini").</param>
    /// <returns>A readable <see cref="Stream"/> if the asset was found; otherwise, <see langword="null"/>.</returns>
    public static Stream? TryOpenAssetStream(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        // 1. Try Avalonia resource loader
        try
        {
            var uri = new Uri($"avares://{GenHubFolder}/{AssetsFolder}/{GenHotkeysFolder}/{relativePath.Replace('\\', '/')}");
            if (AssetLoader.Exists(uri))
            {
                return AssetLoader.Open(uri);
            }
        }
        catch
        {
            // Ignore and fall back to filesystem
        }

        // 2. Try AppContext.BaseDirectory
        try
        {
            var fileOnDisk = Path.Combine(AppContext.BaseDirectory, AssetsFolder, GenHotkeysFolder, relativePath);
            if (File.Exists(fileOnDisk))
            {
                return File.OpenRead(fileOnDisk);
            }
        }
        catch
        {
            // Ignore and fall back to search roots
        }

        // 3. Try relative to project/solution directories during development
        var searchRoots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", AssetsFolder, GenHotkeysFolder, relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", GenHubFolder, AssetsFolder, GenHotkeysFolder, relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", GenHubFolder, AssetsFolder, GenHotkeysFolder, relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", GenHubFolder, GenHubFolder, AssetsFolder, GenHotkeysFolder, relativePath),
        };

        foreach (var root in searchRoots)
        {
            try
            {
                if (File.Exists(root))
                {
                    return File.OpenRead(root);
                }
            }
            catch
            {
                // Ignore and try next root
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to open an asset stream for a faction emblem icon.
    /// </summary>
    /// <param name="filename">The icon filename (e.g. "usa.png").</param>
    /// <returns>A readable <see cref="Stream"/> if the asset was found; otherwise, <see langword="null"/>.</returns>
    public static Stream? TryOpenFactionIconStream(string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        try
        {
            var uri = new Uri(string.Format(GenHotkeysConstants.FactionIconUriPattern, filename));
            if (AssetLoader.Exists(uri))
            {
                return AssetLoader.Open(uri);
            }
        }
        catch
        {
            // Ignore and return null
        }

        return null;
    }
}
