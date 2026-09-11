using System;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.True(plugin.Metadata.IsBundled);
        Assert.Contains("Hotkeys", plugin.Metadata.Tags);
    }

    /// <summary>
    /// Verifies that CreateControl returns a fallback control before activation.
    /// </summary>
    [Fact]
    public void CreateControl_WithoutActivation_ReturnsErrorControl()
    {
        var plugin = new GenHotkeysToolPlugin();
        var control = plugin.CreateControl();

        Assert.NotNull(control);
    }

    /// <summary>
    /// Verifies that CreateControl returns the initialized view after OnActivated.
    /// </summary>
    [Fact]
    public void CreateControl_WhenActivated_InstantiatesViewWithViewModel()
    {
        var mockTechTree = new Mock<ITechTreeService>();
        mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), default))
            .ReturnsAsync(Array.Empty<HotkeyFaction>());

        var mockStorage = new Mock<IHotkeyProfileStorageService>();
        mockStorage.Setup(s => s.GetProfilesAsync(It.IsAny<GameType>(), default))
            .ReturnsAsync(new[] { new HotkeyProfile { Name = "Test Profile", TargetGame = GameType.ZeroHour } });

        var mockPackage = new Mock<IHotkeyPackageService>();

        var services = new ServiceCollection();
        services.AddSingleton(mockTechTree.Object);
        services.AddSingleton(mockStorage.Object);
        services.AddSingleton(mockPackage.Object);
        services.AddTransient<GenHotkeysViewModel>(sp => new GenHotkeysViewModel(
            mockTechTree.Object,
            mockStorage.Object,
            mockPackage.Object,
            NullLogger<GenHotkeysViewModel>.Instance));

        var serviceProvider = services.BuildServiceProvider();

        var plugin = new GenHotkeysToolPlugin();
        plugin.OnActivated(serviceProvider);

        var control = plugin.CreateControl();
        Assert.NotNull(control);
        Assert.NotNull(control.DataContext);
        Assert.IsType<GenHotkeysViewModel>(control.DataContext);

        plugin.OnDeactivated();
        plugin.Dispose();
    }
}
