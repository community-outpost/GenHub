using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GenHub.Features.GitHub.ViewModels.Items;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// Tree node for GitHub items display.
/// </summary>
public class TreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _hasPlaceholder;

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeNode"/> class.
    /// </summary>
    public TreeNode()
    {
        // PropertyChanged handlers will call CheckAndAddPlaceholder when properties are set
    }

    private void CheckAndAddPlaceholder()
    {
        // Add placeholder if this is an expandable node without children
        // This makes the chevron appear in Avalonia TreeView
        bool needsPlaceholder = (Child?.IsExpandable == true || IsFolder) && !Children.Any() && !_hasPlaceholder;

        if (needsPlaceholder)
        {
            _hasPlaceholder = true;
            Children.Add(new TreeNode { DisplayName = string.Empty });
            System.Diagnostics.Debug.WriteLine($"[TreeNode] Added placeholder for expandable node: {DisplayName}");
        }
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the icon source.
    /// </summary>
    public string IconSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is a folder node.
    /// </summary>
    public bool IsFolder
    {
        get => _isFolder;
        set
        {
            if (_isFolder != value)
            {
                _isFolder = value;
                OnPropertyChanged();

                // When IsFolder is set to true, check if we need a placeholder
                CheckAndAddPlaceholder();
            }
        }
    }

    private bool _isFolder;

    /// <summary>
    /// Gets or sets a value indicating whether this node is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();

                // Sync with child item if it exists
                if (Child != null)
                {
                    Child.IsExpanded = value;

                    // When expanding, populate TreeNode children from the GitHubDisplayItemViewModel children
                    if (value && Child.IsExpandable)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TreeNode] Expanding node: {DisplayName}");

                        // CollectionChanged subscription already done when Child was set

                        // Clear placeholder if it exists
                        if (_hasPlaceholder || Children.Any())
                        {
                            System.Diagnostics.Debug.WriteLine($"[TreeNode] Clearing {Children.Count} children (hasPlaceholder={_hasPlaceholder})");
                            Children.Clear();
                            _hasPlaceholder = false;
                        }

                        // Load children if not loaded yet - subscription will sync when complete
                        if (!Child.Children.Any())
                        {
                            System.Diagnostics.Debug.WriteLine($"[TreeNode] Loading children async for {DisplayName}");
                            _ = Child.LoadChildrenAsync();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[TreeNode] Children already loaded ({Child.Children.Count}), syncing immediately");

                            // Already loaded, sync immediately
                            SyncChildrenFromViewModel();
                        }
                    }
                }
            }
        }
    }

    private void OnChildViewModelChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[TreeNode] CollectionChanged event fired for {DisplayName}, action: {e.Action}");
        SyncChildrenFromViewModel();
    }

    private void SyncChildrenFromViewModel()
    {
        if (Child == null) return;

        System.Diagnostics.Debug.WriteLine($"[TreeNode] SyncChildrenFromViewModel for {DisplayName}, syncing {Child.Children.Count} children");

        // Clear existing children
        Children.Clear();

        // Add TreeNode wrappers for each child view model
        foreach (var childVm in Child.Children)
        {
            var childNode = new TreeNode
            {
                DisplayName = childVm.DisplayName,
                Description = childVm.Description,
                Child = childVm,
                IsFolder = childVm.IsExpandable,
            };

            Children.Add(childNode);
        }

        System.Diagnostics.Debug.WriteLine($"[TreeNode] Synced {Children.Count} children for {DisplayName}");
    }

    /// <summary>
    /// Gets or sets the child item if leaf node.
    /// </summary>
    public GitHubDisplayItemViewModel? Child
    {
        get => _child;
        set
        {
            if (_child != value)
            {
                // Unsubscribe from old child's CollectionChanged
                if (_child?.Children is INotifyCollectionChanged oldObservable)
                {
                    oldObservable.CollectionChanged -= OnChildViewModelChildrenChanged;
                }

                _child = value;
                OnPropertyChanged();

                // Subscribe to new child's CollectionChanged IMMEDIATELY
                if (_child?.Children is INotifyCollectionChanged newObservable)
                {
                    newObservable.CollectionChanged -= OnChildViewModelChildrenChanged;
                    newObservable.CollectionChanged += OnChildViewModelChildrenChanged;
                    System.Diagnostics.Debug.WriteLine($"[TreeNode] Subscribed to CollectionChanged for {DisplayName} immediately when Child was set");
                }

                // When Child is set, check if we need a placeholder
                CheckAndAddPlaceholder();
            }
        }
    }

    private GitHubDisplayItemViewModel? _child;

    /// <summary>
    /// Gets the children collection for folder nodes.
    /// </summary>
    public ObservableCollection<TreeNode> Children { get; } = new();

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">Name of the property that changed.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
