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
    /// <param name="overrideRoot">Optional fallback base root. Ignored for Zero Hour targets, which enforce strict per-game isolation to keep previews self-contained to the active installation and workspace.</param>
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
            var directories = SplitProjectDirectories(projectDirectory);
            foreach (var dir in directories)
            {
                if (Directory.Exists(dir))
                {
                    LayerProjectDirectory(fileSystem, dir, logger);
                }
                else
                {
                    logger.LogWarning("Project directory '{Path}' does not exist and was skipped", dir);
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
            var directories = SplitProjectDirectories(projectDirectory);
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
                    else if (Directory.Exists(file))
                    {
                        var dirInfo = new DirectoryInfo(file);
                        long latestTicks = dirInfo.LastWriteTimeUtc.Ticks;
                        foreach (var f in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            if (f.LastWriteTimeUtc.Ticks > latestTicks)
                            {
                                latestTicks = f.LastWriteTimeUtc.Ticks;
                            }
                        }

                        sb.Append(":DIR:").Append(latestTicks);
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

    /// <summary>
    /// Splits a semicolon-delimited list of project directories into distinct paths.
    /// </summary>
    /// <param name="projectDirectory">Semicolon-delimited directory string or null.</param>
    /// <returns>Array of normalized directory strings.</returns>
    public static string[] SplitProjectDirectories(string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Array.Empty<string>();
        }

        return projectDirectory.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static SageVirtualFileSystem CreateBaseFileSystem(
        string baseRoot,
        string? overrideRoot,
        ILogger logger,
        bool isZeroHour,
        CancellationToken cancellationToken)
    {
        if (isZeroHour)
        {
            // Zero Hour targets enforce strict per-game isolation: previews mount only
            // the target Zero Hour install (plus mod project and linked assets).
            // While the retail Windows game binary mounts Generals BIG archives via
            // registry lookup as a fallback in Win32BIGFileSystem::init, GenHub isolates
            // installations so previews truthfully reflect self-contained assets in the
            // target install or mod without hidden cross-install dependencies.
            return new SageVirtualFileSystem(
                baseRoot,
                isZeroHour: true,
                logger: logger,
                cancellationToken: cancellationToken,
                skipIniZhBig: false,
                initialTier: SageFileTier.Expansion);
        }

        var baseFileSystem = new SageVirtualFileSystem(
            baseRoot,
            isZeroHour: false,
            logger: logger,
            cancellationToken: cancellationToken,
            skipIniZhBig: false,
            initialTier: SageFileTier.BaseGame);

        if (HasUsableOverrideRoot(overrideRoot, baseRoot))
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
