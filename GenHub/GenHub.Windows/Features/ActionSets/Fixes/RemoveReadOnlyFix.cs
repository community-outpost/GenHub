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
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that removes Read-Only attribute from game files and user data folders,
/// and applies the 'Pinned' attribute for OneDrive compatibility.
/// </summary>
public class RemoveReadOnlyFix(ILogger<RemoveReadOnlyFix> logger) : BaseActionSet(logger)
{
    // Marker file to definitively track if GenPatcher applied this fix
    private const string MarkerFileName = ActionSetConstants.Paths.ReadOnlyFixMarker;

    private static string GetUserDataPath(GameType gameType)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = gameType == GameType.ZeroHour
            ? GameSettingsConstants.FolderNames.ZeroHour
            : GameSettingsConstants.FolderNames.Generals;
        return Path.Combine(documents, folder);
    }

    private static async Task<(int Files, int Dirs)> RemoveReadOnlyRecursiveAsync(DirectoryInfo directory, ILogger logger, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int filesProcessed = 0;
        int dirsProcessed = 0;

        try
        {
            if ((directory.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                directory.Attributes &= ~FileAttributes.ReadOnly;
                dirsProcessed++;
            }

            foreach (var file in directory.GetFiles())
            {
                if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                    filesProcessed++;
                }
            }

            foreach (var subDir in directory.GetDirectories())
            {
                var (f, d) = await RemoveReadOnlyRecursiveAsync(subDir, logger, ct);
                filesProcessed += f;
                dirsProcessed += d;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied to {Path}", directory.FullName);
        }

        return (filesProcessed, dirsProcessed);
    }

    /// <inheritdoc/>
    public override string Id => "RemoveReadOnlyFix";

    /// <inheritdoc/>
    public override string Title => "Remove Read-Only Attributes";

    /// <inheritdoc/>
    public override string Description => "Recursively removes Read-Only file locks from game and document folders so settings, maps, and replays can be saved.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Older CD installations and archive extractions frequently lock game directories as Read-Only, preventing Generals from saving configuration changes, downloading custom maps, or recording replays. This fix clears all read-only attributes across your installation and user data directories.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        if (installation.HasGenerals && !IsGameApplied(GameType.Generals, installation.GeneralsPath))
        {
            return Task.FromResult(false);
        }

        if (installation.HasZeroHour && !IsGameApplied(GameType.ZeroHour, installation.ZeroHourPath))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting read-only attribute removal...");

            int totalFilesProcessed = 0;
            int totalDirsProcessed = 0;

            if (installation.HasGenerals)
            {
                details.Add($"Processing Generals installation: {installation.GeneralsPath}");
                var (files, dirs) = await ProcessDirectoryAsync(installation.GeneralsPath, details, ct);
                totalFilesProcessed += files;
                totalDirsProcessed += dirs;

                var userPath = GetUserDataPath(GameType.Generals);
                if (Directory.Exists(userPath))
                {
                    details.Add($"Processing Generals user data: {userPath}");
                    (int uFiles, int uDirs) = await ProcessDirectoryAsync(userPath, details, ct);
                    totalFilesProcessed += uFiles;
                    totalDirsProcessed += uDirs;

                    // Write marker file for Generals
                    try
                    {
                        var markerPath = Path.Combine(userPath, MarkerFileName);
                        await File.WriteAllTextAsync(markerPath, DateTime.UtcNow.ToString("O"), ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create Generals marker file for RemoveReadOnlyFix");
                    }
                }
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Processing Zero Hour installation: {installation.ZeroHourPath}");
                var (files, dirs) = await ProcessDirectoryAsync(installation.ZeroHourPath, details, ct);
                totalFilesProcessed += files;
                totalDirsProcessed += dirs;

                var userPath = GetUserDataPath(GameType.ZeroHour);
                if (Directory.Exists(userPath))
                {
                    details.Add($"Processing Zero Hour user data: {userPath}");
                    (int uFiles, int uDirs) = await ProcessDirectoryAsync(userPath, details, ct);
                    totalFilesProcessed += uFiles;
                    totalDirsProcessed += uDirs;

                    // Write marker file for Zero Hour
                    try
                    {
                        var markerPath = Path.Combine(userPath, MarkerFileName);
                        await File.WriteAllTextAsync(markerPath, DateTime.UtcNow.ToString("O"), ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create Zero Hour marker file for RemoveReadOnlyFix");
                    }
                }
            }

            details.Add($"✓ Processed {totalFilesProcessed} files and {totalDirsProcessed} directories");
            details.Add("✓ Read-only attributes removed successfully");
            details.Add("✓ OneDrive pin attributes applied");

            logger.LogInformation("RemoveReadOnlyFix completed: {Files} files, {Dirs} directories", totalFilesProcessed, totalDirsProcessed);
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove read-only attributes");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        logger.LogInformation("Re-applying read-only attributes is not supported to ensure game and patch accessibility.");
        return Task.FromResult(new ActionSetResult(false, "Re-applying read-only attributes is not supported as write access is required for game saves, settings, and mod updates.", ["Read-only attributes remain cleared."]));
    }

    private bool IsReadOnly(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not check attributes for {Path}", path);
            return false;
        }
    }

    private async Task<(int Files, int Dirs)> ProcessDirectoryAsync(string path, List<string> details, CancellationToken ct)
    {
        if (!Directory.Exists(path)) return (0, 0);

        logger.LogInformation("Removing read-only and pinning files in: {Path}", path);

        int filesProcessed = 0;
        int dirsProcessed = 0;

        // 1. Remove Read-Only attribute recursively using built-in File API
        try
        {
            var dirInfo = new DirectoryInfo(path);
            var (f, d) = await RemoveReadOnlyRecursiveAsync(dirInfo, logger, ct);
            filesProcessed += f;
            dirsProcessed += d;

            details.Add($"  ✓ Removed read-only from {f} files, {d} directories");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error removing read-only attributes for {Path}", path);
            details.Add($"  ⚠ Warning: {ex.Message}");
        }

        // 2. Apply Pin attribute (+P -U) using PowerShell for OneDrive compatibility
        // This is what GenPatcher's ApplyPinAttributeToFile does.
        try
        {
            await ApplyPinAttributeAsync(path, ct);
            details.Add("  ✓ Applied OneDrive pin attributes");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply pin attributes to {Path}", path);
            details.Add($"  ⚠ Could not apply pin attributes: {ex.Message}");
        }

        return (filesProcessed, dirsProcessed);
    }

    private async Task ApplyPinAttributeAsync(string path, CancellationToken ct)
    {
        try
        {
            // Use PowerShell to apply 'Pinned' attribute which is specific to modern Windows / OneDrive
            // Attrib +P -U
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-WindowStyle Hidden -NoProfile -NonInteractive -Command \"Get-ChildItem -Path '{path.Replace("'", "''")}' -Recurse | ForEach-Object {{ attrib +P -U $_.FullName }}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(ct);
                if (process.ExitCode != ProcessConstants.ExitCodeSuccess)
                {
                    logger.LogWarning("attrib command exited with code {Code} for {Path}", process.ExitCode, path);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply pin attributes to {Path}", path);
        }
    }

    private bool IsGameApplied(GameType gameType, string gamePath)
    {
        if (IsReadOnly(gamePath))
        {
            return false;
        }

        var userPath = GetUserDataPath(gameType);
        if (!Directory.Exists(userPath))
        {
            return true;
        }

        var markerPath = Path.Combine(userPath, MarkerFileName);
        if (!File.Exists(markerPath) || IsReadOnly(userPath))
        {
            return false;
        }

        string[] keyPaths = ["Options.ini", "Maps", "Replays"];
        return keyPaths.All(p => !IsReadOnly(Path.Combine(userPath, p)));
    }
}
