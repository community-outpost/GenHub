namespace GenHub.Core.Models.Enums;

/// <summary>
/// Different Linux Package Installation type.
/// </summary>
public enum LinuxPackageInstallationType
{
    /// <summary>
    /// Binary Package Installation type aka installed by package manager.
    /// This is how different distros distribute apps. Therefore, Different on each distro.
    /// Valve officially support steam this way (deb file).
    /// </summary>
    Binary = 0,

    /// <summary>
    /// flatpack Package Installation type.
    /// most app devs prefer flatpack to other Installation type, except for valve.
    /// Standard, Therefore, same cross all distros.
    /// </summary>
    Flatpack = 1,

    /// <summary>
    /// Snap Package Installation type.
    /// Snap is supported by Canonical (owner of Ubuntu).
    /// </summary>
    Snap = 2,

    /// <summary>
    /// Unknown Package Installation type
    /// </summary>
    Unknown = 3,
}