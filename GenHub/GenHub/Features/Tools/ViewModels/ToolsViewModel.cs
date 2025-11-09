using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for managing tool plugins.
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly IToolService _toolService;
    private readonly ILogger<ToolsViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
    /// </summary>
    /// <param name="toolService">The tool service for managing plugins.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public ToolsViewModel(IToolService toolService, ILogger<ToolsViewModel> logger, IServiceProvider serviceProvider)
    {
        _toolService = toolService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the collection of installed tools.
    /// </summary>
    public ObservableCollection<IToolPlugin> InstalledTools { get; } = new();

    /// <summary>
    /// Initializes the ViewModel by loading saved tools.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading tools...";

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
                    StatusMessage = $"✓ {InstalledTools.Count} tool(s) loaded successfully.";
                    SetStatusType(success: true);

                    // Select the first tool by default
                    if (InstalledTools.Count > 0)
                    {
                        SelectedTool = InstalledTools[0];
                    }
                }
                else
                {
                    StatusMessage = "No tools installed. Click 'Add Tool' to install a tool plugin.";
                    SetStatusType(info: true);
                }

                _logger.LogInformation("Loaded {Count} tool plugins", InstalledTools.Count);
            }
            else
            {
                StatusMessage = $"⚠ Failed to load tools: {string.Join(", ", result.Errors)}";
                SetStatusType(error: true);
                _logger.LogWarning("Failed to load tools: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ An error occurred while loading tools: {ex.Message}";
            SetStatusType(error: true);
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
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Tool Plugin Assembly")
                    {
                        Patterns = new[] { "*.dll" },
                    },
                },
            });

            if (files.Count > 0)
            {
                var assemblyPath = files[0].Path.LocalPath;
                IsLoading = true;
                StatusMessage = "Installing tool...";
                SetStatusType(info: true);

                var result = await _toolService.AddToolAsync(assemblyPath);

                if (result.Success && result.Data != null)
                {
                    InstalledTools.Add(result.Data);
                    HasTools = true;
                    SelectedTool = result.Data;
                    StatusMessage = $"✓ Tool '{result.Data.Metadata.Name}' v{result.Data.Metadata.Version} installed successfully.";
                    SetStatusType(success: true);
                    _logger.LogInformation("Tool {ToolName} added successfully", result.Data.Metadata.Name);
                }
                else
                {
                    StatusMessage = $"✗ Failed to install tool: {string.Join(", ", result.Errors)}";
                    SetStatusType(error: true);
                    _logger.LogWarning("Failed to add tool: {Errors}", string.Join(", ", result.Errors));
                }

                IsLoading = false;
            }
        }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusMessage = $"✗ An error occurred while adding the tool: {ex.Message}";
            SetStatusType(error: true);
            _logger.LogError(ex, "Error adding tool");
        }
    }

    /// <summary>
    /// Removes the currently selected tool.
    /// </summary>
    [RelayCommand]
    private async Task RemoveToolAsync()
    {
        if (SelectedTool == null) return;

        try
        {
            var toolToRemove = SelectedTool;
            IsLoading = true;
            StatusMessage = $"Removing tool '{toolToRemove.Metadata.Name}'...";
            SetStatusType(info: true);

            // Deactivate the tool before removal
            toolToRemove.OnDeactivated();
            CurrentToolControl = null;

            var result = await _toolService.RemoveToolAsync(toolToRemove.Metadata.Id);

            if (result.Success)
            {
                InstalledTools.Remove(toolToRemove);
                HasTools = InstalledTools.Count > 0;

                // Dispose the tool
                toolToRemove.Dispose();

                // Select another tool if available
                SelectedTool = InstalledTools.FirstOrDefault();

                if (HasTools)
                {
                    StatusMessage = $"✓ Tool '{toolToRemove.Metadata.Name}' removed successfully.";
                    SetStatusType(success: true);
                }
                else
                {
                    StatusMessage = "No tools installed. Click 'Add Tool' to install a tool plugin.";
                    SetStatusType(info: true);
                }

                _logger.LogInformation("Tool {ToolId} removed successfully", toolToRemove.Metadata.Id);
            }
            else
            {
                StatusMessage = $"✗ Failed to remove tool: {string.Join(", ", result.Errors)}";
                SetStatusType(error: true);
                _logger.LogWarning("Failed to remove tool: {Errors}", string.Join(", ", result.Errors));
            }

            IsLoading = false;
        }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusMessage = $"✗ An error occurred while removing the tool: {ex.Message}";
            SetStatusType(error: true);
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
            SetStatusType(info: true);

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

                    StatusMessage = $"✓ Refreshed {InstalledTools.Count} tool(s) successfully.";
                    SetStatusType(success: true);
                }
                else
                {
                    StatusMessage = "No tools installed. Click 'Add Tool' to install a tool plugin.";
                    SetStatusType(info: true);
                }

                _logger.LogInformation("Refreshed {Count} tool plugins", InstalledTools.Count);
            }
            else
            {
                StatusMessage = $"⚠ Failed to refresh tools: {string.Join(", ", result.Errors)}";
                SetStatusType(error: true);
                _logger.LogWarning("Failed to refresh tools: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ An error occurred while refreshing tools: {ex.Message}";
            SetStatusType(error: true);
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
                StatusMessage = $"Tool '{newValue.Metadata.Name}' loaded successfully.";
                SetStatusType(success: true);
                _logger.LogDebug("Activated tool: {ToolName}", newValue.Metadata.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating tool: {ToolName}", newValue.Metadata.Name);
                CurrentToolControl = null;
                StatusMessage = $"✗ Error loading tool '{newValue.Metadata.Name}': {ex.Message}";
                SetStatusType(error: true);
            }
        }
        else
        {
            CurrentToolControl = null;
        }
    }

    private void SetStatusType(bool success = false, bool error = false, bool info = false)
    {
        IsStatusSuccess = success;
        IsStatusError = error;
        IsStatusInfo = info;
    }
}
