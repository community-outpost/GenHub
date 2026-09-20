using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.WndEditor;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Tree node wrapping a window of the edited document.
/// </summary>
public sealed partial class WndTreeNodeViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndTreeNodeViewModel"/> class.
    /// </summary>
    /// <param name="window">The wrapped window.</param>
    /// <param name="parent">The parent node, or null for top-level windows.</param>
    public WndTreeNodeViewModel(WndWindow window, WndTreeNodeViewModel? parent)
    {
        Window = window;
        Parent = parent;
        _isExpanded = true;
    }

    /// <summary>
    /// Gets the wrapped window.
    /// </summary>
    public WndWindow Window { get; }

    /// <summary>
    /// Gets the parent node, or null for top-level windows.
    /// </summary>
    public WndTreeNodeViewModel? Parent { get; }

    /// <summary>
    /// Gets the child nodes.
    /// </summary>
    public ObservableCollection<WndTreeNodeViewModel> Children { get; } = [];

    /// <summary>
    /// Gets the display name combining the window name and control type.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var name = Window.Name?.Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return Window.ControlTypeName;
            }

            return $"{name} [{Window.ControlTypeName}]";
        }
    }

    /// <summary>
    /// Gets or sets whether the node is expanded in the tree.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Refreshes display text after the wrapped window changed.
    /// </summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}
