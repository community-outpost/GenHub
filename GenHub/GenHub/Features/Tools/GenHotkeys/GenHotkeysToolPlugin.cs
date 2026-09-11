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
/// Tool plugin for GenHotkeys Visual Hotkey Editor.
/// </summary>
public sealed class GenHotkeysToolPlugin : IToolPlugin
{
    private GenHotkeysView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = GenHotkeysConstants.ToolId,
        Name = GenHotkeysConstants.ToolName,
        Version = "1.0.0",
        Author = AppConstants.AppName,
        Description = GenHotkeysConstants.ToolDescription,
        IconPath = "⌨️",
        IsBundled = true,
        Tags = ["Configuration", "Hotkeys", "Modding"],
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_view == null && _serviceProvider != null)
        {
            var viewModel = _serviceProvider.GetRequiredService<GenHotkeysViewModel>();
            _view = new GenHotkeysView { DataContext = viewModel };

            _ = viewModel.InitializeAsync();
        }

        return _view ?? (Control)new TextBlock { Text = "Error loading Hotkeys Editor" };
    }

    /// <inheritdoc />
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void OnDeactivated()
    {
        if (_view?.DataContext is GenHotkeysViewModel vm)
        {
            _ = vm.SaveCurrentProfileAsync();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _view = null;
        _serviceProvider = null;
    }
}
