using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Features.Tools.GenHotkeys.ViewModels;

/// <summary>
/// ViewModel representing a building, unit, or structure with its command cards.
/// </summary>
public partial class HotkeyGameObjectViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private HotkeyCategory _category = HotkeyCategory.Buildings;

    [ObservableProperty]
    private string _iconName = string.Empty;

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    [ObservableProperty]
    private bool _hasConflicts;

    /// <summary>Gets the list of command layouts (each layout is a collection of action buttons).</summary>
    public ObservableCollection<ObservableCollection<HotkeyActionViewModel>> Layouts { get; } = [];
}
