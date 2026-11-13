using GenHub.Features.GameClients;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.GameClients;

/// <summary>
/// Verifies the stable platform discriminator used to scope standalone-client
/// manifest IDs, so distinct community builds cannot alias one identity.
/// </summary>
public sealed class GameClientDetectorDiscriminatorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"GenHub-DiscriminatorTests-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientDetectorDiscriminatorTests"/> class.
    /// </summary>
    public GameClientDetectorDiscriminatorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    /// <summary>
    /// File names map to their platform without touching the filesystem.
    /// </summary>
    /// <param name="executablePath">The executable path or name.</param>
    /// <param name="expected">The expected discriminator.</param>
    [Theory]
    [InlineData("generals.exe", "windows")]
    [InlineData("GeneralsX.flatpak", "linux")]
    [InlineData("Generals.app/Contents/MacOS/Generals", "macos")]
    [InlineData("README", "unknown")]
    [InlineData(null, "unknown")]
    public void GetClientPlatformDiscriminator_ByName_MapsPlatform(string? executablePath, string expected)
    {
        Assert.Equal(expected, GameClientDetector.GetClientPlatformDiscriminator(executablePath));
    }

    /// <summary>
    /// On-disk binaries map by content sniffing even without a telling extension,
    /// so a macOS and a Linux build of the same client discriminate apart.
    /// </summary>
    [Fact]
    public void GetClientPlatformDiscriminator_ByContent_MapsPlatform()
    {
        var windowsBinary = Path.Combine(_tempDirectory, "game.dat");
        File.WriteAllBytes(windowsBinary, [(byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        var linuxBinary = Path.Combine(_tempDirectory, "game.bin");
        File.WriteAllBytes(linuxBinary, [0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00]);

        Assert.Equal("windows", GameClientDetector.GetClientPlatformDiscriminator(windowsBinary));
        Assert.Equal("linux", GameClientDetector.GetClientPlatformDiscriminator(linuxBinary));
    }
}
