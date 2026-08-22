namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that creates or fixes start menu shortcuts for Generals and Zero Hour.
/// This fix ensures proper shortcuts are available in Windows Start Menu.
/// </summary>
public class StartMenuFix(IShortcutService shortcutService, ILogger<StartMenuFix> logger) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "StartMenuFix";

    /// <inheritdoc/>
    public override string Title => "Start Menu Shortcuts";

    /// <inheritdoc/>
    public override string Description => "Creates Windows Start Menu shortcuts for Generals, Zero Hour, and Windowed Mode gameplay.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Digital installations often fail to create clean Start Menu shortcuts or windowed mode launch targets. This fix generates official Windows Start Menu shortcuts, including dedicated windowed mode launchers and EdgeScroller entries for seamless multi-monitor gaming.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult(DoShortcutsExist(installation));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking start menu shortcuts status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        bool hasFailures = false;
        int shortcutsCreated = 0;

        try
        {
            details.Add("Creating Start Menu shortcuts...");

            var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            if (installation.HasGenerals)
            {
                var startMenuPath = Path.Combine(commonPrograms, "Command and Conquer Generals");
                var exe = Path.Combine(installation.GeneralsPath, "Generals.exe");
                var shortcutPath = Path.Combine(startMenuPath, "Command & Conquer Generals Windowed.lnk");

                var (created, failed) = await CreateShortcutIfExeExistsAsync(
                    shortcutPath,
                    exe,
                    "-win",
                    installation.GeneralsPath,
                    "Launch Generals in Windowed Mode",
                    details);

                if (created) shortcutsCreated++;
                if (failed) hasFailures = true;
            }

            if (installation.HasZeroHour)
            {
                var startMenuPath = Path.Combine(commonPrograms, "Command and Conquer Generals Zero Hour");
                var exe = Path.Combine(installation.ZeroHourPath, "generals.exe");
                var shortcutPath = Path.Combine(startMenuPath, "Command & Conquer Generals Zero Hour Windowed.lnk");

                var (created, failed) = await CreateShortcutIfExeExistsAsync(
                    shortcutPath,
                    exe,
                    "-win",
                    installation.ZeroHourPath,
                    "Launch Zero Hour in Windowed Mode",
                    details);

                if (created) shortcutsCreated++;
                if (failed) hasFailures = true;

                // EdgeScroller shortcut
                var edgeScroller = Path.Combine(installation.ZeroHourPath, "EdgeScroller.exe");
                var edgeScrollerShortcut = Path.Combine(startMenuPath, "EdgeScroller.lnk");

                var (esCreated, esFailed) = await CreateShortcutIfExeExistsAsync(
                    edgeScrollerShortcut,
                    edgeScroller,
                    null,
                    installation.ZeroHourPath,
                    "Window Edge Scroller",
                    details);

                if (esCreated) shortcutsCreated++;
                if (esFailed) hasFailures = true;
            }

            if (hasFailures)
            {
                return new ActionSetResult(false, "Failed to create one or more Start Menu shortcuts", details);
            }

            if (shortcutsCreated == 0)
            {
                details.Add("⚠ No game executables found to create shortcuts for.");
                return new ActionSetResult(false, "No game executables found to create shortcuts.", details);
            }

            details.Add(string.Empty);
            details.Add($"✓ Start Menu shortcuts created successfully ({shortcutsCreated} shortcuts)");

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying start menu shortcuts fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

        try
        {
            if (installation.HasGenerals)
            {
                var folder = Path.Combine(commonPrograms, "Command and Conquer Generals");
                var lnk = Path.Combine(folder, "Command & Conquer Generals Windowed.lnk");
                if (File.Exists(lnk))
                {
                    File.Delete(lnk);
                    details.Add("✓ Removed Generals windowed shortcut");
                }

                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }

            if (installation.HasZeroHour)
            {
                var folder = Path.Combine(commonPrograms, "Command and Conquer Generals Zero Hour");
                var lnk1 = Path.Combine(folder, "Command & Conquer Generals Zero Hour Windowed.lnk");
                var lnk2 = Path.Combine(folder, "EdgeScroller.lnk");
                if (File.Exists(lnk1))
                {
                    File.Delete(lnk1);
                    details.Add("✓ Removed Zero Hour windowed shortcut");
                }

                if (File.Exists(lnk2))
                {
                    File.Delete(lnk2);
                    details.Add("✓ Removed EdgeScroller shortcut");
                }

                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error undoing Start Menu shortcuts fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private async Task<(bool Created, bool Failed)> CreateShortcutIfExeExistsAsync(
        string shortcutPath,
        string exePath,
        string? arguments,
        string workingDir,
        string description,
        List<string> details)
    {
        if (!File.Exists(exePath))
        {
            return (false, false);
        }

        var result = await shortcutService.CreateShortcutAsync(shortcutPath, exePath, arguments, workingDir, description);
        if (result.Success)
        {
            details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
            return (true, false);
        }

        details.Add($"✗ Failed to create {Path.GetFileName(shortcutPath)}: {result.Errors.FirstOrDefault()}");
        return (false, true);
    }

    private static bool DoShortcutsExist(GameInstallation installation)
    {
        var searchPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };

        bool generalsFound = !installation.HasGenerals || HasAnyShortcut(
            searchPaths,
            ["Command and Conquer Generals", "Command & Conquer Generals"],
            "Command & Conquer Generals Windowed.lnk");

        bool zhFound = !installation.HasZeroHour || HasAnyShortcut(
            searchPaths,
            ["Command and Conquer Generals Zero Hour", "Command & Conquer Generals Zero Hour"],
            "Command & Conquer Generals Zero Hour Windowed.lnk");

        return generalsFound && zhFound;
    }

    private static bool HasAnyShortcut(string[] searchPaths, string[] folderVariants, string shortcutFileName)
    {
        return searchPaths.Any(programsPath =>
            folderVariants.Any(folder =>
                File.Exists(Path.Combine(programsPath, folder, shortcutFileName))));
    }
}
