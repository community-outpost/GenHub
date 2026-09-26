using GenHub.Core.Constants;
using GenHub.Core.Models.Online;
using GenHub.Tests.Core.Collections;
using System;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Tests for relay host resolution precedence (environment override, then
/// server-advertised host, then default).
/// </summary>
[Collection(OnlineEnvironmentCollection.Name)]
public class RelayHostResolutionTests
{
    /// <summary>
    /// Tests that an explicit override wins over the advertised host.
    /// </summary>
    [Fact]
    public void ResolveRelayHost_WithOverrideSet_ShouldReturnOverride()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, "198.51.100.9");
        try
        {
            // Act
            var resolved = ApiConstants.ResolveRelayHost("203.0.113.7");

            // Assert
            Assert.Equal("198.51.100.9", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        }
    }

    /// <summary>
    /// Tests that a whitespace-only override is ignored and falls back to the advertised host.
    /// </summary>
    /// <param name="whitespaceOverride">The whitespace override string.</param>
    [Theory]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    public void ResolveRelayHost_WithWhitespaceOverride_ShouldFallBackToAdvertisedHost(string whitespaceOverride)
    {
        // Arrange
        Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, whitespaceOverride);
        try
        {
            // Act
            var resolved = ApiConstants.ResolveRelayHost("203.0.113.7");

            // Assert
            Assert.Equal("203.0.113.7", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        }
    }

    /// <summary>
    /// Tests that the advertised host wins over the default without an override.
    /// </summary>
    [Fact]
    public void ResolveRelayHost_WithoutOverride_ShouldReturnAdvertisedHost()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        try
        {
            // Act
            var resolved = ApiConstants.ResolveRelayHost("203.0.113.7");

            // Assert
            Assert.Equal("203.0.113.7", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        }
    }

    /// <summary>
    /// Tests that the default applies without an override or advertised host.
    /// </summary>
    /// <param name="advertisedHost">The missing advertised host.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveRelayHost_WithoutOverrideOrAdvertised_ShouldReturnDefault(string? advertisedHost)
    {
        // Arrange
        Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        try
        {
            // Act
            var resolved = ApiConstants.ResolveRelayHost(advertisedHost);

            // Assert
            Assert.Equal(ApiConstants.DefaultOnlineRelayHost, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        }
    }

    /// <summary>
    /// Tests that sidecar config parsing honors the relay override.
    /// </summary>
    [Fact]
    public void Parse_WithRelayOverrideSet_ShouldUseOverride()
    {
        // Arrange
        const string json = """{"overlayIp":"10.42.0.2","relayHost":"203.0.113.7","relayPort":8088,"networkId":"net-1"}""";
        Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, "198.51.100.9");
        try
        {
            // Act
            var result = OverlaySidecarConfig.Parse(json);

            // Assert
            Assert.True(result.Success, result.AllErrors);
            Assert.NotNull(result.Data);
            Assert.Equal("198.51.100.9", result.Data.RelayHost);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiConstants.OnlineRelayHostEnvVar, null);
        }
    }
}
