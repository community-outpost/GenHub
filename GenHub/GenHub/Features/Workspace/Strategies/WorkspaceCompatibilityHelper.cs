using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Helper providing asset and DRM compatibility operations for prepared workspaces.
/// </summary>
public static class WorkspaceCompatibilityHelper
{
    /// <summary>
    /// Ensures DRM marker directory and compatibility assets (ZH_Generals base assets, Core directory, d3d8 wrapper)
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
        // The 2024 updated game executable inspects parent directories for the __Installer folder.
        // When present, Steam DRM verification is bypassed.
        EnsureDrmMarkerDirectory(workspaceInfo.WorkspacePath, workspaceInfo, logger);

        // 2. Ensure ZH_Generals base assets and Core runtime are linked if present in the game installation.
        EnsureDirectoryLink(workspaceInfo, configuration, GameClientConstants.ZhGeneralsDirectory, logger);
        EnsureDirectoryLink(workspaceInfo, configuration, GameClientConstants.CoreDirectory, logger);

        // 3. Ensure d3d8.dll is present in workspace (Direct3D 8 wrapper required for modern Windows 10/11).
        EnsureDirect3DWrapper(workspaceInfo, configuration, logger);
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

    private static void EnsureDrmMarkerDirectory(string workspacePath, WorkspaceInfo workspaceInfo, ILogger logger)
    {
        try
        {
            var parentDir = Path.GetDirectoryName(workspacePath);
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
            logger.LogWarning(ex, "Failed to ensure DRM marker directory for workspace at {WorkspacePath}", workspacePath);
            workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                $"Failed to ensure DRM marker directory for workspace at {workspacePath}: {ex.Message}",
                ValidationSeverity.Warning));
        }
    }

    private static void EnsureDirectoryLink(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        string directoryName,
        ILogger logger)
    {
        var targetPath = Path.Combine(workspaceInfo.WorkspacePath, directoryName);
        if (Directory.Exists(targetPath))
        {
            return;
        }

        try
        {
            FileOperationsService.DeleteDirectoryIfExists(targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to clean up stale entry at {Target}", targetPath);
        }

        if (Path.Exists(targetPath))
        {
            logger.LogWarning("Target path {Target} still exists after cleanup attempt; skipping link creation", targetPath);
            workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                $"Conflicting target path {targetPath} could not be cleaned up prior to linking",
                ValidationSeverity.Warning));
            return;
        }

        var sourceDir = EnumerateCandidateDirectories(configuration)
            .FirstOrDefault(d => Directory.Exists(Path.Combine(d, directoryName)));

        if (string.IsNullOrEmpty(sourceDir))
        {
            return;
        }

        var sourcePath = Path.Combine(sourceDir, directoryName);
        try
        {
            Directory.CreateSymbolicLink(targetPath, sourcePath);
            logger.LogInformation("Linked {Directory} directory via symlink from {Source} to {Target}", directoryName, sourcePath, targetPath);
        }
        catch (Exception symlinkEx)
        {
            logger.LogDebug(symlinkEx, "Failed to create symbolic link for {Directory} directory at {Target}; attempting junction fallback", directoryName, targetPath);
            if (TryCreateDirectoryJunction(targetPath, sourcePath, logger))
            {
                logger.LogInformation("Linked {Directory} directory via junction from {Source} to {Target}", directoryName, sourcePath, targetPath);
            }
            else if (string.Equals(directoryName, GameClientConstants.CoreDirectory, StringComparison.OrdinalIgnoreCase))
            {
                // Core contains critical DRM and activation libraries (Activation.dll, ~2MB total).
                // If symlink and junction fail, copy files directly so retail/EA/Steam client does not crash with 0xC0000135.
                try
                {
                    Directory.CreateDirectory(targetPath);
                    foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        var relativeFile = Path.GetRelativePath(sourcePath, file);
                        var destFile = Path.Combine(targetPath, relativeFile);
                        var destDir = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrEmpty(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        File.Copy(file, destFile, overwrite: true);
                    }

                    logger.LogInformation("Copied {Directory} directory contents from {Source} to {Target} as fallback", directoryName, sourcePath, targetPath);
                }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(copyEx, "Failed to copy {Directory} directory to {Target}", directoryName, targetPath);
                    workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                        $"Failed to copy fallback {directoryName} directory contents to {targetPath}: {copyEx.Message}",
                        ValidationSeverity.Warning));
                }
            }
            else
            {
                logger.LogWarning("Failed to create symbolic link or junction for {Directory} directory at {Target}; skipping materialization to avoid freezing UI with large directory copy", directoryName, targetPath);
                workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                    $"Failed to create symbolic link or junction for {directoryName} directory at {targetPath}",
                    ValidationSeverity.Warning));
            }
        }
    }

    private static void EnsureDirect3DWrapper(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        ILogger logger)
    {
        try
        {
            var d3d8TargetPath = Path.Combine(workspaceInfo.WorkspacePath, GameClientConstants.Direct3D8WrapperDll);
            if (File.Exists(d3d8TargetPath))
            {
                return;
            }

            var d3d8Source = EnumerateCandidateDirectories(configuration)
                .Select(d => Path.Combine(d, GameClientConstants.Direct3D8WrapperDll))
                .FirstOrDefault(File.Exists);

            if (string.IsNullOrEmpty(d3d8Source))
            {
                return;
            }

            MaterializeDirect3DWrapperFile(d3d8Source, d3d8TargetPath, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to materialize {Dll} to workspace at {WorkspacePath}", GameClientConstants.Direct3D8WrapperDll, workspaceInfo.WorkspacePath);
            workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                $"Failed to materialize {GameClientConstants.Direct3D8WrapperDll} to workspace: {ex.Message}",
                ValidationSeverity.Warning));
        }
    }

    private static void MaterializeDirect3DWrapperFile(string sourcePath, string targetPath, ILogger logger)
    {
        try
        {
            File.CreateSymbolicLink(targetPath, sourcePath);
            logger.LogInformation("Linked {Dll} from {Source} to {Target}", GameClientConstants.Direct3D8WrapperDll, sourcePath, targetPath);
        }
        catch (Exception symlinkEx)
        {
            logger.LogDebug(symlinkEx, "Failed to create symlink for {Dll}, falling back to copy: {Target}", GameClientConstants.Direct3D8WrapperDll, targetPath);
            try
            {
                File.Delete(targetPath);
            }
            catch (Exception delEx)
            {
                logger.LogDebug(delEx, "Failed to delete existing target file before copy: {Target}", targetPath);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            logger.LogInformation("Copied {Dll} from {Source} to {Target}", GameClientConstants.Direct3D8WrapperDll, sourcePath, targetPath);
        }
    }

    /// <summary>
    /// Enumerates candidate directories where game files or compatibility assets might be found,
    /// prioritized by BaseInstallationPath, GameClient working directory, GameClient executable directory,
    /// and manifest source paths.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <returns>An enumeration of candidate directory paths.</returns>
    private static IEnumerable<string> EnumerateCandidateDirectories(WorkspaceConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration.BaseInstallationPath))
        {
            yield return configuration.BaseInstallationPath;
        }

        if (configuration.GameClient != null)
        {
            if (!string.IsNullOrEmpty(configuration.GameClient.WorkingDirectory))
            {
                yield return configuration.GameClient.WorkingDirectory;
            }

            if (!string.IsNullOrEmpty(configuration.GameClient.ExecutablePath))
            {
                var exeDir = Path.GetDirectoryName(configuration.GameClient.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    yield return exeDir;
                }
            }
        }

        var manifestDirs = configuration.Manifests
            .Where(m => m.ContentType is ContentType.GameClient or ContentType.GameInstallation)
            .SelectMany(m => (ManifestVariantResolver.ResolveFiles(m) ?? [])
                .Select(f => Path.GetDirectoryName(ResolveSourcePath(f, m, configuration))))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in manifestDirs)
        {
            yield return dir!;
        }
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
                if (!process.WaitForExit(ProcessConstants.HelperProcessTimeoutMs))
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
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "Failed to create directory junction for {LinkPath} targeting {TargetPath}", linkPath, targetPath);
        }

        return false;
    }
}
