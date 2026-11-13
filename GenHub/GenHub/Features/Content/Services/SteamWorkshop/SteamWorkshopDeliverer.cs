using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Delivers Steam Workshop content from locally subscribed workshop folders.
/// Workshop files are downloaded by the Steam client on subscribe; GenHub imports
/// them from the subscribed content directory into the staging area.
/// </summary>
/// <param name="installationService">The game installation service used to locate Steam libraries.</param>
/// <param name="logger">The logger.</param>
/// <param name="clientDownloader">Optional client downloader for direct in-app downloading when not locally cached.</param>
public class SteamWorkshopDeliverer(
    IGameInstallationService installationService,
    ILogger<SteamWorkshopDeliverer> logger,
    ISteamWorkshopClientDownloader? clientDownloader = null) : IContentDeliverer
{
    /// <inheritdoc />
    public string SourceName => SteamWorkshopConstants.DelivererSourceName;

    /// <inheritdoc />
    public string Description => SteamWorkshopConstants.DelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        if (manifest == null)
        {
            return false;
        }

        return string.Equals(manifest.Publisher?.PublisherType, SteamWorkshopConstants.PublisherType, StringComparison.OrdinalIgnoreCase)
            && SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(manifest.OriginalContentId, out _);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageManifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        if (!SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(packageManifest.OriginalContentId, out var publishedFileId))
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Manifest {packageManifest.Id} is not a Steam Workshop manifest.");
        }

        try
        {
            var appId = SteamWorkshopHelper.AppIdForGame(packageManifest.TargetGame);
            var sourceDirectory = await FindSubscribedContentDirectoryAsync(appId, publishedFileId, cancellationToken).ConfigureAwait(false);
            if (sourceDirectory == null)
            {
                if (clientDownloader != null)
                {
                    logger.LogInformation(
                        "Steam Workshop item {PublishedFileId} not found locally on disk. Initiating direct 1-click in-app download.",
                        publishedFileId);

                    var downloadResult = await clientDownloader.DownloadWorkshopItemAsync(
                        appId,
                        publishedFileId,
                        targetDirectory,
                        progress,
                        cancellationToken).ConfigureAwait(false);

                    if (!downloadResult.Success)
                    {
                        return OperationResult<ContentManifest>.CreateFailure(downloadResult.Errors);
                    }

                    logger.LogInformation(
                        "Successfully delivered Steam Workshop item {PublishedFileId} via direct download to {TargetDirectory}",
                        publishedFileId,
                        targetDirectory);

                    return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
                }

                var detailsUrl = SteamWorkshopHelper.BuildFileDetailsUrl(publishedFileId);
                logger.LogWarning("Steam Workshop item {PublishedFileId} is not subscribed locally", publishedFileId);
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Subscribe to '{packageManifest.Name}' in the Steam client first, then retry. Workshop page: {detailsUrl}");
            }

            await CopyContentDirectoryAsync(sourceDirectory, targetDirectory, progress, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Delivered Steam Workshop item {PublishedFileId} from {SourceDirectory}",
                publishedFileId,
                sourceDirectory);

            return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to deliver Steam Workshop item {PublishedFileId}", publishedFileId);
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied while delivering Steam Workshop item {PublishedFileId}", publishedFileId);
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: access denied. {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Invalid content path while delivering Steam Workshop item {PublishedFileId}", publishedFileId);
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!CanDeliver(manifest))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(manifest.OriginalContentId, out var publishedFileId);
        var appId = SteamWorkshopHelper.AppIdForGame(manifest.TargetGame);

        try
        {
            var sourceDirectory = await FindSubscribedContentDirectoryAsync(appId, publishedFileId, cancellationToken).ConfigureAwait(false);
            if (sourceDirectory != null)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            return OperationResult<bool>.CreateSuccess(clientDownloader != null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to probe subscribed Steam Workshop content for validation");
            return OperationResult<bool>.CreateFailure($"Content validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied while probing subscribed Steam Workshop content for validation");
            return OperationResult<bool>.CreateFailure($"Content validation failed: access denied. {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves and validates a target file path against directory traversal attacks.
    /// </summary>
    /// <param name="targetDirectory">The target base directory.</param>
    /// <param name="relativePath">The relative path to resolve within the target directory.</param>
    /// <returns>The fully qualified destination path.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the path escapes the target directory.</exception>
    internal static string ResolveTargetPath(string targetDirectory, string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/');
        var targetRoot = Path.GetFullPath(targetDirectory);
        var targetPath = Path.GetFullPath(normalizedRelativePath, targetRoot);
        var relativeTargetPath = Path.GetRelativePath(targetRoot, targetPath);

        if (relativeTargetPath.Equals("..", StringComparison.Ordinal) ||
            relativeTargetPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativeTargetPath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeTargetPath))
        {
            throw new InvalidOperationException($"Content path '{relativePath}' resolves outside target directory.");
        }

        return targetPath;
    }

    /// <summary>
    /// Enumerates well-known Steam client roots for the current platform.
    /// Virtual so tests can suppress host filesystem probing.
    /// </summary>
    /// <returns>Candidate Steam client root directories.</returns>
    protected virtual IEnumerable<string> EnumerateWellKnownSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, SteamConstants.SteamDirectoryName);
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, SteamConstants.SteamDirectoryName);
            }

            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            yield break;
        }

        yield return Path.Combine(home, SteamConstants.DotSteamDirectoryName, SteamConstants.SteamClientLinkName);
        yield return Path.Combine(home, SteamConstants.DotLocalDirectoryName, SteamConstants.ShareDirectoryName, SteamConstants.SteamDirectoryName);
        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, SteamConstants.MacLibraryDirectoryName, SteamConstants.MacApplicationSupportDirectoryName, SteamConstants.SteamDirectoryName);
        }
    }

    private static string? FindSteamAppsAncestor(string? installationPath)
    {
        if (string.IsNullOrWhiteSpace(installationPath))
        {
            return null;
        }

        var current = installationPath;
        for (var depth = 0; depth < 4 && !string.IsNullOrEmpty(current); depth++)
        {
            if (string.Equals(Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), SteamConstants.SteamAppsDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static IEnumerable<int> EnumerateCandidateAppIds(int appId)
    {
        yield return appId;

        var alternate = appId == SteamWorkshopConstants.GeneralsAppId
            ? SteamWorkshopConstants.ZeroHourAppId
            : SteamWorkshopConstants.GeneralsAppId;
        if (alternate != appId)
        {
            yield return alternate;
        }
    }

    private static void AddDirectoryIfMissing(List<string> directories, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && !directories.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            directories.Add(candidate);
        }
    }

    private static async Task CopyContentDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(file);
        }

        files.Sort(StringComparer.Ordinal);

        var totalFiles = files.Count;
        if (totalFiles == 0)
        {
            ReportDeliveryProgress(progress, 0, 0, string.Empty);
            return;
        }

        var newlyCreatedFiles = new List<string>();
        var backedUpFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var completed = false;
        try
        {
            var processedFiles = 0;
            foreach (var sourcePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
                var targetPath = ResolveTargetPath(targetDirectory, relativePath);
                PrepareTargetDirectory(targetPath);
                BackupOrTrackTarget(targetPath, backedUpFiles, newlyCreatedFiles);

                await CopyFileAtomicallyAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
                processedFiles++;
                ReportDeliveryProgress(progress, processedFiles, totalFiles, relativePath);
            }

            completed = true;
        }
        finally
        {
            if (!completed)
            {
                RollbackDelivery(newlyCreatedFiles, backedUpFiles);
            }
            else
            {
                CleanupBackups(backedUpFiles.Values);
            }
        }
    }

    private static void PrepareTargetDirectory(string targetPath)
    {
        var targetParent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetParent))
        {
            Directory.CreateDirectory(targetParent);
        }
    }

    private static void BackupOrTrackTarget(string targetPath, Dictionary<string, string> backedUpFiles, List<string> newlyCreatedFiles)
    {
        if (File.Exists(targetPath))
        {
            if (!backedUpFiles.ContainsKey(targetPath))
            {
                var backupPath = $"{targetPath}.{Guid.NewGuid():N}{SteamWorkshopConstants.BackupFileExtension}";
                File.Copy(targetPath, backupPath, overwrite: true);
                backedUpFiles[targetPath] = backupPath;
            }
        }
        else if (!newlyCreatedFiles.Contains(targetPath, StringComparer.Ordinal))
        {
            newlyCreatedFiles.Add(targetPath);
        }
    }

    private static void RollbackDelivery(List<string> newlyCreatedFiles, Dictionary<string, string> backedUpFiles)
    {
        foreach (var path in newlyCreatedFiles)
        {
            DeleteFileBestEffort(path);
        }

        foreach (var (targetPath, backupPath) in backedUpFiles)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, targetPath, overwrite: true);
                }
            }
            catch (IOException)
            {
                // Best-effort restore
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort restore
            }
        }
    }

    private static void CleanupBackups(IEnumerable<string> backupPaths)
    {
        foreach (var backupPath in backupPaths)
        {
            DeleteFileBestEffort(backupPath);
        }
    }

    private static void ReportDeliveryProgress(
        IProgress<ContentAcquisitionProgress>? progress,
        int processedFiles,
        int totalFiles,
        string relativePath)
    {
        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Delivering,
            ProgressPercentage = totalFiles == 0 ? 100 : (double)processedFiles / totalFiles * 100,
            CurrentOperation = string.IsNullOrEmpty(relativePath) ? "Importing workshop content" : $"Importing {relativePath}",
            CurrentFile = relativePath,
            FilesProcessed = processedFiles,
            TotalFiles = totalFiles,
        });
    }

    private static async Task CopyFileAtomicallyAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}{SteamWorkshopConstants.StagingFileExtension}";
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            DeleteFileBestEffort(tempPath);
            throw;
        }
    }

    private static void DeleteFileBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the staging file.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the staging file.
        }
    }

    private async Task<string?> FindSubscribedContentDirectoryAsync(int appId, string publishedFileId, CancellationToken cancellationToken)
    {
        var steamAppsDirectories = await EnumerateSteamAppsDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidateAppId in EnumerateCandidateAppIds(appId))
        {
            foreach (var steamApps in steamAppsDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = Path.Combine(
                    steamApps,
                    SteamWorkshopConstants.WorkshopDirectoryName,
                    SteamWorkshopConstants.WorkshopContentDirectoryName,
                    candidateAppId.ToString(),
                    publishedFileId);
                if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                {
                    if (candidateAppId != appId)
                    {
                        logger.LogInformation(
                            "Steam Workshop item {PublishedFileId} found under AppID {FoundAppId} instead of {RequestedAppId}",
                            publishedFileId,
                            candidateAppId,
                            appId);
                    }

                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> EnumerateSteamAppsDirectoriesAsync(CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        foreach (var installation in await GetSteamInstallationsAsync(cancellationToken).ConfigureAwait(false))
        {
            var steamApps = FindSteamAppsAncestor(installation.InstallationPath);
            AddDirectoryIfMissing(directories, steamApps);
        }

        foreach (var root in EnumerateWellKnownSteamRoots())
        {
            AddDirectoryIfMissing(directories, Path.Combine(root, SteamConstants.SteamAppsDirectoryName));
        }

        return directories.Where(Directory.Exists).ToList();
    }

    private async Task<IReadOnlyList<GameInstallation>> GetSteamInstallationsAsync(CancellationToken cancellationToken)
    {
        var cached = installationService.CachedInstallations;
        if (cached != null)
        {
            return cached.Where(installation => installation.InstallationType == GameInstallationType.Steam).ToList();
        }

        var result = await installationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            logger.LogDebug("Steam installation lookup failed: {Error}", result.FirstError);
            return [];
        }

        return result.Data.Where(installation => installation.InstallationType == GameInstallationType.Steam).ToList();
    }
}
