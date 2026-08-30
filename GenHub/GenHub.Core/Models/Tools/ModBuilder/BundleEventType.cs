namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the 17 event types across the build lifecycle.
/// </summary>
public enum BundleEventType
{
    /// <summary>
    /// Fired before the build process starts.
    /// </summary>
    OnPreBuild = 0,

    /// <summary>
    /// Fired during the build process.
    /// </summary>
    OnBuild = 1,

    /// <summary>
    /// Fired after the build process completes.
    /// </summary>
    OnPostBuild = 2,

    /// <summary>
    /// Fired during the release process.
    /// </summary>
    OnRelease = 3,

    /// <summary>
    /// Fired during the install process.
    /// </summary>
    OnInstall = 4,

    /// <summary>
    /// Fired when the game is run.
    /// </summary>
    OnRun = 5,

    /// <summary>
    /// Fired during the uninstall process.
    /// </summary>
    OnUninstall = 6,

    /// <summary>
    /// Fired at the start of RawBundleItem stage.
    /// </summary>
    OnStartBuildRawBundleItem = 7,

    /// <summary>
    /// Fired at the finish of RawBundleItem stage.
    /// </summary>
    OnFinishBuildRawBundleItem = 8,

    /// <summary>
    /// Fired at the start of BigBundleItem stage.
    /// </summary>
    OnStartBuildBigBundleItem = 9,

    /// <summary>
    /// Fired at the finish of BigBundleItem stage.
    /// </summary>
    OnFinishBuildBigBundleItem = 10,

    /// <summary>
    /// Fired at the start of RawBundlePack stage.
    /// </summary>
    OnStartBuildRawBundlePack = 11,

    /// <summary>
    /// Fired at the finish of RawBundlePack stage.
    /// </summary>
    OnFinishBuildRawBundlePack = 12,

    /// <summary>
    /// Fired at the start of ReleaseBundlePack stage.
    /// </summary>
    OnStartBuildReleaseBundlePack = 13,

    /// <summary>
    /// Fired at the finish of ReleaseBundlePack stage.
    /// </summary>
    OnFinishBuildReleaseBundlePack = 14,

    /// <summary>
    /// Fired at the start of InstallBundlePack stage.
    /// </summary>
    OnStartBuildInstallBundlePack = 15,

    /// <summary>
    /// Fired at the finish of InstallBundlePack stage.
    /// </summary>
    OnFinishBuildInstallBundlePack = 16,
}
