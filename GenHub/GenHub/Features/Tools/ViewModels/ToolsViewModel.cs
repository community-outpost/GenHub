using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Tools;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// View model for the Tools tab.
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly ILogger<ToolsViewModel>? _logger;
    private readonly IToolRegistry _toolRegistry;

    [ObservableProperty]
    private ITool? _selectedTool;

    [ObservableProperty]
    private IToolViewModel? _selectedToolViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
    /// </summary>
    /// <param name="toolRegistry">The tool registry service.</param>
    /// <param name="logger">Logger instance.</param>
    public ToolsViewModel(
        IToolRegistry toolRegistry,
        ILogger<ToolsViewModel>? logger = null)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
    }

    /// <summary>
    /// Gets the collection of available tools.
    /// </summary>
    public ObservableCollection<ITool> AvailableTools { get; } = new();

    /// <summary>
    /// Initializes the view model asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        _logger?.LogInformation("ToolsViewModel initialized");

        // Load available tools from registry
        LoadAvailableTools();

        // Select the first tool by default if available
        if (AvailableTools.Count > 0)
        {
            await SelectToolAsync(AvailableTools[0]);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles the SelectedTool property change.
    /// </summary>
    /// <param name="value">The new selected tool.</param>
    partial void OnSelectedToolChanged(ITool? value)
    {
        // Trigger selection asynchronously when tool changes
        _ = SelectToolAsync(value);
    }

    /// <summary>
    /// Selects a tool and creates its view model.
    /// </summary>
    /// <param name="tool">The tool to select.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task SelectToolAsync(ITool? tool)
    {
        if (tool == null)
        {
            return;
        }

        // Deactivate current tool
        if (SelectedToolViewModel != null)
        {
            await SelectedToolViewModel.DeactivateAsync();
        }

        SelectedTool = tool;

        // Create and initialize new tool view model
        SelectedToolViewModel = tool.CreateViewModel();
        await SelectedToolViewModel.InitializeAsync();
        await SelectedToolViewModel.ActivateAsync();

        _logger?.LogInformation("Selected tool: {ToolName}", tool.Metadata.Name);
    }

    /// <summary>
    /// Refreshes the current tool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task RefreshCurrentToolAsync()
    {
        if (SelectedToolViewModel != null)
        {
            await SelectedToolViewModel.RefreshAsync();
            _logger?.LogInformation("Refreshed current tool");
        }
    }

    /// <summary>
    /// Loads available tools from the registry.
    /// </summary>
    private void LoadAvailableTools()
    {
        AvailableTools.Clear();

        var tools = _toolRegistry.GetEnabledTools();
        foreach (var tool in tools)
        {
            AvailableTools.Add(tool);
        }

        _logger?.LogInformation("Loaded {Count} tools", AvailableTools.Count);
    }
}
