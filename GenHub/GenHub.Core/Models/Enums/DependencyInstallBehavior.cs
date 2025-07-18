namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines how a dependency should be handled during installation.
/// </summary>
public enum DependencyInstallBehavior
{
    /// <summary>Dependency must already exist, don't auto-install.</summary>
    RequireExisting = 0,

    /// <summary>Install if missing.</summary>
    AutoInstall = 1,

    /// <summary>User can choose to install.</summary>
    Optional = 2,

    /// <summary>Recommend but don't require.</summary>
    Suggest = 3,
}
