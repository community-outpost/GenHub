using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using System.ComponentModel;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for managing tool plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
/// </remarks>
/// <param name="toolService">The tool service for managing plugins.</param>
/// <param name="logger">The logger instance.</param>
/// <param name="serviceProvider">The service provider for dependency injection.</param>
/// <param name="localizationService">The optional localization service for language change notifications.</param>
public partial class ToolsViewModel(
    IToolManager toolService,
    ILogger<ToolsViewModel> logger,
    IServiceProvider serviceProvider,
    ILocalizationService? localizationService = null) : ObservableObject, IRecipient<ToolStatusMessage>, IDisposable
{
    private ILocalizationService? _effectiveLocalizationService;
    [ObservableProperty]
    private IToolPlugin? _selectedTool;

    [ObservableProperty]
    private Control? _currentToolControl;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _hasTools = false;

    [ObservableProperty]
    private string _statusMessage = "No tools installed. Click 'Add Tool' to install a tool plugin.";

    [ObservableProperty]
    private bool _isStatusSuccess = false;

    [ObservableProperty]
    private bool _isStatusError = false;

    [ObservableProperty]
    private bool _isStatusInfo = true;

    [ObservableProperty]
    private bool _isStatusVisible = false;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = SidebarConstants.DefaultOpenPaneLength;

    [ObservableProperty]
    private bool _isDetailsDialogOpen = false;

    [ObservableProperty]
    private IToolPlugin? _toolForDetails;

    private IToolPlugin? _lastOpenedTool;
    private System.Threading.CancellationTokenSource? _statusHideCts;

    /// <summary>
    /// Gets the most recently opened tool plugin, remembered across tab switches.
    /// </summary>
    public IToolPlugin? LastOpenedTool => _lastOpenedTool;

    /// <summary>
    /// Gets the collection of installed tools.
    /// </summary>
    public ObservableCollection<IToolPlugin> InstalledTools { get; } = [];

    /// <summary>
    /// Receives tool status messages.
    /// </summary>
    /// <param name="message">The tool status message.</param>
    public void Receive(ToolStatusMessage message)
    {
        ShowStatusMessage(message.Message, message.Type);
    }

    /// <summary>
    /// Initializes the ViewModel by loading saved tools.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            if (!WeakReferenceMessenger.Default.IsRegistered<ToolStatusMessage>(this))
            {
                WeakReferenceMessenger.Default.Register(this);
            }

            _effectiveLocalizationService ??= localizationService ?? serviceProvider.GetService<ILocalizationService>();
            if (_effectiveLocalizationService != null)
            {
                _effectiveLocalizationService.PropertyChanged -= OnLocalizationPropertyChanged;
                _effectiveLocalizationService.PropertyChanged += OnLocalizationPropertyChanged;
            }

            IsLoading = true;

            var result = await toolService.LoadSavedToolsAsync();

            if (result.Success && result.Data != null)
            {
                InstalledTools.Clear();
                foreach (var tool in result.Data)
                {
                    InstalledTools.Add(tool);
                }

                HasTools = InstalledTools.Count > 0;

                if (HasTools)
                {
                    // Select the first tool by default
                    SelectedTool = InstalledTools[0];
                }

                logger.LogInformation("Loaded {Count} tool plugins", InstalledTools.Count);
            }
            else
            {
                ShowStatusMessage($"⚠ Failed to load tools: {string.Join(", ", result.Errors)}", MessageType.Error);
                logger.LogWarning("Failed to load tools: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"⚠ An error occurred while loading tools: {ex.Message}", MessageType.Error);
            logger.LogError(ex, "Error loading tools");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Called when the Tools tab is activated.
    /// Restores the previously opened tool if no tool is currently selected.
    /// </summary>
    public void OnTabActivated()
    {
        if (SelectedTool == null && _lastOpenedTool != null)
        {
            var matchingTool = InstalledTools.FirstOrDefault(t =>
                t == _lastOpenedTool ||
                string.Equals(t.Metadata.Id, _lastOpenedTool.Metadata.Id, StringComparison.OrdinalIgnoreCase));

            if (matchingTool != null)
            {
                SelectedTool = matchingTool;
            }
            else if (InstalledTools.Count > 0)
            {
                _lastOpenedTool = InstalledTools[0];
                SelectedTool = _lastOpenedTool;
            }
            else
            {
                _lastOpenedTool = null;
            }
        }
        else if (SelectedTool != null && CurrentToolControl == null)
        {
            ActivateTool(SelectedTool);
        }
    }

    private static async Task AutoHideStatusAsync(Action onHide, System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(3000, cancellationToken);
            onHide();
        }
        catch (OperationCanceledException)
        {
            // Timer was cancelled, ignore
        }
    }

    [RelayCommand]
    private void OpenPane() => IsPaneOpen = true;

    [RelayCommand]
    private void ClosePane() => IsPaneOpen = false;

    /// <summary>
    /// Adds a new tool plugin from a file.
    /// </summary>
    [RelayCommand]
    private async Task AddToolAsync()
    {
        try
        {
            logger.LogDebug("Add tool requested");

            var lifetime = Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;

            if (topLevel == null)
            {
                logger.LogWarning("Could not get top level window");
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Tool Plugin Assembly",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Tool Plugin Assembly")
                    {
                        Patterns = ["*.dll"],
                    },
                ],
            });

            if (files.Count > 0)
            {
                var assemblyPath = files[0].Path.LocalPath;
                IsLoading = true;
                ShowStatusMessage("Installing tool...", MessageType.Info);

                var result = await toolService.AddToolAsync(assemblyPath);

                if (result.Success && result.Data != null)
                {
                    InstalledTools.Add(result.Data);
                    HasTools = true;
                    SelectedTool = result.Data;

                    var versionDisplay = string.IsNullOrEmpty(result.Data.Metadata.Version) ? string.Empty : $" v{result.Data.Metadata.Version}";
                    ShowStatusMessage($"✓ Tool '{result.Data.Metadata.Name}'{versionDisplay} installed successfully.", MessageType.Success);
                    logger.LogInformation("Tool {ToolName} added successfully", result.Data.Metadata.Name);
                }
                else
                {
                    ShowStatusMessage($"✗ Failed to install tool: {string.Join(", ", result.Errors)}", MessageType.Error);
                    logger.LogWarning("Failed to add tool: {Errors}", string.Join(", ", result.Errors));
                }

                IsLoading = false;
            }
        }
        catch (Exception ex)
        {
            IsLoading = false;
            ShowStatusMessage($"✗ An error occurred while adding the tool: {ex.Message}", MessageType.Error);
            logger.LogError(ex, "Error adding tool");
        }
    }

    /// <summary>
    /// Removes the currently selected tool or a specified tool.
    /// </summary>
    [RelayCommand]
    private async Task RemoveToolAsync(IToolPlugin? tool = null)
    {
        var toolToRemove = tool ?? SelectedTool;
        if (toolToRemove == null) return;
        if (toolToRemove.Metadata.IsBundled)
        {
            ShowStatusMessage($"✗ Tool '{toolToRemove.Metadata.Name}' is a bundled tool and cannot be removed.", MessageType.Error);
            return;
        }

        try
        {
            IsLoading = true;
            ShowStatusMessage($"Removing tool '{toolToRemove.Metadata.Name}'...", MessageType.Info);

            // Deactivate the tool before removal
            toolToRemove.OnDeactivated();

            // Clear current control if removing the selected tool
            if (toolToRemove == SelectedTool)
            {
                CurrentToolControl = null;
            }

            var result = await toolService.RemoveToolAsync(toolToRemove.Metadata.Id);

            if (result.Success)
            {
                InstalledTools.Remove(toolToRemove);
                HasTools = InstalledTools.Count > 0;

                // Dispose the tool
                toolToRemove.Dispose();

                // Select another tool if we removed the selected one
                if (toolToRemove == SelectedTool)
                {
                    SelectedTool = InstalledTools.FirstOrDefault();
                }

                if (toolToRemove == _lastOpenedTool)
                {
                    _lastOpenedTool = SelectedTool;
                }

                ShowStatusMessage($"✓ Tool '{toolToRemove.Metadata.Name}' removed successfully.", MessageType.Success);

                logger.LogInformation("Tool {ToolId} removed successfully", toolToRemove.Metadata.Id);
            }
            else
            {
                ShowStatusMessage($"✗ Failed to remove tool: {string.Join(", ", result.Errors)}", MessageType.Error);
                logger.LogWarning("Failed to remove tool: {Errors}", string.Join(", ", result.Errors));
            }

            IsLoading = false;
        }
        catch (Exception ex)
        {
            IsLoading = false;
            ShowStatusMessage($"✗ An error occurred while removing the tool: {ex.Message}", MessageType.Error);
            logger.LogError(ex, "Error removing tool");
        }
    }

    /// <summary>
    /// Refreshes the list of tools.
    /// </summary>
    [RelayCommand]
    private async Task RefreshToolsAsync()
    {
        try
        {
            IsLoading = true;
            ShowStatusMessage("Refreshing tools...", MessageType.Info);

            var previousSelectedId = SelectedTool?.Metadata.Id ?? _lastOpenedTool?.Metadata.Id;

            // Deactivate current tool before refresh
            if (SelectedTool != null)
            {
                try
                {
                    SelectedTool.OnDeactivated();
                    CurrentToolControl = null;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deactivating tool during refresh: {ToolName}", SelectedTool.Metadata.Name);
                }
            }

            // Load tools from saved settings
            var result = await toolService.LoadSavedToolsAsync();

            if (result.Success && result.Data != null)
            {
                InstalledTools.Clear();
                foreach (var tool in result.Data)
                {
                    InstalledTools.Add(tool);
                }

                HasTools = InstalledTools.Count > 0;

                if (HasTools)
                {
                    // Try to restore previous selection, otherwise select first
                    var toolToSelect = InstalledTools.FirstOrDefault(t => t.Metadata.Id == previousSelectedId)
                                      ?? InstalledTools[0];
                    SelectedTool = toolToSelect;

                    ShowStatusMessage($"✓ Refreshed {InstalledTools.Count} tool(s) successfully.", MessageType.Success);
                }
                else
                {
                    SelectedTool = null;
                    _lastOpenedTool = null;
                    ShowStatusMessage("✓ Refreshed tools list.", MessageType.Success);
                }

                logger.LogInformation("Refreshed {Count} tool plugins", InstalledTools.Count);
            }
            else
            {
                ShowStatusMessage($"⚠ Failed to refresh tools: {string.Join(", ", result.Errors)}", MessageType.Error);
                logger.LogWarning("Failed to refresh tools: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"⚠ An error occurred while refreshing tools: {ex.Message}", MessageType.Error);
            logger.LogError(ex, "Error refreshing tools");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedToolChanged(IToolPlugin? oldValue, IToolPlugin? newValue)
    {
        if (newValue != null)
        {
            _lastOpenedTool = newValue;
        }

        // Deactivate the old tool
        if (oldValue != null)
        {
            try
            {
                oldValue.OnDeactivated();
                logger.LogDebug("Deactivated tool: {ToolName}", oldValue.Metadata.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deactivating tool: {ToolName}", oldValue.Metadata.Name);
            }
        }

        // Activate and load the new tool
        if (newValue != null)
        {
            ActivateTool(newValue);
        }
        else
        {
            CurrentToolControl = null;
        }
    }

    private void ActivateTool(IToolPlugin tool)
    {
        try
        {
            tool.OnActivated(serviceProvider);
            CurrentToolControl = tool.CreateControl();
            logger.LogDebug("Activated tool: {ToolName}", tool.Metadata.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error activating tool: {ToolName}", tool.Metadata.Name);
            CurrentToolControl = null;
            ShowStatusMessage($"✗ Error loading tool '{tool.Metadata.Name}': {ex.Message}", MessageType.Error);
        }
    }

    /// <summary>
    /// Shows the details dialog for a specific tool.
    /// </summary>
    [RelayCommand]
    private void ShowToolDetails(IToolPlugin? tool)
    {
        if (tool != null)
        {
            ToolForDetails = tool;
            IsDetailsDialogOpen = true;
        }
    }

    /// <summary>
    /// Closes the details dialog.
    /// </summary>
    [RelayCommand]
    private void CloseDetailsDialog()
    {
        IsDetailsDialogOpen = false;
        ToolForDetails = null;
    }

    private void ShowStatusMessage(string message, MessageType type = MessageType.Info)
    {
        // Cancel any existing hide timer
        _statusHideCts?.Cancel();
        _statusHideCts?.Dispose();

        StatusMessage = message;
        IsStatusSuccess = type == MessageType.Success;
        IsStatusError = type == MessageType.Error || type == MessageType.Warning;
        IsStatusInfo = type == MessageType.Info;
        IsStatusVisible = true;

        var cts = new System.Threading.CancellationTokenSource();
        _statusHideCts = cts;
        _ = AutoHideStatusAsync(() => IsStatusVisible = false, cts.Token);
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == "Item[]")
        {
            if (InstalledTools.Count > 0)
            {
                var currentSelected = SelectedTool;
                var tools = InstalledTools.ToList();
                InstalledTools.Clear();
                foreach (var tool in tools)
                {
                    InstalledTools.Add(tool);
                }

                SelectedTool = currentSelected != null
                    ? InstalledTools.FirstOrDefault(t => t.Metadata.Id == currentSelected.Metadata.Id)
                    : null;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_effectiveLocalizationService != null)
        {
            _effectiveLocalizationService.PropertyChanged -= OnLocalizationPropertyChanged;
        }

        _statusHideCts?.Cancel();
        _statusHideCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
