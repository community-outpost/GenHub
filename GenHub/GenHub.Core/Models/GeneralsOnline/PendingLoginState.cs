namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents the state of a pending browser login operation.
/// Mirrors the backend EPendingLoginState values.
/// </summary>
public enum PendingLoginState
{
    /// <summary>
    /// No login operation in progress or unknown state.
    /// </summary>
    None = -1,

    /// <summary>
    /// Waiting for the user to complete browser authentication.
    /// </summary>
    Waiting = 0,

    /// <summary>
    /// Login completed successfully.
    /// </summary>
    LoginSuccess = 1,

    /// <summary>
    /// Login failed, expired, or was rejected.
    /// </summary>
    LoginFailed = 2,
}
