using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;
using System;
using System.Collections.ObjectModel;
using System.Linq;

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
    /// Gets the short display name without the file prefix.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var shortName = WndDecoratedName.Parse(Window.GetProperty(WndConstants.PropertyKeys.Name)).ShortName;
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return Window.ControlTypeName;
            }

            return shortName;
        }
    }

    /// <summary>
    /// Gets the full decorated name for tooltips.
    /// </summary>
    public string FullName => Window.GetProperty(WndConstants.PropertyKeys.Name)?.Trim('"') ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the window carries the hidden flag.
    /// </summary>
    public bool IsHidden
    {
        get
        {
            var status = WndStatusValue.ParseStatus(Window.GetProperty(WndConstants.PropertyKeys.Status));
            return status.Flags.Contains(WndConstants.StatusFlags.Hidden, StringComparer.OrdinalIgnoreCase);
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
        OnPropertyChanged(nameof(FullName));
        OnPropertyChanged(nameof(IsHidden));
    }
}
