using System;
using Avalonia.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using GenHub.Features.Tools.GenHotkeys.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Features.Tools.GenHotkeys;

/// <summary>
/// Tool plugin implementation for the visual C&amp;C Generals / Zero Hour Hotkeys Editor.
/// </summary>
public sealed class GenHotkeysToolPlugin : IToolPlugin, IDisposable
{
    private IServiceProvider? _serviceProvider;
    private GenHotkeysView? _view;

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new()
    {
        Id = GenHotkeysConstants.ToolId,
        Name = GenHotkeysConstants.ToolName,
        Version = GenHotkeysConstants.PluginVersion,
        Author = AppConstants.AppName,
        Description = GenHotkeysConstants.ToolDescription,
        IconPath = GenHotkeysConstants.ToolIconUri,
        IsBundled = true,
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_view != null)
        {
            return _view;
        }

        if (_serviceProvider != null)
        {
            var viewModel = _serviceProvider.GetRequiredService<GenHotkeysViewModel>();
            _view = new GenHotkeysView
            {
                DataContext = viewModel,
            };

            // Trigger activation load on first view creation
            _ = viewModel.InitializeAsync();
        }

        return _view ?? new GenHotkeysView();
    }

    /// <inheritdoc />
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void OnDeactivated()
    {
        // No deactivation cleanup required for hotkeys tool
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_view?.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _view = null;
        _serviceProvider = null;
    }
}
