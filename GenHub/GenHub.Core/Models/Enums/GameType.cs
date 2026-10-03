using GenHub.Core.Serialization;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Enums;

/// <summary>
/// Represents the type of Command and Conquer game.
/// </summary>
[JsonConverter(typeof(JsonGameTypeConverter))]
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
