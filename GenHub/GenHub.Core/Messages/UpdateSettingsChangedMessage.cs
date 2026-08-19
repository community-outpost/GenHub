namespace GenHub.Core.Messages;

/// <summary>
/// Message sent when update settings have changed.
/// </summary>
/// <param name="AutoCheckForUpdatesOnStartup">Whether to check for updates on startup.</param>
/// <param name="AutoCheckForUpdatesPeriodically">Whether to check for updates periodically.</param>
/// <param name="PeriodicUpdateCheckIntervalHours">Interval in hours between periodic update checks.</param>
public record UpdateSettingsChangedMessage(
    bool AutoCheckForUpdatesOnStartup,
    bool AutoCheckForUpdatesPeriodically,
    int PeriodicUpdateCheckIntervalHours);
