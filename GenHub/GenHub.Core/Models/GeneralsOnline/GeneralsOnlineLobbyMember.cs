using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a single member slot in a Generals Online lobby.
/// </summary>
public sealed class GeneralsOnlineLobbyMember
{
    /// <summary>
    /// Gets or sets the member user id.
    /// </summary>
    [JsonPropertyName("UserID")]
    public long UserId { get; set; }

    /// <summary>
    /// Gets or sets the member display name.
    /// </summary>
    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the slot index inside the lobby.
    /// </summary>
    [JsonPropertyName("SlotIndex")]
    public int SlotIndex { get; set; }

    /// <summary>
    /// Gets or sets the slot state.
    /// </summary>
    [JsonPropertyName("SlotState")]
    public GeneralsOnlineSlotState SlotState { get; set; } = GeneralsOnlineSlotState.SlotOpen;

    /// <summary>
    /// Gets or sets the chosen faction side.
    /// </summary>
    [JsonPropertyName("Side")]
    public int Side { get; set; }

    /// <summary>
    /// Gets or sets the chosen army color.
    /// </summary>
    [JsonPropertyName("Color")]
    public int Color { get; set; }

    /// <summary>
    /// Gets or sets the team index, or -1 for free-for-all.
    /// </summary>
    [JsonPropertyName("Team")]
    public int Team { get; set; } = -1;

    /// <summary>
    /// Gets or sets the starting position on the map.
    /// </summary>
    [JsonPropertyName("StartingPosition")]
    public int StartingPosition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the member has the map.
    /// </summary>
    [JsonPropertyName("HasMap")]
    public bool HasMap { get; set; }

    /// <summary>
    /// Gets or sets the member region.
    /// </summary>
    [JsonPropertyName("Region")]
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the member is ready.
    /// </summary>
    [JsonPropertyName("IsReady")]
    public bool IsReady { get; set; }

    /// <summary>
    /// Gets a value indicating whether the slot holds a human player.
    /// </summary>
    [JsonIgnore]
    public bool IsPlayer => SlotState == GeneralsOnlineSlotState.SlotPlayer;

    /// <summary>
    /// Gets a value indicating whether the slot holds any AI opponent.
    /// </summary>
    [JsonIgnore]
    public bool IsAi => SlotState is GeneralsOnlineSlotState.SlotEasyAi
        or GeneralsOnlineSlotState.SlotMediumAi
        or GeneralsOnlineSlotState.SlotBrutalAi;
}
