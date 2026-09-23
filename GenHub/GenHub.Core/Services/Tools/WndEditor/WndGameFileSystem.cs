using GenHub.Core.Constants;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GenHub.Core.Services.Tools.WndEditor;

/// <summary>
/// Opens the layered game file system used by WND editor asset lookups:
/// base game files, an optional higher-priority install layered over them,
/// and an optional mod project directory on top.
/// </summary>
public static class WndGameFileSystem
{
    /// <summary>
    /// Opens a virtual file system with override and mod layers applied.
    /// </summary>
    /// <param name="baseRoot">The primary game root directory (Zero Hour if isZeroHour is true, otherwise Generals).</param>
    /// <param name="overrideRoot">Optional fallback base root (e.g. Generals when Zero Hour is primary, or vice versa).</param>
    /// <param name="projectDirectory">Optional mod project directory layered above game files (multiple paths can be semicolon-delimited).</param>
    /// <param name="logger">The logger sink.</param>
    /// <param name="additionalBigFiles">Optional additional .BIG archive files to load.</param>
    /// <param name="isZeroHour">Whether the target game is Zero Hour (expansion tier) or vanilla Generals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The layered virtual file system.</returns>
    public static SageVirtualFileSystem Open(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        ILogger logger,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseRoot);
        ArgumentNullException.ThrowIfNull(logger);

        var fileSystem = CreateBaseFileSystem(baseRoot, overrideRoot, logger, isZeroHour, cancellationToken);

        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            var directories = projectDirectory.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dir in directories)
            {
                if (Directory.Exists(dir))
                {
                    LayerProjectDirectory(fileSystem, dir, logger);
                }
            }
        }

        LayerLinkedBigFiles(fileSystem, additionalBigFiles, logger);

        return fileSystem;
    }

    /// <summary>
    /// Builds a cache key for asset services factoring in roots, target game mode, and linked archives with size/mtime stamps.
    /// </summary>
    /// <param name="baseRoot">Primary game root directory.</param>
    /// <param name="overrideRoot">Optional override game root directory.</param>
    /// <param name="projectDirectory">Optional mod project directory.</param>
    /// <param name="additionalBigFiles">Optional linked .BIG archive files.</param>
    /// <param name="isZeroHour">Whether the target game is Zero Hour.</param>
    /// <returns>A unique cache key string.</returns>
    public static string BuildAssetCacheKey(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles,
        bool isZeroHour)
    {
        var sb = new StringBuilder();
        sb.Append(baseRoot).Append('|')
          .Append(overrideRoot ?? string.Empty).Append('|')
          .Append(isZeroHour ? "ZH" : "GEN").Append('|');

        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            var directories = projectDirectory.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dir in directories)
            {
                sb.Append(dir).Append('|');
            }
        }

        if (additionalBigFiles != null && additionalBigFiles.Count > 0)
        {
            var ordered = additionalBigFiles
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in ordered)
            {
                sb.Append(file);
                try
                {
                    if (File.Exists(file))
                    {
                        var info = new FileInfo(file);
                        sb.Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // Fall back to path alone if file inspection fails
                }

                sb.Append(';');
            }
        }

        return sb.ToString();
    }

    private static SageVirtualFileSystem CreateBaseFileSystem(
        string baseRoot,
        string? overrideRoot,
        ILogger logger,
        bool isZeroHour,
        CancellationToken cancellationToken)
    {
        if (isZeroHour && HasUsableOverrideRoot(overrideRoot, baseRoot))
        {
            // baseRoot is Zero Hour active target; overrideRoot is Generals base fallback
            var fileSystem = new SageVirtualFileSystem(
                baseRoot,
                isZeroHour: true,
                logger: logger,
                cancellationToken: cancellationToken,
                skipIniZhBig: false,
                initialTier: SageFileTier.Expansion);

            fileSystem.AddBaseFallback(overrideRoot!);
            return fileSystem;
        }

        var baseFileSystem = new SageVirtualFileSystem(
            baseRoot,
            isZeroHour: isZeroHour,
            logger: logger,
            cancellationToken: cancellationToken,
            skipIniZhBig: false,
            initialTier: isZeroHour ? SageFileTier.Expansion : SageFileTier.BaseGame);

        if (!isZeroHour && HasUsableOverrideRoot(overrideRoot, baseRoot))
        {
            baseFileSystem.AddBaseFallback(overrideRoot!);
        }

        return baseFileSystem;
    }

    private static bool HasUsableOverrideRoot(string? overrideRoot, string baseRoot)
    {
        return !string.IsNullOrWhiteSpace(overrideRoot)
            && Directory.Exists(overrideRoot)
            && !string.Equals(overrideRoot, baseRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void LayerLinkedBigFiles(
        SageVirtualFileSystem fileSystem,
        IReadOnlyCollection<string>? additionalBigFiles,
        ILogger logger)
    {
        if (additionalBigFiles == null)
        {
            return;
        }

        foreach (var bigFile in additionalBigFiles)
        {
            if (string.IsNullOrWhiteSpace(bigFile))
            {
                continue;
            }

            if (File.Exists(bigFile) || Directory.Exists(bigFile))
            {
                fileSystem.AddLinkedAsset(bigFile);
            }
            else
            {
                logger.LogWarning("Linked asset path '{Path}' does not exist and was skipped", bigFile);
            }
        }
    }

    private static void LayerProjectDirectory(SageVirtualFileSystem fileSystem, string projectDirectory, ILogger logger)
    {
        fileSystem.AddMod(projectDirectory);

        // Layer loose files in GameFilesEdited if present
        var gameFilesEdited = Path.Combine(projectDirectory, ModBuilderConstants.GameFilesEditedDir);
        if (Directory.Exists(gameFilesEdited))
        {
            fileSystem.AddMod(gameFilesEdited);
        }

        // Layer .Release and Release output folders containing packed .big archives
        LayerReleaseDirectories(fileSystem, projectDirectory);

        // If projectDirectory is GameFilesEdited itself, also layer parent directory and parent's releases/archives
        var normalizedDir = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(normalizedDir);
        if (string.Equals(dirName, ModBuilderConstants.GameFilesEditedDir, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(projectDirectory)?.FullName;
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                fileSystem.AddMod(parent);
                LayerReleaseDirectories(fileSystem, parent);
                MountAllBigArchives(fileSystem, parent, logger);
            }
        }
        else
        {
            MountAllBigArchives(fileSystem, projectDirectory, logger);
        }
    }

    private static void LayerReleaseDirectories(SageVirtualFileSystem fileSystem, string directory)
    {
        foreach (var releaseSub in new[] { ModBuilderConstants.DefaultReleaseDir, ModBuilderConstants.BuildConfigurationRelease })
        {
            var releaseDir = Path.Combine(directory, releaseSub);
            if (Directory.Exists(releaseDir))
            {
                fileSystem.AddMod(releaseDir);
            }
        }
    }

    private static void MountAllBigArchives(SageVirtualFileSystem fileSystem, string directory, ILogger logger)
    {
        try
        {
            var bigFiles = Directory.GetFiles(directory, SageChecksumConstants.BigFileSearchPattern, SearchOption.AllDirectories);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var bigFile in bigFiles)
            {
                fileSystem.AddMod(bigFile);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to enumerate .BIG archives under {Directory}", directory);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied enumerating .BIG archives under {Directory}", directory);
        }
    }
}
