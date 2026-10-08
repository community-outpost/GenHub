using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.IniEditor.ViewModels;
using GenHub.Features.Tools.IniEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.IniEditor;

/// <summary>
/// Tool plugin implementation for the INI Editor.
/// </summary>
public sealed class IniEditorToolPlugin : IToolPlugin, IFileOpenTarget
{
    private IniEditorView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = ToolConstants.IniEditor.Id,
        Name = ToolConstants.IniEditor.Name,
        Version = ToolConstants.IniEditor.Version,
        Author = ToolConstants.IniEditor.Author,
        Description = ToolConstants.IniEditor.Description,
        IconPath = ToolConstants.IniEditor.IconPath,
        Tags = [.. ToolConstants.IniEditor.Tags],
        IsBundled = ToolConstants.IniEditor.IsBundled,
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

        try
        {
            var viewModel = _serviceProvider.GetRequiredService<IniEditorViewModel>();
            _view = new IniEditorView { DataContext = viewModel };
            return _view;
        }
        catch (InvalidOperationException)
        {
            return new TextBlock { Text = ResolveLoadErrorText() };
        }
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
        const string fallback = "Error loading INI Editor";
        var localization = _serviceProvider?.GetService<ILocalizationService>();
        if (localization != null && localization.TryGetString("Tools.IniEditor.Plugin.LoadError", out var text))
        {
            return text;
        }

        return fallback;
    }

    private async Task<IniEditorViewModel?> GetViewModelAsync()
    {
        if (_view?.DataContext is IniEditorViewModel existing)
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

        return _view?.DataContext as IniEditorViewModel;
    }
}
