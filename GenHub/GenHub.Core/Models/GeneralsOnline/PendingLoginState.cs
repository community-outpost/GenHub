namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents the state of a pending browser login operation.
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
    /// Login failed or was cancelled.
    /// </summary>
    LoginFailed = 2,
}
