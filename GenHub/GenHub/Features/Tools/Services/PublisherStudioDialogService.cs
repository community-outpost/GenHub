using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels.Dialogs;
using GenHub.Features.Tools.Views.Dialogs;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Implementation of IPublisherStudioDialogService.
/// </summary>
public class PublisherStudioDialogService(IHostingProviderFactory hostingProviderFactory) : IPublisherStudioDialogService
{
    private readonly IHostingProviderFactory _hostingProviderFactory = hostingProviderFactory;

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
            callback => new AddContentDialogViewModel(callback));
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog)
    {
         return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
            callback => new AddReleaseDialogViewModel(contentItem, catalog, callback, this));
    }

    /// <inheritdoc/>
    public async Task<ReleaseArtifact?> ShowAddArtifactDialogAsync()
    {
         return await ShowDialogAsync<AddArtifactDialogViewModel, AddArtifactDialogView, ReleaseArtifact>(
            callback => new AddArtifactDialogViewModel(callback, _hostingProviderFactory));
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
        // Get available publishers from subscriptions (for now, we'll include static known publishers)
        // TODO: Inject IPublisherSubscriptionStore and fetch actual subscriptions
        var availablePublishers = GetKnownPublishers();

        return await ShowDialogAsync<AddReferralDialogViewModel, AddReferralDialogView, PublisherReferral>(
            callback => new AddReferralDialogViewModel(callback, availablePublishers));
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

    /// <summary>
    /// Gets a list of known/static publishers for quick selection in referrals.
    /// </summary>
    private static List<PublisherReferralOption> GetKnownPublishers()
    {
        return
        [
            new() { PublisherId = "moddb", PublisherName = "ModDB", CatalogUrl = "https://api.moddb.com/catalog.json" },
            new() { PublisherId = "cnclabs", PublisherName = "CNC Labs", CatalogUrl = "https://github.com/CnC-Labs/mods-catalog/raw/main/catalog.json" },
            new() { PublisherId = "generals", PublisherName = "Generals", CatalogUrl = "https://example.com/generals/catalog.json" },

            // Add more known publishers as needed
        ];
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
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
}
