using GenHub.Core.Services.Online.Tun;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Net;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="LinuxTunSetup"/>. Only argument validation and
/// command building run here; nothing spawns processes or needs privileges.
/// </summary>
public class LinuxTunSetupTests
{
    /// <summary>
    /// Tests that the create arguments order the tuntap command correctly.
    /// </summary>
    [Fact]
    public void BuildCreateArgs_ShouldOrderTuntapArguments()
    {
        // Act
        var args = LinuxTunSetup.BuildCreateArgs("genhub0", "mint");

        // Assert
        Assert.Equal(["tuntap", "add", "dev", "genhub0", "mode", "tun", "user", "mint"], args);
    }

    /// <summary>
    /// Tests that the flush arguments target the interface address list.
    /// </summary>
    [Fact]
    public void BuildFlushArgs_ShouldTargetInterface()
    {
        // Act
        var args = LinuxTunSetup.BuildFlushArgs("genhub0");

        // Assert
        Assert.Equal(["-4", "addr", "flush", "dev", "genhub0"], args);
    }

    /// <summary>
    /// Tests that the address arguments format the overlay CIDR correctly.
    /// </summary>
    [Fact]
    public void BuildAddressArgs_ShouldFormatCidr()
    {
        // Act
        var args = LinuxTunSetup.BuildAddressArgs("genhub0", IPAddress.Parse("10.42.0.2"), 20);

        // Assert
        Assert.Equal(["addr", "add", "10.42.0.2/20", "dev", "genhub0"], args);
    }

    /// <summary>
    /// Tests that the link arguments set the MTU and bring the link up.
    /// </summary>
    [Fact]
    public void BuildLinkUpArgs_ShouldSetMtuAndUp()
    {
        // Act
        var args = LinuxTunSetup.BuildLinkUpArgs("genhub0", 1400);

        // Assert
        Assert.Equal(["link", "set", "dev", "genhub0", "mtu", "1400", "up"], args);
    }

    /// <summary>
    /// Tests that the manual script chains sudo commands with the real values.
    /// </summary>
    [Fact]
    public void BuildManualSetupScript_ShouldChainSudoCommands()
    {
        // Act
        var script = LinuxTunSetup.BuildManualSetupScript(
            "genhub0", IPAddress.Parse("10.42.0.2"), 20, 1400, "mint");

        // Assert
        Assert.StartsWith("ip link show genhub0 >/dev/null 2>&1 || sudo ip tuntap add mode tun dev genhub0 user \"mint\"", script);
        Assert.Contains("sudo ip addr flush dev genhub0", script);
        Assert.Contains("sudo ip addr add 10.42.0.2/20 dev genhub0", script);
        Assert.Contains("sudo ip link set dev genhub0 mtu 1400 up", script);
    }

    /// <summary>
    /// Tests that an invalid interface name fails before spawning anything.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SetupAsync_WithInvalidInterfaceName_ShouldFailAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            // Validation short-circuits on platform first; nothing to assert elsewhere.
            return;
        }

        // Arrange
        var setup = new LinuxTunSetup(NullLogger<LinuxTunSetup>.Instance);

        // Act
        var result = await setup.SetupAsync("bad name!", IPAddress.Parse("10.42.0.2"), 20, 1400);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid TUN interface name", result.FirstError);
    }

    /// <summary>
    /// Tests that a non-IPv4 overlay address fails validation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SetupAsync_WithIpv6Address_ShouldFailAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var setup = new LinuxTunSetup(NullLogger<LinuxTunSetup>.Instance);

        // Act
        var result = await setup.SetupAsync("genhub0", IPAddress.IPv6Loopback, 20, 1400);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("invalid overlay IP", result.FirstError);
    }

    /// <summary>
    /// Tests that an out-of-range prefix length fails validation.
    /// </summary>
    /// <param name="prefixLength">The invalid prefix length.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public async Task SetupAsync_WithBadPrefixLength_ShouldFailAsync(int prefixLength)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var setup = new LinuxTunSetup(NullLogger<LinuxTunSetup>.Instance);

        // Act
        var result = await setup.SetupAsync("genhub0", IPAddress.Parse("10.42.0.2"), prefixLength, 1400);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("invalid prefix length", result.FirstError);
    }

    /// <summary>
    /// Tests that an out-of-range MTU fails validation.
    /// </summary>
    /// <param name="mtu">The invalid MTU.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(575)]
    [InlineData(9001)]
    public async Task SetupAsync_WithBadMtu_ShouldFailAsync(int mtu)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var setup = new LinuxTunSetup(NullLogger<LinuxTunSetup>.Instance);

        // Act
        var result = await setup.SetupAsync("genhub0", IPAddress.Parse("10.42.0.2"), 20, mtu);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("invalid MTU", result.FirstError);
    }
}
