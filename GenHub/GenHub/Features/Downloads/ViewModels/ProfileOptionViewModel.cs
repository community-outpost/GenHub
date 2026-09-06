using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents a profile option in the selection list.
/// </summary>
public sealed partial class ProfileOptionViewModel : ProfilePickerItemViewModel
{
    private const int PreviewItemLimit = 3;

    private readonly List<string> _contentItems;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileOptionViewModel"/> class.
    /// </summary>
    /// <param name="profile">The profile represented by this option.</param>
    /// <param name="contentNames">A lookup of enabled content IDs to their display names.</param>
    public ProfileOptionViewModel(GameProfile profile, IReadOnlyDictionary<string, string>? contentNames = null)
    {
        Profile = profile;
        ProfileCard = new GameProfileItemViewModel(
            profile.Id,
            profile,
            profile.IconPath ?? string.Empty,
            profile.CoverPath ?? string.Empty);
        _contentItems = (profile.EnabledContentIds ?? [])
            .Select(contentId => contentNames?.TryGetValue(contentId, out var contentName) == true
                ? contentName
                : "Content unavailable")
            .Take(PreviewItemLimit)
            .ToList();
    }

    /// <summary>
    /// Gets the underlying game profile.
    /// </summary>
    public GameProfile Profile { get; }

    /// <summary>
    /// Gets the same presentation model used by the profile launcher cards.
    /// </summary>
    public GameProfileItemViewModel ProfileCard { get; }

    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name => Profile.Name ?? "Unnamed Profile";

    /// <summary>
    /// Gets the game type.
    /// </summary>
    public GameType GameType => Profile.GameClient?.GameType ?? GameType.Generals;

    /// <summary>
    /// Gets the profile's chosen icon path.
    /// </summary>
    public string? IconPath => Profile.IconPath;

    /// <summary>
    /// Gets a value indicating whether the profile has a custom icon to render.
    /// </summary>
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);

    /// <summary>
    /// Gets the profile's chosen color, with a game-specific fallback for older profiles.
    /// </summary>
    public string ProfileColor => !string.IsNullOrWhiteSpace(Profile.ThemeColor)
        ? Profile.ThemeColor
        : GameType switch
        {
            GameType.ZeroHour => "#14B8A6",
            GameType.Generals => "#3B82F6",
            _ => "#8B5CF6",
        };

    /// <summary>
    /// Gets a compact game label for the icon fallback.
    /// </summary>
    public string GameBadgeLabel => GameType switch
    {
        GameType.ZeroHour => "ZH",
        GameType.Generals => "GEN",
        _ => "?",
    };

    /// <summary>
    /// Gets a friendly game label for the profile context.
    /// </summary>
    public string GameLabel => GameType switch
    {
        GameType.ZeroHour => "Zero Hour",
        GameType.Generals => "Generals",
        _ => "Unknown game",
    };

    /// <summary>
    /// Gets the game client name.
    /// </summary>
    public string GameClientName => Profile.GameClient?.Name ?? "Unknown Client";

    /// <summary>
    /// Gets the number of content items currently enabled in this profile.
    /// </summary>
    public int ContentCount => Profile.EnabledContentIds?.Count ?? 0;

    /// <summary>
    /// Gets a value indicating whether this profile already has content in it.
    /// </summary>
    public bool HasContent => ContentCount > 0;

    /// <summary>
    /// Gets the first enabled content items to display as chips.
    /// </summary>
    public IReadOnlyList<string> ContentItems => _contentItems;

    /// <summary>
    /// Gets a value indicating whether there are more enabled items than fit in the preview.
    /// </summary>
    public bool HasMoreContentItems => ContentCount > PreviewItemLimit;

    /// <summary>
    /// Gets a label for content items hidden from the compact preview.
    /// </summary>
    public string MoreContentItemsLabel => $"+{ContentCount - PreviewItemLimit} more";

    /// <summary>
    /// Gets a short summary of how much content is in the profile.
    /// </summary>
    public string ContentSummary => ContentCount switch
    {
        0 => "Empty profile",
        1 => "1 item",
        _ => $"{ContentCount} items",
    };

    /// <summary>
    /// Gets a friendly description of when the profile was last played.
    /// </summary>
    public string LastPlayedText
    {
        get
        {
            if (Profile.LastPlayedAt == default || Profile.LastPlayedAt == DateTime.MinValue)
            {
                return "Never played";
            }

            var elapsed = DateTime.UtcNow - Profile.LastPlayedAt;
            return elapsed.TotalDays switch
            {
                < 1 => "Played today",
                < 2 => "Played yesterday",
                < 30 => $"Played {elapsed.Days} days ago",
                < 365 => $"Played {elapsed.Days / 30} month{((elapsed.Days / 30) == 1 ? string.Empty : "s")} ago",
                _ => $"Played {elapsed.Days / 365} year{((elapsed.Days / 365) == 1 ? string.Empty : "s")} ago",
            };
        }
    }

    /// <summary>
    /// Gets a value indicating whether this profile has ever been played.
    /// </summary>
    public bool HasBeenPlayed => Profile.LastPlayedAt != default && Profile.LastPlayedAt != DateTime.MinValue;

    /// <summary>
    /// Gets or sets a value indicating whether a warning should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _showWarning;

    /// <summary>
    /// Gets or sets the warning message.
    /// </summary>
    [ObservableProperty]
    private string? _warningMessage;

    /// <summary>
    /// Gets the description for display.
    /// </summary>
    public string Description => string.IsNullOrEmpty(Profile.Description)
        ? GameClientName
        : Profile.Description;
}
