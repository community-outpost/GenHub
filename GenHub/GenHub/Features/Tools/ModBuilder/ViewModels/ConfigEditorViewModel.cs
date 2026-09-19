using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing ModBuilder configuration (bundle items and packs).
/// </summary>
public partial class ConfigEditorViewModel(
    IConfigurationLoaderService configurationLoaderService,
    INotificationService notificationService,
    ILocalizationService localizationService,
    ILogger<ConfigEditorViewModel> logger) : ObservableObject
{
    /// <summary>
    /// Gets or sets the current project.
    /// </summary>
    [ObservableProperty]
    private ModBuilderProject? _currentProject;

    /// <summary>
    /// Gets or sets the build configuration.
    /// </summary>
    [ObservableProperty]
    private BuildConfiguration? _configuration;

    /// <summary>
    /// Gets the list of bundle items.
    /// </summary>
    public ObservableCollection<BundleItemEditorViewModel> BundleItems { get; } = [];

    /// <summary>
    /// Gets the list of bundle packs.
    /// </summary>
    public ObservableCollection<BundlePackConfigViewModel> BundlePacks { get; } = [];

    /// <summary>
    /// Gets the list of selectable bundle items for the currently selected bundle pack.
    /// </summary>
    public ObservableCollection<BundleItemSelectionItemViewModel> PackItemSelections { get; } = [];

    /// <summary>
    /// Gets or sets the selected bundle item.
    /// </summary>
    [ObservableProperty]
    private BundleItemEditorViewModel? _selectedBundleItem;

    /// <summary>
    /// Gets or sets the selected bundle pack.
    /// </summary>
    [ObservableProperty]
    private BundlePackConfigViewModel? _selectedBundlePack;

    /// <summary>
    /// Gets or sets the active tab index (0 = Items, 1 = Packs).
    /// </summary>
    [ObservableProperty]
    private int _activeTabIndex;

    /// <summary>
    /// Gets or sets a value indicating whether changes have been made.
    /// </summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>
    /// Gets or sets a value indicating whether the configuration is still loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = true;

    private ProjectFileSnapshot? _fileSnapshot;

    /// <summary>
    /// Initializes the editor with a project.
    /// </summary>
    /// <param name="project">The mod project to initialize with.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync(ModBuilderProject project, CancellationToken cancellationToken = default)
    {
        CurrentProject = project;
        Configuration = project.Configuration;

        if (Configuration == null)
        {
            Configuration = new BuildConfiguration();
            project.Configuration = Configuration;
        }

        try
        {
            await LoadConfigurationAsync().ConfigureAwait(false);
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private static async Task RunOnUIThreadAsync(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    /// <summary>
    /// Handles changes when the selected bundle pack changes.
    /// </summary>
    /// <param name="value">The new selected pack.</param>
    partial void OnSelectedBundlePackChanged(BundlePackConfigViewModel? value)
    {
        UpdatePackItemSelections();
        UpdateBundleItemPackLinks();
        RemoveBundlePackCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBundleItemChanged(BundleItemEditorViewModel? value)
    {
        UpdateBundleItemPackLinks();
        RemoveBundleItemCommand.NotifyCanExecuteChanged();
    }

    private void UpdatePackItemSelections()
    {
        if (SelectedBundlePack == null)
        {
            PackItemSelections.Clear();
            return;
        }

        if (PackItemSelections.Count == BundleItems.Count &&
            PackItemSelections.Select(p => p.Name).SequenceEqual(BundleItems.Select(b => b.Name), StringComparer.OrdinalIgnoreCase))
        {
            foreach (var selection in PackItemSelections)
            {
                selection.IsSelected = SelectedBundlePack.ItemNames.Contains(selection.Name, StringComparer.OrdinalIgnoreCase);
            }

            return;
        }

        PackItemSelections.Clear();
        foreach (var item in BundleItems)
        {
            var itemName = item.Name;
            var isSelected = SelectedBundlePack.ItemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            var selectionVm = new BundleItemSelectionItemViewModel(
                itemName,
                isSelected,
                selected => OnPackItemSelectedChanged(this, itemName, selected));
            PackItemSelections.Add(selectionVm);
        }
    }

    private void UpdateBundleItemPackLinks()
    {
        if (SelectedBundleItem == null)
        {
            return;
        }

        if (SelectedBundleItem.PackLinks.Count == BundlePacks.Count &&
            SelectedBundleItem.PackLinks.Select(l => l.PackName).SequenceEqual(BundlePacks.Select(p => p.Name), StringComparer.OrdinalIgnoreCase))
        {
            foreach (var link in SelectedBundleItem.PackLinks)
            {
                var targetPack = BundlePacks.FirstOrDefault(p => p.Name.Equals(link.PackName, StringComparison.OrdinalIgnoreCase));
                link.IsLinked = targetPack != null && targetPack.ItemNames.Contains(SelectedBundleItem.Name, StringComparer.OrdinalIgnoreCase);
            }

            return;
        }

        SelectedBundleItem.PackLinks.Clear();
        foreach (var pack in BundlePacks)
        {
            var isLinked = pack.ItemNames.Contains(SelectedBundleItem.Name, StringComparer.OrdinalIgnoreCase);
            var packLink = new BundlePackLinkItemViewModel(pack.Name, isLinked, (packName, linked) =>
            {
                var targetPack = BundlePacks.FirstOrDefault(p => p.Name.Equals(packName, StringComparison.OrdinalIgnoreCase));
                if (targetPack != null)
                {
                    if (linked)
                    {
                        AddPackItem(targetPack.ItemNames, SelectedBundleItem.Name);
                    }
                    else
                    {
                        RemovePackItem(targetPack.ItemNames, SelectedBundleItem.Name);
                    }

                    HasChanges = true;
                    UpdatePackItemSelections();
                }
            });
            SelectedBundleItem.PackLinks.Add(packLink);
        }
    }

    private static void OnPackItemSelectedChanged(ConfigEditorViewModel vm, string itemName, bool selected)
    {
        if (vm.SelectedBundlePack == null)
        {
            return;
        }

        if (selected)
        {
            AddPackItem(vm.SelectedBundlePack.ItemNames, itemName);
        }
        else
        {
            RemovePackItem(vm.SelectedBundlePack.ItemNames, itemName);
        }

        vm.HasChanges = true;
        vm.UpdateBundleItemPackLinks();
    }

    private static void AddPackItem(IList<string> itemNames, string itemName)
    {
        if (!itemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase))
        {
            itemNames.Add(itemName);
        }
    }

    private static void RemovePackItem(IList<string> itemNames, string itemName)
    {
        var existing = itemNames.FirstOrDefault(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            itemNames.Remove(existing);
        }
    }

    private static List<BundleItemEditorViewModel> PrecalculateBundleItems(
        IEnumerable<BundleItem>? items,
        string? projectDir,
        ProjectFileSnapshot? snapshot)
    {
        var viewModels = new List<BundleItemEditorViewModel>();
        if (items == null)
        {
            return viewModels;
        }

        foreach (var item in items)
        {
            viewModels.Add(CreateBundleItemEditorViewModel(item, projectDir, snapshot));
        }

        return viewModels;
    }

    private static BundleItemEditorViewModel CreateBundleItemEditorViewModel(
        BundleItem item,
        string? projectDir,
        ProjectFileSnapshot? snapshot)
    {
        var pattern = ResolveEditorPattern(item);

        var itemVm = new BundleItemEditorViewModel
        {
            Name = item.Name,
            NamePrefix = item.NamePrefix,
            NameSuffix = item.NameSuffix,
            IsBig = item.IsBig,
            BigSuffix = item.BigSuffix,
            SetGameLanguageOnInstall = item.SetGameLanguageOnInstall,
            FileCount = item.Files.Count,
            SourcePattern = pattern,
            OutputFormat = ReadOutputFormat(item),
            NoConvert = ReadNoConvert(item),
            ManifestFile = item.ManifestFile,
            Description = item.Description,
            TargetDir = item.TargetDir,
            BaseDir = item.BaseDir,
        };

        itemVm.RecalculateMatches(projectDir, snapshot);
        return itemVm;
    }

    private static string ResolveEditorPattern(BundleItem item)
    {
        // Prefer the original configured patterns: after wildcard resolution
        // AbsSourceFile holds resolved absolute paths that must never be saved back.
        if (item.SourcePatterns.Count > 0)
        {
            return string.Join("; ", item.SourcePatterns);
        }

        if (item.Files.Count > 0)
        {
            return string.Join("; ", item.Files.Select(f => f.AbsSourceFile));
        }

        return ModBuilderConstants.GameFilesEditedAllFilesGlob;
    }

    private static string? ReadOutputFormat(BundleItem item)
    {
        var raw = item.Files.FirstOrDefault()?.Params?
            .FirstOrDefault(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.OutputFormat, StringComparison.OrdinalIgnoreCase)).Value?
            .ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static bool ReadNoConvert(BundleItem item)
    {
        var fileParams = item.Files.FirstOrDefault()?.Params;
        if (fileParams == null)
        {
            return false;
        }

        return fileParams.Any(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.NoConvert, StringComparison.OrdinalIgnoreCase)) ||
            fileParams.Any(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.Raw, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(ReadOutputFormat(item), ModBuilderConstants.BundleParams.RawValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads the configuration into the editor.
    /// </summary>
    private async Task LoadConfigurationAsync()
    {
        if (Configuration == null)
        {
            return;
        }

        var configuration = Configuration;

        // Enumerate project files once on a background thread so large projects
        // do not freeze the UI, then share the snapshot across all match queries.
        var projectDir = CurrentProject?.ProjectDir;
        _fileSnapshot = string.IsNullOrWhiteSpace(projectDir)
            ? null
            : await Task.Run(() => ProjectFileSnapshot.Create(projectDir)).ConfigureAwait(false);

        var precalculatedItems = await Task.Run(
            () => PrecalculateBundleItems(configuration.Items, projectDir, _fileSnapshot)).ConfigureAwait(false);

        void LoadData()
        {
            BundleItems.Clear();
            BundlePacks.Clear();

            foreach (var itemVm in precalculatedItems)
            {
                BundleItems.Add(itemVm);
            }

            PopulateBundlePacks(configuration);

            SelectedBundleItem = BundleItems.FirstOrDefault();
            SelectedBundlePack = BundlePacks.FirstOrDefault();

            UpdateBundleItemPackLinks();

            HasChanges = false;
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            LoadData();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(LoadData);
        }
    }

    private void PopulateBundleItems(BuildConfiguration configuration)
    {
        foreach (var item in configuration.Items)
        {
            BundleItems.Add(CreateBundleItemEditorViewModel(item, CurrentProject?.ProjectDir, _fileSnapshot));
        }
    }

    private void PopulateBundlePacks(BuildConfiguration configuration)
    {
        foreach (var pack in configuration.Packs)
        {
            var viewModel = new BundlePackConfigViewModel
            {
                Name = pack.Name,
                NamePrefix = pack.NamePrefix,
                NameSuffix = pack.NameSuffix,
                AllowBuild = pack.AllowBuild,
                AllowInstall = pack.AllowInstall,
                Big = pack.IsBigPack,
                OutputFile = pack.OutputFile,
                SetGameLanguageOnInstall = pack.SetGameLanguageOnInstall,
                ManifestFile = pack.ManifestFile,
                Description = pack.Description,
            };
            foreach (var itemName in pack.ItemNames)
            {
                viewModel.ItemNames.Add(itemName);
            }

            BundlePacks.Add(viewModel);
        }
    }

    /// <summary>
    /// Sets a predefined source pattern on the selected bundle item.
    /// </summary>
    /// <param name="pattern">The glob pattern to apply.</param>
    [RelayCommand]
    private void SetSourcePattern(string pattern)
    {
        if (SelectedBundleItem != null && !string.IsNullOrEmpty(pattern))
        {
            SelectedBundleItem.ClearPatterns();
            SelectedBundleItem.AddPattern(pattern, CurrentProject?.ProjectDir, _fileSnapshot);
            HasChanges = true;
        }
    }

    /// <summary>
    /// Adds one or more files from the project to the selected bundle item.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    [RelayCommand]
    private async Task AddFilesAsync(Window? owner)
    {
        if (SelectedBundleItem == null || CurrentProject == null)
        {
            return;
        }

        var projectDir = CurrentProject.ProjectDir;
        var topLevel = GetTopLevelWindow(owner);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var startFolder = await ResolveStartFolderAsync(topLevel, projectDir).ConfigureAwait(false);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizationService.GetString("Tools.ModBuilder.ConfigEditor.AddFilesPickerTitle"),
            AllowMultiple = true,
            SuggestedStartLocation = startFolder,
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0)
        {
            return;
        }

        var relativePaths = new List<string>();
        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                continue;
            }

            relativePaths.Add(Path.GetRelativePath(projectDir, localPath).Replace('\\', '/'));
        }

        SelectedBundleItem.AddPatterns(relativePaths, projectDir, _fileSnapshot);
        HasChanges = true;
    }

    /// <summary>
    /// Adds a directory from the project to the selected bundle item as a recursive glob.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    [RelayCommand]
    private async Task AddFolderAsync(Window? owner)
    {
        if (SelectedBundleItem == null || CurrentProject == null)
        {
            return;
        }

        var projectDir = CurrentProject.ProjectDir;
        var topLevel = GetTopLevelWindow(owner);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var startFolder = await ResolveStartFolderAsync(topLevel, projectDir).ConfigureAwait(false);
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localizationService.GetString("Tools.ModBuilder.ConfigEditor.AddFolderPickerTitle"),
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
        }).ConfigureAwait(false);

        if (folders == null || folders.Count == 0)
        {
            return;
        }
        var folder = folders[0];
        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return;
        }

        var rel = Path.GetRelativePath(projectDir, localPath).Trim('/').Replace('\\', '/');
        var glob = $"{rel}/**/*.*";
        SelectedBundleItem.AddPattern(glob, projectDir, _fileSnapshot);
        HasChanges = true;
    }

    /// <summary>
    /// Opens the interactive project tree selector dialog.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    [RelayCommand]
    private async Task OpenProjectTreeSelectorAsync(Window? owner)
    {
        if (SelectedBundleItem == null || CurrentProject == null)
        {
            return;
        }

        var existingPatterns = SelectedBundleItem.SourcePatternsList.Select(p => p.Pattern).ToList();
        var pickerVm = new ProjectItemPickerViewModel(CurrentProject.ProjectDir, existingPatterns, _fileSnapshot);
        var dialog = new Views.ProjectItemPickerDialog(pickerVm);
        var parentWindow = owner ?? GetActiveWindow();
        if (parentWindow == null)
        {
            return;
        }

        // Show the window first with a loading state, then build the tree in the background.
        using var cancellationTokenSource = new CancellationTokenSource();
        dialog.Closed += (_, _) => cancellationTokenSource.Cancel();
        var initializeTask = pickerVm.InitializeAsync(cancellationTokenSource.Token);

        var confirmed = await dialog.ShowDialog<bool>(parentWindow).ConfigureAwait(false);

        try
        {
            await initializeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (confirmed)
        {
            SelectedBundleItem.SetPatterns(dialog.ResultPatterns, CurrentProject.ProjectDir, _fileSnapshot);
            HasChanges = true;
        }
    }

    /// <summary>
    /// Adds a custom pattern from the input field.
    /// </summary>
    [RelayCommand]
    private void AddCustomPattern()
    {
        if (SelectedBundleItem == null || string.IsNullOrWhiteSpace(SelectedBundleItem.CustomPatternInput))
        {
            return;
        }

        SelectedBundleItem.AddPattern(SelectedBundleItem.CustomPatternInput, CurrentProject?.ProjectDir, _fileSnapshot);
        SelectedBundleItem.CustomPatternInput = string.Empty;
        HasChanges = true;
    }

    /// <summary>
    /// Removes a pattern item from the selected bundle item.
    /// </summary>
    /// <param name="item">The pattern item to remove.</param>
    [RelayCommand]
    private void RemoveSourcePattern(SourcePathItemViewModel? item)
    {
        if (SelectedBundleItem == null || item == null)
        {
            return;
        }

        SelectedBundleItem.RemovePattern(item, CurrentProject?.ProjectDir, _fileSnapshot);
        HasChanges = true;
    }

    /// <summary>
    /// Clears all patterns on the selected bundle item.
    /// </summary>
    [RelayCommand]
    private void ClearSourcePatterns()
    {
        if (SelectedBundleItem == null)
        {
            return;
        }

        SelectedBundleItem.ClearPatterns(CurrentProject?.ProjectDir, _fileSnapshot);
        HasChanges = true;
    }

    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return lifetime.Windows.FirstOrDefault(w => w is Views.ConfigEditorDialog) ?? lifetime.MainWindow;
        }

        return null;
    }

    private static TopLevel? GetTopLevelWindow(Window? owner = null)
    {
        var targetWindow = owner ?? GetActiveWindow();
        return targetWindow != null ? TopLevel.GetTopLevel(targetWindow) : null;
    }

    private static async Task<IStorageFolder?> ResolveStartFolderAsync(TopLevel topLevel, string? projectDir)
    {
        if (string.IsNullOrEmpty(projectDir))
        {
            return null;
        }

        var gameFilesDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        var targetDir = Directory.Exists(gameFilesDir) ? gameFilesDir : projectDir;

        if (Directory.Exists(targetDir))
        {
            return await topLevel.StorageProvider.TryGetFolderFromPathAsync(targetDir).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Adds a new bundle item.
    /// </summary>
    [RelayCommand]
    private void AddBundleItem()
    {
        var newItem = new BundleItemEditorViewModel
        {
            Name = $"NewBundleItem{BundleItems.Count + 1}",
            NamePrefix = string.Empty,
            NameSuffix = string.Empty,
            IsBig = false,
            BigSuffix = string.Empty,
            SetGameLanguageOnInstall = string.Empty,
            FileCount = 0,
            SourcePattern = ModBuilderConstants.GameFilesEditedAllFilesGlob,
        };

        newItem.RecalculateMatches(CurrentProject?.ProjectDir, _fileSnapshot);
        BundleItems.Add(newItem);
        SelectedBundleItem = newItem;
        HasChanges = true;
        UpdatePackItemSelections();
        UpdateBundleItemPackLinks();
    }

    /// <summary>
    /// Removes the selected bundle item.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveBundleItem))]
    private void RemoveBundleItem()
    {
        if (SelectedBundleItem == null)
        {
            return;
        }

        BundleItems.Remove(SelectedBundleItem);
        SelectedBundleItem = null;
        HasChanges = true;
        UpdatePackItemSelections();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand CanExecute callback")]
    private bool CanRemoveBundleItem() => SelectedBundleItem != null;

    /// <summary>
    /// Adds a new bundle pack.
    /// </summary>
    [RelayCommand]
    private void AddBundlePack()
    {
        var newPack = new BundlePackConfigViewModel
        {
            Name = $"NewBundlePack{BundlePacks.Count + 1}",
            NamePrefix = string.Empty,
            NameSuffix = string.Empty,
            AllowBuild = true,
            AllowInstall = true,
            Big = false,
            OutputFile = null,
            SetGameLanguageOnInstall = string.Empty,
        };

        foreach (var item in BundleItems.Where(item => !string.IsNullOrEmpty(item.Name)))
        {
            newPack.ItemNames.Add(item.Name);
        }

        BundlePacks.Add(newPack);
        SelectedBundlePack = newPack;
        HasChanges = true;
        UpdateBundleItemPackLinks();
    }

    /// <summary>
    /// Removes the selected bundle pack.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveBundlePack))]
    private void RemoveBundlePack()
    {
        if (SelectedBundlePack == null)
        {
            return;
        }

        BundlePacks.Remove(SelectedBundlePack);
        SelectedBundlePack = null;
        HasChanges = true;
        UpdateBundleItemPackLinks();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand CanExecute callback")]
    private bool CanRemoveBundlePack() => SelectedBundlePack != null;

    /// <summary>
    /// Saves the configuration changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (Configuration == null || CurrentProject == null)
        {
            return;
        }

        try
        {
            var projectDir = CurrentProject.ProjectDir;
            var existingItems = Configuration.Items
                .Where(i => !string.IsNullOrEmpty(i.Name))
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Configuration.Items.Clear();
            foreach (var itemVm in BundleItems)
            {
                existingItems.TryGetValue(itemVm.Name, out var existingItem);
                var parsedFiles = ParseItemFiles(itemVm, existingItem, projectDir);
                Configuration.Items.Add(new BundleItem
                {
                    Name = itemVm.Name,
                    NamePrefix = itemVm.NamePrefix,
                    NameSuffix = itemVm.NameSuffix,
                    IsBig = itemVm.IsBig,
                    BigSuffix = itemVm.BigSuffix,
                    SetGameLanguageOnInstall = itemVm.SetGameLanguageOnInstall,
                    ManifestFile = itemVm.ManifestFile,
                    Description = itemVm.Description,
                    TargetDir = itemVm.TargetDir,
                    BaseDir = itemVm.BaseDir,
                    SourcePatterns = parsedFiles.Select(f => f.AbsSourceFile).ToList(),
                    Files = parsedFiles,
                    Events = existingItem?.Events != null ? new Dictionary<BundleEventType, BundleEvent>(existingItem.Events) : [],
                });
            }

            Configuration.Packs.Clear();
            foreach (var packVm in BundlePacks)
            {
                Configuration.Packs.Add(new BundlePack
                {
                    Name = packVm.Name,
                    NamePrefix = packVm.NamePrefix,
                    NameSuffix = packVm.NameSuffix,
                    AllowBuild = packVm.AllowBuild,
                    AllowInstall = packVm.AllowInstall,
                    Big = packVm.Big,
                    OutputFile = packVm.OutputFile,
                    SetGameLanguageOnInstall = packVm.SetGameLanguageOnInstall,
                    ManifestFile = packVm.ManifestFile,
                    Description = packVm.Description,
                    ItemNames = packVm.ItemNames.ToList(),
                });
            }

            await PersistConfigurationToDiskAsync(CurrentProject.ProjectDir, cancellationToken).ConfigureAwait(false);

            HasChanges = false;
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.ModBuilder.ConfigEditor.Notifications.Saved.Title"),
                localizationService.GetString("Tools.ModBuilder.ConfigEditor.Notifications.Saved.Message"));
            logger.LogInformation("Configuration saved successfully");

            if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                CloseDialog();
            }
            else
            {
                Dispatcher.UIThread.Post(CloseDialog);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save configuration");
            notificationService.ShowError(
                localizationService.GetString("Tools.ModBuilder.ConfigEditor.Notifications.SaveFailed.Title"),
                localizationService.GetString("Tools.ModBuilder.ConfigEditor.Notifications.SaveFailed.Message", ex.Message));
        }
    }

    private static List<BundleFile> ParseItemFiles(BundleItemEditorViewModel itemVm, BundleItem? existingItem, string projectDir)
    {
        // Always store unresolved patterns: persisting resolved absolute paths
        // corrupts the configuration (targets collapse onto sources on reload).
        var patterns = ResolveSavePatterns(itemVm, existingItem);
        if (patterns.Length == 0)
        {
            patterns = [ModBuilderConstants.GameFilesEditedAllFilesGlob];
        }

        var fileParams = ConfigurationLoaderService.BuildFileParameters(itemVm.OutputFormat, itemVm.NoConvert);
        var configuredTarget = !string.IsNullOrWhiteSpace(itemVm.TargetDir) ? itemVm.TargetDir : string.Empty;
        var files = new List<BundleFile>(patterns.Length);
        foreach (var rawPattern in patterns)
        {
            // Relativize so entries corrupted by older saves heal back to portable patterns.
            var pattern = ConfigurationLoaderService.RelativizeToProject(rawPattern.Trim(), projectDir);
            var relTarget = ConfigurationLoaderService.ContainsWildcard(pattern)
                ? configuredTarget
                : ConfigurationLoaderService.StripGameFilesEditedPrefix(pattern.Replace('\\', '/'));
            files.Add(new BundleFile
            {
                AbsSourceParent = projectDir,
                AbsSourceFile = pattern,
                RelTargetFile = relTarget,
                Params = fileParams,
            });
        }

        return files;
    }

    private static string[] ResolveSavePatterns(BundleItemEditorViewModel itemVm, BundleItem? existingItem)
    {
        if (!string.IsNullOrWhiteSpace(itemVm.SourcePattern))
        {
            return itemVm.SourcePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (existingItem?.SourcePatterns.Count > 0)
        {
            return existingItem.SourcePatterns.ToArray();
        }

        return existingItem?.Files.Select(f => f.AbsSourceFile).ToArray() ?? [];
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static bool? NullableTrue(bool? value) => value is true ? true : null;

    private static object CreateItemsDto(IEnumerable<BundleItem> items) =>
        new
        {
            BundleItems = items.Select(item => new
            {
                item.Name,
                NamePrefix = NullIfEmpty(item.NamePrefix),
                NameSuffix = NullIfEmpty(item.NameSuffix),
                Big = item.IsBig ? null : (bool?)false,
                BigSuffix = NullIfEmpty(item.BigSuffix),
                SetGameLanguageOnInstall = NullIfEmpty(item.SetGameLanguageOnInstall),
                SourceFiles = item.SourcePatterns.Count > 0
                    ? item.SourcePatterns.ToArray()
                    : item.Files.Select(f => f.AbsSourceFile).ToArray(),
                BaseDir = NullIfEmpty(item.BaseDir),
                TargetDir = NullIfEmpty(item.TargetDir),
                OutputFormat = ReadOutputFormat(item),
                NoConvert = NullableTrue(ReadNoConvert(item)),
                ManifestFile = NullIfEmpty(item.ManifestFile),
                Description = NullIfEmpty(item.Description),
            }).ToArray(),
        };

    private static object CreatePacksDto(IEnumerable<BundlePack> packs) =>
        new
        {
            BundlePacks = packs.Select(pack => new
            {
                pack.Name,
                NamePrefix = NullIfEmpty(pack.NamePrefix),
                NameSuffix = NullIfEmpty(pack.NameSuffix),
                Big = pack.Big,
                OutputFile = NullIfEmpty(pack.OutputFile),
                SetGameLanguageOnInstall = NullIfEmpty(pack.SetGameLanguageOnInstall),
                AllowBuild = pack.AllowBuild ? null : (bool?)false,
                AllowInstall = pack.AllowInstall ? null : (bool?)false,
                ManifestFile = NullIfEmpty(pack.ManifestFile),
                Description = NullIfEmpty(pack.Description),
                Items = pack.ItemNames.ToArray(),
            }).ToArray(),
        };

    private async Task PersistConfigurationToDiskAsync(string projectDir, CancellationToken cancellationToken)
    {
        if (Configuration == null)
        {
            return;
        }

        var configDir = Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir);
        Directory.CreateDirectory(configDir);

        var itemsPath = Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);
        var packsPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);

        var serializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        var itemsJson = System.Text.Json.JsonSerializer.Serialize(CreateItemsDto(Configuration.Items), serializerOptions);
        var packsJson = System.Text.Json.JsonSerializer.Serialize(CreatePacksDto(Configuration.Packs), serializerOptions);

        await File.WriteAllTextAsync(itemsPath, itemsJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Saved bundle configuration to {ItemsPath} and {PacksPath}", itemsPath, packsPath);
    }

    /// <summary>
    /// Cancels changes and closes the dialog.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges && CurrentProject != null)
        {
            try
            {
                var loaded = await configurationLoaderService.LoadProjectConfigurationAsync(CurrentProject.ProjectDir).ConfigureAwait(false);
                if (loaded != null)
                {
                    Configuration = loaded;
                    CurrentProject.Configuration = loaded;
                    await LoadConfigurationAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reload configuration on cancel");
            }
        }

        HasChanges = false;
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            CloseDialog();
        }
        else
        {
            Dispatcher.UIThread.Post(CloseDialog);
        }
    }

    private static void CloseDialog()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            var windows = lifetime.Windows;
            var configDialog = windows.FirstOrDefault(w => w is Views.ConfigEditorDialog);
            configDialog?.Close();
        }
    }
}
