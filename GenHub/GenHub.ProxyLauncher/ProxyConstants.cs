namespace GenHub.ProxyLauncher;

/// <summary>
/// Constants for the GenHub Proxy Launcher.
/// </summary>
internal static class ProxyConstants
{
    /// <summary>
    /// The name of the configuration file.
    /// </summary>
    public const string ConfigFileName = "proxy_config.json";

    /// <summary>
    /// The name of the log file.
    /// </summary>
    public const string LogFileName = "genhub_proxy.log";

    /// <summary>
    /// The name of the mutex used to ensure a single instance.
    /// </summary>
    public const string SingleInstanceMutexName = "GenHubProxyLauncher_SingleInstance";

    /// <summary>
    /// Delay in milliseconds to wait for the launcher to spawn the game process.
    /// </summary>
    public const int LauncherToGameSpawnDelayMs = 500;
}
