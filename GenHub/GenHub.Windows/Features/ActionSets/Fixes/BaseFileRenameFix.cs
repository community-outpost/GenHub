using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Abstract base class for fixes that disable problematic DLLs/files by renaming them to a GenHub-owned backup name.
/// </summary>
/// <remarks>
/// The backup name is never a generic <c>.bak</c>, so apply never overwrites or consumes a backup the user already keeps.
/// Undo falls back to a <c>.bak</c> left by earlier builds only when the target file is absent, so no bytes are lost.
/// Neither apply nor undo deletes a file whose content is not preserved in another file.
/// </remarks>
public abstract class BaseFileRenameFix(
    ILogger logger,
    string targetFileName,
    string backupFileName)
    : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) && File.Exists(Path.Combine(installation.GeneralsPath, targetFileName)))
        {
            return Task.FromResult(true);
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) && File.Exists(Path.Combine(installation.ZeroHourPath, targetFileName)))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        bool generalsApplied = !installation.HasGenerals ||
                               string.IsNullOrEmpty(installation.GeneralsPath) ||
                               !File.Exists(Path.Combine(installation.GeneralsPath, targetFileName));

        bool zeroHourApplied = !installation.HasZeroHour ||
                               string.IsNullOrEmpty(installation.ZeroHourPath) ||
                               !File.Exists(Path.Combine(installation.ZeroHourPath, targetFileName));

        return Task.FromResult(generalsApplied && zeroHourApplied);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string> { $"Starting {Title}..." };
        var allSucceeded = true;

        foreach (var (gameName, directory) in GetGameDirectories(installation))
        {
            details.Add($"Processing {gameName}: {directory}");
            ct.ThrowIfCancellationRequested();
            allSucceeded &= DisableTarget(directory, details);
        }

        if (!allSucceeded)
        {
            return Task.FromResult(new ActionSetResult(false, $"{Title} could not disable {targetFileName} in every game directory.", details));
        }

        details.Add($"OK: {Title} completed successfully");
        return Task.FromResult(new ActionSetResult(true, null, details));
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string> { $"Restoring {targetFileName}..." };
        var allSucceeded = true;

        foreach (var (gameName, directory) in GetGameDirectories(installation))
        {
            details.Add($"Processing {gameName}: {directory}");
            ct.ThrowIfCancellationRequested();
            allSucceeded &= RestoreTarget(directory, details);
        }

        if (!allSucceeded)
        {
            return Task.FromResult(new ActionSetResult(false, $"Could not restore {targetFileName} in every game directory.", details));
        }

        details.Add($"OK: {targetFileName} restoration completed successfully");
        return Task.FromResult(new ActionSetResult(true, null, details));
    }

    private static List<(string GameName, string Directory)> GetGameDirectories(GameInstallation installation)
    {
        var directories = new List<(string GameName, string Directory)>();
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            directories.Add((GameClientConstants.GeneralsShortName, installation.GeneralsPath));
        }

        if (installation.HasZeroHour
            && !string.IsNullOrEmpty(installation.ZeroHourPath)
            && !directories.Exists(d => IsSameDirectory(d.Directory, installation.ZeroHourPath)))
        {
            directories.Add((GameClientConstants.ZeroHourShortName, installation.ZeroHourPath));
        }

        return directories;
    }

    private static bool IsSameDirectory(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(first),
        Path.TrimEndingDirectorySeparator(second),
        StringComparison.OrdinalIgnoreCase);

    private bool DisableTarget(string directory, List<string> details)
    {
        var originalPath = Path.Combine(directory, targetFileName);
        var backupPath = Path.Combine(directory, backupFileName);

        if (!File.Exists(originalPath))
        {
            details.Add($"  OK: {targetFileName} not present, nothing to disable");
            return true;
        }

        try
        {
            if (!File.Exists(backupPath))
            {
                File.Move(originalPath, backupPath);
                details.Add($"  OK: Renamed: {targetFileName} -> {backupFileName}");
                Logger.LogInformation("Renamed {OriginalPath} to {BackupPath}", originalPath, backupPath);
                return true;
            }

            // Never delete a path after comparing its bytes: another process can replace it
            // between the comparison and deletion. Preserve both files for explicit recovery.
            details.Add($"  Error: {backupFileName} already exists. Both files were left unchanged.");
            Logger.LogWarning("Not disabling {OriginalPath}: backup already exists at {BackupPath}", originalPath, backupPath);
            return false;
        }
        catch (IOException ex)
        {
            Logger.LogError(ex, "Failed to disable {OriginalPath}", originalPath);
            AddFailureDetail(details, ex, $"renaming {targetFileName}", indent: "  ");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex, "Access denied disabling {OriginalPath}", originalPath);
            AddFailureDetail(details, ex, $"renaming {targetFileName}", indent: "  ");
            return false;
        }
    }

    private bool RestoreTarget(string directory, List<string> details)
    {
        var originalPath = Path.Combine(directory, targetFileName);
        var backupPath = Path.Combine(directory, backupFileName);

        if (!File.Exists(backupPath))
        {
            return RestoreLegacyBackup(directory, originalPath, details);
        }

        try
        {
            if (!File.Exists(originalPath))
            {
                File.Move(backupPath, originalPath);
                details.Add($"  OK: Restored: {backupFileName} -> {targetFileName}");
                Logger.LogInformation("Restored {BackupPath} to {OriginalPath}", backupPath, originalPath);
                return true;
            }

            details.Add($"  Error: {targetFileName} already exists. Both files were left unchanged.");
            Logger.LogWarning("Not restoring {BackupPath}: target already exists at {OriginalPath}", backupPath, originalPath);
            return false;
        }
        catch (IOException ex)
        {
            Logger.LogError(ex, "Failed to restore {BackupPath}", backupPath);
            AddFailureDetail(details, ex, $"restoring {backupFileName}", indent: "  ");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex, "Access denied restoring {BackupPath}", backupPath);
            AddFailureDetail(details, ex, $"restoring {backupFileName}", indent: "  ");
            return false;
        }
    }

    private bool RestoreLegacyBackup(string directory, string originalPath, List<string> details)
    {
        var legacyFileName = targetFileName + FileTypes.LegacyBackupExtension;
        var legacyPath = Path.Combine(directory, legacyFileName);

        if (File.Exists(originalPath) || !File.Exists(legacyPath))
        {
            details.Add($"  OK: {backupFileName} not present, nothing to restore");
            return true;
        }

        try
        {
            File.Move(legacyPath, originalPath);
            details.Add($"  OK: Restored from earlier backup: {legacyFileName} -> {targetFileName}");
            Logger.LogInformation("Restored legacy backup {LegacyPath} to {OriginalPath}", legacyPath, originalPath);
            return true;
        }
        catch (IOException ex)
        {
            Logger.LogError(ex, "Failed to restore legacy backup {LegacyPath}", legacyPath);
            AddFailureDetail(details, ex, $"restoring {legacyFileName}", indent: "  ");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex, "Access denied restoring legacy backup {LegacyPath}", legacyPath);
            AddFailureDetail(details, ex, $"restoring {legacyFileName}", indent: "  ");
            return false;
        }
    }
}
