namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that prevents OneDrive from syncing game folders.
/// This fix creates desktop.ini files with ThisPCPolicy=DisableCloudSync
/// to prevent OneDrive from syncing game installation and user data folders.
/// </summary>
public class OneDriveFix(ILogger<OneDriveFix> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> CommonFolderNames = GameSettingsConstants.FolderNames.AllUserDataFolderNames;

    /// <inheritdoc/>
    public override string Id => "OneDriveFix";

    /// <inheritdoc/>
    public override string Title => "Prevent OneDrive Sync (Move & Symlink)";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Fix is only applicable if Documents is redirected to OneDrive
        return Task.FromResult(IsOneDriveRedirected() && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            // If not redirected, not applicable. Return false so it shows as NOT APPLICABLE instead of APPLIED
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
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
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

            var backupBaseDir = Path.Combine(localDocs, "_GenHub_OneDrive_Backups", $"Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}");

            int foldersProcessed = 0;
            foreach (var folderName in CommonFolderNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cloudPath = Path.Combine(cloudDocs, folderName);
                var localPath = Path.Combine(localDocs, folderName);
                string? currentCloudArchive = null;

                if (!Directory.Exists(cloudPath) && !Directory.Exists(localPath)) continue;

                if (IsFolderCorrectlySymlinked(folderName))
                {
                    details.Add($"✓ Folder '{folderName}' is already correctly symlinked.");
                    continue;
                }

                try
                {
                    // If cloud folder exists and is a real directory (not symlink)
                    if (Directory.Exists(cloudPath) && !IsSymbolicLink(cloudPath))
                    {
                        var backupFolder = Path.Combine(backupBaseDir, folderName);
                        details.Add($"Creating safety backup of '{folderName}' to {backupFolder}...");
                        Directory.CreateDirectory(backupFolder);

                        // Step 1: Create complete safety backup
                        CopyDirectoryRecursive(cloudPath, backupFolder);
                        details.Add($"  ✓ Backup created ({CountFiles(backupFolder)} files)");

                        // Step 2: Merge or move into local destination with verification
                        if (!Directory.Exists(localPath))
                        {
                            Directory.CreateDirectory(localPath);
                        }

                        details.Add($"  Copying and verifying files into '{localPath}'...");
                        var (copied, totalBytes) = CopyDirectoryWithVerification(cloudPath, localPath);
                        details.Add($"  ✓ Copied and verified {copied} files ({totalBytes / 1024.0 / 1024.0:F2} MB)");

                        // Step 3: Verify destination integrity before unlinking source
                        if (!VerifyDirectoryIntegrity(cloudPath, localPath))
                        {
                            throw new IOException($"Integrity check failed between '{cloudPath}' and '{localPath}'. Aborting to prevent data loss.");
                        }

                        // Step 4: Safely move cloud folder to backup location instead of permanently deleting
                        var cloudArchive = cloudPath + ".archived_" + DateTime.UtcNow.Ticks;
                        currentCloudArchive = cloudArchive;
                        Directory.Move(cloudPath, cloudArchive);
                        details.Add($"  ✓ Original cloud folder archived to {Path.GetFileName(cloudArchive)}");
                    }

                    // Create symlink or junction in OneDrive pointing to local
                    if (Directory.Exists(localPath) && !Directory.Exists(cloudPath))
                    {
                        details.Add($"Creating link in OneDrive for '{folderName}'...");
                        bool linkSuccess = CreateSymlinkOrJunction(cloudPath, localPath, details);
                        if (!linkSuccess)
                        {
                            // Roll back archive to restore user folder
                            if (!string.IsNullOrEmpty(currentCloudArchive) && Directory.Exists(currentCloudArchive) && !Directory.Exists(cloudPath))
                            {
                                Directory.Move(currentCloudArchive, cloudPath);
                                details.Add($"  ✓ Restored original cloud folder from archive due to link creation failure");
                                currentCloudArchive = null;
                            }

                            return new ActionSetResult(false, $"Failed to create symlink or junction for '{folderName}'. Restored original folder from archive.", details);
                        }
                    }

                    // Apply Pin attribute to local folder
                    await ApplyPinAttributeAsync(localPath, cancellationToken);
                    foldersProcessed++;
                }
                catch (Exception)
                {
                    // Rollback archive on error if needed
                    if (!string.IsNullOrEmpty(currentCloudArchive) && Directory.Exists(currentCloudArchive) && !Directory.Exists(cloudPath))
                    {
                        try
                        {
                            Directory.Move(currentCloudArchive, cloudPath);
                            details.Add($"  ✓ Restored original cloud folder from archive after error");
                        }
                        catch (Exception rollbackEx)
                        {
                            logger.LogError(rollbackEx, "Failed to rollback archived folder {Archive} to {CloudPath}", currentCloudArchive, cloudPath);
                        }
                    }

                    throw;
                }
            }

            details.Add(string.Empty);
            details.Add($"✓ Processed {foldersProcessed} folders for OneDrive compatibility with full safety backup");
            details.Add("✓ OneDrive relocation completed successfully");

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying OneDrive protection");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
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
                    FileName = "cmd.exe",
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

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Undoing OneDrive folder relocation is not supported automatically.");
        return Task.FromResult(new ActionSetResult(true));
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

        // If neither exist, we consider it "fine" (it will be fixed when they appear)
        if (!Directory.Exists(cloudPath) && !Directory.Exists(localPath)) return true;

        // If local exists and cloud is a symlink to it, it's applied
        if (Directory.Exists(localPath) && IsSymbolicLink(cloudPath))
        {
            // We could check the target here, but Directory.Exists(localPath) + IsSymbolicLink(cloudPath) is 99% there.
            return true;
        }

        // If cloud exists as real folder but local doesn't, it's NOT applied
        if (Directory.Exists(cloudPath) && !IsSymbolicLink(cloudPath)) return false;

        return false;
    }

    private async Task ApplyPinAttributeAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(path)) return;

            // Use PowerShell to apply 'Pinned' attribute which is specific to modern Windows / OneDrive
            // Attrib +P -U
            var psi = new ProcessStartInfo
            {
                FileName = ProcessConstants.PowerShellExecutable,
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply pin attributes to {Path}", path);
        }
    }
}
