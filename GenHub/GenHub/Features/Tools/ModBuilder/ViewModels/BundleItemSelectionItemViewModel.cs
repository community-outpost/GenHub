using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// Checkbox item for selecting which Bundle Items belong to a Bundle Pack.
/// </summary>
public partial class BundleItemSelectionItemViewModel(string name, bool isSelected, Action<bool> onChanged) : ObservableObject
{
    /// <summary>
    /// Gets the name of the bundle item.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets a value indicating whether this item is included in the pack.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        onChanged(value);
    }
}
