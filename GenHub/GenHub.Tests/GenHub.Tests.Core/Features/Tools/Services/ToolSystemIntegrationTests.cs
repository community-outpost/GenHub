using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Services.Tools;
using GenHub.Tests.Core.Features.Tools.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Integration tests for the tool plugin system.
/// </summary>
public class ToolSystemIntegrationTests
{
    private readonly Mock<ILogger<ToolPluginLoader>> _mockLoaderLogger;
    private readonly Mock<ILogger<ToolService>> _mockServiceLogger;
    private readonly Mock<IUserSettingsService> _mockSettingsService;
    private readonly UserSettings _testSettings;
    private readonly IToolPluginLoader _pluginLoader;
    private readonly IToolRegistry _registry;
    private readonly IToolManager _toolService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSystemIntegrationTests"/> class.
    /// </summary>
    public ToolSystemIntegrationTests()
    {
        _mockLoaderLogger = new Mock<ILogger<ToolPluginLoader>>();
        _mockServiceLogger = new Mock<ILogger<ToolService>>();
        _mockSettingsService = new Mock<IUserSettingsService>();

        _testSettings = new UserSettings
        {
            InstalledToolAssemblyPaths = new List<string>(),
        };

        _mockSettingsService.Setup(x => x.Get()).Returns(_testSettings);

        // Create real instances for integration testing
        _pluginLoader = new ToolPluginLoader(_mockLoaderLogger.Object);
        _registry = new ToolRegistry();
        _toolService = new ToolService(
            _pluginLoader,
            _registry,
            _mockSettingsService.Object,
            _mockServiceLogger.Object);
    }

    /// <summary>
    /// Tests the complete workflow of adding and removing a tool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CompleteWorkflow_AddAndRemoveTool_WorksCorrectly()
    {
        // Arrange
        var mockPlugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        var assemblyPath = @"C:\Test\Tool.dll";

        // Mock the loader to return our plugin
        var mockLoader = new Mock<IToolPluginLoader>();
        mockLoader.Setup(x => x.ValidatePlugin(assemblyPath)).Returns(true);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(assemblyPath)).Returns(mockPlugin);

        var service = new ToolService(
            mockLoader.Object,
            _registry,
            _mockSettingsService.Object,
            _mockServiceLogger.Object);

        Action<UserSettings>? capturedUpdateAction = null;
        _mockSettingsService.Setup(x => x.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => capturedUpdateAction = action);

        // Act - Add Tool
        var addResult = await service.AddToolAsync(assemblyPath);

        // Assert - Tool Added
        Assert.True(addResult.Success);
        Assert.NotNull(addResult.Data);
        Assert.Equal(mockPlugin, addResult.Data);

        // Verify tool is in registry
        var allTools = _registry.GetAllTools();
        Assert.Single(allTools);
        Assert.Equal(mockPlugin, allTools[0]);

        // Verify settings were updated
        capturedUpdateAction?.Invoke(_testSettings);
        Assert.NotNull(_testSettings.InstalledToolAssemblyPaths);
        Assert.Contains(assemblyPath, _testSettings.InstalledToolAssemblyPaths);

        // Act - Remove Tool
        var removeResult = await service.RemoveToolAsync(mockPlugin.Metadata.Id);

        // Assert - Tool Removed
        Assert.True(removeResult.Success);
        Assert.True(removeResult.Data);

        // Verify tool is no longer in registry
        var toolsAfterRemoval = _registry.GetAllTools();
        Assert.Empty(toolsAfterRemoval);

        // Verify tool was disposed
        Assert.True(mockPlugin.IsDisposed);

        // Verify settings were updated
        capturedUpdateAction?.Invoke(_testSettings);
        Assert.DoesNotContain(assemblyPath, _testSettings.InstalledToolAssemblyPaths);
    }

    /// <summary>
    /// Tests loading saved tools from settings.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadSavedTools_LoadsMultipleToolsFromSettings()
    {
        // Arrange
        var path1 = @"C:\Test\Tool1.dll";
        var path2 = @"C:\Test\Tool2.dll";
        var path3 = @"C:\Test\Tool3.dll";

        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        var plugin3 = new MockToolPlugin("test.tool3", "Test Tool 3", "1.0.0", "Author 3");

        _testSettings.InstalledToolAssemblyPaths = new List<string> { path1, path2, path3 };

        var mockLoader = new Mock<IToolPluginLoader>();
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path1)).Returns(plugin1);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path2)).Returns(plugin2);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path3)).Returns(plugin3);

        var service = new ToolService(
            mockLoader.Object,
            _registry,
            _mockSettingsService.Object,
            _mockServiceLogger.Object);

        // Act
        var result = await service.LoadSavedToolsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.Count);
        Assert.Contains(plugin1, result.Data);
        Assert.Contains(plugin2, result.Data);
        Assert.Contains(plugin3, result.Data);

        // Verify all tools are registered
        var allTools = _registry.GetAllTools();
        Assert.Equal(3, allTools.Count);
    }

    /// <summary>
    /// Tests that duplicate tool IDs are prevented.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task AddTool_PreventsDuplicateToolIds()
    {
        // Arrange
        var path1 = @"C:\Test\Tool_v1.dll";
        var path2 = @"C:\Test\Tool_v2.dll";

        var plugin1 = new MockToolPlugin("test.tool", "Test Tool V1", "1.0.0", "Test Author");
        var plugin2 = new MockToolPlugin("test.tool", "Test Tool V2", "2.0.0", "Test Author");

        var mockLoader = new Mock<IToolPluginLoader>();
        mockLoader.Setup(x => x.ValidatePlugin(path1)).Returns(true);
        mockLoader.Setup(x => x.ValidatePlugin(path2)).Returns(true);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path1)).Returns(plugin1);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path2)).Returns(plugin2);

        var service = new ToolService(
            mockLoader.Object,
            _registry,
            _mockSettingsService.Object,
            _mockServiceLogger.Object);

        // Act
        var result1 = await service.AddToolAsync(path1);
        var result2 = await service.AddToolAsync(path2);

        // Assert
        Assert.True(result1.Success);
        Assert.False(result2.Success);
        Assert.NotNull(result2.Errors);
        Assert.Contains(result2.Errors, e => e.Contains("already registered"));

        // Verify only first tool is in registry
        var allTools = _registry.GetAllTools();
        Assert.Single(allTools);
        Assert.Equal(plugin1, allTools[0]);
    }

    /// <summary>
    /// Tests that tools can be replaced by unregistering and re-registering.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ReplaceTool_ByRemovingAndAddingNewVersion()
    {
        // Arrange
        var path1 = @"C:\Test\Tool_v1.dll";
        var path2 = @"C:\Test\Tool_v2.dll";

        var plugin1 = new MockToolPlugin("test.tool", "Test Tool V1", "1.0.0", "Test Author");
        var plugin2 = new MockToolPlugin("test.tool", "Test Tool V2", "2.0.0", "Test Author");

        var mockLoader = new Mock<IToolPluginLoader>();
        mockLoader.Setup(x => x.ValidatePlugin(It.IsAny<string>())).Returns(true);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path1)).Returns(plugin1);
        mockLoader.Setup(x => x.LoadPluginFromAssembly(path2)).Returns(plugin2);

        var service = new ToolService(
            mockLoader.Object,
            _registry,
            _mockSettingsService.Object,
            _mockServiceLogger.Object);

        // Act - Add first version
        var addResult1 = await service.AddToolAsync(path1);
        Assert.True(addResult1.Success);

        // Remove first version
        var removeResult = await service.RemoveToolAsync(plugin1.Metadata.Id);
        Assert.True(removeResult.Success);

        // Add second version
        var addResult2 = await service.AddToolAsync(path2);

        // Assert
        Assert.True(addResult2.Success);
        Assert.Equal(plugin2, addResult2.Data);
        Assert.True(plugin1.IsDisposed);

        var allTools = _registry.GetAllTools();
        Assert.Single(allTools);
        Assert.Equal(plugin2, allTools[0]);
        Assert.Equal("2.0.0", allTools[0].Metadata.Version);
    }

    /// <summary>
    /// Tests that the registry handles concurrent operations correctly.
    /// </summary>
    [Fact]
    public void Registry_HandlesConcurrentOperations()
    {
        // Arrange
        var plugins = Enumerable.Range(1, 10)
            .Select(i => new MockToolPlugin($"test.tool{i}", $"Test Tool {i}", "1.0.0", $"Author {i}"))
            .ToList();

        // Act - Register tools concurrently
        Parallel.ForEach(plugins, plugin =>
        {
            _registry.RegisterTool(plugin, $@"C:\Test\Tool{plugin.Metadata.Id}.dll");
        });

        // Assert
        var allTools = _registry.GetAllTools();
        Assert.Equal(10, allTools.Count);

        foreach (var plugin in plugins)
        {
            Assert.Contains(plugin, allTools);
            var registeredTool = _registry.GetToolById(plugin.Metadata.Id);
            Assert.NotNull(registeredTool);
            Assert.Equal(plugin, registeredTool);
        }
    }

    /// <summary>
    /// Tests that removing tools concurrently works correctly.
    /// </summary>
    [Fact]
    public void Registry_HandlesConcurrentRemoval()
    {
        // Arrange
        var plugins = Enumerable.Range(1, 10)
            .Select(i => new MockToolPlugin($"test.tool{i}", $"Test Tool {i}", "1.0.0", $"Author {i}"))
            .ToList();

        foreach (var plugin in plugins)
        {
            _registry.RegisterTool(plugin, $@"C:\Test\Tool{plugin.Metadata.Id}.dll");
        }

        // Act - Remove tools concurrently
        Parallel.ForEach(plugins.Take(5), plugin =>
        {
            _registry.UnregisterTool(plugin.Metadata.Id);
        });

        // Assert
        var allTools = _registry.GetAllTools();
        Assert.Equal(5, allTools.Count);

        for (int i = 0; i < 5; i++)
        {
            Assert.True(plugins[i].IsDisposed);
        }

        for (int i = 5; i < 10; i++)
        {
            Assert.False(plugins[i].IsDisposed);
            Assert.Contains(plugins[i], allTools);
        }
    }

    /// <summary>
    /// Tests the complete lifecycle of a tool plugin.
    /// </summary>
    [Fact]
    public void ToolLifecycle_ActivationDeactivationDisposal()
    {
        // Arrange
        var plugin = new MockToolPlugin("test.tool", "Test Tool", "1.0.0", "Test Author");
        var serviceProvider = Mock.Of<IServiceProvider>();

        // Act & Assert - Initial state
        Assert.False(plugin.IsActivated);
        Assert.False(plugin.IsDisposed);
        Assert.Equal(0, plugin.OnActivatedCallCount);
        Assert.Equal(0, plugin.OnDeactivatedCallCount);

        // Activate
        plugin.OnActivated(serviceProvider);
        Assert.True(plugin.IsActivated);
        Assert.Equal(1, plugin.OnActivatedCallCount);
        Assert.Equal(serviceProvider, plugin.LastServiceProvider);

        // Create control
        var control = plugin.CreateControl();
        Assert.NotNull(control);
        Assert.Equal(1, plugin.CreateControlCallCount);

        // Deactivate
        plugin.OnDeactivated();
        Assert.False(plugin.IsActivated);
        Assert.Equal(1, plugin.OnDeactivatedCallCount);

        // Reactivate
        plugin.OnActivated(serviceProvider);
        Assert.True(plugin.IsActivated);
        Assert.Equal(2, plugin.OnActivatedCallCount);

        // Dispose
        plugin.Dispose();
        Assert.True(plugin.IsDisposed);
        Assert.False(plugin.IsActivated);
    }

    /// <summary>
    /// Tests that GetAllTools returns consistent results.
    /// </summary>
    [Fact]
    public void GetAllTools_ReturnsConsistentResults()
    {
        // Arrange
        var plugin1 = new MockToolPlugin("test.tool1", "Test Tool 1", "1.0.0", "Author 1");
        var plugin2 = new MockToolPlugin("test.tool2", "Test Tool 2", "1.0.0", "Author 2");
        var plugin3 = new MockToolPlugin("test.tool3", "Test Tool 3", "1.0.0", "Author 3");

        _registry.RegisterTool(plugin1, @"C:\Test\Tool1.dll");
        _registry.RegisterTool(plugin2, @"C:\Test\Tool2.dll");
        _registry.RegisterTool(plugin3, @"C:\Test\Tool3.dll");

        // Act - Call GetAllTools multiple times
        var result1 = _toolService.GetAllTools();
        var result2 = _toolService.GetAllTools();
        var result3 = _toolService.GetAllTools();

        // Assert
        Assert.Equal(3, result1.Count);
        Assert.Equal(3, result2.Count);
        Assert.Equal(3, result3.Count);

        Assert.Contains(plugin1, result1);
        Assert.Contains(plugin2, result1);
        Assert.Contains(plugin3, result1);
    }
}
