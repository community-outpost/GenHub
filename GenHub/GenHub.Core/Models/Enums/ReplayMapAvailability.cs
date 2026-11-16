namespace GenHub.Core.Models.Enums;

/// <summary>
/// Whether the map a replay was recorded on is available locally, matched by map CRC and size.
/// </summary>
public enum ReplayMapAvailability
{
    /// <summary>
    /// Availability was not checked, the replay carries no map CRC or size, or the map sources could not be read.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A user map or a stock map of an installed game matches the replay map CRC and size.
    /// </summary>
    Available = 1,

    /// <summary>
    /// No user map or stock map of an installed game matches the replay map CRC and size.
    /// </summary>
    NotFound = 2,
}
