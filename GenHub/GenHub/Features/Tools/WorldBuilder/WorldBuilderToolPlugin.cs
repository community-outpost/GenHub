using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.WorldBuilder.ViewModels;
using GenHub.Features.Tools.WorldBuilder.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder;

/// <summary>
/// Tool plugin implementation for WorldBuilder.
/// </summary>
public sealed class WorldBuilderToolPlugin : IToolPlugin, IFileOpenTarget
{
    private const string LoadErrorKey = "Tools.WorldBuilder.Plugin.LoadError";

    private WorldBuilderView? _view;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = ToolConstants.WorldBuilder.Id,
        Name = ToolConstants.WorldBuilder.Name,
        Version = ToolConstants.WorldBuilder.Version,
        Author = ToolConstants.WorldBuilder.Author,
        Description = ToolConstants.WorldBuilder.Description,
        IconPath = ToolConstants.WorldBuilder.IconPath,
        Tags = [.. ToolConstants.WorldBuilder.Tags],
        IsBundled = ToolConstants.WorldBuilder.IsBundled,
        IsFullScreen = true,
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
            var viewModel = _serviceProvider.GetRequiredService<WorldBuilderViewModel>();
            _view = new WorldBuilderView { DataContext = viewModel };
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

        return await viewModel.OpenMapAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    private static string LoadErrorFromResources()
    {
        try
        {
            var manager = new ResourceManager("GenHub.Resources.Localization.Strings", typeof(WorldBuilderToolPlugin).Assembly);
            return manager.GetString(LoadErrorKey, CultureInfo.CurrentUICulture) ?? LoadErrorKey;
        }
        catch (InvalidOperationException)
        {
            return LoadErrorKey;
        }
        catch (MissingManifestResourceException)
        {
            return LoadErrorKey;
        }
        catch (MissingSatelliteAssemblyException)
        {
            return LoadErrorKey;
        }
    }

    private string ResolveLoadErrorText()
    {
        var localization = _serviceProvider?.GetService<ILocalizationService>();
        if (localization?.TryGetString(LoadErrorKey, out var text) == true && !string.IsNullOrEmpty(text))
        {
            return text;
        }

        return LoadErrorFromResources();
    }

    private async Task<WorldBuilderViewModel?> GetViewModelAsync()
    {
        if (_view?.DataContext is WorldBuilderViewModel existing)
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

        return _view?.DataContext as WorldBuilderViewModel;
    }
}
