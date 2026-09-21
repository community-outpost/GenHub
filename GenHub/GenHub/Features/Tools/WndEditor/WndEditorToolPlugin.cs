using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.WndEditor.ViewModels;
using GenHub.Features.Tools.WndEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor;

/// <summary>
/// Tool plugin implementation for the WND Editor.
/// </summary>
public sealed class WndEditorToolPlugin : IToolPlugin, IFileOpenTarget
{
    private WndEditorView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = ToolConstants.WndEditor.Id,
        Name = ToolConstants.WndEditor.Name,
        Version = ToolConstants.WndEditor.Version,
        Author = ToolConstants.WndEditor.Author,
        Description = ToolConstants.WndEditor.Description,
        Tags = [.. ToolConstants.WndEditor.Tags],
        IsBundled = ToolConstants.WndEditor.IsBundled,
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_view != null)
        {
            return _view;
        }

        if (_serviceProvider == null)
        {
            return new TextBlock { Text = "Error loading WND Editor" };
        }

        var viewModel = _serviceProvider.GetRequiredService<WndEditorViewModel>();
        _view = new WndEditorView { DataContext = viewModel };
        return _view;
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
        _view = null;
        _serviceProvider = null;
    }

    /// <inheritdoc />
    public async Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var viewModel = await GetViewModelAsync().ConfigureAwait(false);
        if (viewModel == null)
        {
            return false;
        }

        return await viewModel.OpenFileAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WndEditorViewModel?> GetViewModelAsync()
    {
        if (_view?.DataContext is WndEditorViewModel existing)
        {
            return existing;
        }

        if (_serviceProvider == null)
        {
            return null;
        }

        if (Application.Current != null && !Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => CreateControl());
        }
        else
        {
            CreateControl();
        }

        return _view?.DataContext as WndEditorViewModel;
    }
}
