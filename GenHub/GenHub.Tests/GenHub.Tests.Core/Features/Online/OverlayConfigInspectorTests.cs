using GenHub.Core.Helpers;
using System.Text;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OverlayConfigInspector"/>.
/// </summary>
public class OverlayConfigInspectorTests
{
    /// <summary>
    /// Tests that a string overlay name is returned.
    /// </summary>
    [Fact]
    public void TryGetOverlayName_WithStringOverlay_ShouldReturnName()
    {
        // Arrange
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"overlay":"nebula"}"""));

        // Act
        var name = OverlayConfigInspector.TryGetOverlayName(config);

        // Assert
        Assert.Equal("nebula", name);
    }

    /// <summary>
    /// Tests that a non-string overlay value returns null instead of throwing.
    /// </summary>
    /// <param name="json">The config payload.</param>
    [Theory]
    [InlineData("""{"overlay":42}""")]
    [InlineData("""{"overlay":true}""")]
    [InlineData("""{"overlay":{"name":"nebula"}}""")]
    [InlineData("""{"overlay":null}""")]
    [InlineData("""{"other":"value"}""")]
    [InlineData("[1,2,3]")]
    [InlineData(""""x"""")]
    [InlineData("42")]
    public void TryGetOverlayName_WithNonStringOverlay_ShouldReturnNull(string json)
    {
        // Arrange
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // Act
        var name = OverlayConfigInspector.TryGetOverlayName(config);

        // Assert
        Assert.Null(name);
    }

    /// <summary>
    /// Tests that unreadable payloads return null.
    /// </summary>
    /// <param name="config">The config payload.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!!")]
    public void TryGetOverlayName_WithUnreadableConfig_ShouldReturnNull(string config)
    {
        // Act
        var name = OverlayConfigInspector.TryGetOverlayName(config);

        // Assert
        Assert.Null(name);
    }
}
