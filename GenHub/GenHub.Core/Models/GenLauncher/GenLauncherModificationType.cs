namespace GenHub.Core.Models.GenLauncher;

/// <summary>
/// Modification types used by GenLauncher.
/// </summary>
public enum GenLauncherModificationType
{
    /// <summary>Full modification.</summary>
    Mod = 0,

    /// <summary>Addon for a mod or original game.</summary>
    Addon = 1,

    /// <summary>Patch for a mod or original game.</summary>
    Patch = 2,

    /// <summary>Advertising / promotional banner entry.</summary>
    Advertising = 3,

    /// <summary>Executable or game client tool.</summary>
    Executable = 4,
}
