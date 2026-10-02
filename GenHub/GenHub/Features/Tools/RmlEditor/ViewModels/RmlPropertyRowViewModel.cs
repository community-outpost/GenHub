using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace GenHub.Features.Tools.RmlEditor.ViewModels;

/// <summary>
/// An editable name and value row used for attributes, style declarations, and computed styles.
/// </summary>
public sealed partial class RmlPropertyRowViewModel : ObservableObject
{
    private readonly Action<string, string> _commitEdit;
    private readonly Action<string>? _remove;
    private bool _suppressCommit;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlPropertyRowViewModel"/> class.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The property value.</param>
    /// <param name="commitEdit">Callback invoked when the user commits a new value.</param>
    /// <param name="remove">Callback invoked when the user removes the row, or null when fixed.</param>
    /// <param name="isReadOnly">Whether the row value cannot be edited.</param>
    public RmlPropertyRowViewModel(string name, string value, Action<string, string> commitEdit, Action<string>? remove = null, bool isReadOnly = false)
    {
        Name = name;
        _value = value;
        _commitEdit = commitEdit;
        _remove = remove;
        _isReadOnly = isReadOnly;
    }

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the property value.
    /// </summary>
    [ObservableProperty]
    private string _value;

    /// <summary>
    /// Gets or sets a value indicating whether the value cannot be edited.
    /// </summary>
    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>
    /// Gets a value indicating whether the row can be removed.
    /// </summary>
    public bool CanRemove => _remove is not null && !IsReadOnly;

    /// <summary>
    /// Refreshes the row value without committing an edit.
    /// </summary>
    /// <param name="value">The new value.</param>
    public void RefreshValue(string value)
    {
        _suppressCommit = true;
        try
        {
            Value = value;
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    /// <summary>
    /// Removes the row.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        _remove?.Invoke(Name);
    }

    partial void OnValueChanged(string value)
    {
        if (!_suppressCommit && !IsReadOnly)
        {
            _commitEdit(Name, value);
        }
    }

    partial void OnIsReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRemove));
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
