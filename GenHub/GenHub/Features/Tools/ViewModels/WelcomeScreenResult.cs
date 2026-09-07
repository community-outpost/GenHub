namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Result from the welcome screen.
/// </summary>
public class WelcomeScreenResult
{
    /// <summary>
    /// Gets or sets the action selected by the user.
    /// </summary>
    public WelcomeAction Action { get; set; }

    /// <summary>
    /// Gets or sets the import path if importing an existing profile.
    /// </summary>
    public string? ImportPath { get; set; }
}

/// <summary>
/// Actions available on the welcome screen.
/// </summary>
public enum WelcomeAction
{
    /// <summary>
    /// Create a new publisher profile.
    /// </summary>
    CreateNew,

    /// <summary>
    /// Import an existing publisher profile.
    /// </summary>
    Import,

    /// <summary>
    /// Skip the welcome screen.
    /// </summary>
    Skip,
}
