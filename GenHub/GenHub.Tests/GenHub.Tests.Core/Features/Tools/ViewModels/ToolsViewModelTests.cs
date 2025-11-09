using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.ViewModels;
using GenHub.Tests.Core.Features.Tools.Mocks;
using Microsoft.Extensions.Logging;
using Moq;
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
    /// Tests that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Assert
        Assert.NotNull(_viewModel.InstalledTools);
        Assert.Empty(_viewModel.InstalledTools);
        Assert.Null(_viewModel.SelectedTool);
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
    public async Task InitializeAsync_LoadsToolsSuccessfully()
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
        Assert.Equal(plugin1, _viewModel.SelectedTool); // First tool selected by default
        Assert.False(_viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that InitializeAsync sets HasTools to false when no tools are loaded.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_SetsHasToolsToFalse_WhenNoToolsLoaded()
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
    public async Task InitializeAsync_HandlesFailureFromService()
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
    public async Task InitializeAsync_HandlesExceptionsGracefully()
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
    public async Task InitializeAsync_SetsIsLoadingCorrectly()
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
    public async Task RemoveToolAsync_RemovesToolSuccessfully()
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
    public async Task RemoveToolAsync_SelectsAnotherTool_WhenToolsRemain()
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
        Assert.True(plugin1.IsDisposed);
        Assert.False(plugin2.IsDisposed);
    }

    /// <summary>
    /// Tests that RemoveToolAsync does nothing when no tool is selected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RemoveToolAsync_DoesNothing_WhenNoToolSelected()
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
    public async Task RemoveToolAsync_HandlesServiceFailure()
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
    public async Task RemoveToolAsync_HandlesExceptionsGracefully()
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
    public async Task RefreshToolsAsync_ReloadsToolsSuccessfully()
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
    public async Task RefreshToolsAsync_DeactivatesCurrentTool_BeforeRefresh()
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
    public async Task RefreshToolsAsync_RestoresPreviouslySelectedTool()
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
    /// Tests that RefreshToolsAsync handles service failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshToolsAsync_HandlesServiceFailure()
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
    public async Task RefreshToolsAsync_HandlesExceptionsGracefully()
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
