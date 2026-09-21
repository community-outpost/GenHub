using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// A single checkable engine flag.
/// </summary>
public sealed partial class WndFlagViewModel : ObservableObject
{
    private readonly Action<bool> _commit;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndFlagViewModel"/> class.
    /// </summary>
    /// <param name="name">The engine flag name.</param>
    /// <param name="isChecked">Whether the flag is set.</param>
    /// <param name="commit">Callback invoked when the user toggles the flag.</param>
    public WndFlagViewModel(string name, bool isChecked, Action<bool> commit)
    {
        Name = name;
        _isChecked = isChecked;
        _commit = commit;
    }

    /// <summary>
    /// Gets the engine flag name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets whether the flag is set.
    /// </summary>
    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        _commit(value);
    }
}
