using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.GenHotkeys;

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
    private string _displayName = string.Empty;

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

    /// <summary>Gets the display badge text for the hotkey.</summary>
    public string HotkeyBadge => Hotkey.HasValue ? $"[{char.ToUpperInvariant(Hotkey.Value)}]" : "[-]";

    partial void OnHotkeyChanged(char? value)
    {
        OnPropertyChanged(nameof(HotkeyBadge));
    }
}
