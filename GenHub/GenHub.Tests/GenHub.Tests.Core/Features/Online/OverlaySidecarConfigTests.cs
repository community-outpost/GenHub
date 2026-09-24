using GenHub.Core.Models.Online;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OverlaySidecarConfig"/> parsing and validation.
/// </summary>
public class OverlaySidecarConfigTests
{
    /// <summary>
    /// Verifies valid JSON parses with explicit values.
    /// </summary>
    [Fact]
    public void Parse_ValidJson_ReturnsSuccessWithValues()
    {
        // Arrange
        const string json = """{"interface":"genhub0","overlayIp":"10.42.0.2","prefixLength":20,"relayHost":"203.0.113.7","relayPort":8088,"networkId":"89df25a8-a748-42b8-ab41-98339d2e6c68","mtu":1400}""";

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal("genhub0", result.Data.InterfaceName);
        Assert.Equal("10.42.0.2", result.Data.OverlayIp);
        Assert.Equal(20, result.Data.PrefixLength);
        Assert.Equal("203.0.113.7", result.Data.RelayHost);
        Assert.Equal(8088, result.Data.RelayPort);
        Assert.Equal("89df25a8-a748-42b8-ab41-98339d2e6c68", result.Data.NetworkId);
        Assert.Equal(1400, result.Data.Mtu);
    }

    /// <summary>
    /// Verifies omitted optionals fall back to defaults.
    /// </summary>
    [Fact]
    public void Parse_OmittedOptionals_UsesDefaults()
    {
        // Arrange
        const string json = """{"interface":"genhub0","overlayIp":"10.42.0.2","relayHost":"203.0.113.7","relayPort":8088,"networkId":"89df25a8-a748-42b8-ab41-98339d2e6c68"}""";

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(20, result.Data.PrefixLength);
        Assert.Equal(1400, result.Data.Mtu);
    }

    /// <summary>
    /// Verifies malformed JSON fails instead of throwing.
    /// </summary>
    [Fact]
    public void Parse_InvalidJson_ReturnsFailure()
    {
        // Act
        var result = OverlaySidecarConfig.Parse("{not json");

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies an unparsable overlay IP fails.
    /// </summary>
    [Fact]
    public void Parse_InvalidOverlayIp_ReturnsFailure()
    {
        // Arrange
        const string json = """{"interface":"genhub0","overlayIp":"not-an-ip","relayHost":"203.0.113.7","relayPort":8088,"networkId":"89df25a8-a748-42b8-ab41-98339d2e6c68"}""";

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies an out-of-range relay port fails.
    /// </summary>
    [Fact]
    public void Parse_InvalidRelayPort_ReturnsFailure()
    {
        // Arrange
        const string json = """{"interface":"genhub0","overlayIp":"10.42.0.2","relayHost":"203.0.113.7","relayPort":99999,"networkId":"89df25a8-a748-42b8-ab41-98339d2e6c68"}""";

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies a blank interface name fails.
    /// </summary>
    [Fact]
    public void Parse_BlankInterfaceName_ReturnsFailure()
    {
        // Arrange
        const string json = """{"interface":"  ","overlayIp":"10.42.0.2","relayHost":"203.0.113.7","relayPort":8088,"networkId":"89df25a8-a748-42b8-ab41-98339d2e6c68"}""";

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.False(result.Success);
    }
}
