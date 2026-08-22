namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Abstract base class for fixes that disable problematic DLLs/files by renaming them to a backup extension.
/// </summary>
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
        var details = new List<string>();

        try
        {
            details.Add($"Starting {Title}...");

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                details.Add($"Processing Generals: {installation.GeneralsPath}");
                if (!RenameFile(installation.GeneralsPath, details))
                {
                    details.Add($"  ⚠ {targetFileName} not found (may already be fixed)");
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                details.Add($"Processing Zero Hour: {installation.ZeroHourPath}");
                if (!RenameFile(installation.ZeroHourPath, details))
                {
                    details.Add($"  ⚠ {targetFileName} not found (may already be fixed)");
                }
            }

            details.Add($"✓ {Title} completed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error applying {Title}", Title);
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add($"Restoring {targetFileName}...");

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                details.Add($"Processing Generals: {installation.GeneralsPath}");
                if (!RestoreFile(installation.GeneralsPath, details))
                {
                    details.Add($"  ⚠ {backupFileName} not found (nothing to restore)");
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                details.Add($"Processing Zero Hour: {installation.ZeroHourPath}");
                if (!RestoreFile(installation.ZeroHourPath, details))
                {
                    details.Add($"  ⚠ {backupFileName} not found (nothing to restore)");
                }
            }

            details.Add($"✓ {targetFileName} restoration completed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error restoring {TargetFileName}", targetFileName);
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private bool RenameFile(string directory, List<string> details)
    {
        var originalPath = Path.Combine(directory, targetFileName);
        var backupPath = Path.Combine(directory, backupFileName);

        if (!File.Exists(originalPath))
        {
            return false;
        }

        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(originalPath, backupPath);
            details.Add($"  ✓ Renamed: {targetFileName} -> {backupFileName}");
            Logger.LogInformation("Renamed {OriginalPath} to {BackupPath}", originalPath, backupPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to rename {OriginalPath}", originalPath);
            details.Add($"  ✗ Error renaming {targetFileName}: {ex.Message}");
            return false;
        }
    }

    private bool RestoreFile(string directory, List<string> details)
    {
        var originalPath = Path.Combine(directory, targetFileName);
        var backupPath = Path.Combine(directory, backupFileName);

        if (!File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }

            File.Move(backupPath, originalPath);
            details.Add($"  ✓ Restored: {backupFileName} -> {targetFileName}");
            Logger.LogInformation("Restored {BackupPath} to {OriginalPath}", backupPath, originalPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to restore {BackupPath}", backupPath);
            details.Add($"  ✗ Error restoring {backupFileName}: {ex.Message}");
            return false;
        }
    }
}
