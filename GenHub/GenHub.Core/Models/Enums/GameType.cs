namespace GenHub.Core.Models.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Represents the type of Command and Conquer game.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameType
{
    /// <summary>
    /// Command and Conquer: Generals.
    /// </summary>
    Generals,

    /// <summary>
    /// Command and Conquer: Generals – Zero Hour.
    /// </summary>
    ZeroHour,

    /// <summary>
    /// Unknown game type.
    /// </summary>
    Unknown,
}
