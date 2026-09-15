using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Represents an item in the Scan Wizard interactive demo.
/// </summary>
public partial class ScanWizardDemoItemViewModel : ObservableObject
{
    private readonly Action? _onSelectionChanged;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _version;

    [ObservableProperty]
    private string _iconPath;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanWizardDemoItemViewModel"/> class.
    /// </summary>
    /// <param name="title">The game title.</param>
    /// <param name="path">The installation directory.</param>
    /// <param name="version">The detected version badge.</param>
    /// <param name="iconPath">The icon resource path.</param>
    /// <param name="isSelected">Initial selection state.</param>
    /// <param name="onSelectionChanged">Callback when selection state changes.</param>
    public ScanWizardDemoItemViewModel(
        string title,
        string path,
        string version,
        string iconPath,
        bool isSelected = true,
        Action? onSelectionChanged = null)
    {
        _title = title;
        _path = path;
        _version = version;
        _iconPath = iconPath;
        _isSelected = isSelected;
        _onSelectionChanged = onSelectionChanged;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _onSelectionChanged?.Invoke();
    }
}
