using GenHub.Core.Constants;
using GenHub.Core.Models.Online;
using System.Text;
using Xunit;

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
        Assert.Equal("10.42.0.2", result.Data.OverlayIp.ToString());
        Assert.Equal(20, result.Data.PrefixLength);
        Assert.Equal("203.0.113.7", result.Data.RelayHost);
        Assert.Equal(8088, result.Data.RelayPort);
        Assert.Equal("89df25a8-a748-42b8-ab41-98339d2e6c68", result.Data.NetworkId);
        Assert.Equal(1400, result.Data.Mtu);
    }

    /// <summary>
    /// Verifies default values are populated when optional fields are missing.
    /// </summary>
    [Fact]
    public void Parse_MissingOptionalFields_PopulatesDefaults()
    {
        // Arrange
        const string json = """{"overlayIp":"10.42.0.5","relayHost":"198.51.100.1","relayPort":9000,"networkId":"test-net"}""";

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        var expectedInterface = OperatingSystem.IsWindows()
            ? OnlineConstants.TunDefaultWindowsInterfaceName
            : OnlineConstants.TunDefaultInterfaceName;
        Assert.Equal(expectedInterface, result.Data.InterfaceName);
        Assert.Equal(OnlineConstants.TunOverlayPrefixLength, result.Data.PrefixLength);
        Assert.Equal(OnlineConstants.TunDefaultMtu, result.Data.Mtu);
    }

    /// <summary>
    /// Verifies base64 encoded JSON is successfully parsed.
    /// </summary>
    [Fact]
    public void Parse_Base64EncodedJson_ParsesSuccessfully()
    {
        // Arrange
        const string rawJson = """{"interface":"tun42","overlayIp":"10.42.1.10","relayHost":"relay.example.com","relayPort":443,"networkId":"net-1"}""";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawJson));

        // Act
        var result = OverlaySidecarConfig.Parse(base64);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal("tun42", result.Data.InterfaceName);
        Assert.Equal("10.42.1.10", result.Data.OverlayIp.ToString());
        Assert.Equal("relay.example.com", result.Data.RelayHost);
        Assert.Equal(443, result.Data.RelayPort);
        Assert.Equal("net-1", result.Data.NetworkId);
    }

    /// <summary>
    /// Verifies nested Gateway/Edge Gateway JSON structure can be parsed.
    /// </summary>
    [Fact]
    public void Parse_NestedGatewayFormat_ParsesSuccessfully()
    {
        // Arrange
        const string json = """
        {
            "assignedIp": "10.42.2.8",
            "subnetMask": "255.255.240.0",
            "gateway": {
                "host": "198.51.100.50",
                "port": 9999
            },
            "networkId": "edge-cluster-1"
        }
        """;

        // Act
        var result = OverlaySidecarConfig.Parse(json);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal("10.42.2.8", result.Data.OverlayIp.ToString());
        Assert.Equal(20, result.Data.PrefixLength);
        Assert.Equal("198.51.100.50", result.Data.RelayHost);
        Assert.Equal(9999, result.Data.RelayPort);
        Assert.Equal("edge-cluster-1", result.Data.NetworkId);
    }

    /// <summary>
    /// Verifies validation fails when required fields are missing or invalid.
    /// </summary>
    /// <param name="invalidJson">The invalid JSON payload to parse.</param>
    [Theory]
    [InlineData("""{"relayHost":"host","relayPort":80,"networkId":"id"}""")] // Missing overlayIp
    [InlineData("""{"overlayIp":"not-an-ip","relayHost":"host","relayPort":80,"networkId":"id"}""")] // Invalid overlayIp
    [InlineData("""{"overlayIp":"10.42.0.1","relayHost":"host","relayPort":70000,"networkId":"id"}""")] // Port > 65535
    [InlineData("""{"overlayIp":"10.42.0.1","relayHost":"host","relayPort":-5,"networkId":"id"}""")] // Negative port
    [InlineData("""{"overlayIp":"10.42.0.1","relayHost":"host","relayPort":80,"networkId":""}""")] // Empty networkId
    [InlineData("""{"overlayIp":"10.42.0.1","prefixLength":50,"networkId":"id"}""")] // Invalid prefix length
    [InlineData("""{"overlayIp":"10.42.0.1","mtu":100,"networkId":"id"}""")] // Invalid MTU
    public void Parse_InvalidInput_ReturnsFailure(string invalidJson)
    {
        // Act
        var result = OverlaySidecarConfig.Parse(invalidJson);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }
}
