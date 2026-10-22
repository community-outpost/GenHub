using Avalonia.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using GenHub.Features.Tools.TextureEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GenHub.Features.Tools.TextureEditor;

/// <summary>
/// Tool plugin for Texture Editor.
/// </summary>
public sealed class TextureEditorToolPlugin : IToolPlugin
{
    private TextureEditorView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = TextureEditorConstants.ToolId,
        Name = TextureEditorConstants.ToolName,
        Version = "1.0.0",
        Author = AppConstants.AppName,
        Description = TextureEditorConstants.ToolDescription,
        IconPath = TextureEditorConstants.IconPath,
        IsBundled = true,
        Tags = ["Content Management"],
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_view == null && _serviceProvider != null)
        {
            var viewModel = _serviceProvider.GetRequiredService<TextureEditorViewModel>();
            _view = new TextureEditorView { DataContext = viewModel };
        }

        return _view ?? (Control)new TextBlock { Text = "Error loading Texture Editor" };
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
