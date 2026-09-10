using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Helper providing asset and DRM compatibility operations for prepared workspaces.
/// </summary>
public static class WorkspaceCompatibilityHelper
{
    /// <summary>
    /// Ensures DRM marker directory and compatibility assets (ZH_Generals base assets, d3d8 wrapper)
    /// exist so the game engine binary can run reliably without crashing.
    /// </summary>
    /// <param name="workspaceInfo">The workspace info.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public static void EnsureDrmAndAssetCompatibility(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 1. Ensure __Installer exists in the parent directory of the workspace.
        // Modern game.dat (EA 2024 update) checks for '..\__Installer' relative to the executing process;
        // finding it skips Steam DRM checks. This shared marker at the workspace parent root is intentionally
        // preserved across per-workspace lifecycles to allow sibling workspaces to bypass DRM.
        try
        {
            var parentDir = Path.GetDirectoryName(workspaceInfo.WorkspacePath);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                var installerDir = Path.Combine(parentDir, GameClientConstants.SteamDrmMarkerDirectory);
                if (!Directory.Exists(installerDir))
                {
                    Directory.CreateDirectory(installerDir);
                    logger.LogDebug("Ensured DRM marker directory at {InstallerDir}", installerDir);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ensure DRM marker directory for workspace at {WorkspacePath}", workspaceInfo.WorkspacePath);
        }

        // 2. Ensure ZH_Generals base assets are linked if present in the game installation.
        var zhGeneralsTargetPath = Path.Combine(workspaceInfo.WorkspacePath, GameClientConstants.ZhGeneralsDirectory);
        if (!Directory.Exists(zhGeneralsTargetPath) && !Path.Exists(zhGeneralsTargetPath))
        {
            var sourceDirWithZhGenerals = configuration.Manifests
                .Where(m => m.ContentType is ContentType.GameClient or ContentType.GameInstallation)
                .SelectMany(m => (m.Files ?? []).Select(f => Path.GetDirectoryName(ResolveSourcePath(f, m, configuration))))
                .FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(Path.Combine(d, GameClientConstants.ZhGeneralsDirectory)));

            if (!string.IsNullOrEmpty(sourceDirWithZhGenerals))
            {
                var zhGeneralsSource = Path.Combine(sourceDirWithZhGenerals, GameClientConstants.ZhGeneralsDirectory);
                try
                {
                    Directory.CreateSymbolicLink(zhGeneralsTargetPath, zhGeneralsSource);
                    logger.LogInformation("Linked ZH_Generals directory via symlink from {Source} to {Target}", zhGeneralsSource, zhGeneralsTargetPath);
                }
                catch (Exception symlinkEx)
                {
                    logger.LogDebug(symlinkEx, "Failed to create symbolic link for ZH_Generals directory at {Target}; attempting junction fallback", zhGeneralsTargetPath);
                    if (OperatingSystem.IsWindows() && TryCreateDirectoryJunction(zhGeneralsTargetPath, zhGeneralsSource, logger))
                    {
                        logger.LogInformation("Linked ZH_Generals directory via junction from {Source} to {Target}", zhGeneralsSource, zhGeneralsTargetPath);
                    }
                    else
                    {
                        logger.LogWarning("Failed to create symbolic link or junction for ZH_Generals directory at {Target}; skipping materialization to avoid freezing UI with large directory copy", zhGeneralsTargetPath);
                    }
                }
            }
        }

        // 3. Ensure d3d8.dll is present in workspace (Direct3D 8 wrapper required for modern Windows 10/11)
        try
        {
            var d3d8TargetPath = Path.Combine(workspaceInfo.WorkspacePath, GameClientConstants.Direct3D8WrapperDll);
            if (!File.Exists(d3d8TargetPath))
            {
                var d3d8Source = configuration.Manifests
                    .Where(m => m.ContentType is ContentType.GameClient or ContentType.GameInstallation)
                    .SelectMany(m => (m.Files ?? []).Select(f => Path.GetDirectoryName(ResolveSourcePath(f, m, configuration))))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Select(d => Path.Combine(d!, GameClientConstants.Direct3D8WrapperDll))
                    .FirstOrDefault(File.Exists);

                if (!string.IsNullOrEmpty(d3d8Source))
                {
                    try
                    {
                        File.CreateSymbolicLink(d3d8TargetPath, d3d8Source);
                        logger.LogInformation("Linked {Dll} from {Source} to {Target}", GameClientConstants.Direct3D8WrapperDll, d3d8Source, d3d8TargetPath);
                    }
                    catch (Exception symlinkEx)
                    {
                        logger.LogWarning(symlinkEx, "Failed to create symlink for {Dll}, falling back to copy: {Target}", GameClientConstants.Direct3D8WrapperDll, d3d8TargetPath);
                        if (File.Exists(d3d8TargetPath))
                        {
                            try
                            {
                                File.Delete(d3d8TargetPath);
                            }
                            catch
                            {
                                // Best effort delete
                            }
                        }

                        File.Copy(d3d8Source, d3d8TargetPath, overwrite: true);
                        logger.LogInformation("Copied {Dll} from {Source} to {Target}", GameClientConstants.Direct3D8WrapperDll, d3d8Source, d3d8TargetPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to materialize {Dll} to workspace at {WorkspacePath}", GameClientConstants.Direct3D8WrapperDll, workspaceInfo.WorkspacePath);
        }
    }

    /// <summary>
    /// Resolves the source path for a manifest file based on configuration and manifest details.
    /// </summary>
    /// <param name="file">The manifest file.</param>
    /// <param name="manifest">The manifest containing the file.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <returns>The resolved absolute source path.</returns>
    public static string ResolveSourcePath(ManifestFile file, ContentManifest manifest, WorkspaceConfiguration configuration)
    {
        // Use file's SourcePath if already an absolute path
        if (!string.IsNullOrEmpty(file.SourcePath) && Path.IsPathRooted(file.SourcePath))
        {
            return file.SourcePath;
        }

        // Look up manifest-specific source path from configuration (if manifest has an ID)
        // Note: manifest.Id could be default (empty struct) in tests, so check the value
        var manifestIdValue = manifest.Id.Value;
        if (!string.IsNullOrEmpty(manifestIdValue) &&
            configuration.ManifestSourcePaths != null &&
            configuration.ManifestSourcePaths.TryGetValue(manifestIdValue, out var manifestSourcePath))
        {
            // If file has a relative SourcePath, combine it with manifest's source directory
            var relativePath = !string.IsNullOrEmpty(file.SourcePath) ? file.SourcePath : file.RelativePath;
            return Path.Combine(manifestSourcePath, relativePath);
        }

        // Fallback to BaseInstallationPath for GameInstallation manifests
        if (manifest.ContentType == ContentType.GameInstallation)
        {
            var relativePath = !string.IsNullOrEmpty(file.SourcePath) ? file.SourcePath : file.RelativePath;
            return Path.Combine(configuration.BaseInstallationPath, relativePath);
        }

        // If file has SourcePath, treat as relative to BaseInstallationPath
        if (!string.IsNullOrEmpty(file.SourcePath))
        {
            return Path.Combine(configuration.BaseInstallationPath, file.SourcePath);
        }

        // Final fallback - use RelativePath with BaseInstallationPath
        return Path.Combine(configuration.BaseInstallationPath, file.RelativePath);
    }

    /// <summary>
    /// Attempts to create an NTFS directory junction targeting the source path without requiring admin elevation.
    /// </summary>
    /// <param name="linkPath">The junction path to create.</param>
    /// <param name="targetPath">The target directory path.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <returns><c>true</c> if junction creation succeeded; otherwise, <c>false</c>.</returns>
    private static bool TryCreateDirectoryJunction(string linkPath, string targetPath, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Process may have already exited
                    }

                    return false;
                }

                if (process.ExitCode == ProcessConstants.ExitCodeSuccess && Path.Exists(linkPath))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to create directory junction for {LinkPath} targeting {TargetPath}", linkPath, targetPath);
        }

        return false;
    }
}
