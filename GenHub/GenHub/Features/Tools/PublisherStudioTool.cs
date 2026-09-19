using Avalonia.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.Views.PublisherStudio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools;

/// <summary>
/// Built-in tool plugin for the Publisher Studio.
/// </summary>
public class PublisherStudioTool(ILogger<PublisherStudioTool> logger) : IToolPlugin
{
    private const int AutoSaveFlushTimeoutSeconds = 5;

    private PublisherStudioViewModel? _viewModel;
    private PublisherStudioView? _view;
    private Task? _autoSaveTask;

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
        IconPath = UriConstants.PublisherStudioIconUri,
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
        if (_viewModel == null)
        {
            _viewModel = serviceProvider.GetRequiredService<PublisherStudioViewModel>();
            _ = _viewModel.InitializeAsync().ContinueWith(
                task => logger.LogError(task.Exception, "Publisher Studio background initialization failed."),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        if (_view != null)
        {
            // Set DataContext after ViewModel is properly resolved from DI
            _view.DataContext = _viewModel;
        }
    }

    /// <inheritdoc/>
    public void OnDeactivated()
    {
        // Auto-save if project has a path and has unsaved changes. The plugin contract
        // is synchronous, so the save stays fire-and-forget but failures are observed
        // and logged instead of silently discarding the user's changes.
        if (_viewModel?.CurrentProject != null
            && _viewModel.HasUnsavedChanges
            && !string.IsNullOrEmpty(_viewModel.CurrentProject.ProjectPath))
        {
            // Trigger auto-save asynchronously; the task is tracked so Dispose can flush it.
            _autoSaveTask = _viewModel.SaveProjectAsync().ContinueWith(
                task => logger.LogError(task.Exception, "Publisher Studio auto-save on deactivation failed."),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_autoSaveTask is { IsCompleted: false } pendingSave)
        {
            try
            {
                if (!pendingSave.Wait(TimeSpan.FromSeconds(AutoSaveFlushTimeoutSeconds)))
                {
                    logger.LogWarning("Publisher Studio auto-save did not complete before tool disposal.");
                }
            }
            catch (AggregateException ex)
            {
                logger.LogError(ex, "Publisher Studio auto-save failed before tool disposal.");
            }
        }

        _autoSaveTask = null;
        _view = null;
        _viewModel?.Dispose();
        _viewModel = null;
    }
}
