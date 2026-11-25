using GenHub.Core.Helpers;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="GameClientTelemetryHelper"/>.
/// </summary>
public class GameClientTelemetryHelperTests
{
    /// <summary>
    /// Verifies that a null client resolves to the retail publisher fallback.
    /// </summary>
    [Fact]
    public void ResolvePublisher_WhenClientIsNull_ReturnsRetail()
    {
        Assert.Equal("retail", GameClientTelemetryHelper.ResolvePublisher((GameClient?)null));
    }

    /// <summary>
    /// Verifies that a null profile resolves to the retail publisher fallback.
    /// </summary>
    [Fact]
    public void ResolvePublisher_WhenProfileIsNull_ReturnsRetail()
    {
        Assert.Equal("retail", GameClientTelemetryHelper.ResolvePublisher((GameProfile?)null));
    }

    /// <summary>
    /// Verifies that an explicit publisher type is returned unchanged.
    /// </summary>
    [Fact]
    public void ResolvePublisher_WhenPublisherTypeIsSet_ReturnsPublisherType()
    {
        var client = new GameClient { PublisherType = "generalsonline", InstallationId = "steam" };

        Assert.Equal("generalsonline", GameClientTelemetryHelper.ResolvePublisher(client));
    }

    /// <summary>
    /// Verifies that the installation identifier is never reported as the publisher.
    /// </summary>
    [Fact]
    public void ResolvePublisher_WhenOnlyInstallationIdIsSet_ReturnsRetail()
    {
        var client = new GameClient { InstallationId = "steam" };

        Assert.Equal("retail", GameClientTelemetryHelper.ResolvePublisher(client));
    }

    /// <summary>
    /// Verifies that publisher types are normalized to lowercase so dashboard buckets never split on casing.
    /// </summary>
    [Fact]
    public void ResolvePublisher_WhenPublisherTypeHasUppercase_NormalizesToLowercase()
    {
        var client = new GameClient { PublisherType = "Retail" };

        Assert.Equal("retail", GameClientTelemetryHelper.ResolvePublisher(client));
    }

    /// <summary>
    /// Verifies that a publisher client without a usable publisher type resolves to the generic publisher fallback.
    /// </summary>
    [Fact]
    public void ResolvePublisher_WhenPublisherTypeIsBlankButPublisherClient_ReturnsPublisherFallback()
    {
        var client = new GameClient { PublisherType = "  " };

        Assert.Equal("publisher", GameClientTelemetryHelper.ResolvePublisher(client));
    }
}
