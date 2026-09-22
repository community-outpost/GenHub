using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        // Cross-platform: runs on every preparation and every workspace reuse, on all operating
        // systems. It is a no-op unless the launcher configured a supplemental archive root, so
        // existing callers are unaffected.
        EnsureSupplementalArchives(workspaceInfo, configuration, logger);

        if (!IsWindowsTarget(workspaceInfo, configuration))
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

    /// <summary>
    /// Determines whether a base Generals archive is safe to link as a supplemental asset archive for Zero Hour.
    /// Excludes archives that contain game logic, patches, INIs, window definitions, shaders, copy protection,
    /// or localized language string tables which would override or conflict with Zero Hour's own definitions,
    /// causing engine crashes or missing Zero Hour UI strings.
    /// </summary>
    /// <param name="candidatePathOrName">The archive file name or absolute path.</param>
    /// <returns><c>true</c> if the archive is safe to mount in Zero Hour; otherwise <c>false</c>.</returns>
    public static bool IsSafeSupplementalArchive(string? candidatePathOrName)
    {
        if (string.IsNullOrWhiteSpace(candidatePathOrName))
        {
            return false;
        }

        var name = Path.GetFileName(candidatePathOrName);

        // Never link Zero Hour archives from a supplemental root
        if (name.EndsWith(GameClientConstants.ZeroHourArchiveExtensionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Substring matching is deliberate: patch-era variants (PatchINI.big, PatchWindow.big,
        // PatchData.big) must be caught without enumerating every retail filename, and a
        // skipped community archive costs missing optional content while a linked unsafe one
        // costs an engine crash.
        if (GameClientConstants.UnsafeSupplementalArchiveMarkers.Any(
            marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Language archives (e.g. English.big, German.big) contain base generals.csf string tables.
        // Zero Hour ships complete string tables in *ZH.big (e.g. EnglishZH.big). Linking the base
        // language archive causes SAGE to load the base generals.csf first alphabetically, wiping out
        // Zero Hour specific GUI strings (GUI:StartingMoney, GUI:LimitSuperweapons, GUI:StartingMoneyFormat, etc.).
        // Note: Audio and speech variants (AudioEnglish.big, SpeechEnglish.big) do not contain CSF tables
        // and are safe to link.
        var baseNameWithoutExtension = Path.GetFileNameWithoutExtension(name);
        if (GameClientConstants.UnsafeSupplementalLanguageArchiveNames.Any(
            lang => string.Equals(baseNameWithoutExtension, lang, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Deep archive inspection: if the file exists on disk, inspect its directory index.
        // Any archive containing .csf string tables, .wnd window layouts, or .ini definitions
        // must never be linked as a supplemental archive. TryReadIndex already converts I/O,
        // corruption, and format failures into a false return, so when the index cannot be
        // read only the filename checks above apply.
        if (File.Exists(candidatePathOrName) &&
            BigArchiveReader.TryReadIndex(candidatePathOrName, out var index) &&
            index.Keys.Any(entry => GameClientConstants.UnsafeSupplementalArchiveEntryExtensions.Any(
                extension => entry.EndsWith(extension, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Enumerates the top-level retail archives a supplemental root provides, matched the same way
    /// the engine mounts them: <c>*.big</c>, case-insensitive, non-recursive.
    /// </summary>
    /// <remarks>
    /// This is the single enumeration both the workspace linker and the delta reconciler consume,
    /// so the files treated as supplemental content are identical in both places. Names map to
    /// their canonical on-disk source paths so callers never reconstruct a source from workspace
    /// casing, which may differ on case-sensitive filesystems. A missing root is reported as an
    /// empty map rather than an error; only an unreadable one fails, so the caller can warn
    /// instead of silently launching without the archives.
    /// </remarks>
    /// <param name="supplementalRoot">The supplemental archive root, or null when none is configured.</param>
    /// <param name="archives">The on-disk archive filenames mapped to their full source paths, compared case-insensitively.</param>
    /// <param name="logger">Optional logger receiving an informational entry per deliberately skipped archive.</param>
    /// <returns><c>true</c> when the root was enumerated or is absent; <c>false</c> when it exists but could not be read.</returns>
    public static bool TryGetSupplementalArchives(string? supplementalRoot, out IReadOnlyDictionary<string, string> archives, ILogger? logger = null)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(supplementalRoot))
        {
            archives = names;
            return true;
        }

        try
        {
            // Enumerate deterministically: on a case-sensitive filesystem the root may hold
            // both "Textures.big" and "textures.big", and TryAdd keeps whichever arrives
            // first. Ordering pins the same canonical source for the linker and the
            // reconciler on every run instead of churning on enumeration order.
            foreach (var path in Directory.EnumerateFiles(supplementalRoot, RetailArchiveConstants.ArchiveSearchPattern, RetailArchiveConstants.ArchiveSearch)
                .OrderBy(candidate => candidate, StringComparer.Ordinal))
            {
                var fileName = Path.GetFileName(path);
                if (IsSafeSupplementalArchive(path))
                {
                    names.TryAdd(fileName, path);
                }
                else
                {
                    // Informational, not debug: a skip deliberately costs content (see
                    // IsSafeSupplementalArchive), so a missing-content report is only
                    // diagnosable when the skipped names are visible in default logs.
                    logger?.LogInformation("Skipping unsafe supplemental archive {FileName} from {Root}", fileName, supplementalRoot);
                }
            }

            archives = names;
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // A missing root yields nothing, so the map is already empty.
            archives = names;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            archives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    /// <summary>
    /// Determines whether a symbolic link target resides directly inside the given root directory.
    /// Relative targets can never match: supplemental links are always created with absolute
    /// targets, so a relative target proves foreign ownership.
    /// </summary>
    /// <param name="linkTarget">The link target as stored in the link.</param>
    /// <param name="root">The root directory to test against.</param>
    /// <returns><c>true</c> when the target is an absolute path inside <paramref name="root"/>.</returns>
    internal static bool IsLinkTargetUnderRoot(string? linkTarget, string root)
    {
        if (!Path.IsPathRooted(linkTarget))
        {
            return false;
        }

        return string.Equals(
            NormalizeLinkPath(Path.GetDirectoryName(linkTarget)),
            NormalizeLinkPath(root),
            PathHelper.PathComparison);
    }

    /// <summary>
    /// Determines whether the workspace is targeting a Windows executable either via direct host OS or compatibility runner.
    /// </summary>
    private static bool IsWindowsTarget(WorkspaceInfo workspaceInfo, WorkspaceConfiguration configuration)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var launchExecutable = !string.IsNullOrWhiteSpace(workspaceInfo.ExecutablePath)
            ? workspaceInfo.ExecutablePath
            : configuration.GameClient?.ExecutablePath;

        if (string.IsNullOrWhiteSpace(launchExecutable))
        {
            launchExecutable = configuration.Manifests
                .SelectMany(m => m.Files ?? [])
                .Select(f => f.RelativePath)
                .FirstOrDefault(CommandLineHelper.IsWindowsExecutable);
        }

        return CommandLineHelper.IsWindowsExecutable(launchExecutable);
    }

    /// <summary>
    /// Links the supplemental root's top-level archives into the workspace root and removes links
    /// left over from a root that no longer provides them.
    /// </summary>
    /// <remarks>
    /// Existing workspace entries always win: a supplemental archive is only created when nothing
    /// occupies its name, and reconciliation only touches links pointing into the supplemental
    /// root — never regular files and never foreign links — so manifest content, mods, and user
    /// files cannot be removed or shadowed by this step.
    /// <para>
    /// Linking applies only when the resolved workspace executable is a Windows binary: the native
    /// engine resolves its archive roots from the environment instead. The check runs against the
    /// workspace-resolved path — the exact string the runner will wrap — because workspace aliasing
    /// may rewrite the client's declared entry point (e.g. <c>game.dat</c> to <c>generals.exe</c>).
    /// </para>
    /// </remarks>
    /// <param name="workspaceInfo">The workspace info.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="logger">Logger instance.</param>
    private static void EnsureSupplementalArchives(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        ILogger logger)
    {
        var supplementalRoot = configuration.SupplementalArchiveRoot;
        if (string.IsNullOrWhiteSpace(supplementalRoot))
        {
            return;
        }

        var launchExecutable = !string.IsNullOrWhiteSpace(workspaceInfo.ExecutablePath)
            ? workspaceInfo.ExecutablePath
            : configuration.GameClient?.ExecutablePath;
        if (!CommandLineHelper.IsWindowsExecutable(launchExecutable))
        {
            logger.LogDebug(
                "Skipping supplemental archives for non-Windows executable {Executable} in workspace {Workspace}",
                launchExecutable,
                workspaceInfo.WorkspacePath);
            return;
        }

        if (!TryGetSupplementalArchives(supplementalRoot, out var desiredArchives, logger))
        {
            logger.LogWarning("Supplemental archive root could not be read: {Root}", supplementalRoot);
            workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                $"Supplemental archive root could not be read: {supplementalRoot}",
                ValidationSeverity.Warning));
            return;
        }

        var created = LinkMissingSupplementalArchives(workspaceInfo.WorkspacePath, desiredArchives, workspaceInfo, logger);
        var removed = ReconcileStaleSupplementalLinks(workspaceInfo.WorkspacePath, supplementalRoot, desiredArchives, logger);
        workspaceInfo.FileCount += created - removed;
    }

    /// <summary>
    /// Creates workspace-root links for every desired archive that has no entry yet.
    /// </summary>
    /// <remarks>
    /// Existing entries are matched case-insensitively: on a case-sensitive filesystem a
    /// workspace-owned <c>textures.big</c> must still win over a supplemental
    /// <c>Textures.big</c> instead of gaining a second entry.
    /// </remarks>
    /// <returns>The number of links created.</returns>
    private static int LinkMissingSupplementalArchives(
        string workspacePath,
        IReadOnlyDictionary<string, string> desiredArchives,
        WorkspaceInfo workspaceInfo,
        ILogger logger)
    {
        HashSet<string> existingNames;
        try
        {
            existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Directory.EnumerateFileSystemEntries(workspacePath, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(entry);
                if (!string.IsNullOrEmpty(name))
                {
                    existingNames.Add(name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to enumerate workspace root {Workspace} for supplemental archives", workspacePath);
            workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                $"Failed to enumerate workspace root {workspacePath} for supplemental archives: {ex.Message}",
                ValidationSeverity.Warning));
            return 0;
        }

        var created = 0;
        foreach (var (name, sourcePath) in desiredArchives)
        {
            if (existingNames.Contains(name))
            {
                continue;
            }

            var targetPath = Path.Combine(workspacePath, name);
            try
            {
                LinkFileOrCopy(sourcePath, targetPath, name, logger);
                created++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to materialize supplemental archive {Name} from {Source} to {Target}", name, sourcePath, targetPath);
                workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                    $"Failed to materialize supplemental archive {name}: {ex.Message}",
                    ValidationSeverity.Warning));
            }
        }

        return created;
    }

    /// <summary>
    /// Repairs supplemental links whose target is stale and deletes those the root no longer provides.
    /// </summary>
    /// <remarks>
    /// Only links pointing into the supplemental root are ever touched. A link owned by a manifest,
    /// a mod, or the user may share a supplemental archive's name (a mod overriding a retail archive
    /// under a link-based strategy does exactly that), so name alone never establishes ownership.
    /// </remarks>
    /// <returns>The number of stale entries removed without replacement.</returns>
    private static int ReconcileStaleSupplementalLinks(
        string workspacePath,
        string supplementalRoot,
        IReadOnlyDictionary<string, string> desiredArchives,
        ILogger logger)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFiles(workspacePath, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to enumerate workspace root for supplemental reconciliation: {Workspace}", workspacePath);
            return 0;
        }

        var removed = 0;
        foreach (var entry in entries)
        {
            try
            {
                if (ReconcileSupplementalEntry(entry, supplementalRoot, desiredArchives, logger))
                {
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Failed to reconcile supplemental entry {Entry}; it will be retried on the next launch", entry);
            }
        }

        return removed;
    }

    /// <returns><c>true</c> when the entry was removed without replacement; otherwise, <c>false</c>.</returns>
    private static bool ReconcileSupplementalEntry(
        string entry,
        string supplementalRoot,
        IReadOnlyDictionary<string, string> desiredArchives,
        ILogger logger)
    {
        var linkTarget = new FileInfo(entry).LinkTarget;
        if (linkTarget is null || !IsLinkTargetUnderRoot(linkTarget, supplementalRoot))
        {
            return false;
        }

        var name = Path.GetFileName(entry);
        if (!desiredArchives.TryGetValue(name, out var expectedSource))
        {
            File.Delete(entry);
            logger.LogDebug("Removed stale supplemental link {Entry} targeting {Target}", entry, linkTarget);
            return true;
        }

        if (string.Equals(NormalizeLinkPath(linkTarget), NormalizeLinkPath(expectedSource), PathHelper.PathComparison) &&
            File.Exists(entry))
        {
            return false;
        }

        File.Delete(entry);
        try
        {
            LinkFileOrCopy(expectedSource, entry, name, logger);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to relink supplemental entry {Entry}; it will be retried on the next launch", entry);
            return true;
        }
    }

    private static string NormalizeLinkPath(string? path) =>
        (path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
        if (Directory.Exists(targetPath) || !TryCleanStaleTargetPath(targetPath, workspaceInfo, logger))
        {
            return;
        }

        var sourceDir = EnumerateCandidateDirectories(configuration)
            .FirstOrDefault(d => Directory.Exists(Path.Combine(d, directoryName)));

        if (string.IsNullOrEmpty(sourceDir))
        {
            return;
        }

        var sourcePath = Path.Combine(sourceDir, directoryName);
        LinkOrCopyDirectory(workspaceInfo, directoryName, sourcePath, targetPath, logger);
    }

    private static bool TryCleanStaleTargetPath(string targetPath, WorkspaceInfo workspaceInfo, ILogger logger)
    {
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
            return false;
        }

        return true;
    }

    private static void LinkOrCopyDirectory(
        WorkspaceInfo workspaceInfo,
        string directoryName,
        string sourcePath,
        string targetPath,
        ILogger logger)
    {
        try
        {
            Directory.CreateSymbolicLink(targetPath, sourcePath);
            logger.LogInformation("Linked {Directory} directory via symlink from {Source} to {Target}", directoryName, sourcePath, targetPath);
            return;
        }
        catch (Exception symlinkEx)
        {
            logger.LogDebug(symlinkEx, "Failed to create symbolic link for {Directory} directory at {Target}; attempting junction fallback", directoryName, targetPath);
        }

        if (TryCreateDirectoryJunction(targetPath, sourcePath, logger))
        {
            logger.LogInformation("Linked {Directory} directory via junction from {Source} to {Target}", directoryName, sourcePath, targetPath);
            return;
        }

        if (string.Equals(directoryName, GameClientConstants.CoreDirectory, StringComparison.OrdinalIgnoreCase))
        {
            CopyCoreDirectoryFallback(workspaceInfo, sourcePath, targetPath, logger);
            return;
        }

        logger.LogWarning("Failed to link {Directory} directory from {Source} to {Target}", directoryName, sourcePath, targetPath);
        workspaceInfo.ValidationIssues.Add(new ValidationIssue(
            $"Failed to link {directoryName} directory to workspace",
            ValidationSeverity.Warning));
    }

    private static void CopyCoreDirectoryFallback(
        WorkspaceInfo workspaceInfo,
        string sourcePath,
        string targetPath,
        ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(targetPath);
            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourcePath, file);
                var dest = Path.Combine(targetPath, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, dest, overwrite: true);
            }

            logger.LogInformation("Copied {Directory} directory files from {Source} to {Target} as fallback", GameClientConstants.CoreDirectory, sourcePath, targetPath);
        }
        catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(copyEx, "Failed to copy {Directory} directory from {Source} to {Target} as fallback", GameClientConstants.CoreDirectory, sourcePath, targetPath);
            workspaceInfo.ValidationIssues.Add(new ValidationIssue(
                $"Failed to copy {GameClientConstants.CoreDirectory} directory to workspace: {copyEx.Message}",
                ValidationSeverity.Warning));
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
            if (!ManifestDeclaresFile(configuration.Manifests, GameClientConstants.Direct3D8WrapperDll))
            {
                CleanUnrequestedDirect3D8Files(workspaceInfo.WorkspacePath, logger);
                return;
            }

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

    private static bool ManifestDeclaresFile(IEnumerable<ContentManifest>? manifests, string fileName)
    {
        if (manifests == null)
        {
            return false;
        }

        return manifests.Any(m => (ManifestVariantResolver.ResolveFiles(m) ?? [])
            .Any(f => string.Equals(f.RelativePath, fileName, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(Path.GetFileName(f.RelativePath), fileName, StringComparison.OrdinalIgnoreCase)));
    }

    private static void CleanUnrequestedDirect3D8Files(string workspacePath, ILogger logger)
    {
        var d3d8TargetPath = Path.Combine(workspacePath, GameClientConstants.Direct3D8WrapperDll);
        TryDeleteWorkspaceFile(d3d8TargetPath, GameClientConstants.Direct3D8WrapperDll, logger);

        var genToolUpdaterPath = Path.Combine(workspacePath, "GenToolUpdater.exe");
        TryDeleteWorkspaceFile(genToolUpdaterPath, "GenToolUpdater.exe", logger);
    }

    private static void TryDeleteWorkspaceFile(string filePath, string description, ILogger logger)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
            logger.LogInformation("Removed unrequested {File} from workspace", description);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to remove unrequested {File} from workspace", description);
        }
    }

    private static void MaterializeDirect3DWrapperFile(string sourcePath, string targetPath, ILogger logger) =>
        LinkFileOrCopy(sourcePath, targetPath, GameClientConstants.Direct3D8WrapperDll, logger);

    /// <summary>
    /// Materializes one compatibility file as a symbolic link, falling back to a copy when the
    /// filesystem or the process cannot create links.
    /// </summary>
    /// <param name="sourcePath">The file to link or copy from.</param>
    /// <param name="targetPath">The workspace path to create.</param>
    /// <param name="label">The display name used in log messages.</param>
    /// <param name="logger">Logger instance.</param>
    private static void LinkFileOrCopy(string sourcePath, string targetPath, string label, ILogger logger)
    {
        try
        {
            File.CreateSymbolicLink(targetPath, sourcePath);
            logger.LogInformation("Linked {Label} from {Source} to {Target}", label, sourcePath, targetPath);
        }
        catch (Exception symlinkEx)
        {
            logger.LogDebug(symlinkEx, "Failed to create symlink for {Label}, falling back to copy: {Target}", label, targetPath);
            try
            {
                File.Delete(targetPath);
            }
            catch (Exception delEx)
            {
                logger.LogDebug(delEx, "Failed to delete existing target file before copy: {Target}", targetPath);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            logger.LogInformation("Copied {Label} from {Source} to {Target}", label, sourcePath, targetPath);
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
