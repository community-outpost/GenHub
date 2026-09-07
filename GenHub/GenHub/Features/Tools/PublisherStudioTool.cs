using System;
using Avalonia.Controls;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.Views.PublisherStudio;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Features.Tools;

/// <summary>
/// Built-in tool plugin for the Publisher Studio.
/// </summary>
public class PublisherStudioTool : IToolPlugin
{
    private PublisherStudioViewModel? _viewModel;
    private PublisherStudioView? _view;

    /// <inheritdoc/>
    public ToolMetadata Metadata => new()
    {
        Id = "publisher-studio",
        Name = "Publisher Studio",
        Description = "Create, manage, and publish content catalogs.",
        Author = "GenHub Team",
        Version = "1.0.0",
        IsBundled = true,
        IsFullScreen = true,
        IconPath = "/Assets/Icons/tools.png", // Placeholder or existing icon
    };

    /// <inheritdoc/>
    public Control CreateControl()
    {
        _view = new PublisherStudioView();
        if (_viewModel != null)
        {
            _view.DataContext = _viewModel;
        }

        return _view;
    }

    /// <inheritdoc/>
    public void OnActivated(IServiceProvider serviceProvider)
    {
        // Only create ViewModel once - preserve state across activations
        _viewModel ??= serviceProvider.GetRequiredService<PublisherStudioViewModel>();

        if (_view != null)
        {
            // Set DataContext after ViewModel is properly resolved from DI
            _view.DataContext = _viewModel;
        }
    }

    /// <inheritdoc/>
    public void OnDeactivated()
    {
        // Auto-save if project has a path and has unsaved changes
        if (_viewModel?.CurrentProject != null
            && _viewModel.HasUnsavedChanges
            && !string.IsNullOrEmpty(_viewModel.CurrentProject.ProjectPath))
        {
            // Trigger auto-save asynchronously
            _ = _viewModel.SaveProjectAsync();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _view = null;
        _viewModel = null;
    }
}
