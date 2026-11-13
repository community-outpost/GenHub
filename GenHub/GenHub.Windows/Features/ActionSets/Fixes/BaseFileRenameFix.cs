using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Abstract base class for fixes that disable problematic DLLs/files by renaming them to a GenPatcher backup name.
/// </summary>
/// <remarks>
/// Backups are named <c>&lt;file&gt;.genpatcher.ghbak</c>, then <c>&lt;file&gt;.genpatcher.N.ghbak</c> when a game repair
/// brings the file back. Apply never overwrites or consumes a <c>.bak</c> the user already keeps.
/// Undo falls back to a <c>.bak</c> left by earlier builds only when the target file is absent, so no bytes are lost.
/// Neither apply nor undo deletes a file whose content is not preserved in another file.
/// </remarks>
public abstract class BaseFileRenameFix(
    ILogger logger,
    string targetFileName)
    : BaseActionSet(logger)
{
    private readonly string _backupStem = targetFileName + FileTypes.GenPatcherBackupInfix;

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <summary>
    /// Builds the backup file name for a target: index 0 is the first backup, higher indexes follow game repairs.
    /// </summary>
    /// <param name="targetFileName">The file the fix disables.</param>
    /// <param name="index">The backup sequence number.</param>
    /// <returns>The backup file name.</returns>
    public static string GetBackupFileName(string targetFileName, long index) => index == 0
        ? targetFileName + FileTypes.GenPatcherBackupInfix + FileTypes.BackupExtension
        : targetFileName + FileTypes.GenPatcherBackupInfix + "." + index.ToString(CultureInfo.InvariantCulture) + FileTypes.BackupExtension;

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

    private static long ParseBackupIndex(string name, string prefix)
    {
        if (name.Length <= prefix.Length + FileTypes.BackupExtension.Length
            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(FileTypes.BackupExtension, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var digits = name[prefix.Length..^FileTypes.BackupExtension.Length];
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index > 0
            && digits == index.ToString(CultureInfo.InvariantCulture)
                ? index
                : -1;
    }

    private bool DisableTarget(string directory, List<string> details)
    {
        var originalPath = Path.Combine(directory, targetFileName);
        var backupPath = Path.Combine(directory, GetBackupFileName(targetFileName, 0));

        if (!File.Exists(originalPath))
        {
            details.Add($"  OK: {targetFileName} not present, nothing to disable");
            return true;
        }

        try
        {
            var latest = GetLatestBackup(directory);
            if (latest.Index >= 0 || Directory.Exists(backupPath))
            {
                if (latest.Index == long.MaxValue)
                {
                    throw new IOException("No more backup sequence numbers are available.");
                }

                backupPath = Path.Combine(directory, GetBackupFileName(targetFileName, Math.Max(0, latest.Index) + 1));
            }

            // The non-overwriting rename preserves both files, even if another process wins the name.
            File.Move(originalPath, backupPath);
            details.Add($"  OK: Renamed: {targetFileName} -> {Path.GetFileName(backupPath)}");
            Logger.LogInformation("Renamed {OriginalPath} to {BackupPath}", originalPath, backupPath);
            return true;
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
        var backupPath = string.Empty;

        try
        {
            if (File.Exists(originalPath))
            {
                details.Add($"  OK: {targetFileName} already present; backups preserved");
                return true;
            }

            backupPath = GetLatestBackup(directory, filesOnly: true).Path;
            if (string.IsNullOrEmpty(backupPath))
            {
                return RestoreLegacyBackup(directory, originalPath, details);
            }

            File.Move(backupPath, originalPath);
            details.Add($"  OK: Restored: {Path.GetFileName(backupPath)} -> {targetFileName}");
            Logger.LogInformation("Restored {BackupPath} to {OriginalPath}", backupPath, originalPath);
            return true;
        }
        catch (IOException ex)
        {
            Logger.LogError(ex, "Failed to restore {TargetFileName} from {BackupPath}", targetFileName, backupPath);
            AddFailureDetail(details, ex, DescribeRestore(backupPath), indent: "  ");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex, "Access denied restoring {TargetFileName} from {BackupPath}", targetFileName, backupPath);
            AddFailureDetail(details, ex, DescribeRestore(backupPath), indent: "  ");
            return false;
        }
    }

    private string DescribeRestore(string backupPath) => string.IsNullOrEmpty(backupPath)
        ? $"finding a backup of {targetFileName}"
        : $"restoring {Path.GetFileName(backupPath)}";

    private (long Index, string Path) GetLatestBackup(string directory, bool filesOnly = false)
    {
        var basePath = Path.Combine(directory, GetBackupFileName(targetFileName, 0));
        var baseExists = File.Exists(basePath);
        var latest = baseExists ? (Index: 0L, Path: basePath) : (Index: -1L, Path: string.Empty);
        if (!Directory.Exists(directory))
        {
            return latest;
        }

        var prefix = _backupStem + ".";
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, prefix + "*" + FileTypes.BackupExtension))
        {
            var index = ParseBackupIndex(Path.GetFileName(path), prefix);
            if (index > latest.Index && (!filesOnly || File.Exists(path)))
            {
                latest = (index, path);
            }
        }

        return latest;
    }

    private bool RestoreLegacyBackup(string directory, string originalPath, List<string> details)
    {
        var legacyFileName = targetFileName + FileTypes.LegacyBackupExtension;
        var legacyPath = Path.Combine(directory, legacyFileName);

        if (File.Exists(originalPath) || !File.Exists(legacyPath))
        {
            details.Add($"  OK: no backup of {targetFileName} present, nothing to restore");
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
