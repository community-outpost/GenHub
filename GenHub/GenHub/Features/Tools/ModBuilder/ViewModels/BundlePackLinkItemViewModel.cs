using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// Represents a link between a Bundle Item and a Bundle Pack.
/// </summary>
public partial class BundlePackLinkItemViewModel : ObservableObject
{
    private readonly Action<string, bool>? _onLinkChanged;

    [ObservableProperty]
    private string _packName = string.Empty;

    [ObservableProperty]
    private bool _isLinked;

    /// <summary>
    /// Initializes a new instance of the <see cref="BundlePackLinkItemViewModel"/> class.
    /// </summary>
    /// <param name="packName">The name of the bundle pack.</param>
    /// <param name="isLinked">Whether the item is included in the pack.</param>
    /// <param name="onLinkChanged">Callback when the link state changes.</param>
    public BundlePackLinkItemViewModel(string packName, bool isLinked, Action<string, bool>? onLinkChanged = null)
    {
        PackName = packName;
        IsLinked = isLinked;
        _onLinkChanged = onLinkChanged;
    }

    partial void OnIsLinkedChanged(bool value)
    {
        _onLinkChanged?.Invoke(PackName, value);
    }
}
