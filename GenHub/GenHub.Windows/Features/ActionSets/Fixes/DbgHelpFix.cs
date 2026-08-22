namespace GenHub.Windows.Features.ActionSets.Fixes;

using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix for the dbghelp.dll which causes crashes on modern systems.
/// </summary>
public class DbgHelpFix(ILogger<DbgHelpFix> logger)
    : BaseFileRenameFix(logger, GameClientConstants.DbgHelpDll, GameClientConstants.DbgHelpDllBak)
{
    /// <inheritdoc/>
    public override string Id => "DbgHelpFix";

    /// <inheritdoc/>
    public override string Title => "Debug Help DLL Fix";

    /// <inheritdoc/>
    public override string Description => "Disables the outdated dbghelp.dll in the game folder so Windows uses the modern, stable system library.";

    /// <inheritdoc/>
    public override string DetailedDescription => "The legacy dbghelp.dll bundled inside 2003 game installations causes memory faults and random crash-to-desktop errors on modern Windows. Renaming this local DLL allows the game to safely fall back to the stable system version in SysWOW64.";
}
