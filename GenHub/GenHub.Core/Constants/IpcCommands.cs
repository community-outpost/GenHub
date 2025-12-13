using System.Runtime.Versioning;

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
}
