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
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Messages;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for managing tool plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
/// </remarks>
public partial class ToolsViewModel : ObservableObject
{
    private readonly IToolManager _toolService;
    private readonly ILogger<ToolsViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
    /// </summary>
    /// <param name="toolService">The tool service for managing plugins.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public ToolsViewModel(IToolManager toolService, ILogger<ToolsViewModel> logger, IServiceProvider serviceProvider)
    {
        _toolService = toolService;
        _logger = logger;
        _serviceProvider = serviceProvider;

        // Subscribe to tool status messages
        WeakReferenceMessenger.Default.Register<ToolStatusMessage>(this, (r, m) =>
        {
            ((ToolsViewModel)r).ShowStatusMessage(m.Message, m.Type);
        });
    }

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
    private bool _isPaneOpen = false;

    // We'll add commands to explicitly Open and Close the pane.
    [RelayCommand]
    private void OpenPane() => IsPaneOpen = true;

    [RelayCommand]
    private void ClosePane() => IsPaneOpen = false;

    [ObservableProperty]
    private bool _isDetailsDialogOpen = false;

    [ObservableProperty]
    private IToolPlugin? _toolForDetails;

    private System.Threading.CancellationTokenSource? _statusHideCts;

    /// <summary>
    /// Gets the collection of installed tools.
    /// </summary>
    public ObservableCollection<IToolPlugin> InstalledTools { get; } = [];

    /// <summary>
    /// Initializes the ViewModel by loading saved tools.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;

            var result = await _toolService.LoadSavedToolsAsync();

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
                    if (InstalledTools.Count > 0)
                    {
                        SelectedTool = InstalledTools[0];
                    }
                }

                _logger.LogInformation("Loaded {Count} tool plugins", InstalledTools.Count);
            }
            else
            {
                ShowStatusMessage($"⚠ Failed to load tools: {string.Join(", ", result.Errors)}", MessageType.Error);
                _logger.LogWarning("Failed to load tools: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"⚠ An error occurred while loading tools: {ex.Message}", MessageType.Error);
            _logger.LogError(ex, "Error loading tools");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Adds a new tool plugin from a file.
    /// </summary>
    [RelayCommand]
    private async Task AddToolAsync()
    {
        try
        {
            _logger.LogDebug("Add tool requested");

            var lifetime = Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;

            if (topLevel == null)
            {
                _logger.LogWarning("Could not get top level window");
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
                StatusMessage = "Installing tool...";
                SetStatusType(MessageType.Info);
                IsStatusVisible = true;

                var result = await _toolService.AddToolAsync(assemblyPath);

                if (result.Success && result.Data != null)
                {
                    InstalledTools.Add(result.Data);
                    HasTools = true;
                    SelectedTool = result.Data;
                    ShowStatusMessage($"✓ Tool '{result.Data.Metadata.Name}' v{result.Data.Metadata.Version} installed successfully.", MessageType.Success);
                    _logger.LogInformation("Tool {ToolName} added successfully", result.Data.Metadata.Name);
                }
                else
                {
                    ShowStatusMessage($"✗ Failed to install tool: {string.Join(", ", result.Errors)}", MessageType.Error);
                    _logger.LogWarning("Failed to add tool: {Errors}", string.Join(", ", result.Errors));
                }

                IsLoading = false;
            }
        }
        catch (Exception ex)
        {
            IsLoading = false;
            ShowStatusMessage($"✗ An error occurred while adding the tool: {ex.Message}", MessageType.Error);
            _logger.LogError(ex, "Error adding tool");
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
            StatusMessage = $"Removing tool '{toolToRemove.Metadata.Name}'...";
            SetStatusType(MessageType.Info);
            IsStatusVisible = true;

            // Deactivate the tool before removal
            toolToRemove.OnDeactivated();

            // Clear current control if removing the selected tool
            if (toolToRemove == SelectedTool)
            {
                CurrentToolControl = null;
            }

            var result = await _toolService.RemoveToolAsync(toolToRemove.Metadata.Id);

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

                ShowStatusMessage($"✓ Tool '{toolToRemove.Metadata.Name}' removed successfully.", MessageType.Success);

                _logger.LogInformation("Tool {ToolId} removed successfully", toolToRemove.Metadata.Id);
            }
            else
            {
                ShowStatusMessage($"✗ Failed to remove tool: {string.Join(", ", result.Errors)}", MessageType.Error);
                _logger.LogWarning("Failed to remove tool: {Errors}", string.Join(", ", result.Errors));
            }

            IsLoading = false;
        }
        catch (Exception ex)
        {
            IsLoading = false;
            ShowStatusMessage($"✗ An error occurred while removing the tool: {ex.Message}", MessageType.Error);
            _logger.LogError(ex, "Error removing tool");
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
            StatusMessage = "Refreshing tools...";
            SetStatusType(MessageType.Info);
            IsStatusVisible = true;

            // Store the current selection
            var previousSelectedId = SelectedTool?.Metadata.Id;

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
                    _logger.LogError(ex, "Error deactivating tool during refresh: {ToolName}", SelectedTool.Metadata.Name);
                }
            }

            // Load tools from saved settings
            var result = await _toolService.LoadSavedToolsAsync();

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
                    ShowStatusMessage("✓ Refreshed tools list.", MessageType.Success);
                }

                _logger.LogInformation("Refreshed {Count} tool plugins", InstalledTools.Count);
            }
            else
            {
                ShowStatusMessage($"⚠ Failed to refresh tools: {string.Join(", ", result.Errors)}", MessageType.Error);
                _logger.LogWarning("Failed to refresh tools: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"⚠ An error occurred while refreshing tools: {ex.Message}", MessageType.Error);
            _logger.LogError(ex, "Error refreshing tools");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedToolChanged(IToolPlugin? oldValue, IToolPlugin? newValue)
    {
        // Deactivate the old tool
        if (oldValue != null)
        {
            try
            {
                oldValue.OnDeactivated();
                _logger.LogDebug("Deactivated tool: {ToolName}", oldValue.Metadata.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating tool: {ToolName}", oldValue.Metadata.Name);
            }
        }

        // Activate and load the new tool
        if (newValue != null)
        {
            try
            {
                newValue.OnActivated(_serviceProvider);
                CurrentToolControl = newValue.CreateControl();
                _logger.LogDebug("Activated tool: {ToolName}", newValue.Metadata.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating tool: {ToolName}", newValue.Metadata.Name);
                CurrentToolControl = null;
                ShowStatusMessage($"✗ Error loading tool '{newValue.Metadata.Name}': {ex.Message}", MessageType.Error);
            }
        }
        else
        {
            CurrentToolControl = null;
        }
    }

    private void SetStatusType(MessageType type)
    {
        IsStatusSuccess = type == MessageType.Success;
        IsStatusError = type == MessageType.Error || type == MessageType.Warning;
        IsStatusInfo = type == MessageType.Info;
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

    private async void ShowStatusMessage(string message, MessageType type = MessageType.Info)
    {
        // Cancel any existing hide timer
        _statusHideCts?.Cancel();
        _statusHideCts?.Dispose();

        StatusMessage = message;
        SetStatusType(type);
        IsStatusVisible = true;

        // Auto-hide after 5 seconds
        _statusHideCts = new System.Threading.CancellationTokenSource();
        try
        {
            await Task.Delay(3000, _statusHideCts.Token);
            IsStatusVisible = false;
        }
        catch (TaskCanceledException)
        {
            // Timer was cancelled, ignore
        }
    }
}