namespace GenHub.Core.Constants;

/// <summary>
/// Defines IPC command constants shared across platform implementations.
/// </summary>
public static class IpcCommands
{
    /// <summary>
    /// Command prefix used to launch a profile via IPC.
    /// </summary>
    public const string LaunchProfilePrefix = "launch-profile:";

    /// <summary>
    /// Command prefix used to subscribe to a catalog via IPC.
    /// </summary>
    public const string SubscribePrefix = "subscribe:";
}
