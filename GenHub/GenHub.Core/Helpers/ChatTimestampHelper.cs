using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper methods for chat message timestamps.
/// </summary>
public static class ChatTimestampHelper
{
    /// <summary>
    /// Converts a UTC or unspecified timestamp to local time for display.
    /// </summary>
    /// <param name="timestamp">The timestamp.</param>
    /// <returns>The timestamp converted to local time.</returns>
    public static DateTime ToLocalTime(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Utc => timestamp.ToLocalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToLocalTime(),
        _ => timestamp,
    };
}
