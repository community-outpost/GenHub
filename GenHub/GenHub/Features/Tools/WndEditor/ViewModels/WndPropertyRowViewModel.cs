using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// An editable property row of the selected window.
/// </summary>
public sealed partial class WndPropertyRowViewModel : ObservableObject
{
    private readonly Action<string, string> _commitEdit;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndPropertyRowViewModel"/> class.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The property value.</param>
    /// <param name="commitEdit">Callback invoked when the user commits a new value.</param>
    public WndPropertyRowViewModel(string key, string value, Action<string, string> commitEdit)
    {
        Key = key;
        _value = value;
        _commitEdit = commitEdit;
    }

    /// <summary>
    /// Gets the property key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets or sets the property value.
    /// </summary>
    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        _commitEdit(Key, value);
    }
}
