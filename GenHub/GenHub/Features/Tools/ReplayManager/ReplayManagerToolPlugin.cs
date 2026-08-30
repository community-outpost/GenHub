using System;
using System.Collections.Generic;
using Avalonia.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using GenHub.Features.Tools.ReplayManager.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Features.Tools.ReplayManager;

/// <summary>
/// Tool plugin implementation for the Replay Manager.
/// </summary>
public sealed class ReplayManagerToolPlugin : IToolPlugin
{
    private ReplayManagerView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = ToolConstants.ReplayManager.Id,
        Name = ToolConstants.ReplayManager.Name,
        Version = ToolConstants.ReplayManager.Version,
        Author = ToolConstants.ReplayManager.Author,
        Description = ToolConstants.ReplayManager.Description,
        Tags = [.. ToolConstants.ReplayManager.Tags],
        IconPath = ToolConstants.ReplayManager.IconPath,
        IsBundled = ToolConstants.ReplayManager.IsBundled,
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_view == null && _serviceProvider != null)
        {
            var viewModel = _serviceProvider.GetRequiredService<ReplayManagerViewModel>();
            _view = new ReplayManagerView { DataContext = viewModel };

            // Initialize the ViewModel to load replays
            _ = viewModel.InitializeAsync();
        }

        return _view ?? (Control)new TextBlock { Text = "Error loading Replay Manager" };
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
