using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a Generals Online multiplayer lobby.
/// Property names mirror the backend Lobby serialization.
/// </summary>
public sealed class GeneralsOnlineLobby : INotifyPropertyChanged
{
    private int? _localLatencyMs;
    private GeneralsOnlineCompatibility _localCompatibility = GeneralsOnlineCompatibility.Unknown;
    private IReadOnlyList<GeneralsOnlineLobbyMember> _members = [];

    /// <summary>
    /// Gets or sets the lobby id.
    /// </summary>
    [JsonPropertyName("LobbyID")]
    public long LobbyId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the lobby owner user id.
    /// </summary>
    [JsonPropertyName("Owner")]
    public long Owner { get; set; } = -1;

    /// <summary>
    /// Gets or sets the lobby name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lobby lifecycle state.
    /// </summary>
    [JsonPropertyName("State")]
    public GeneralsOnlineLobbyState State { get; set; } = GeneralsOnlineLobbyState.Unknown;

    /// <summary>
    /// Gets or sets the map name.
    /// </summary>
    [JsonPropertyName("MapName")]
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the map path.
    /// </summary>
    [JsonPropertyName("MapPath")]
    public string MapPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the map is official.
    /// </summary>
    [JsonPropertyName("IsMapOfficial")]
    public bool IsMapOfficial { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the lobby requires a password.
    /// </summary>
    [JsonPropertyName("IsPassworded")]
    public bool IsPassworded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether observers are allowed.
    /// </summary>
    [JsonPropertyName("AllowObservers")]
    public bool AllowObservers { get; set; }

    /// <summary>
    /// Gets or sets the executable CRC of the host game binary.
    /// </summary>
    [JsonPropertyName("ExeCRC")]
    public uint ExeCrc { get; set; }

    /// <summary>
    /// Gets or sets the INI CRC of the host game configuration.
    /// </summary>
    [JsonPropertyName("IniCRC")]
    public uint IniCrc { get; set; }

    /// <summary>
    /// Gets or sets the maximum camera height.
    /// </summary>
    [JsonPropertyName("MaximumCameraHeight")]
    public int MaximumCameraHeight { get; set; }

    /// <summary>
    /// Gets or sets the match id assigned when the game starts.
    /// </summary>
    [JsonPropertyName("MatchID")]
    public ulong MatchId { get; set; }

    /// <summary>
    /// Gets or sets the lobby creation time in UTC.
    /// </summary>
    [JsonPropertyName("TimeCreated")]
    public DateTime TimeCreated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the network room id the lobby belongs to.
    /// </summary>
    [JsonPropertyName("NetworkRoomID")]
    public short NetworkRoomId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the lobby type (custom game, quick match, ...).
    /// </summary>
    [JsonPropertyName("LobbyType")]
    public int LobbyType { get; set; }

    /// <summary>
    /// Gets or sets the backend estimated latency to the host in milliseconds.
    /// </summary>
    [JsonPropertyName("EstimatedLatency")]
    public int EstimatedLatency { get; set; } = 999999;

    /// <summary>
    /// Gets or sets a value indicating whether stats tracking is enabled.
    /// </summary>
    [JsonPropertyName("IsTrackingStats")]
    public bool IsTrackingStats { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only vanilla teams are allowed.
    /// </summary>
    [JsonPropertyName("IsVanillaTeamsOnly")]
    public bool IsVanillaTeamsOnly { get; set; }

    /// <summary>
    /// Gets or sets the anticheat id required by the lobby.
    /// </summary>
    [JsonPropertyName("AnticheatID")]
    public int AnticheatId { get; set; }

    /// <summary>
    /// Gets or sets the lobby region.
    /// </summary>
    [JsonPropertyName("Region")]
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the starting cash.
    /// </summary>
    [JsonPropertyName("StartingCash")]
    public uint StartingCash { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether superweapons are limited.
    /// </summary>
    [JsonPropertyName("IsLimitSuperweapons")]
    public bool IsLimitSuperweapons { get; set; }

    /// <summary>
    /// Gets or sets the lobby member slots.
    /// </summary>
    [JsonPropertyName("Members")]
    public IReadOnlyList<GeneralsOnlineLobbyMember> Members
    {
        get => _members;
        set => _members = value ?? [];
    }

    /// <summary>
    /// Gets the number of human players in the lobby.
    /// </summary>
    [JsonIgnore]
    public int PlayerCount => Members.Count(m => m.IsPlayer);

    /// <summary>
    /// Gets the number of occupied slots including AI opponents.
    /// </summary>
    [JsonIgnore]
    public int OccupiedCount => Members.Count(m => m.IsPlayer || m.IsAi);

    /// <summary>
    /// Gets the number of open slots waiting for players.
    /// </summary>
    [JsonIgnore]
    public int OpenSlotCount => Members.Count(m => m.SlotState == GeneralsOnlineSlotState.SlotOpen);

    /// <summary>
    /// Gets the number of joinable slots in the lobby, excluding closed slots.
    /// Mirrors the backend MaxPlayers calculation.
    /// </summary>
    [JsonIgnore]
    public int MaxPlayers => Members.Count(m => m.SlotState != GeneralsOnlineSlotState.SlotClosed);

    /// <summary>
    /// Gets a value indicating whether the lobby has no open slots left.
    /// </summary>
    [JsonIgnore]
    public bool IsFull => OpenSlotCount == 0 && MaxPlayers > 0;

    /// <summary>
    /// Gets the lobby age relative to the current UTC time.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Age => DateTime.UtcNow - TimeCreated.ToUniversalTime();

    /// <summary>
    /// Gets a value indicating whether the lobby is waiting for players.
    /// </summary>
    [JsonIgnore]
    public bool IsWaiting => State == GeneralsOnlineLobbyState.GameSetup;

    /// <summary>
    /// Gets a value indicating whether the match is in progress.
    /// </summary>
    [JsonIgnore]
    public bool IsInProgress => State == GeneralsOnlineLobbyState.InGame;

    /// <summary>
    /// Gets the host display name, or empty when the host slot is unknown.
    /// </summary>
    [JsonIgnore]
    public string HostName => Members.FirstOrDefault(m => m.UserId == Owner)?.DisplayName
        ?? Members.FirstOrDefault(m => m.IsPlayer)?.DisplayName
        ?? string.Empty;

    /// <summary>
    /// Gets or sets the launcher-side estimated latency in milliseconds.
    /// Populated from the lobby list latencies array; not serialized.
    /// </summary>
    [JsonIgnore]
    public int? LocalLatencyMs
    {
        get => _localLatencyMs;
        set => SetField(ref _localLatencyMs, value);
    }

    /// <summary>
    /// Gets or sets the launcher-side compatibility verdict.
    /// Populated after profile matching; not serialized.
    /// </summary>
    [JsonIgnore]
    public GeneralsOnlineCompatibility LocalCompatibility
    {
        get => _localCompatibility;
        set => SetField(ref _localCompatibility, value);
    }

    /// <summary>
    /// Gets the formatted CRC pair for display.
    /// </summary>
    [JsonIgnore]
    public string CrcText => $"0x{ExeCrc:X8} / 0x{IniCrc:X8}";

    /// <summary>
    /// Gets the team layout label derived from occupant teams
    /// (such as 1v1, 2v2, or FFA for free-for-all).
    /// </summary>
    [JsonIgnore]
    public string TeamsText => ComputeTeamsText(); // skipcq: CS-P1026

    private string ComputeTeamsText()
    {
        var active = Members.Where(m => m.IsPlayer || m.IsAi).ToList();
        if (active.Count < 2 || active.Any(m => m.Team < 0))
        {
            return "FFA";
        }

        var teams = active
            .GroupBy(m => m.Team)
            .Select(g => g.Count())
            .OrderByDescending(c => c)
            .ToList();
        if (teams.Count <= 1 || (teams.Count > 2 && teams.All(c => c == 1)))
        {
            return "FFA";
        }

        return string.Join("v", teams);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
