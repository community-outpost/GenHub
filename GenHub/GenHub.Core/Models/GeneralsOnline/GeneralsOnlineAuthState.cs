namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Authentication state of the Generals Online session.
/// </summary>
public enum GeneralsOnlineAuthState
{
    /// <summary>
    /// Not authenticated.
    /// </summary>
    Unauthenticated = 0,

    /// <summary>
    /// Browser login started; waiting for the user to authorize.
    /// </summary>
    PendingBrowserLogin = 1,

    /// <summary>
    /// Authenticated with a live session token.
    /// </summary>
    Authenticated = 2,
}
