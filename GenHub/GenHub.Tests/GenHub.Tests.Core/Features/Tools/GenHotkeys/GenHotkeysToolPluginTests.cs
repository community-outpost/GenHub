using System;
using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
using GenHub.Features.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Views;
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
    /// Verifies that OnDeactivated and Dispose clean up cleanly without errors.
    /// </summary>
    [Fact]
    public void Plugin_Lifecycle_ExecutesCleanly()
    {
        var plugin = new GenHotkeysToolPlugin();
        plugin.OnDeactivated();
        plugin.Dispose();
    }
}
