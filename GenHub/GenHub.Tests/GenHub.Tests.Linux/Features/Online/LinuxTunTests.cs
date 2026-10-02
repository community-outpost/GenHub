using GenHub.Core.Services.Online.Tun;

namespace GenHub.Tests.Linux.Features.Online;

/// <summary>
/// Tests for Linux TUN interface management.
/// </summary>
public class LinuxTunTests
{
    private const string MissingInterfaceName = "genhub-none";

    /// <summary>
    /// Verifies a missing interface reports absent without privileges.
    /// </summary>
    [Fact]
    public void Exists_MissingInterface_ReturnsFalse()
    {
        Assert.False(LinuxTunInterface.Exists(MissingInterfaceName));
    }

    /// <summary>
    /// Verifies attaching to a missing interface fails instead of throwing.
    /// </summary>
    [Fact]
    public void Attach_MissingInterface_ReturnsFailure()
    {
        var result = LinuxTunDevice.Attach(MissingInterfaceName);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies blank interface names fail validation before any syscall.
    /// </summary>
    [Fact]
    public void CreatePersistent_BlankName_ReturnsFailure()
    {
        var result = LinuxTunInterface.TryCreatePersistent(string.Empty);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies overlong interface names fail validation (IFNAMSIZ).
    /// </summary>
    [Fact]
    public void CreatePersistent_TooLongName_ReturnsFailure()
    {
        var result = LinuxTunInterface.TryCreatePersistent(new string('a', 16));

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies a full create, attach, and delete round trip when privileged.
    /// </summary>
    /// <remarks>
    /// Returns quietly without privileges (or without TUN support): only the
    /// negative paths above are observable everywhere.
    /// </remarks>
    [Fact]
    public void RoundTrip_WhenPrivileged_Succeeds()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        const string name = "genhubt0";
        var created = LinuxTunInterface.TryCreatePersistent(name);
        if (!created.Success)
        {
            return;
        }

        try
        {
            Assert.True(LinuxTunInterface.Exists(name));

            var attached = LinuxTunDevice.Attach(name);
            Assert.True(attached.Success, attached.AllErrors);
            Assert.NotNull(attached.Data);
            using var device = attached.Data;
            Assert.Equal(name, device.InterfaceName);
            Assert.NotNull(device.Stream);
        }
        finally
        {
            LinuxTunInterface.Delete(name);
        }

        Assert.False(LinuxTunInterface.Exists(name));
    }
}
