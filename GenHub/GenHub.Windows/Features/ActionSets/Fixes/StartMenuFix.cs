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
    private readonly IShortcutService _shortcutService = shortcutService;
    private readonly ILogger<StartMenuFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "StartMenuFix";

    /// <inheritdoc/>
    public override string Title => "Start Menu Shortcuts";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            return Task.FromResult(DoShortcutsExist(installation));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking start menu shortcuts status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

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
                    var result = await _shortcutService.CreateShortcutAsync(
                        shortcutPath,
                        exe,
                        "-win",
                        installation.GeneralsPath,
                        "Launch Generals in Windowed Mode");

                    if (result.Success)
                    {
                        details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
                    }
                    else
                    {
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
                    var result = await _shortcutService.CreateShortcutAsync(
                        shortcutPath,
                        exe,
                        "-win",
                        installation.ZeroHourPath,
                        "Launch Zero Hour in Windowed Mode");

                    if (result.Success)
                    {
                        details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
                    }
                    else
                    {
                        details.Add($"✗ Failed to create Zero Hour shortcut: {result.Errors.FirstOrDefault()}");
                    }
                }

                // EdgeScroller shortcut
                var edgeScroller = Path.Combine(installation.ZeroHourPath, "EdgeScroller.exe");
                if (File.Exists(edgeScroller))
                {
                    var shortcutPath = Path.Combine(startMenuPath, "EdgeScroller.lnk");
                    var result = await _shortcutService.CreateShortcutAsync(
                        shortcutPath,
                        edgeScroller,
                        null,
                        installation.ZeroHourPath,
                        "Window Edge Scroller");

                    if (result.Success)
                    {
                        details.Add($"✓ Created: {Path.GetFileName(shortcutPath)}");
                    }
                }
            }

            details.Add(string.Empty);
            details.Add("✓ Start Menu shortcuts created successfully");

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying start menu shortcuts fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Start Menu Shortcuts Fix is not supported.");
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
