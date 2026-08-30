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
    /// Command prefix used to forward a subscribe URL to the primary instance
    /// (<c>subscribe:&lt;absolute-url&gt;</c>). Same payload as <c>genhub://subscribe?url=...</c>.
    /// </summary>
    public const string SubscribePrefix = "subscribe:";

    /// <summary>
    /// Command prefix used to forward a shared profile URI or payload to the primary instance
    /// (<c>import-profile:&lt;uri-or-payload&gt;</c>).
    /// </summary>
    public const string ImportProfilePrefix = "import-profile:";
}
