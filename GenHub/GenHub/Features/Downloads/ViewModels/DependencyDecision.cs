namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents the user's decision regarding missing dependencies.
/// </summary>
public enum DependencyDecision
{
    /// <summary>
    /// Install all missing dependencies automatically.
    /// </summary>
    InstallAll,

    /// <summary>
    /// Skip dependencies and continue with main content download.
    /// </summary>
    SkipDependencies,

    /// <summary>
    /// Cancel the entire download operation.
    /// </summary>
    Cancel,
}
