namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that prevents OneDrive from syncing game folders.
/// Relocates game user data out of OneDrive and creates local symbolic links to prevent cloud sync locks and crashes.
/// </summary>
public class OneDriveFix(ILogger<OneDriveFix> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> CommonFolderNames = GameSettingsConstants.FolderNames.AllUserDataFolderNames;

    /// <inheritdoc/>
    public override string Id => "OneDriveFix";

    /// <inheritdoc/>
    public override string Title => "Prevent OneDrive Sync (Move & Symlink)";

    /// <inheritdoc/>
    public override string Description => "Relocates game user data out of OneDrive and creates local symbolic links to prevent cloud sync locks and crashes.";

    /// <inheritdoc/>
    public override string DetailedDescription => "OneDrive cloud synchronization locks active game files and offloads save data, leading to severe stuttering, lost replays, and Technical Difficulties crashes. This fix safely migrates your Generals and Zero Hour data to local storage and creates NTFS directory junctions with local file pinning.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(IsOneDriveRedirected() && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (!IsOneDriveRedirected()) return Task.FromResult(false);

            foreach (var folderName in CommonFolderNames)
            {
                if (!IsFolderCorrectlySymlinked(folderName))
                {
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking OneDrive protection status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            if (!IsOneDriveRedirected())
            {
                details.Add("OneDrive redirection not detected. No action needed.");
                return new ActionSetResult(true, null, details);
            }

            details.Add("Starting transactional OneDrive folder relocation...");
            var cloudDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var localDocs = GetLocalDocumentsPath();

            if (!Directory.Exists(localDocs))
            {
                Directory.CreateDirectory(localDocs);
                details.Add($"Created local Documents folder: {localDocs}");
            }

            var backupBaseDir = Path.Combine(localDocs, "_GenHub_OneDrive_Backups", $"Backup_{DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)}");
            int foldersProcessed = 0;

            foreach (var folderName in CommonFolderNames)
            {
                ct.ThrowIfCancellationRequested();
                var processed = await ProcessFolderAsync(folderName, cloudDocs, localDocs, backupBaseDir, details, ct);
                if (processed)
                {
                    foldersProcessed++;
                }
            }

            details.Add(string.Empty);
            details.Add($"✓ Processed {foldersProcessed} folders for OneDrive compatibility with full safety backup");
            details.Add("✓ OneDrive relocation completed successfully");

            return new ActionSetResult(true, null, details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying OneDrive protection");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            var cloudDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var localDocs = GetLocalDocumentsPath();

            int restoredCount = 0;
            foreach (var folderName in CommonFolderNames)
            {
                var cloudPath = Path.Combine(cloudDocs, folderName);
                var localPath = Path.Combine(localDocs, folderName);

                if (Directory.Exists(cloudPath) && IsSymbolicLink(cloudPath))
                {
                    try
                    {
                        Directory.Delete(cloudPath);
                        details.Add($"✓ Removed symbolic link/junction for '{folderName}' in OneDrive");

                        if (Directory.Exists(localPath))
                        {
                            Directory.CreateDirectory(cloudPath);
                            CopyDirectoryRecursive(localPath, cloudPath);
                            details.Add($"✓ Restored original files for '{folderName}' into OneDrive");
                        }

                        restoredCount++;
                    }
                    catch (IOException ex)
                    {
                        logger.LogWarning(ex, "Failed to restore OneDrive folder {Folder}", folderName);
                        details.Add($"⚠ Warning restoring '{folderName}': {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        logger.LogWarning(ex, "Access denied restoring OneDrive folder {Folder}", folderName);
                        details.Add($"⚠ Access denied restoring '{folderName}'");
                    }
                }
            }

            if (restoredCount == 0)
            {
                details.Add("ℹ No active OneDrive symlinks found to undo.");
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error undoing OneDrive folder relocation");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private static void CopyDirectoryRecursive(string source, string target)
    {
        foreach (var dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dirPath);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var filePath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, filePath);
            var targetFile = Path.Combine(target, relative);
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            File.Copy(filePath, targetFile, overwrite: true);
        }
    }

    private static (int Copied, long TotalBytes) CopyDirectoryWithVerification(string source, string target)
    {
        int count = 0;
        long bytes = 0;

        foreach (var dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dirPath);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var filePath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, filePath);
            var targetFile = Path.Combine(target, relative);
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

            var srcInfo = new FileInfo(filePath);
            if (!File.Exists(targetFile) || srcInfo.LastWriteTimeUtc > new FileInfo(targetFile).LastWriteTimeUtc)
            {
                File.Copy(filePath, targetFile, overwrite: true);
            }

            var tgtInfo = new FileInfo(targetFile);
            if (!tgtInfo.Exists || tgtInfo.Length != srcInfo.Length)
            {
                throw new IOException($"Copy verification failed for file '{relative}'. Source size: {srcInfo.Length}, Target size: {tgtInfo.Length}");
            }

            count++;
            bytes += srcInfo.Length;
        }

        return (count, bytes);
    }

    private static bool VerifyDirectoryIntegrity(string source, string target)
    {
        var sourceFiles = Directory.GetFiles(source, "*.*", SearchOption.AllDirectories);
        foreach (var srcFile in sourceFiles)
        {
            var relative = Path.GetRelativePath(source, srcFile);
            var tgtFile = Path.Combine(target, relative);
            if (!File.Exists(tgtFile)) return false;

            var srcInfo = new FileInfo(srcFile);
            var tgtInfo = new FileInfo(tgtFile);
            if (srcInfo.Length != tgtInfo.Length) return false;
        }

        return true;
    }

    private static int CountFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).Length
            : 0;
    }

    private static bool IsOneDriveRedirected()
    {
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return myDocs.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLocalDocumentsPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            var pathInfo = new DirectoryInfo(path);
            return pathInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFolderCorrectlySymlinked(string folderName)
    {
        var cloudDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localDocs = GetLocalDocumentsPath();
        var cloudPath = Path.Combine(cloudDocs, folderName);
        var localPath = Path.Combine(localDocs, folderName);

        if (!Directory.Exists(cloudPath) && !Directory.Exists(localPath)) return true;

        if (Directory.Exists(localPath) && IsSymbolicLink(cloudPath))
        {
            return true;
        }

        if (Directory.Exists(cloudPath) && !IsSymbolicLink(cloudPath)) return false;

        return false;
    }

    private async Task<bool> ProcessFolderAsync(
        string folderName,
        string cloudDocs,
        string localDocs,
        string backupBaseDir,
        List<string> details,
        CancellationToken ct)
    {
        var cloudPath = Path.Combine(cloudDocs, folderName);
        var localPath = Path.Combine(localDocs, folderName);
        string? currentCloudArchive = null;

        if (!Directory.Exists(cloudPath) && !Directory.Exists(localPath))
        {
            return false;
        }

        if (IsFolderCorrectlySymlinked(folderName))
        {
            details.Add($"✓ Folder '{folderName}' is already correctly symlinked.");
            return false;
        }

        try
        {
            if (Directory.Exists(cloudPath) && !IsSymbolicLink(cloudPath))
            {
                var backupFolder = Path.Combine(backupBaseDir, folderName);
                details.Add($"Creating safety backup of '{folderName}' to {backupFolder}...");
                Directory.CreateDirectory(backupFolder);

                CopyDirectoryRecursive(cloudPath, backupFolder);
                details.Add($"  ✓ Backup created ({CountFiles(backupFolder)} files)");

                if (!Directory.Exists(localPath))
                {
                    Directory.CreateDirectory(localPath);
                }

                details.Add($"  Copying and verifying files into '{localPath}'...");
                var (copied, totalBytes) = CopyDirectoryWithVerification(cloudPath, localPath);
                details.Add($"  ✓ Copied and verified {copied} files ({totalBytes / 1024.0 / 1024.0:F2} MB)");

                if (!VerifyDirectoryIntegrity(cloudPath, localPath))
                {
                    throw new IOException($"Integrity check failed between '{cloudPath}' and '{localPath}'. Aborting to prevent data loss.");
                }

                var cloudArchive = cloudPath + ".archived_" + DateTime.UtcNow.Ticks;
                currentCloudArchive = cloudArchive;
                Directory.Move(cloudPath, cloudArchive);
                details.Add($"  ✓ Original cloud folder archived to {Path.GetFileName(cloudArchive)}");
            }

            if (Directory.Exists(localPath) && !Directory.Exists(cloudPath))
            {
                details.Add($"Creating link in OneDrive for '{folderName}'...");
                bool linkSuccess = CreateSymlinkOrJunction(cloudPath, localPath, details);
                if (!linkSuccess)
                {
                    TryRestoreArchive(currentCloudArchive, cloudPath, details);
                    throw new IOException($"Failed to create symlink or junction for '{folderName}'. Restored original folder from archive.");
                }
            }

            await ApplyPinAttributeAsync(localPath, ct);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O error processing folder {LocalPath}", localPath);
            details.Add($"✗ Failed to process '{folderName}': {ex.Message}");
            TryRestoreArchive(currentCloudArchive, cloudPath, details);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied processing folder {LocalPath}", localPath);
            details.Add($"✗ Access denied processing '{folderName}'");
            TryRestoreArchive(currentCloudArchive, cloudPath, details);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unexpected error processing folder {LocalPath}", localPath);
            details.Add($"✗ Error processing '{folderName}': {ex.Message}");
            TryRestoreArchive(currentCloudArchive, cloudPath, details);
            return false;
        }
    }

    private void TryRestoreArchive(string? currentCloudArchive, string cloudPath, List<string> details)
    {
        if (string.IsNullOrEmpty(currentCloudArchive) || !Directory.Exists(currentCloudArchive) || Directory.Exists(cloudPath))
        {
            return;
        }

        try
        {
            Directory.Move(currentCloudArchive, cloudPath);
            details.Add("  ✓ Restored original cloud folder from archive");
        }
        catch (IOException rollbackEx)
        {
            logger.LogError(rollbackEx, "Failed to rollback archived folder {Archive} to {CloudPath}", currentCloudArchive, cloudPath);
        }
        catch (UnauthorizedAccessException rollbackEx)
        {
            logger.LogError(rollbackEx, "Access denied rolling back archived folder {Archive} to {CloudPath}", currentCloudArchive, cloudPath);
        }
    }

    private bool CreateSymlinkOrJunction(string linkPath, string targetPath, List<string> details)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            details.Add($"  ✓ Symlink created: {linkPath} -> {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CreateSymbolicLink failed, falling back to directory junction for {Path}", linkPath);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                if (p?.ExitCode == ProcessConstants.ExitCodeSuccess)
                {
                    details.Add($"  ✓ Junction created: {linkPath} -> {targetPath}");
                    return true;
                }
            }
            catch (Exception juncEx)
            {
                logger.LogWarning(juncEx, "Junction creation failed for {Path}", linkPath);
            }

            details.Add($"  ✗ Failed to create link: {linkPath}");
            return false;
        }
    }

    private async Task ApplyPinAttributeAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(path)) return;

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-WindowStyle Hidden -NoProfile -NonInteractive -Command \"attrib +P -U '{path.Replace("'", "''")}' /S /D\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply pin attributes to {Path}", path);
        }
    }
}
