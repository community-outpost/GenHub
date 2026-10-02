using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.RmlEditor.ViewModels;
using GenHub.Features.Tools.RmlEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.RmlEditor;

/// <summary>
/// Tool plugin implementation for the RML Editor.
/// </summary>
public sealed class RmlEditorToolPlugin : IToolPlugin, IFileOpenTarget
{
    private RmlEditorView? _view;
    private IServiceProvider? _serviceProvider;
    private int _openedTracked;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = ToolConstants.RmlEditor.Id,
        Name = ToolConstants.RmlEditor.Name,
        Version = ToolConstants.RmlEditor.Version,
        Author = ToolConstants.RmlEditor.Author,
        Description = ToolConstants.RmlEditor.Description,
        IconPath = ToolConstants.RmlEditor.IconPath,
        Tags = [.. ToolConstants.RmlEditor.Tags],
        IsBundled = ToolConstants.RmlEditor.IsBundled,
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
            return new TextBlock { Text = ResolveLoadErrorText() };
        }

        var viewModel = _serviceProvider.GetRequiredService<RmlEditorViewModel>();
        _view = new RmlEditorView { DataContext = viewModel };
        return _view;
    }

    /// <inheritdoc />
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        if (Interlocked.Exchange(ref _openedTracked, 1) == 0)
        {
            _serviceProvider.GetService<ITelemetryService>()?.TrackEvent(TelemetryConstants.Events.RmlEditorOpened);
        }
    }

    /// <inheritdoc />
    public void OnDeactivated()
    {
        // View and ViewModel state is preserved for now.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_view?.DataContext is IDisposable disposableVm)
        {
            disposableVm.Dispose();
        }

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

    private string ResolveLoadErrorText()
    {
        const string fallback = "Error loading RML Editor";
        var localization = _serviceProvider?.GetService<ILocalizationService>();
        if (localization?.TryGetString("Tools.RmlEditor.Plugin.LoadError", out var text) == true)
        {
            return text;
        }

        return fallback;
    }

    private async Task<RmlEditorViewModel?> GetViewModelAsync()
    {
        if (_view?.DataContext is RmlEditorViewModel existing)
        {
            return existing;
        }

        if (_serviceProvider == null)
        {
            return null;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => CreateControl());
        }
        else
        {
            CreateControl();
        }

        return _view?.DataContext as RmlEditorViewModel;
    }
}
