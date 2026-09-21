using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
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
    /// <param name="baseRoot">The primary game root directory.</param>
    /// <param name="overrideRoot">Optional higher-priority root layered over the base.</param>
    /// <param name="projectDirectory">Optional mod project directory layered above game files.</param>
    /// <param name="logger">The logger sink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The layered virtual file system.</returns>
    public static SageVirtualFileSystem Open(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseRoot);
        ArgumentNullException.ThrowIfNull(logger);
        var fileSystem = new SageVirtualFileSystem(baseRoot, false, logger, cancellationToken);
        if (!string.IsNullOrWhiteSpace(overrideRoot)
            && Directory.Exists(overrideRoot)
            && !string.Equals(overrideRoot, baseRoot, StringComparison.OrdinalIgnoreCase))
        {
            fileSystem.AddSideload(overrideRoot);
        }

        if (!string.IsNullOrWhiteSpace(projectDirectory) && Directory.Exists(projectDirectory))
        {
            fileSystem.AddMod(projectDirectory);

            // Layer loose files in GameFilesEdited if present
            var gameFilesEdited = Path.Combine(projectDirectory, "GameFilesEdited");
            if (Directory.Exists(gameFilesEdited))
            {
                fileSystem.AddMod(gameFilesEdited);
            }

            // Layer .Release and Release output folders containing packed .big archives
            foreach (var releaseSub in new[] { ".Release", "Release" })
            {
                var releaseDir = Path.Combine(projectDirectory, releaseSub);
                if (Directory.Exists(releaseDir))
                {
                    fileSystem.AddMod(releaseDir);
                }
            }

            // If projectDirectory is GameFilesEdited itself, also layer parent directory and parent's releases
            var parent = Directory.GetParent(projectDirectory)?.FullName;
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                foreach (var releaseSub in new[] { ".Release", "Release" })
                {
                    var parentReleaseDir = Path.Combine(parent, releaseSub);
                    if (Directory.Exists(parentReleaseDir))
                    {
                        fileSystem.AddMod(parentReleaseDir);
                    }
                }
            }
        }

        return fileSystem;
    }
}
