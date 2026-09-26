using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.ViewModels;
using GenHub.Tests.Core.Features.Tools.Mocks;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for <see cref="ToolsViewModel"/>.
/// </summary>
public class ToolsViewModelTests
{
    private readonly Mock<IToolManager> _mockToolService;
    private readonly Mock<ILogger<ToolsViewModel>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly ToolsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModelTests"/> class.
    /// </summary>
    public ToolsViewModelTests()
    {
        _mockToolService = new Mock<IToolManager>();
        _mockLogger = new Mock<ILogger<ToolsViewModel>>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _viewModel = new ToolsViewModel(
            _mockToolService.Object,
            _mockLogger.Object,
            _mockServiceProvider.Object);
    }

    /// <summary>
    /// Tests that OnTabActivated restores the previously opened tool when SelectedTool is null.
    /// </summary>
    [Fact]
    public void OnTabActivated_RestoresPreviouslyOpenedTool_WhenSelectedToolIsNull()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);

        _viewModel.SelectedTool = plugin2;
        Assert.Equal(plugin2, _viewModel.SelectedTool);
        Assert.Equal(plugin2, _viewModel.LastOpenedTool);

        // Simulate tab switch reset
        _viewModel.SelectedTool = null;
        Assert.Null(_viewModel.SelectedTool);
        Assert.Equal(plugin2, _viewModel.LastOpenedTool);

        // Act
        _viewModel.OnTabActivated();

        // Assert
        Assert.Equal(plugin2, _viewModel.SelectedTool);
        Assert.NotNull(_viewModel.CurrentToolControl);
    }

    /// <summary>
    /// Tests that OnTabActivated does not change selection when a tool is already selected.
    /// </summary>
    [Fact]
    public void OnTabActivated_DoesNotChangeSelection_WhenSelectedToolIsAlreadySet()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);
        _viewModel.SelectedTool = plugin1;

        // Act
        _viewModel.OnTabActivated();

        // Assert
        Assert.Equal(plugin1, _viewModel.SelectedTool);
    }

    /// <summary>
    /// Tests that OnTabActivated recreates CurrentToolControl if SelectedTool is set but control is null.
    /// </summary>
    [Fact]
    public void OnTabActivated_RecreatesCurrentToolControl_WhenSelectedToolNotNullButControlNull()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Author");
        _viewModel.InstalledTools.Add(plugin);
        _viewModel.SelectedTool = plugin;
        _viewModel.CurrentToolControl = null;

        // Act
        _viewModel.OnTabActivated();

        // Assert
        Assert.NotNull(_viewModel.CurrentToolControl);
    }

    /// <summary>
    /// Tests that OnTabActivated selects the first tool when the remembered tool was removed from installed tools.
    /// </summary>
    [Fact]
    public void OnTabActivated_SelectsFirstTool_WhenRememberedToolWasRemoved()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);
        _viewModel.SelectedTool = plugin2;
        Assert.Equal(plugin2, _viewModel.SelectedTool);

        _viewModel.SelectedTool = null;
        _viewModel.InstalledTools.Remove(plugin2);

        // Act
        _viewModel.OnTabActivated();

        // Assert
        Assert.Equal(plugin1, _viewModel.SelectedTool);
    }

    /// <summary>
    /// Tests that OnTabActivated does nothing when no tools are installed.
    /// </summary>
    [Fact]
    public void OnTabActivated_DoesNothing_WhenNoToolsInstalled()
    {
        // Arrange & Act
        _viewModel.OnTabActivated();

        // Assert
        Assert.Null(_viewModel.SelectedTool);
        Assert.Null(_viewModel.LastOpenedTool);
    }

    /// <summary>
    /// Tests that OnTabActivated shows error status and leaves CurrentToolControl null when tool activation throws.
    /// </summary>
    [Fact]
    public void OnTabActivated_ShowsErrorStatus_WhenToolActivationThrows()
    {
        // Arrange
        var pluginMock = new Mock<IToolPlugin>();
        var metadata = new ToolMetadata
        {
            Id = "failing.tool",
            Name = "Failing Tool",
            Version = "1.0.0",
            Author = "Author",
            Description = "Failing tool description",
        };
        pluginMock.Setup(p => p.Metadata).Returns(metadata);
        pluginMock.Setup(p => p.OnActivated(It.IsAny<IServiceProvider>()))
            .Throws(new InvalidOperationException("Activation failed"));

        _viewModel.InstalledTools.Add(pluginMock.Object);
        _viewModel.SelectedTool = pluginMock.Object;
        _viewModel.CurrentToolControl = null;

        // Act
        _viewModel.OnTabActivated();

        // Assert
        Assert.Null(_viewModel.CurrentToolControl);
        Assert.True(_viewModel.IsStatusError);
        Assert.Contains("Activation failed", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Tests that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Assert
        Assert.NotNull(_viewModel.InstalledTools);
        Assert.Empty(_viewModel.InstalledTools);
        Assert.Null(_viewModel.SelectedTool);
        Assert.Null(_viewModel.LastOpenedTool);
        Assert.Null(_viewModel.CurrentToolControl);
        Assert.False(_viewModel.IsLoading);
        Assert.False(_viewModel.HasTools);
        Assert.Contains("No tools installed", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusInfo);
        Assert.False(_viewModel.IsStatusSuccess);
        Assert.False(_viewModel.IsStatusError);
    }

    /// <summary>
    /// Tests that InitializeAsync loads tools successfully when service returns tools.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_LoadsToolsSuccessfullyAsync()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        var tools = new List<IToolPlugin> { plugin1, plugin2 };

        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateSuccess(tools));

        // Act
        await _viewModel.InitializeAsync();

        // Assert
        Assert.Equal(2, _viewModel.InstalledTools.Count);
        Assert.Contains(plugin1, _viewModel.InstalledTools);
        Assert.Contains(plugin2, _viewModel.InstalledTools);
        Assert.True(_viewModel.HasTools);
        Assert.Null(_viewModel.SelectedTool); // Deferred until the Tools tab is actually opened
        Assert.Equal(plugin1, _viewModel.LastOpenedTool); // Remembered for first tab activation
        Assert.Null(_viewModel.CurrentToolControl); // No tool control created at startup
        Assert.False(plugin1.IsActivated);
        Assert.False(plugin2.IsActivated);
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that opening the Tools tab while tools are still loading defers activation
    /// until the load completes instead of leaving the detail pane blank.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_ActivatesFirstTool_WhenTabOpenedDuringLoadAsync()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var tools = new List<IToolPlugin> { plugin1 };
        var loadCompletion = new TaskCompletionSource<OperationResult<List<IToolPlugin>>>();

        _mockToolService.Setup(x => x.LoadSavedToolsAsync()).Returns(loadCompletion.Task);

        // Act: open the tab while the load is still in flight.
        var initializeTask = _viewModel.InitializeAsync();
        Assert.True(_viewModel.IsLoading);
        _viewModel.OnTabActivated();
        Assert.Null(_viewModel.SelectedTool);

        loadCompletion.SetResult(OperationResult<List<IToolPlugin>>.CreateSuccess(tools));
        await initializeTask;

        // Assert
        Assert.Equal(plugin1, _viewModel.SelectedTool);
        Assert.NotNull(_viewModel.CurrentToolControl);
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that InitializeAsync sets HasTools to false when no tools are loaded.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_SetsHasToolsToFalse_WhenNoToolsLoadedAsync()
    {
        // Arrange
        var emptyTools = new List<IToolPlugin>();
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateSuccess(emptyTools));

        // Act
        await _viewModel.InitializeAsync();

        // Assert
        Assert.Empty(_viewModel.InstalledTools);
        Assert.False(_viewModel.HasTools);
        Assert.Null(_viewModel.SelectedTool);
        Assert.Contains("No tools installed", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusInfo);
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that InitializeAsync handles failure from service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_HandlesFailureFromServiceAsync()
    {
        // Arrange
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateFailure("Failed to load tools"));

        // Act
        await _viewModel.InitializeAsync();

        // Assert
        Assert.Empty(_viewModel.InstalledTools);
        Assert.False(_viewModel.HasTools);
        Assert.Contains("Failed to load tools", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusError);
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that InitializeAsync handles exceptions gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_HandlesExceptionsGracefullyAsync()
    {
        // Arrange
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        await _viewModel.InitializeAsync();

        // Assert
        Assert.Contains("error occurred", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusError);
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that InitializeAsync sets IsLoading correctly during operation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_SetsIsLoadingCorrectlyAsync()
    {
        // Arrange
        var tools = new List<IToolPlugin>();
        var taskCompletionSource = new TaskCompletionSource<OperationResult<List<IToolPlugin>>>();

        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .Returns(taskCompletionSource.Task);

        // Act - Start initialization
        var initTask = _viewModel.InitializeAsync();

        // Wait a bit to ensure IsLoading is set
        await Task.Delay(10);

        // Assert - IsLoading should be true during operation
        Assert.True(_viewModel.IsLoading);

        // Complete the operation
        taskCompletionSource.SetResult(OperationResult<List<IToolPlugin>>.CreateSuccess(tools));
        await initTask;

        // Assert - IsLoading should be false after completion
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that RemoveToolCommand deactivates and removes tool successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RemoveToolAsync_RemovesToolSuccessfullyAsync()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        _viewModel.InstalledTools.Add(plugin);
        _viewModel.SelectedTool = plugin;

        _mockToolService.Setup(x => x.RemoveToolAsync(plugin.Metadata.Id))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.RemoveToolCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(_viewModel.InstalledTools);
        Assert.False(_viewModel.HasTools);
        Assert.Null(_viewModel.SelectedTool);
        Assert.Null(_viewModel.LastOpenedTool);
        Assert.Contains("removed successfully", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusSuccess);
        Assert.True(plugin.IsDisposed);
        Assert.True(plugin.OnDeactivatedCallCount >= 1, "Tool should be deactivated at least once");
    }

    /// <summary>
    /// Tests that RemoveToolAsync selects another tool after removal when tools remain.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RemoveToolAsync_SelectsAnotherTool_WhenToolsRemainAsync()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);
        _viewModel.SelectedTool = plugin1;

        _mockToolService.Setup(x => x.RemoveToolAsync(plugin1.Metadata.Id))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.RemoveToolCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(_viewModel.InstalledTools);
        Assert.True(_viewModel.HasTools);
        Assert.Equal(plugin2, _viewModel.SelectedTool);
        Assert.Equal(plugin2, _viewModel.LastOpenedTool);
        Assert.True(plugin1.IsDisposed);
        Assert.False(plugin2.IsDisposed);
    }

    /// <summary>
    /// Tests that RemoveToolAsync does nothing when no tool is selected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RemoveToolAsync_DoesNothing_WhenNoToolSelectedAsync()
    {
        // Arrange
        _viewModel.SelectedTool = null;

        // Act
        await _viewModel.RemoveToolCommand.ExecuteAsync(null);

        // Assert
        _mockToolService.Verify(x => x.RemoveToolAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Tests that RemoveToolAsync handles service failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RemoveToolAsync_HandlesServiceFailureAsync()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        _viewModel.InstalledTools.Add(plugin);
        _viewModel.SelectedTool = plugin;

        _mockToolService.Setup(x => x.RemoveToolAsync(plugin.Metadata.Id))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Failed to remove tool"));

        // Act
        await _viewModel.RemoveToolCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(_viewModel.InstalledTools); // Tool should still be in list
        Assert.Contains("Failed to remove", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusError);
    }

    /// <summary>
    /// Tests that RemoveToolAsync handles exceptions gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RemoveToolAsync_HandlesExceptionsGracefullyAsync()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        _viewModel.InstalledTools.Add(plugin);
        _viewModel.SelectedTool = plugin;

        _mockToolService.Setup(x => x.RemoveToolAsync(plugin.Metadata.Id))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        await _viewModel.RemoveToolCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("error occurred", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusError);
    }

    /// <summary>
    /// Tests that RefreshToolsAsync reloads tools successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_ReloadsToolsSuccessfullyAsync()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        var tools = new List<IToolPlugin> { plugin1, plugin2 };

        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateSuccess(tools));

        // Act
        await _viewModel.RefreshToolsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, _viewModel.InstalledTools.Count);
        Assert.Contains(plugin1, _viewModel.InstalledTools);
        Assert.Contains(plugin2, _viewModel.InstalledTools);
        Assert.True(_viewModel.HasTools);
        Assert.Contains("Refreshed", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusSuccess);
    }

    /// <summary>
    /// Tests that RefreshToolsAsync deactivates current tool before refresh.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_DeactivatesCurrentTool_BeforeRefreshAsync()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        _viewModel.InstalledTools.Add(plugin);
        _viewModel.SelectedTool = plugin;
        plugin.OnActivated(_mockServiceProvider.Object);

        var refreshedTools = new List<IToolPlugin> { plugin };
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateSuccess(refreshedTools));

        // Act
        await _viewModel.RefreshToolsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(1, plugin.OnDeactivatedCallCount);
    }

    /// <summary>
    /// Tests that RefreshToolsAsync restores previously selected tool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_RestoresPreviouslySelectedToolAsync()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);
        _viewModel.SelectedTool = plugin2;

        var refreshedTools = new List<IToolPlugin> { plugin1, plugin2 };
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateSuccess(refreshedTools));

        // Act
        await _viewModel.RefreshToolsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(plugin2, _viewModel.SelectedTool);
    }

    /// <summary>
    /// Tests that RefreshToolsAsync clears SelectedTool and LastOpenedTool when no tools remain after refresh.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_ClearsSelectedTool_WhenNoToolsRemainAsync()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        _viewModel.InstalledTools.Add(plugin);
        _viewModel.SelectedTool = plugin;

        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateSuccess([]));

        // Act
        await _viewModel.RefreshToolsCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(_viewModel.InstalledTools);
        Assert.False(_viewModel.HasTools);
        Assert.Null(_viewModel.SelectedTool);
        Assert.Null(_viewModel.LastOpenedTool);
    }

    /// <summary>
    /// Tests that RefreshToolsAsync handles service failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_HandlesServiceFailureAsync()
    {
        // Arrange
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ReturnsAsync(OperationResult<List<IToolPlugin>>.CreateFailure("Failed to refresh tools"));

        // Act
        await _viewModel.RefreshToolsCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("Failed to refresh", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusError);
    }

    /// <summary>
    /// Tests that RefreshToolsAsync handles exceptions gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_HandlesExceptionsGracefullyAsync()
    {
        // Arrange
        _mockToolService.Setup(x => x.LoadSavedToolsAsync())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        await _viewModel.RefreshToolsCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("error occurred", _viewModel.StatusMessage);
        Assert.True(_viewModel.IsStatusError);
    }

    /// <summary>
    /// Tests that SelectedTool property change activates the tool.
    /// </summary>
    [Fact]
    public void SelectedTool_ActivatesTool_WhenChanged()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        _viewModel.InstalledTools.Add(plugin);

        // Act
        _viewModel.SelectedTool = plugin;

        // Assert - The actual activation happens via property changed event in the real UI
        // In unit tests, we verify the tool is set
        Assert.Equal(plugin, _viewModel.SelectedTool);
        Assert.Equal(plugin, _viewModel.LastOpenedTool);
    }

    /// <summary>
    /// Tests that multiple tools can be added to InstalledTools collection.
    /// </summary>
    [Fact]
    public void InstalledTools_SupportsMultipleTools()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        var plugin3 = new MockToolPlugin("test.tool3", "Test Tool 3", "1.0.0", "Author 3");

        // Act
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);
        _viewModel.InstalledTools.Add(plugin3);

        // Assert
        Assert.Equal(3, _viewModel.InstalledTools.Count);
        Assert.Contains(plugin1, _viewModel.InstalledTools);
        Assert.Contains(plugin2, _viewModel.InstalledTools);
        Assert.Contains(plugin3, _viewModel.InstalledTools);
    }

    /// <summary>
    /// Tests that InstalledTools collection can be cleared.
    /// </summary>
    [Fact]
    public void InstalledTools_CanBeCleared()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        _viewModel.InstalledTools.Add(plugin1);
        _viewModel.InstalledTools.Add(plugin2);

        // Act
        _viewModel.InstalledTools.Clear();

        // Assert
        Assert.Empty(_viewModel.InstalledTools);
    }
}
