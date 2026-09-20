namespace GenHub.Core.Models.Online;

/// <summary>
/// Lifecycle state of the virtual LAN adapter.
/// </summary>
public enum OnlineAdapterState
{
    /// <summary>
    /// Adapter is down. No overlay routes exist.
    /// </summary>
    Down,

    /// <summary>
    /// Adapter bring-up is in progress.
    /// </summary>
    Starting,

    /// <summary>
    /// Adapter is up and overlay routes are active.
    /// </summary>
    Up,

    /// <summary>
    /// Adapter teardown is in progress.
    /// </summary>
    Stopping,

    /// <summary>
    /// Adapter entered an error state. Teardown is still required.
    /// </summary>
    Error,
}
