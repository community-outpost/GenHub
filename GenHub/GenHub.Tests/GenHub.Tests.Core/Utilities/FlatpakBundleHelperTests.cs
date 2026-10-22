using GenHub.Core.Utilities;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Unit tests for <see cref="FlatpakBundleHelper"/>.
/// </summary>
public sealed class FlatpakBundleHelperTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "GenHub_Flatpak_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="FlatpakBundleHelperTests"/> class.
    /// </summary>
    public FlatpakBundleHelperTests()
    {
        Directory.CreateDirectory(_scratch);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    /// <summary>
    /// A well-formed embedded ref yields the application ID.
    /// </summary>
    [Fact]
    public void TryExtractAppId_WellFormedRef_ReturnsId()
    {
        var header = BundleHeader("app/com.example.GameClient/x86_64/stable");

        Assert.Equal("com.example.GameClient", FlatpakBundleHelper.TryExtractAppId(header));
    }

    /// <summary>
    /// Malformed headers yield null instead of guessing.
    /// </summary>
    /// <param name="embedded">The embedded ref text.</param>
    [Theory]
    [InlineData("app/com.example.GameClient/x86_64")]
    [InlineData("app/com.example.GameClient")]
    [InlineData("app/bareid/x86_64/stable")]
    [InlineData("no ref here")]
    public void TryExtractAppId_MalformedRef_ReturnsNull(string embedded)
    {
        Assert.Null(FlatpakBundleHelper.TryExtractAppId(BundleHeader(embedded)));
    }

    /// <summary>
    /// Missing files yield null instead of throwing.
    /// </summary>
    [Fact]
    public void TryExtractAppId_MissingFile_ReturnsNull()
    {
        Assert.Null(FlatpakBundleHelper.TryExtractAppId(Path.Combine(_scratch, "absent.flatpak")));
    }

    /// <summary>
    /// Bundle files on disk are inspected through their header bytes.
    /// </summary>
    [Fact]
    public void TryExtractAppId_BundleFile_ReturnsId()
    {
        var path = Path.Combine(_scratch, "game.flatpak");
        File.WriteAllBytes(path, BundleHeader("prefix-bytes app/org.example.Client/aarch64/master trailing"));

        Assert.Equal("org.example.Client", FlatpakBundleHelper.TryExtractAppId(path));
    }

    private static byte[] BundleHeader(string embedded)
    {
        var header = new byte[512];
        Encoding.ASCII.GetBytes("flatpak").CopyTo(header, 0);
        Encoding.ASCII.GetBytes(embedded).CopyTo(header, 64);
        return header;
    }
}
