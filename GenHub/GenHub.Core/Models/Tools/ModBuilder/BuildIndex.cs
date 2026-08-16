namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the 5-stage build pipeline index for the ModBuilder system.
/// </summary>
public enum BuildIndex
{
    /// <summary>
    /// Stage 1: Process source files with format conversions.
    /// </summary>
    RawBundleItem = 0,

    /// <summary>
    /// Stage 2: Package processed files into .big archives.
    /// </summary>
    BigBundleItem = 1,

    /// <summary>
    /// Stage 3: Group bundle items into packs.
    /// </summary>
    RawBundlePack = 2,

    /// <summary>
    /// Stage 4: Create distribution archives (.zip) for release.
    /// </summary>
    ReleaseBundlePack = 3,

    /// <summary>
    /// Stage 5: Install bundle packs to the game directory.
    /// </summary>
    InstallBundlePack = 4,
}
