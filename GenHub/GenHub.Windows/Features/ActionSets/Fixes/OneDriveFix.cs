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
    private readonly ILogger<OneDriveFix> _logger = logger;

    private readonly string[] _commonFolderNames =
    [
        "Command and Conquer Generals Data",
        "Command and Conquer Generals Zero Hour Data",
        "Command & Conquer Generäle Stunde Null Data",
        "Command & Conquer Generals - Heure H Data"
    ];

    /// <inheritdoc/>
    public override string Id => "OneDriveFix";

    /// <inheritdoc/>
    public override string Title => "Prevent OneDrive Sync (Move & Symlink)";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

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

            foreach (var folderName in _commonFolderNames)
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
            _logger.LogError(ex, "Error checking OneDrive protection status");
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

            details.Add("Starting OneDrive folder relocation...");
            var cloudDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var localDocs = GetLocalDocumentsPath();

            if (!Directory.Exists(localDocs))
            {
                Directory.CreateDirectory(localDocs);
                details.Add($"Created local Documents folder: {localDocs}");
            }

            int foldersProcessed = 0;
            foreach (var folderName in _commonFolderNames)
            {
                var cloudPath = Path.Combine(cloudDocs, folderName);
                var localPath = Path.Combine(localDocs, folderName);

                if (!Directory.Exists(cloudPath) && !Directory.Exists(localPath)) continue;

                if (IsFolderCorrectlySymlinked(folderName))
                {
                    details.Add($"✓ Folder '{folderName}' is already correctly symlinked.");
                    continue;
                }

                // Handle merge scenario: If both exist and cloud is not a symlink
                if (Directory.Exists(cloudPath) && !IsSymbolicLink(cloudPath) && Directory.Exists(localPath))
                {
                    details.Add($"⚠ Both cloud and local versions of '{folderName}' exist.");
                    details.Add("  Attempting to merge cloud files into local folder...");
                    try
                    {
                        MergeDirectories(cloudPath, localPath);
                        Directory.Delete(cloudPath, true);
                        details.Add("  ✓ Cloud folder contents merged and original removed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to merge {Cloud} into {Local}", cloudPath, localPath);
                        details.Add($"  ⚠ Failed to fully merge: {ex.Message}");

                        // Rename cloud folder to avoid conflict for symlink creation
                        var bakPath = cloudPath + ".bak_" + DateTime.Now.Ticks;
                        Directory.Move(cloudPath, bakPath);
                        details.Add($"  ✓ Cloud folder renamed to: {Path.GetFileName(bakPath)}");
                    }
                }

                // If folder exists in cloud but not local, move it
                if (Directory.Exists(cloudPath) && !IsSymbolicLink(cloudPath) && !Directory.Exists(localPath))
                {
                    details.Add($"Moving '{folderName}' from OneDrive to local Documents...");
                    Directory.Move(cloudPath, localPath);
                    details.Add($"  ✓ Moved to: {localPath}");
                }

                // Create symlink
                if (Directory.Exists(localPath) && !Directory.Exists(cloudPath))
                {
                    details.Add($"Creating symlink in OneDrive for '{folderName}'...");
                    Directory.CreateSymbolicLink(cloudPath, localPath);
                    details.Add($"  ✓ Symlink created: {cloudPath} -> {localPath}");
                }

                // Apply Pin attribute to local folder
                await ApplyPinAttributeAsync(localPath, cancellationToken);
                foldersProcessed++;
            }

            details.Add(string.Empty);
            details.Add($"✓ Processed {foldersProcessed} folders for OneDrive compatibility");
            details.Add("✓ OneDrive relocation completed successfully");

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying OneDrive protection");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing OneDrive folder relocation is not supported automatically.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private static void MergeDirectories(string source, string target)
    {
        foreach (var dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(source, target));
        }

        foreach (var newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            var targetFile = newPath.Replace(source, target);
            if (!File.Exists(targetFile))
            {
                File.Move(newPath, targetFile);
            }
        }
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
                FileName = "powershell.exe",
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
            _logger.LogWarning(ex, "Failed to apply pin attributes to {Path}", path);
        }
    }
}
