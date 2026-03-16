using Avalonia.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Features.Tools.MapManager.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GenHub.Features.Tools.MapManager;

/// <summary>
/// Tool plugin for Map Manager.
/// </summary>
public sealed class MapManagerToolPlugin : IToolPlugin
{
    private MapManagerView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = MapManagerConstants.ToolId,
        Name = MapManagerConstants.ToolName,
        Version = "1.0.0",
        Author = AppConstants.AppName,
        Description = MapManagerConstants.ToolDescription,
        IconPath = "🗺️",
        IsBundled = true,
        Tags = ["Content Management"],
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_view == null && _serviceProvider != null)
        {
            var viewModel = _serviceProvider.GetRequiredService<MapManagerViewModel>();
            _view = new MapManagerView { DataContext = viewModel };

            // Initialize the ViewModel to load maps
            _ = viewModel.InitializeAsync();
        }

        return _view ?? (Control)new TextBlock { Text = "Error loading Map Manager" };
    }

    /// <inheritdoc />
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void OnDeactivated()
    {
        // View and ViewModel state is preserved for now.
        // Could call a reset or save method on ViewModel if needed.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _view = null;
        _serviceProvider = null;
    }
}
