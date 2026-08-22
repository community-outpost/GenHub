namespace GenHub.Windows.Features.ActionSets.Fixes;

using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix for the BrowserEngine.dll which causes crashes on modern systems.
/// </summary>
public class BrowserEngineFix(ILogger<BrowserEngineFix> logger)
    : BaseFileRenameFix(logger, GameClientConstants.BrowserEngineDll, GameClientConstants.BrowserEngineDllBak)
{
    /// <inheritdoc/>
    public override string Id => "BrowserEngineFix";

    /// <inheritdoc/>
    public override string Title => "Browser Engine DLL Fix";

    /// <inheritdoc/>
    public override string Description => "Disables the obsolete BrowserEngine.dll that causes instant crashes during game startup on modern Windows.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Generals originally bundled an embedded web browser DLL from 2002 to display EA in-game news. On modern Windows, this outdated library triggers memory access violations that crash the game before reaching the main menu. This fix renames BrowserEngine.dll to safely bypass the crash.";
}
