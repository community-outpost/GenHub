using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace GenHub.Features.Tools.GenHotkeys.ViewModels;

/// <summary>
/// ViewModel representing an individual action button in a command card.
/// </summary>
public partial class HotkeyActionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _iconName = string.Empty;

    [ObservableProperty]
    private string _hotkeyString = string.Empty;

    [ObservableProperty]
    private string? _tooltipString;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _defaultDisplayName = string.Empty;

    [ObservableProperty]
    private string? _tooltip;

    [ObservableProperty]
    private string? _defaultTooltip;

    [ObservableProperty]
    private string? _customImagePath;

    [ObservableProperty]
    private bool _hasCustomImage;

    [ObservableProperty]
    private bool _isEditingDescription;

    [ObservableProperty]
    private char? _hotkey;

    [ObservableProperty]
    private char? _defaultHotkey;

    [ObservableProperty]
    private bool _isConflict;

    [ObservableProperty]
    private string? _conflictReason;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    [ObservableProperty]
    private string _titleBeforeHotkey = string.Empty;

    [ObservableProperty]
    private string _titleHotkeyChar = string.Empty;

    [ObservableProperty]
    private string _titleAfterHotkey = string.Empty;

    [ObservableProperty]
    private bool _hasHotkeyInTitle;

#pragma warning disable S2325 // SonarCloud false positive on MVVM Toolkit generated property
    /// <summary>Gets the display badge text for the hotkey.</summary>
    public string HotkeyBadge => Hotkey.HasValue ? $"[{char.ToUpperInvariant(Hotkey.Value)}]" : "[-]";

    /// <summary>Gets a value indicating whether a non-empty tooltip description is set.</summary>
    public bool HasTooltip => !string.IsNullOrWhiteSpace(Tooltip);

    /// <summary>Gets a value indicating whether a tooltip description is present or currently being edited.</summary>
    public bool CanShowTooltipEditor => HasTooltip || IsEditingDescription;

    /// <summary>Gets a value indicating whether the display name has been modified from default.</summary>
    public bool CanResetDisplayName => !string.Equals(DisplayName, DefaultDisplayName, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether the tooltip description has been modified from default.</summary>
    public bool CanResetTooltip => !string.Equals(Tooltip, DefaultTooltip, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether a custom cameo image is active.</summary>
    public bool CanResetCameo => HasCustomImage;

    /// <summary>
    /// Updates the title token breakdown matching the SAGE engine's HOTKEY_TEXT rendering behavior.
    /// </summary>
    public void UpdateInGameTitleBreakdown()
    {
        var (before, hotkeyChar, after, hasInTitle) = FormatTitleBreakdown(DisplayName, Hotkey);
        TitleBeforeHotkey = before;
        TitleHotkeyChar = hotkeyChar;
        TitleAfterHotkey = after;
        HasHotkeyInTitle = hasInTitle;
    }
#pragma warning restore S2325

    private static (string Before, string HotkeyChar, string After, bool HasInTitle) FormatTitleBreakdown(string? displayName, char? hotkey)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            return (string.Empty, hotkey.HasValue ? hotkey.Value.ToString() : string.Empty, string.Empty, false);
        }

        if (hotkey.HasValue)
        {
            var keyChar = hotkey.Value;
            var idx = displayName.IndexOf(keyChar, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return (displayName[..idx], displayName.Substring(idx, 1), displayName[(idx + 1)..], true);
            }

            return ("[", keyChar.ToString().ToUpperInvariant(), $"] {displayName}", false);
        }

        return (displayName, string.Empty, string.Empty, false);
    }

    private void OnTitleConfigurationChanged()
    {
        OnPropertyChanged(nameof(CanResetDisplayName));
        UpdateInGameTitleBreakdown();
    }

    partial void OnHotkeyChanged(char? value)
    {
        OnPropertyChanged(nameof(HotkeyBadge));
        UpdateInGameTitleBreakdown();
    }

    partial void OnDisplayNameChanged(string value) => OnTitleConfigurationChanged();

    partial void OnDefaultDisplayNameChanged(string value) => OnTitleConfigurationChanged();

    partial void OnTooltipChanged(string? value)
    {
        OnPropertyChanged(nameof(HasTooltip));
        OnPropertyChanged(nameof(CanShowTooltipEditor));
        OnPropertyChanged(nameof(CanResetTooltip));
    }

    partial void OnIsEditingDescriptionChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowTooltipEditor));
    }

    partial void OnDefaultTooltipChanged(string? value)
    {
        OnPropertyChanged(nameof(CanResetTooltip));
    }

    partial void OnCustomImagePathChanged(string? value)
    {
        HasCustomImage = !string.IsNullOrWhiteSpace(value);
        OnPropertyChanged(nameof(CanResetCameo));
    }
}
