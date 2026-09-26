using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GenHub.Features.Launching;

/// <summary>
/// Launches Windows executables through Wine on Linux and macOS.
/// </summary>
public class WineRunner(
    WineRunnerOptions options,
    ILogger<WineRunner> logger,
    ILocalizationService? localizationService = null) : IGameLaunchRunner
{
    /// <summary>
    /// File name used to track the synchronization manifest across launches.
    /// </summary>
    private const string SyncStateFileName = ".genhub-sync.json";

    /// <summary>
    /// Standard user data subdirectories bridged into the Wine prefix.
    /// </summary>
    private static readonly string[] StandardUserDataDirectories =
    [
        GameSettingsConstants.FolderNames.Maps,
        GameSettingsConstants.FolderNames.Replays,
        GameSettingsConstants.FolderNames.Screenshots,
        GameSettingsConstants.FolderNames.Save,
    ];

    /// <inheritdoc/>
    public string Name => "Wine";

    /// <summary>Gets the configured runner options.</summary>
    public WineRunnerOptions Options => options;

    /// <inheritdoc/>
    public bool CanLaunchWindowsExecutables()
    {
        return TryFindWineBinary(out _);
    }

    /// <inheritdoc/>
    public OperationResult<RunnerCommand> ResolveCommand(GameLaunchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var executablePath = configuration.ExecutablePath;
        if (executablePath.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<RunnerCommand>.CreateFailure(RunnerTargetResolver.FlatpakGuidance(executablePath, logger, localizationService));
        }

        var mismatch = LaunchGuardMessages.GetCrossOsError(ExecutableFileClassifier.DetectPlatform(executablePath), localizationService);
        if (mismatch is not null)
        {
            logger.LogWarning("Launch blocked by OS guard: {Error} ({ExecutablePath})", mismatch, executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(mismatch);
        }

        executablePath = RunnerTargetResolver.ResolveBundleTarget(executablePath, logger);
        if (executablePath is null)
        {
            return OperationResult<RunnerCommand>.CreateFailure(
                RunnerTargetResolver.Localize(localizationService, LaunchMessageConstants.BundleUnresolvableKey, LaunchMessageConstants.BundleUnresolvable, configuration.ExecutablePath));
        }

        mismatch = LaunchGuardMessages.GetCrossOsError(ExecutableFileClassifier.DetectPlatform(executablePath), localizationService);
        if (mismatch is not null)
        {
            logger.LogWarning("Launch blocked by OS guard: {Error} ({ExecutablePath})", mismatch, executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(mismatch);
        }

        // Extension decides first; content decides second. A Windows binary without the
        // .exe extension (Steam launches game.dat, which is a PE) must still run under
        // Wine: passing it through to a native exec can only fail with ENOEXEC.
        if (!CommandLineHelper.IsWindowsExecutable(executablePath)
            && ExecutableFileClassifier.DetectPlatform(executablePath) != ExecutablePlatform.Windows)
        {
            logger.LogDebug("Target {ExecutablePath} is not a Windows executable; passing through directly", executablePath);
            return OperationResult<RunnerCommand>.CreateSuccess(
                new RunnerCommand(executablePath, string.Empty, new Dictionary<string, string>()));
        }

        if (!TryFindWineBinary(out var wineBinary))
        {
            logger.LogWarning("Wine binary not found; cannot launch {ExecutablePath}", executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(
                $"Wine binary not found (searched {string.Join(", ", options.BinaryNames)}). Install Wine to launch Windows games.");
        }

        EnsurePrefix();
        MirrorOptionsIni(configuration);

        logger.LogInformation("Launching {ExecutablePath} through Wine ({WineBinary})", executablePath, wineBinary);
        var environment = new Dictionary<string, string>(configuration.EnvironmentVariables)
        {
            [WineConstants.PrefixEnvironmentVariable] = options.PrefixPath,
        };

        ConfigureDirect3DOverride(configuration, environment);

        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(wineBinary, CommandLineHelper.QuoteArgument(executablePath), environment));
    }

    private static string SanitizeUserName(string userName)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .ToHashSet();
        var clean = new string(userName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? WineConstants.FallbackPrefixUserName : clean;
    }

    /// <summary>
    /// Copies the native Options.ini into one prefix user shell folder when the source is newer,
    /// and bridges user data directories (Maps, Replays, Screenshots, Save) via symlinks or mirroring.
    /// </summary>
    /// <param name="sourcePath">The native Options.ini path.</param>
    /// <param name="userDirectory">The prefix user profile directory.</param>
    /// <param name="documentsDirectoryName">The shell folder name ("Documents" or "My Documents").</param>
    /// <param name="dataDirectoryName">The game data directory name.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns><c>true</c> when any destination was written or bridged; otherwise, <c>false</c>.</returns>
    private static bool MirrorOptionsIniToShellFolder(
        string sourcePath,
        string userDirectory,
        string documentsDirectoryName,
        string dataDirectoryName,
        ILogger? logger = null)
    {
        var userDocuments = Path.Combine(userDirectory, documentsDirectoryName, dataDirectoryName);
        Directory.CreateDirectory(userDocuments);

        var destinationPath = Path.Combine(userDocuments, Path.GetFileName(sourcePath));
        var optionsMirrored = false;
        if (!File.Exists(destinationPath) || File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(destinationPath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
            optionsMirrored = true;
        }

        var bridgedAny = BridgeUserDataDirectories(Path.GetDirectoryName(sourcePath), userDocuments, logger);

        return optionsMirrored || bridgedAny;
    }

    /// <summary>
    /// Bridges user data subdirectories (e.g. Maps, Replays, Screenshots, Save) from the native
    /// user data folder into the Wine prefix personal documents folder.
    /// </summary>
    /// <param name="nativeDataDirectory">The native user data directory path.</param>
    /// <param name="prefixDataDirectory">The Wine prefix user data directory path.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns><c>true</c> if any directory was linked or mirrored; otherwise, <c>false</c>.</returns>
    private static bool BridgeUserDataDirectories(string? nativeDataDirectory, string prefixDataDirectory, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(nativeDataDirectory) || !Directory.Exists(nativeDataDirectory))
        {
            return false;
        }

        if (PathHelper.AreSamePath(nativeDataDirectory, prefixDataDirectory))
        {
            return false;
        }

        var bridgedAny = false;
        var directoryNames = new HashSet<string>(StandardUserDataDirectories, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(nativeDataDirectory))
            {
                directoryNames.Add(Path.GetFileName(dir));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore directory enumeration errors
        }

        foreach (var subDirName in directoryNames)
        {
            var nativeSubDir = Path.Combine(nativeDataDirectory, subDirName);
            var prefixSubDir = Path.Combine(prefixDataDirectory, subDirName);

            try
            {
                if (!Directory.Exists(nativeSubDir))
                {
                    Directory.CreateDirectory(nativeSubDir);
                }

                bridgedAny |= BridgeDirectory(nativeSubDir, prefixSubDir, logger);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Continue to next directory if bridging fails for one
            }
        }

        return bridgedAny;
    }

    /// <summary>
    /// Bridges an individual subdirectory by creating a symbolic link, or falling back to file synchronization.
    /// </summary>
    private static bool BridgeDirectory(string nativeSubDir, string prefixSubDir, ILogger? logger = null)
    {
        if (Directory.Exists(prefixSubDir))
        {
            return BridgeExistingDirectory(nativeSubDir, prefixSubDir, logger);
        }

        return CreateNewBridgeSymlinkOrSync(nativeSubDir, prefixSubDir, logger);
    }

    private static bool BridgeExistingDirectory(string nativeSubDir, string prefixSubDir, ILogger? logger = null)
    {
        var dirInfo = new DirectoryInfo(prefixSubDir);
        if (dirInfo.LinkTarget != null)
        {
            var resolvedTarget = Path.GetFullPath(dirInfo.LinkTarget, Path.GetDirectoryName(prefixSubDir) ?? ".");
            var normalizedTarget = Path.TrimEndingDirectorySeparator(resolvedTarget);
            var normalizedNative = Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeSubDir));

            if (string.Equals(normalizedTarget, normalizedNative, PathHelper.PathComparison))
            {
                return false;
            }

            // Stale symlink pointing to the wrong target - remove and recreate
            if (TryRecreateSymlink(prefixSubDir, nativeSubDir))
            {
                return true;
            }

            var remainingLinkInfo = new DirectoryInfo(prefixSubDir);
            if (remainingLinkInfo.LinkTarget != null || (remainingLinkInfo.Exists && (remainingLinkInfo.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                // The stale symlink could not be removed; do not synchronize through the wrong directory
                return false;
            }

            Directory.CreateDirectory(prefixSubDir);
            SyncDirectoryFiles(nativeSubDir, prefixSubDir, logger);
            return true;
        }

        // Real directory: convert it to a symlink (migrating existing files first)
        if (TryConvertDirectoryToSymlink(prefixSubDir, nativeSubDir, logger))
        {
            return true;
        }

        // If symlink creation failed, synchronize files with deletion propagation
        SyncDirectoryFiles(nativeSubDir, prefixSubDir, logger);
        return true;
    }

    private static bool TryRecreateSymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.Delete(linkPath, recursive: false);
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryConvertDirectoryToSymlink(string dirPath, string targetPath, ILogger? logger = null)
    {
        try
        {
            // Probe whether symlink creation is supported in this environment before touching or moving any files.
            var probeSymlink = dirPath + ".symlink_probe_" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateSymbolicLink(probeSymlink, targetPath);
                Directory.Delete(probeSymlink, recursive: false);
            }
            catch (Exception probeEx) when (probeEx is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return false;
            }

            if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
            {
                Directory.Delete(dirPath, recursive: false);
                Directory.CreateSymbolicLink(dirPath, targetPath);
                return true;
            }

            // Pre-existing non-empty directory (e.g. from a prior plain-Wine installation).
            // First migrate files from prefix to native directory to prevent data loss.
            if (!MirrorDirectoryContent(sourceDir: dirPath, targetDir: targetPath, logger: logger))
            {
                logger?.LogWarning("[WineRunner] Failed to mirror content from '{DirPath}' to '{TargetPath}'; aborting symlink conversion to prevent data loss.", dirPath, targetPath);
                return false;
            }

            var tempDir = dirPath + ".symlink_tmp_" + Guid.NewGuid().ToString("N");
            Directory.Move(dirPath, tempDir);
            try
            {
                Directory.CreateSymbolicLink(dirPath, targetPath);
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning(ex, "[WineRunner] Created symlink for '{DirPath}' but failed to clean up temporary directory '{TempDir}'.", dirPath, tempDir);
                }

                return true;
            }
            catch
            {
                try
                {
                    if (Directory.Exists(dirPath))
                    {
                        Directory.Delete(dirPath, recursive: false);
                    }

                    Directory.Move(tempDir, dirPath);
                }
                catch (Exception restoreEx)
                {
                    logger?.LogError(restoreEx, "[WineRunner] Failed to restore '{DirPath}' from '{TempDir}' after failed symlink conversion.", dirPath, tempDir);
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool CreateNewBridgeSymlinkOrSync(string nativeSubDir, string prefixSubDir, ILogger? logger = null)
    {
        // Target does not exist yet (or is a broken/dangling symlink): create symlink pointing to native folder
        try
        {
            var dirInfo = new DirectoryInfo(prefixSubDir);
            if (dirInfo.LinkTarget != null)
            {
                Directory.Delete(prefixSubDir, recursive: false);
            }
            else if (File.Exists(prefixSubDir))
            {
                return false;
            }

            Directory.CreateSymbolicLink(prefixSubDir, nativeSubDir);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            SyncDirectoryFiles(nativeSubDir, prefixSubDir, logger);
            return true;
        }
    }

    private static void SyncDirectoryFiles(string nativeDir, string prefixDir, ILogger? logger = null)
    {
        if (!Directory.Exists(nativeDir) && !Directory.Exists(prefixDir))
        {
            return;
        }

        Directory.CreateDirectory(nativeDir);
        Directory.CreateDirectory(prefixDir);

        var normalizedNative = Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeDir)) + Path.DirectorySeparatorChar;
        var normalizedPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(prefixDir)) + Path.DirectorySeparatorChar;

        if (string.Equals(normalizedNative, normalizedPrefix, PathHelper.PathComparison))
        {
            return;
        }

        var stateFilePath = Path.Combine(prefixDir, SyncStateFileName);
        HashSet<string>? previousSyncedFiles = null;
        if (File.Exists(stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(stateFilePath);
                previousSyncedFiles = JsonSerializer.Deserialize<HashSet<string>>(json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger?.LogWarning(ex, "[WineRunner] Failed to read sync state from '{StateFile}'.", stateFilePath);
            }
        }

        var nativeFiles = EnumerateFilesRelative(nativeDir);
        var prefixFiles = EnumerateFilesRelative(prefixDir);

        if (previousSyncedFiles != null)
        {
            foreach (var relPath in previousSyncedFiles)
            {
                var inNative = nativeFiles.ContainsKey(relPath);
                var inPrefix = prefixFiles.ContainsKey(relPath);

                if (!inNative && inPrefix)
                {
                    // Deleted on native side (e.g. via GenHub Map Manager) -> propagate deletion to prefix
                    try
                    {
                        File.Delete(prefixFiles[relPath].FullName);
                        prefixFiles.Remove(relPath);
                        logger?.LogInformation("[WineRunner] Propagated native deletion of '{RelativePath}' to Wine prefix.", relPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger?.LogWarning(ex, "[WineRunner] Failed to delete '{RelativePath}' from prefix during deletion propagation.", relPath);
                    }
                }
                else if (inNative && !inPrefix)
                {
                    // Deleted on prefix side (e.g. deleted in-game) -> propagate deletion to native
                    try
                    {
                        File.Delete(nativeFiles[relPath].FullName);
                        nativeFiles.Remove(relPath);
                        logger?.LogInformation("[WineRunner] Propagated Wine prefix deletion of '{RelativePath}' to native documents.", relPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger?.LogWarning(ex, "[WineRunner] Failed to delete '{RelativePath}' from native directory during deletion propagation.", relPath);
                    }
                }
            }
        }

        // Copy new or newer files from native to prefix
        foreach (var (relPath, nativeFileInfo) in nativeFiles)
        {
            var destFile = Path.Combine(prefixDir, relPath);
            if (!prefixFiles.TryGetValue(relPath, out var prefixFileInfo))
            {
                CopySyncFile(nativeFileInfo.FullName, destFile, logger);
            }
            else if (nativeFileInfo.LastWriteTimeUtc > prefixFileInfo.LastWriteTimeUtc)
            {
                CopySyncFile(nativeFileInfo.FullName, destFile, logger);
            }
        }

        // Copy new or newer files from prefix to native
        foreach (var (relPath, prefixFileInfo) in prefixFiles)
        {
            var destFile = Path.Combine(nativeDir, relPath);
            if (!nativeFiles.TryGetValue(relPath, out var nativeFileInfo))
            {
                CopySyncFile(prefixFileInfo.FullName, destFile, logger);
            }
            else if (prefixFileInfo.LastWriteTimeUtc > nativeFileInfo.LastWriteTimeUtc)
            {
                CopySyncFile(prefixFileInfo.FullName, destFile, logger);
            }
        }

        CleanEmptySubdirectories(nativeDir);
        CleanEmptySubdirectories(prefixDir);

        try
        {
            var finalPrefixFiles = EnumerateFilesRelative(prefixDir);
            var finalNativeFiles = EnumerateFilesRelative(nativeDir);
            var updatedFiles = finalPrefixFiles.Keys.Intersect(finalNativeFiles.Keys, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(updatedFiles);
            File.WriteAllText(stateFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "[WineRunner] Failed to write sync state to '{StateFile}'.", stateFilePath);
        }
    }

    private static void CopySyncFile(string sourceFile, string destFile, ILogger? logger = null)
    {
        try
        {
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourceFile, destFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "[WineRunner] Failed to copy '{Source}' to '{Destination}' during directory sync.", sourceFile, destFile);
        }
    }

    private static Dictionary<string, FileInfo> EnumerateFilesRelative(string rootDir)
    {
        var result = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootDir))
        {
            return result;
        }

        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var file in Directory.EnumerateFiles(rootDir, "*", enumerationOptions))
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, SyncStateFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relPath = Path.GetRelativePath(rootDir, file);
            result[relPath] = new FileInfo(file);
        }

        return result;
    }

    private static void CleanEmptySubdirectories(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                CleanEmptySubdirectories(subDir);
                if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                {
                    Directory.Delete(subDir, recursive: false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore directory cleanup failure
        }
    }

    private static bool MirrorDirectoryContent(string sourceDir, string targetDir, ILogger? logger = null)
    {
        if (!Directory.Exists(sourceDir))
        {
            return true;
        }

        var normalizedSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDir)) + Path.DirectorySeparatorChar;
        var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir)) + Path.DirectorySeparatorChar;

        if (string.Equals(normalizedSource, normalizedTarget, PathHelper.PathComparison))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "[WineRunner] Failed to create target directory '{TargetDir}' during directory sync.", targetDir);
            return false;
        }

        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var allSuccess = true;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", enumerationOptions))
        {
            if (string.Equals(Path.GetFileName(file), SyncStateFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullFilePath = Path.GetFullPath(file);
            if (fullFilePath.StartsWith(normalizedTarget, PathHelper.PathComparison))
            {
                // Target directory is nested inside source directory; avoid recursive copy into target
                continue;
            }

            var relative = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(targetDir, relative);

            try
            {
                var destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(destFile))
                {
                    File.Copy(file, destFile, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                allSuccess = false;
                logger?.LogWarning(ex, "[WineRunner] Failed to copy '{Source}' to '{Destination}' during directory sync.", file, destFile);
            }
        }

        return allSuccess;
    }

    /// <summary>
    /// Determines whether a WINEDLLOVERRIDES value already configures the Direct3D 8 DLL.
    /// Entries are separated by semicolons, and each entry names one or more comma-separated
    /// DLLs before the load order, so only an exact DLL-name match counts: a substring check
    /// would mistake d3d8proxy for d3d8 and leave the wrapper override unset.
    /// </summary>
    /// <param name="existingOverrides">The current WINEDLLOVERRIDES value.</param>
    /// <returns><c>true</c> when an entry already names the Direct3D 8 DLL; otherwise, <c>false</c>.</returns>
    private static bool HasDirect3D8Override(string existingOverrides) =>
        existingOverrides
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(entry => entry
                .Split('=', 2)[0]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(key => string.Equals(key, WineConstants.Direct3D8DllName, StringComparison.OrdinalIgnoreCase));

    private static bool IsExcludedPrefixUser(string dirName) =>
        WineConstants.ExcludedPrefixUserNames.Contains(dirName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Collects the prefix user profiles targeted when mirroring the native Options.ini:
    /// the current login plus every existing non-system profile directory.
    /// </summary>
    /// <param name="usersRoot">The prefix users directory.</param>
    /// <returns>The candidate user profile names, compared case-insensitively.</returns>
    private static HashSet<string> CollectCandidateUsernames(string usersRoot)
    {
        var candidateUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SanitizeUserName(Environment.UserName),
        };

        if (!Directory.Exists(usersRoot))
        {
            return candidateUsernames;
        }

        foreach (var dir in Directory.EnumerateDirectories(usersRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (!string.IsNullOrWhiteSpace(dirName) && !IsExcludedPrefixUser(dirName))
            {
                candidateUsernames.Add(dirName);
            }
        }

        return candidateUsernames;
    }

    /// <summary>
    /// Mirrors the native Options.ini into every candidate user shell folder.
    /// </summary>
    /// <remarks>
    /// Plain Wine resolves the personal shell folder to "My Documents" while Proton
    /// uses "Documents", and on a fresh managed prefix neither directory exists yet to
    /// disambiguate. Guessing one risks the game silently ignoring the mirrored
    /// Options.ini — including the profile's resolution — with no error anywhere, and
    /// the guess can never self-heal because the first run creates the guessed
    /// directory itself. Mirror into both: the game reads exactly one, and the other
    /// copy stays inert.
    /// </remarks>
    /// <param name="sourcePath">The native Options.ini path.</param>
    /// <param name="usersRoot">The prefix users directory.</param>
    /// <param name="candidateUsernames">The user profiles to mirror into.</param>
    /// <param name="dataDirectoryName">The game data directory name.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns><c>true</c> when at least one destination was written; otherwise, <c>false</c>.</returns>
    private static bool MirrorToCandidateUsers(
        string sourcePath,
        string usersRoot,
        HashSet<string> candidateUsernames,
        string dataDirectoryName,
        ILogger? logger = null)
    {
        var mirroredAny = false;
        foreach (var userName in candidateUsernames)
        {
            var userDirectory = Path.Combine(usersRoot, userName);
            foreach (var documentsDirectoryName in new[] { WineConstants.DocumentsDirectoryName, WineConstants.MyDocumentsDirectoryName })
            {
                mirroredAny |= MirrorOptionsIniToShellFolder(sourcePath, userDirectory, documentsDirectoryName, dataDirectoryName, logger);
            }
        }

        return mirroredAny;
    }

    private void ConfigureDirect3DOverride(GameLaunchConfiguration configuration, Dictionary<string, string> environment)
    {
        var workingDir = configuration.WorkingDirectory ?? Path.GetDirectoryName(configuration.ExecutablePath);
        if (string.IsNullOrEmpty(workingDir) || !File.Exists(Path.Combine(workingDir, GameClientConstants.Direct3D8WrapperDll)))
        {
            return;
        }

        if (environment.TryGetValue(WineConstants.DllOverridesEnvironmentVariable, out var existingOverrides))
        {
            if (!HasDirect3D8Override(existingOverrides))
            {
                environment[WineConstants.DllOverridesEnvironmentVariable] = $"{existingOverrides};{WineConstants.Direct3D8DllOverride}";
                logger.LogInformation("Configured Wine DLL override for {Dll} (appended to existing overrides)", GameClientConstants.Direct3D8WrapperDll);
            }
        }
        else
        {
            environment[WineConstants.DllOverridesEnvironmentVariable] = WineConstants.Direct3D8DllOverride;
            logger.LogInformation("Configured Wine DLL override for {Dll}", GameClientConstants.Direct3D8WrapperDll);
        }
    }

    private bool TryFindWineBinary([NotNullWhen(true)] out string? wineBinary)
    {
        wineBinary = options.AbsoluteBinaryPaths
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));

        if (wineBinary is not null)
        {
            return true;
        }

        var pathVariable = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        var pathDirectories = string.IsNullOrEmpty(pathVariable)
            ? []
            : pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var searchDirectories = options.ExtraSearchDirectories
            .Concat(pathDirectories)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToArray();

        foreach (var binaryName in options.BinaryNames)
        {
            var match = searchDirectories
                .Select(d => Path.Combine(d, binaryName))
                .FirstOrDefault(File.Exists);

            if (match is not null)
            {
                wineBinary = match;
                return true;
            }
        }

        return false;
    }

    private void EnsurePrefix()
    {
        try
        {
            Directory.CreateDirectory(options.PrefixPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to create Wine prefix directory {PrefixPath}; Wine will attempt first-run initialization", options.PrefixPath);
        }
    }

    /// <summary>
    /// Determines whether the configured prefix is Proton-managed from the steamuser
    /// profile, a compatdata path, or Proton's tracked-files marker.
    /// </summary>
    /// <remarks>
    /// <c>tracked_files</c> lives in the compatdata root, next to the <c>pfx</c> prefix
    /// directory rather than inside it, so the parent directory is probed as well as
    /// the prefix itself (the latter covers prefix paths pointing at a compatdata root).
    /// </remarks>
    /// <param name="usersRoot">The prefix users directory.</param>
    /// <returns><c>true</c> when the prefix looks Proton-managed; otherwise, <c>false</c>.</returns>
    private bool IsProtonPrefix(string usersRoot)
    {
        if (Directory.Exists(Path.Combine(usersRoot, WineConstants.ProtonUserName)))
        {
            return true;
        }

        if (options.PrefixPath.Contains(WineConstants.CompatDataDirectoryMarker, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parent = Directory.GetParent(options.PrefixPath);
        return File.Exists(Path.Combine(options.PrefixPath, WineConstants.ProtonTrackedFilesMarker))
            || (parent is not null && File.Exists(Path.Combine(parent.FullName, WineConstants.ProtonTrackedFilesMarker)));
    }

    /// <summary>
    /// Writes a baseline native Options.ini so a first launch has settings to mirror.
    /// </summary>
    /// <param name="sourcePath">The native Options.ini path to create.</param>
    /// <param name="args">The launch arguments, used for the bootstrap resolution when present.</param>
    /// <returns><c>true</c> when the baseline was written; otherwise, <c>false</c>.</returns>
    private bool BootstrapNativeOptionsIni(string sourcePath, Dictionary<string, string>? args)
    {
        try
        {
            var dir = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var resolutionLine = $"{GameSettingsIniConstants.ResolutionKey} = {WineConstants.BootstrapResolutionWidth} {WineConstants.BootstrapResolutionHeight}";
            if (args is { Count: > 0 }
                && args.TryGetValue(GameClientConstants.XResolutionArgument, out var xres) && !string.IsNullOrWhiteSpace(xres)
                && args.TryGetValue(GameClientConstants.YResolutionArgument, out var yres) && !string.IsNullOrWhiteSpace(yres))
            {
                resolutionLine = $"{GameSettingsIniConstants.ResolutionKey} = {xres} {yres}";
            }

            File.WriteAllText(sourcePath, $"{resolutionLine}\r\n{GameSettingsIniConstants.IdealStaticGameLODKey} = {WineConstants.BootstrapIdealStaticGameLOD}\r\n");
            logger.LogInformation("Bootstrapped baseline Options.ini at {SourcePath}", sourcePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "Could not bootstrap missing native Options.ini at {SourcePath}", sourcePath);
            return false;
        }
    }

    private void MirrorOptionsIni(GameLaunchConfiguration configuration)
    {
        if (configuration.GameType is null || string.IsNullOrWhiteSpace(configuration.NativeOptionsIniPath))
        {
            return;
        }

        var sourcePath = configuration.NativeOptionsIniPath;
        if (!File.Exists(sourcePath) && !BootstrapNativeOptionsIni(sourcePath, configuration.Arguments))
        {
            return;
        }

        try
        {
            var dataDirectoryName = configuration.GameType == GameType.ZeroHour
                ? MapManagerConstants.ZeroHourDataDirectoryName
                : MapManagerConstants.GeneralsDataDirectoryName;

            var usersRoot = Path.Combine(
                options.PrefixPath,
                WineConstants.DriveCDirectoryName,
                WineConstants.PrefixUsersDirectoryName);

            var candidateUsernames = CollectCandidateUsernames(usersRoot);
            if (IsProtonPrefix(usersRoot))
            {
                candidateUsernames.Add(WineConstants.ProtonUserName);
            }

            if (MirrorToCandidateUsers(sourcePath, usersRoot, candidateUsernames, dataDirectoryName, logger))
            {
                logger.LogInformation("Mirrored Options.ini and user data into Wine prefix for {GameType}", configuration.GameType);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to mirror Options.ini into Wine prefix; continuing launch with prefix defaults");
        }
    }
}
