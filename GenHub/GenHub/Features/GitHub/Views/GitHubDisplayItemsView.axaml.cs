using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using GenHub.Features.GitHub.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// View for displaying GitHub items tree - handles workflow expansion to load artifacts.
/// </summary>
public partial class GitHubDisplayItemsView : UserControl
{
    private readonly ILogger<GitHubDisplayItemsView>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubDisplayItemsView"/> class.
    /// </summary>
    public GitHubDisplayItemsView()
    {
        try
        {
            InitializeComponent();
            _logger = AppLocator.GetServiceOrDefault<ILogger<GitHubDisplayItemsView>>();
            _logger?.LogDebug("GitHubDisplayItemsView initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing GitHubDisplayItemsView: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Called when the view is loaded.
    /// </summary>
    /// <param name="e">The routed event args.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        try
        {
            if (DataContext is GitHubItemsTreeViewModel viewModel)
            {
                _logger?.LogDebug("GitHubDisplayItemsView loaded with ViewModel");
            }

            // Find the TreeView control
            var treeView = this.FindControl<TreeView>("MainTreeView");
            if (treeView != null)
            {
                // Subscribe to container prepared to handle TreeViewItem expansion
                treeView.ContainerPrepared += OnContainerPrepared;
                _logger?.LogDebug("TreeView handlers attached");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during GitHubDisplayItemsView loaded event");
        }
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is TreeViewItem treeViewItem && treeViewItem.DataContext is TreeNode node)
        {
            _logger?.LogDebug("Container prepared for {DisplayName}, IsExpandable: {IsExpandable}", node.DisplayName, node.Child?.IsExpandable);

            // Unsubscribe first to avoid duplicate subscriptions
            treeViewItem.PropertyChanged -= OnTreeViewItemPropertyChanged;

            // Subscribe to TreeViewItem property changes
            treeViewItem.PropertyChanged += OnTreeViewItemPropertyChanged;

            // Subscribe to TreeNode property changes to sync back to UI
            node.PropertyChanged -= OnTreeNodePropertyChanged;
            node.PropertyChanged += OnTreeNodePropertyChanged;

            // Sync initial state
            treeViewItem.IsExpanded = node.IsExpanded;
        }
    }

    private void OnTreeViewItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(TreeViewItem.IsExpanded) && sender is TreeViewItem item && item.DataContext is TreeNode node)
        {
            _logger?.LogInformation("TreeViewItem IsExpanded changed to {IsExpanded} for {DisplayName}", item.IsExpanded, node.DisplayName);

            // When UI expands, update model which triggers loading
            if (item.IsExpanded && !node.IsExpanded)
            {
                _logger?.LogInformation("Triggering node expansion for {DisplayName}", node.DisplayName);
                node.IsExpanded = true;
            }
            else if (!item.IsExpanded && node.IsExpanded)
            {
                node.IsExpanded = false;
            }
        }
    }

    private void OnTreeNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeNode.IsExpanded) && sender is TreeNode node)
        {
            _logger?.LogDebug("TreeNode IsExpanded changed to {IsExpanded} for {DisplayName}, looking for TreeViewItem", node.IsExpanded, node.DisplayName);

            // Find the TreeViewItem for this node and sync
            var treeView = this.FindControl<TreeView>("MainTreeView");
            if (treeView != null)
            {
                var container = treeView.ContainerFromItem(node);
                if (container is TreeViewItem treeViewItem)
                {
                    _logger?.LogDebug("Syncing TreeViewItem IsExpanded to {IsExpanded} for {DisplayName}", node.IsExpanded, node.DisplayName);
                    treeViewItem.IsExpanded = node.IsExpanded;
                }
            }
        }
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
