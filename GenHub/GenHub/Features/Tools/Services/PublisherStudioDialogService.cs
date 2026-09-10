using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using GenHub.Features.Tools.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Implementation of IPublisherStudioDialogService.
/// </summary>
public class PublisherStudioDialogService(IDialogService? dialogService = null) : IPublisherStudioDialogService
{
    private readonly IDialogService? _dialogService = dialogService;

    /// <inheritdoc/>
    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        string? sessionKey = null)
    {
        if (_dialogService != null)
        {
            return await _dialogService.ShowConfirmationAsync(title, message, confirmText, cancelText, sessionKey);
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ShowSetupWizardAsync(PublisherStudioProject project)
    {
        return await ShowWizardAsync<PublisherSetupWizardViewModel, PublisherSetupWizardView>(
            closeAction => new PublisherSetupWizardViewModel(project, closeAction));
    }

    /// <inheritdoc/>
    public async Task<bool> ShowHostingSettingsDialogAsync()
    {
        // For now, this is handled within the Publisher Profile tab connections.
        // If we need a dedicated dialog later, it should be implemented here.
        await Task.CompletedTask;
        return true;
    }

    /// <inheritdoc/>
    public async Task<CatalogContentItem?> ShowAddContentDialogAsync()
    {
        return await ShowDialogAsync<AddContentDialogViewModel, AddContentDialogView, CatalogContentItem>(
            callback => new AddContentDialogViewModel(callback, this));
    }

    /// <inheritdoc/>
    public async Task<CatalogContentItem?> ShowEditContentDialogAsync(CatalogContentItem existing)
    {
        return await ShowDialogAsync<AddContentDialogViewModel, AddContentDialogView, CatalogContentItem>(
            callback => new AddContentDialogViewModel(existing, callback, this));
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog)
    {
         return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
            callback => new AddReleaseDialogViewModel(contentItem, catalog, callback, this));
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowEditReleaseDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog)
    {
        return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
            callback => new AddReleaseDialogViewModel(existing, parent, catalog, callback, this));
    }

    /// <inheritdoc/>
    public async Task<ReleaseArtifact?> ShowAddArtifactDialogAsync()
    {
         return await ShowDialogAsync<AddArtifactDialogViewModel, AddArtifactDialogView, ReleaseArtifact>(
            callback => new AddArtifactDialogViewModel(callback));
    }

    /// <inheritdoc/>
    public async Task<CatalogDependency?> ShowAddDependencyDialogAsync(PublisherCatalog catalog, CatalogContentItem currentContent)
    {
        return await ShowDialogAsync<AddDependencyDialogViewModel, AddDependencyDialogView, CatalogDependency>(
            callback => new AddDependencyDialogViewModel(catalog, currentContent, callback));
    }

    /// <inheritdoc/>
    public async Task<PublisherReferral?> ShowAddReferralDialogAsync()
    {
        // In a future release, IPublisherSubscriptionStore will be queried to supplement known publishers with user subscriptions.
        var availablePublishers = GetKnownPublishers();

        return await ShowDialogAsync<AddReferralDialogViewModel, AddReferralDialogView, PublisherReferral>(
            callback => new AddReferralDialogViewModel(callback, availablePublishers));
    }

    /// <inheritdoc/>
    public async Task<string?> ShowProjectOpenPromptAsync(string title)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("JSON Files") { Patterns = ["*.json"] },
            ],
        };

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    /// <inheritdoc/>
    public async Task<string?> ShowProjectSavePromptAsync(string title)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var options = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = ".json",
            SuggestedFileName = "publisher-project.json",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("JSON Files") { Patterns = ["*.json"] },
            ],
        };

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
    }

    /// <inheritdoc/>
    public async Task<string?> ShowFilePickerAsync(string title)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Supported Content Archives (*.zip, *.7z, *.rar, *.tar.gz, *.big)")
                {
                    Patterns = ["*.zip", "*.7z", "*.rar", "*.tar.gz", "*.big"],
                },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"],
                },
            ],
        };

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    /// <inheritdoc/>
    public async Task<string?> ShowRenameCatalogDialogAsync(string currentName)
    {
        return await ShowDialogAsync<RenameCatalogDialogViewModel, RenameCatalogDialogView, string>(
            callback => new RenameCatalogDialogViewModel(currentName, callback!));
    }

    /// <summary>
    /// Gets a list of known/static publishers for quick selection in referrals.
    /// </summary>
    private static List<PublisherReferralOption> GetKnownPublishers()
    {
        return
        [
            new()
            {
                PublisherId = "generals-online",
                PublisherName = "GeneralsOnline",
                CatalogUrl = "https://cdn.playgenerals.online/catalog.json",
            },
            new()
            {
                PublisherId = "cnc-labs",
                PublisherName = "CNC Labs",
                CatalogUrl = "https://www.cnclabs.com/downloads/catalog.json",
            },
            new()
            {
                PublisherId = "community-outpost",
                PublisherName = "Community Outpost",
                CatalogUrl = "https://raw.githubusercontent.com/community-outpost/genhub-catalog/main/catalog.json",
            },
        ];
    }

    private static async Task<TResult?> ShowDialogAsync<TViewModel, TView, TResult>(
        Func<Action<TResult>, TViewModel> viewModelFactory)
        where TViewModel : class
        where TView : Control, new()
        where TResult : class
    {
        var tcs = new TaskCompletionSource<TResult?>();
        Window? window = null;

        void SetResult(TResult result)
        {
            tcs.TrySetResult(result);
            window?.Close();
        }

        var viewModel = viewModelFactory(SetResult);
        var view = new TView { DataContext = viewModel };

        window = new ToolDialogWindow
        {
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        window.Closed += (s, e) => tcs.TrySetResult(null);

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
           await window.ShowDialog(mainWindow);
        }
        else
        {
           tcs.TrySetResult(null);
        }

        return await tcs.Task;
    }

    private static async Task<bool> ShowWizardAsync<TViewModel, TView>(
        Func<Action<bool>, TViewModel> viewModelFactory)
        where TViewModel : class
        where TView : Control, new()
    {
        var tcs = new TaskCompletionSource<bool>();
        Window? window = null;

        void SetResult(bool result)
        {
            tcs.TrySetResult(result);
            window?.Close();
        }

        var viewModel = viewModelFactory(SetResult);
        var view = new TView { DataContext = viewModel };

        window = new ToolDialogWindow
        {
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "Publisher Setup Wizard",
        };

        window.Closed += (s, e) => tcs.TrySetResult(false);

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);
        }
        else
        {
            tcs.TrySetResult(false);
        }

        return await tcs.Task;
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
