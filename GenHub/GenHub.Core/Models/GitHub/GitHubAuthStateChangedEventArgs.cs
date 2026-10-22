namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Provides data for the <see cref="Interfaces.GitHub.IGitHubAuthService.AuthStateChanged"/> event.
/// </summary>
/// <param name="isAuthenticated">Whether GitHub API calls are authenticated after the change.</param>
/// <param name="user">The signed in user profile, or null when signed out or not yet loaded.</param>
public sealed class GitHubAuthStateChangedEventArgs(bool isAuthenticated, GitHubUserProfile? user) : EventArgs
{
    /// <summary>
    /// Gets a value indicating whether GitHub API calls are authenticated after the change.
    /// </summary>
    public bool IsAuthenticated { get; } = isAuthenticated;

    /// <summary>
    /// Gets the signed in user profile, or null when signed out or not yet loaded.
    /// </summary>
    public GitHubUserProfile? User { get; } = user;
}
