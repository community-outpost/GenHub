using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using GenHub.Features.Tools.Views.Dialogs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Implementation of IPublisherStudioDialogService.
/// </summary>
public class PublisherStudioDialogService(
    IDialogService dialogService,
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null,
    ILogger<PublisherStudioDialogService>? logger = null,
    INotificationService? notificationService = null) : IPublisherStudioDialogService
{
    private const string AllFilesFilterName = "All Files";

    /// <inheritdoc/>
    public Func<string, (string Name, string Url, long Size)?>? DuplicateAssetLookup { get; set; }

    /// <inheritdoc/>
    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string? confirmText = null,
        string? cancelText = null,
        string? sessionKey = null)
    {
        confirmText ??= localizationService?.GetString("Tools.PublisherStudio.Dialogs.ConfirmButton") ?? "Confirm";
        cancelText ??= localizationService?.GetString("Tools.PublisherStudio.Dialogs.CancelButton") ?? "Cancel";
        return await dialogService.ShowConfirmationAsync(title, message, confirmText, cancelText, sessionKey);
    }

    /// <inheritdoc/>
    public async Task<bool> ShowSetupWizardAsync(PublisherStudioProject project)
    {
        var wizardTitle = localizationService?.GetString("Tools.PublisherStudio.SetupWizard.Title") ?? "Publisher Setup Wizard";
        return await ShowWizardAsync<PublisherSetupWizardViewModel, PublisherSetupWizardView>(
            closeAction => new PublisherSetupWizardViewModel(project, closeAction, localizationService),
            wizardTitle);
    }

    /// <inheritdoc/>
    public async Task<bool> ShowHostingSettingsDialogAsync()
    {
        // For now, this is handled within the Publisher Profile tab connections.
        // If we need a dedicated dialog later, it should be implemented here.
        await Task.CompletedTask;
        return false;
    }

    /// <inheritdoc/>
    public async Task<CatalogContentItem?> ShowAddContentDialogAsync(string? initialPath = null, PublisherCatalog? catalog = null)
    {
        return await ShowAddContentDialogAsync(initialPath != null ? [initialPath] : null, catalog);
    }

    /// <inheritdoc/>
    public async Task<CatalogContentItem?> ShowAddContentDialogAsync(IEnumerable<string>? initialPaths = null, PublisherCatalog? catalog = null)
    {
        return await ShowDialogAsync<AddContentDialogViewModel, AddContentDialogView, CatalogContentItem>(
            callback =>
            {
                var vm = new AddContentDialogViewModel(res => callback(res!), this, localizationService, catalog, notificationService);
                var pathsList = initialPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                if (pathsList is { Count: > 0 })
                {
                    vm.PopulateFromPaths(pathsList);
                }

                return vm;
            });
    }

    /// <inheritdoc/>
    public async Task<CatalogContentItem?> ShowEditContentDialogAsync(CatalogContentItem existing, PublisherCatalog? catalog = null, Func<CatalogContentItem, Task>? onDelete = null)
    {
        return await ShowDialogAsync<AddContentDialogViewModel, AddContentDialogView, CatalogContentItem>(
            callback => new AddContentDialogViewModel(existing, res => callback(res!), this, localizationService, catalog, notificationService, onDelete));
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog, IEnumerable<string>? initialPaths = null)
    {
        return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
           async callback =>
           {
               var vm = new AddReleaseDialogViewModel(contentItem, catalog, callback, this, localizationService, notificationService: notificationService);
               await StageInitialArtifactsAsync(vm, initialPaths);
               return vm;
           });
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowEditReleaseDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog)
    {
        return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
            callback => new AddReleaseDialogViewModel(existing, parent, catalog, callback, this, localizationService, notificationService: notificationService));
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowAddAddonDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog, IEnumerable<string>? initialPaths = null)
    {
        return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
           async callback =>
           {
               var vm = new AddReleaseDialogViewModel(contentItem, catalog, callback, this, localizationService, isAddon: true, notificationService: notificationService);
               await StageInitialArtifactsAsync(vm, initialPaths);
               return vm;
           });
    }

    /// <inheritdoc/>
    public async Task<ContentRelease?> ShowEditAddonDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog)
    {
        return await ShowDialogAsync<AddReleaseDialogViewModel, AddReleaseDialogView, ContentRelease>(
            callback => new AddReleaseDialogViewModel(existing, parent, catalog, callback, this, localizationService, isAddon: true, notificationService: notificationService));
    }

    /// <inheritdoc/>
    public async Task<ReleaseArtifact?> ShowAddArtifactDialogAsync()
    {
        return await ShowDialogAsync<AddArtifactDialogViewModel, AddArtifactDialogView, ReleaseArtifact>(
           callback => new AddArtifactDialogViewModel(callback, localizationService));
    }

    /// <inheritdoc/>
    public async Task<CatalogDependency?> ShowAddDependencyDialogAsync(PublisherCatalog catalog, CatalogContentItem currentContent)
    {
        return await ShowDialogAsync<AddDependencyDialogViewModel, AddDependencyDialogView, CatalogDependency>(
            callback => new AddDependencyDialogViewModel(catalog, currentContent, callback, localizationService));
    }

    /// <inheritdoc/>
    public async Task<PublisherReferral?> ShowAddReferralDialogAsync()
    {
        // In a future release, IPublisherSubscriptionStore will be queried to supplement known publishers with user subscriptions.
        var availablePublishers = GetKnownPublishers();

        return await ShowDialogAsync<AddReferralDialogViewModel, AddReferralDialogView, PublisherReferral>(
            callback => new AddReferralDialogViewModel(callback, availablePublishers, localizationService));
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
    public async Task<string?> ShowCatalogFilePickerAsync(string title)
    {
        return await ShowOpenPickerAsync(
            title,
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Catalog JSON (*.json)")
                {
                    Patterns = ["*.json"],
                },
                new Avalonia.Platform.Storage.FilePickerFileType(AllFilesFilterName)
                {
                    Patterns = ["*.*"],
                },
            ]);
    }

    /// <inheritdoc/>
    public async Task<string?> ShowFilePickerAsync(string title)
    {
        return await ShowOpenPickerAsync(
            title,
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Supported Content Archives (*.zip, *.7z, *.rar, *.tar.gz, *.big)")
                {
                    Patterns = ["*.zip", "*.7z", "*.rar", "*.tar.gz", "*.big"],
                },
                new Avalonia.Platform.Storage.FilePickerFileType(AllFilesFilterName)
                {
                    Patterns = ["*.*"],
                },
            ]);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ShowFilesPickerAsync(string title)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return [];

        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Supported Content Files (*.zip, *.7z, *.rar, *.tar.gz, *.big)")
                {
                    Patterns = ["*.zip", "*.7z", "*.rar", "*.tar.gz", "*.big"],
                },
                new Avalonia.Platform.Storage.FilePickerFileType(AllFilesFilterName)
                {
                    Patterns = ["*.*"],
                },
            ],
        };

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    /// <inheritdoc/>
    public async Task<string?> ShowImagePickerAsync(string title)
    {
        return await ShowOpenPickerAsync(
            title,
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Image Files (*.png, *.jpg, *.jpeg, *.webp)")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                },
                new Avalonia.Platform.Storage.FilePickerFileType(AllFilesFilterName)
                {
                    Patterns = ["*.*"],
                },
            ]);
    }

    /// <inheritdoc/>
    public async Task<string?> ShowVideoPickerAsync(string title)
    {
        return await ShowOpenPickerAsync(
            title,
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Video Files (*.mp4, *.webm, *.mkv, *.avi)")
                {
                    Patterns = ["*.mp4", "*.webm", "*.mkv", "*.avi"],
                },
                new Avalonia.Platform.Storage.FilePickerFileType(AllFilesFilterName)
                {
                    Patterns = ["*.*"],
                },
            ]);
    }

    /// <inheritdoc/>
    public async Task<string?> ShowFolderPickerAsync(string title)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var options = new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        };

        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    /// <inheritdoc/>
    public async Task<string?> ShowRenameCatalogDialogAsync(string currentName)
    {
        return await ShowDialogAsync<RenameCatalogDialogViewModel, RenameCatalogDialogView, string>(
            callback => new RenameCatalogDialogViewModel(currentName, res => callback(res!)));
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
                CatalogUrl = CatalogConstants.GeneralsOnlineCatalogUrl,
            },
            new()
            {
                PublisherId = "cnc-labs",
                PublisherName = "CNC Labs",
                CatalogUrl = CatalogConstants.CncLabsDownloadsCatalogUrl,
            },
            new()
            {
                PublisherId = "community-outpost",
                PublisherName = "Community Outpost",
                CatalogUrl = CatalogConstants.CommunityOutpostCatalogUrl,
            },
        ];
    }

    private static async Task<TResult?> ShowDialogCoreAsync<TViewModel, TView, TResult>(
        Func<Action<TResult>, Task<TViewModel>> viewModelFactory,
        string? title = null,
        TResult? defaultResult = default)
        where TViewModel : class
        where TView : Control, new()
    {
        var tcs = new TaskCompletionSource<TResult?>();
        Window? window = null;

        void SetResult(TResult result)
        {
            tcs.TrySetResult(result);
            window?.Close();
        }

        var viewModel = await viewModelFactory(SetResult);
        var view = new TView { DataContext = viewModel };

        var toolWindow = new ToolDialogWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        if (!string.IsNullOrEmpty(title))
        {
            toolWindow.Title = title;
        }

        var mainWindow = GetMainWindow();
        ConstrainDialogToWorkingArea(toolWindow, mainWindow);

        toolWindow.SetDialogContent(view);
        window = toolWindow;

        window.Closed += (s, e) => tcs.TrySetResult(defaultResult);

        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);
        }
        else
        {
            tcs.TrySetResult(defaultResult);
        }

        return await tcs.Task;
    }

    private static async Task<bool> ShowWizardAsync<TViewModel, TView>(
        Func<Action<bool>, TViewModel> viewModelFactory,
        string title)
        where TViewModel : class
        where TView : Control, new()
    {
        var tcs = new TaskCompletionSource<bool>();
        var view = new TView();
        var toolWindow = new ToolDialogWindow
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var mainWindow = GetMainWindow();
        ConstrainDialogToWorkingArea(toolWindow, mainWindow);

        var vm = viewModelFactory(result =>
        {
            tcs.TrySetResult(result);
            toolWindow.Close();
        });
        view.DataContext = vm;

        toolWindow.SetDialogContent(view);

        toolWindow.Closed += (s, e) => tcs.TrySetResult(false);

        if (mainWindow != null)
        {
            await toolWindow.ShowDialog(mainWindow);
        }
        else
        {
            tcs.TrySetResult(false);
        }

        return await tcs.Task;
    }

    private static Task<TResult?> ShowDialogAsync<TViewModel, TView, TResult>(
        Func<Action<TResult>, Task<TViewModel>> viewModelFactory,
        string? title = null,
        TResult? defaultResult = default)
        where TViewModel : class
        where TView : Control, new()
    {
        return ShowDialogCoreAsync<TViewModel, TView, TResult>(viewModelFactory, title, defaultResult);
    }

    private static Task<TResult?> ShowDialogAsync<TViewModel, TView, TResult>(
        Func<Action<TResult>, TViewModel> viewModelFactory,
        string? title = null,
        TResult? defaultResult = default)
        where TViewModel : class
        where TView : Control, new()
    {
        return ShowDialogCoreAsync<TViewModel, TView, TResult>(
            callback => Task.FromResult(viewModelFactory(callback)),
            title,
            defaultResult);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    private static void ConstrainDialogToWorkingArea(Window dialog, Window? owner)
    {
        var screen = (owner != null ? dialog.Screens.ScreenFromWindow(owner) : null) ?? dialog.Screens.Primary;
        if (screen == null)
        {
            return;
        }

        var workingArea = screen.WorkingArea;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var maxDialogWidth = Math.Max(320, (workingArea.Width / scaling) - 48);
        var maxDialogHeight = Math.Max(320, (workingArea.Height / scaling) - 64);

        dialog.MaxWidth = maxDialogWidth;
        dialog.MaxHeight = maxDialogHeight;

        if (dialog.Width > maxDialogWidth)
        {
            dialog.Width = maxDialogWidth;
        }

        if (dialog.Height > maxDialogHeight)
        {
            dialog.Height = maxDialogHeight;
        }
    }


    private static async Task<string?> ShowOpenPickerAsync(
        string title,
        IReadOnlyList<Avalonia.Platform.Storage.FilePickerFileType> fileTypes)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes,
        };

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task StageInitialArtifactsAsync(AddReleaseDialogViewModel vm, IEnumerable<string>? initialPaths)
    {
        var pathsList = initialPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (pathsList is { Count: > 0 })
        {
            try
            {
                await vm.AddArtifactsFromPathsAsync(pathsList);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to stage initial artifacts for dialog.");
                var title = localizationService?.GetString("Tools.PublisherStudio.Dialogs.StageArtifactsFailedTitle") ?? "Could Not Stage Files";
                var message = localizationService?.GetString("Tools.PublisherStudio.Dialogs.StageArtifactsFailedMessage") ?? "Some dropped files could not be staged and were skipped. See logs for details.";
                notificationService?.ShowWarning(title, message);
            }
        }
    }
}
