namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Information about a running overlay sidecar.
/// </summary>
/// <param name="ProcessId">The sidecar process id.</param>
/// <param name="ConfigPath">The staged configuration file path.</param>
public sealed record SidecarInfo(int ProcessId, string ConfigPath);
