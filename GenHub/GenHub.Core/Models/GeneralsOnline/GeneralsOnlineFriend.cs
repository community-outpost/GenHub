using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A Generals Online friend or pending request entry.
/// Mirrors the backend FriendEntry serialization.
/// </summary>
public sealed class GeneralsOnlineFriend : ObservableObject
{
    private bool _isOnline;
    private string _presence = string.Empty;

    /// <summary>
    /// Gets or sets the friend user id.
    /// </summary>
    [JsonPropertyName("user_id")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the friend display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the friend is online.
    /// </summary>
    [JsonPropertyName("online")]
    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    /// <summary>
    /// Gets or sets the presence text (in lobby, in game, ...).
    /// </summary>
    [JsonPropertyName("presence")]
    public string Presence
    {
        get => _presence;
        set => SetProperty(ref _presence, value);
    }
}
