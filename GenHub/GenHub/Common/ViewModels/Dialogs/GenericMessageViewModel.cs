using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;

namespace GenHub.Common.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the generic message dialog.
/// </summary>
public partial class GenericMessageViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the dialog title.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets or sets the dialog content (Markdown supported).
    /// </summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to show the "Do not show again" checkbox.
    /// </summary>
    [ObservableProperty]
    private bool _showDoNotAskAgain;

    /// <summary>
    /// Gets or sets a value indicating whether the "Do not show again" checkbox is checked.
    /// </summary>
    [ObservableProperty]
    private bool _doNotAskAgain;

    /// <summary>
    /// Gets the list of actions (buttons).
    /// </summary>
    public ObservableCollection<DialogAction> Actions { get; } = [];

    /// <summary>
    /// Gets the action result.
    /// </summary>
    public DialogAction? Result { get; private set; }

    /// <summary>
    /// Request to close the dialog.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Executes the specified action.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    [RelayCommand]
    private void ExecuteAction(DialogAction action)
    {
        Result = action;
        action.Action?.Invoke();
        CloseRequested?.Invoke();
    }
}
