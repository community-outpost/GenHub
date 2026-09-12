using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using GenHub.Features.Tools.GenHotkeys.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="GenHotkeysToolPlugin"/>.
/// </summary>
public class GenHotkeysToolPluginTests
{
    /// <summary>
    /// Verifies that Metadata returns expected values.
    /// </summary>
    [Fact]
    public void Metadata_HasExpectedValues()
    {
        var plugin = new GenHotkeysToolPlugin();

        Assert.Equal(GenHotkeysConstants.ToolId, plugin.Metadata.Id);
        Assert.Equal(GenHotkeysConstants.ToolName, plugin.Metadata.Name);
        Assert.NotNull(plugin.Metadata.Version);
        Assert.True(plugin.Metadata.IsBundled);
    }

    /// <summary>
    /// Verifies that CreateControl returns an empty view when not activated with DI container.
    /// </summary>
    [AvaloniaFact]
    public void CreateControl_WhenNotActivated_ReturnsEmptyView()
    {
        var plugin = new GenHotkeysToolPlugin();
        var control = plugin.CreateControl();

        Assert.NotNull(control);
    }

    /// <summary>
    /// Verifies that CreateControl wires the ViewModel and caches the control instance when activated with DI.
    /// </summary>
    [AvaloniaFact]
    public void CreateControl_WhenActivated_WiresViewModelAndCachesView()
    {
        var mockTechTree = new Mock<ITechTreeService>();
        mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HotkeyFaction>());
        var mockProfileStorage = new Mock<IHotkeyProfileStorageService>();
        mockProfileStorage.Setup(p => p.GetProfilesAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HotkeyProfile>());
        var mockPackageService = new Mock<IHotkeyPackageService>();
        var mockLogger = new Mock<ILogger<GenHotkeysViewModel>>();

        using var vm = new GenHotkeysViewModel(
            mockTechTree.Object,
            mockProfileStorage.Object,
            mockPackageService.Object,
            mockLogger.Object);

        var services = new ServiceCollection();
        services.AddSingleton(vm);
        var provider = services.BuildServiceProvider();

        var plugin = new GenHotkeysToolPlugin();
        plugin.OnActivated(provider);

        var control1 = plugin.CreateControl();
        var control2 = plugin.CreateControl();

        Assert.NotNull(control1);
        Assert.Same(control1, control2);
        Assert.Same(vm, control1.DataContext);

        plugin.Dispose();
    }

    /// <summary>
    /// Verifies that OnDeactivated and Dispose clean up cleanly without errors.
    /// </summary>
    [Fact]
    public void Plugin_Lifecycle_ExecutesCleanly()
    {
        var plugin = new GenHotkeysToolPlugin();
        plugin.OnDeactivated();
        plugin.Dispose();
    }

    /// <summary>
    /// Verifies that multiple calls to Dispose execute cleanly without throwing exceptions.
    /// </summary>
    [Fact]
    public void Plugin_DoubleDispose_ExecutesCleanly()
    {
        var plugin = new GenHotkeysToolPlugin();
        plugin.Dispose();
        plugin.Dispose();
    }
}
