using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OnlineLogScrubber"/>.
/// </summary>
public class OnlineLogScrubberTests
{
    /// <summary>
    /// Tests that IPv4 addresses are redacted.
    /// </summary>
    [Fact]
    public void Scrub_WithIpv4_ShouldRedact()
    {
        // Arrange
        const string input = "Peer at 192.168.1.10 failed.";

        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.DoesNotContain("192.168.1.10", result);
        Assert.Contains(OnlineLogScrubber.RedactedIp, result);
    }

    /// <summary>
    /// Tests that IPv4 addresses with ports are redacted.
    /// </summary>
    [Fact]
    public void Scrub_WithIpv4AndPort_ShouldRedact()
    {
        // Arrange
        const string input = "Dialing 203.0.113.7:4321 now.";

        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.DoesNotContain("203.0.113.7", result);
        Assert.Contains(OnlineLogScrubber.RedactedIp, result);
    }

    /// <summary>
    /// Tests that IPv6 addresses are redacted.
    /// </summary>
    [Fact]
    public void Scrub_WithIpv6_ShouldRedact()
    {
        // Arrange
        const string input = "Endpoint [2001:db8::1] unreachable.";

        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.DoesNotContain("2001:db8::1", result);
        Assert.Contains(OnlineLogScrubber.RedactedIp, result);
    }

    /// <summary>
    /// Tests that bearer tokens and grants are redacted.
    /// </summary>
    /// <param name="input">The input text.</param>
    [Theory]
    [InlineData("Authorization: Bearer abcdef123456")]
    [InlineData("grant: secret-grant-token")]
    [InlineData("password= hunter2 value")]
    public void Scrub_WithCredential_ShouldRedact(string input)
    {
        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.Contains(OnlineLogScrubber.RedactedCredential, result);
    }

    /// <summary>
    /// Tests that clean text passes through unchanged.
    /// </summary>
    [Fact]
    public void Scrub_WithCleanText_ShouldReturnUnchanged()
    {
        // Arrange
        const string input = "Joined network lobby with 4 members.";

        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.Equal(input, result);
    }

    /// <summary>
    /// Tests that empty text passes through unchanged.
    /// </summary>
    [Fact]
    public void Scrub_WithEmpty_ShouldReturnEmpty()
    {
        // Act
        var result = OnlineLogScrubber.Scrub(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }
}
