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
    /// <param name="expectedSecret">The expected secret value that should not appear in the scrubbed output.</param>
    [Theory]
    [InlineData("Authorization: Bearer abcdef123456", "abcdef123456")]
    [InlineData("grant: secret-grant-token", "secret-grant-token")]
    [InlineData("password= hunter2 value", "hunter2")]
    [InlineData("{\"password\":\"super-secret\"}", "super-secret")]
    [InlineData("{\"grant\":\"eyJleHAiOjEyMw\"}", "eyJleHAiOjEyMw")]
    [InlineData("wss://edge/v1/networks/net-1/presence?ticket=secret-grant", "secret-grant")]
    public void Scrub_WithCredential_ShouldRedact(string input, string expectedSecret)
    {
        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.Contains(OnlineLogScrubber.RedactedCredential, result);
        Assert.DoesNotContain(expectedSecret, result);
    }

    /// <summary>
    /// Tests that full IPv6 addresses without compression are redacted.
    /// </summary>
    [Fact]
    public void Scrub_WithFullIpv6_ShouldRedact()
    {
        // Arrange
        const string input = "Peer 2001:0db8:85a3:0000:0000:8a2e:0370:7334 timed out.";

        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.DoesNotContain("2001:0db8", result);
        Assert.Contains(OnlineLogScrubber.RedactedIp, result);
    }

    /// <summary>
    /// Tests that timestamps are not mistaken for IPv6 addresses.
    /// </summary>
    [Fact]
    public void Scrub_WithTimestamp_ShouldPreserve()
    {
        // Arrange
        const string input = "Last seen at 12:34:56 today.";

        // Act
        var result = OnlineLogScrubber.Scrub(input);

        // Assert
        Assert.Equal(input, result);
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
