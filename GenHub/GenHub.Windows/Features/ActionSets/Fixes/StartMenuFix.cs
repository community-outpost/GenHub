namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
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

                if (File.Exists(exe))
                {
                    var shortcutPath = Path.Combine(startMenuPath, "Command & Conquer Generals Windowed.lnk");
                    var result = await shortcutService.CreateShortcutAsync(
                        shortcutPath,
                        exe,
                        "-win",
                        installation.GeneralsPath,
                        "Launch Generals in Windowed Mode");

                    if (result.Success)
                    {
                        shortcutsCreated++;
                        details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
                    }
                    else
                    {
                        hasFailures = true;
                        details.Add($"✗ Failed to create Generals shortcut: {result.Errors.FirstOrDefault()}");
                    }
                }
            }

            if (installation.HasZeroHour)
            {
                var startMenuPath = Path.Combine(commonPrograms, "Command and Conquer Generals Zero Hour");
                var exe = Path.Combine(installation.ZeroHourPath, "generals.exe");

                if (File.Exists(exe))
                {
                    var shortcutPath = Path.Combine(startMenuPath, "Command & Conquer Generals Zero Hour Windowed.lnk");
                    var result = await shortcutService.CreateShortcutAsync(
                        shortcutPath,
                        exe,
                        "-win",
                        installation.ZeroHourPath,
                        "Launch Zero Hour in Windowed Mode");

                    if (result.Success)
                    {
                        shortcutsCreated++;
                        details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
                    }
                    else
                    {
                        hasFailures = true;
                        details.Add($"✗ Failed to create Zero Hour shortcut: {result.Errors.FirstOrDefault()}");
                    }
                }

                // EdgeScroller shortcut
                var edgeScroller = Path.Combine(installation.ZeroHourPath, "EdgeScroller.exe");
                if (File.Exists(edgeScroller))
                {
                    var shortcutPath = Path.Combine(startMenuPath, "EdgeScroller.lnk");
                    var result = await shortcutService.CreateShortcutAsync(
                        shortcutPath,
                        edgeScroller,
                        null,
                        installation.ZeroHourPath,
                        "Window Edge Scroller");

                    if (result.Success)
                    {
                        shortcutsCreated++;
                        details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
                    }
                    else
                    {
                        hasFailures = true;
                        details.Add($"✗ Failed to create EdgeScroller shortcut: {result.Errors.FirstOrDefault()}");
                    }
                }
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
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Undoing Start Menu Shortcuts Fix is not supported.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private bool DoShortcutsExist(GameInstallation installation)
    {
        var searchPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };

        var generalsFound = !installation.HasGenerals;
        var zhFound = !installation.HasZeroHour;

        foreach (var programsPath in searchPaths)
        {
            if (installation.HasGenerals && !generalsFound)
            {
                // Try both variants of '&' vs 'and'
                var folderVariants = new[] { "Command and Conquer Generals", "Command & Conquer Generals" };
                foreach (var folder in folderVariants)
                {
                    var path = Path.Combine(programsPath, folder, "Command & Conquer Generals Windowed.lnk");
                    if (File.Exists(path))
                    {
                        generalsFound = true;
                        break;
                    }
                }
            }

            if (installation.HasZeroHour && !zhFound)
            {
                var folderVariants = new[] { "Command and Conquer Generals Zero Hour", "Command & Conquer Generals Zero Hour" };
                foreach (var folder in folderVariants)
                {
                    var path = Path.Combine(programsPath, folder, "Command & Conquer Generals Zero Hour Windowed.lnk");
                    if (File.Exists(path))
                    {
                        zhFound = true;
                        break;
                    }
                }
            }
        }

        return generalsFound && zhFound;
    }
}
