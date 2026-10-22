using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Publishers;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for <see cref="CommunityGameClientIdentifier"/> and cross-platform executable classification.
/// </summary>
public sealed class CommunityGameClientIdentifierTests : IDisposable
{
    private static readonly byte[] ElfHeader = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00];
    private static readonly byte[] MachOHeader = [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00];

    private readonly CommunityGameClientIdentifier _identifier = new();
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "GenHub_CommunityId_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityGameClientIdentifierTests"/> class.
    /// </summary>
    public CommunityGameClientIdentifierTests()
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
    /// Flatpak bundles are Linux game client packages and are always claimed.
    /// </summary>
    [Fact]
    public void CanIdentify_FlatpakBundle_ReturnsTrue()
    {
        Assert.True(_identifier.CanIdentify("/games/GeneralsZH.flatpak"));
    }

    /// <summary>
    /// Bare Windows executables belong to the retail, GeneralsOnline, and SuperHackers
    /// identifiers. Claiming every .exe would flood installations with phantom clients.
    /// </summary>
    /// <param name="path">The executable path to inspect.</param>
    [Theory]
    [InlineData("/games/generals.exe")]
    [InlineData("C:\\Games\\Generals\\generals.exe")]
    [InlineData("/games/worldbuilder.exe")]
    public void CanIdentify_WindowsExe_ReturnsFalse(string path)
    {
        Assert.False(_identifier.CanIdentify(path));
    }

    /// <summary>
    /// Tests that macOS app bundles are recognized by the community client identifier.
    /// </summary>
    /// <param name="path">The bundle path to inspect.</param>
    [Theory]
    [InlineData("/games/ZeroHour.app")]
    [InlineData("/games/ZeroHour.app/Contents/MacOS/ZeroHour")]
    public void CanIdentify_MacApp_ReturnsTrue(string path)
    {
        Assert.True(_identifier.CanIdentify(path));
    }

    /// <summary>
    /// Extensionless files are only claimed when their magic bytes prove a native binary.
    /// </summary>
    /// <param name="fileName">The binary file name.</param>
    [Theory]
    [InlineData("GeneralsOnlineZH")]
    [InlineData("custom-client-name")]
    public void CanIdentify_ExtensionlessNativeBinary_ReturnsTrue(string fileName)
    {
        var path = WriteBinary(fileName, ElfHeader);

        Assert.True(_identifier.CanIdentify(path));
    }

    /// <summary>
    /// Extensionless or shared library files starting with "lib" or known library extensions are rejected.
    /// </summary>
    /// <param name="fileName">The candidate library file name.</param>
    [Theory]
    [InlineData("libtolua")]
    [InlineData("libgame")]
    public void CanIdentify_LibraryFiles_ReturnsFalse(string fileName)
    {
        var path = WriteBinary(fileName, ElfHeader);

        Assert.False(_identifier.CanIdentify(path));
    }

    /// <summary>
    /// Binaries without Generals or Zero Hour markers identify as Unknown game type.
    /// </summary>
    [Fact]
    public void Identify_CustomBinaryWithoutGameMarkers_IdentifiesAsUnknownGameType()
    {
        var path = WriteBinary("custom-client-name", ElfHeader);

        var result = _identifier.Identify(path);

        Assert.NotNull(result);
        Assert.Equal(GameType.Unknown, result.GameType);
        Assert.Equal("Community Client (Linux)", result.DisplayName);
    }

    /// <summary>
    /// Extensionless text files and missing paths are never claimed: the name alone proves nothing.
    /// </summary>
    [Fact]
    public void CanIdentify_ExtensionlessWithoutNativeMagic_ReturnsFalse()
    {
        var readme = Path.Combine(_scratch, "README");
        File.WriteAllText(readme, "documentation without magic bytes");

        Assert.False(_identifier.CanIdentify(readme));
        Assert.False(_identifier.CanIdentify(Path.Combine(_scratch, "absent-binary")));
    }

    /// <summary>
    /// Tests that empty or unrelated file paths are not recognized.
    /// </summary>
    [Fact]
    public void CanIdentify_InvalidPath_ReturnsFalse()
    {
        Assert.False(_identifier.CanIdentify(string.Empty));
        Assert.False(_identifier.CanIdentify("unknown.txt"));
    }

    /// <summary>
    /// Flatpak bundles are identified as Linux community clients.
    /// </summary>
    [Fact]
    public void Identify_FlatpakBundle_IdentifiesAsLinuxCommunityClient()
    {
        var result = _identifier.Identify("/games/GeneralsZH.flatpak");

        Assert.NotNull(result);
        Assert.Equal(PublisherTypeConstants.Community, result.PublisherId);
        Assert.Equal("linux", result.Variant);
        Assert.Equal(GameType.ZeroHour, result.GameType);
        Assert.Contains("Linux", result.DisplayName);
    }

    /// <summary>
    /// Tests that macOS app bundles are identified as macOS community clients.
    /// </summary>
    [Fact]
    public void Identify_MacAppBundle_IdentifiesAsMacOsCommunityClient()
    {
        var result = _identifier.Identify("/Applications/GeneralsZH.app");

        Assert.NotNull(result);
        Assert.Equal(PublisherTypeConstants.Community, result.PublisherId);
        Assert.Equal("macos", result.Variant);
        Assert.Equal(GameType.ZeroHour, result.GameType);
        Assert.Contains("macOS", result.DisplayName);
    }

    /// <summary>
    /// Windows executables are not identified as community clients.
    /// </summary>
    [Fact]
    public void Identify_WindowsExe_ReturnsNull()
    {
        Assert.Null(_identifier.Identify("C:\\Games\\Generals\\generals.exe"));
    }

    /// <summary>
    /// A generals-only binary under a spaced Zero Hour directory still reads as Zero Hour.
    /// </summary>
    [Fact]
    public void Identify_SpacedZeroHourDirectory_IdentifiesAsZeroHour()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_scratch, "Command & Conquer Generals Zero Hour")).FullName;
        var path = Path.Combine(directory, "generals");
        var payload = new byte[64];
        Buffer.BlockCopy(MachOHeader, 0, payload, 0, MachOHeader.Length);
        File.WriteAllBytes(path, payload);

        var result = _identifier.Identify(path);

        Assert.NotNull(result);
        Assert.Equal(GameType.ZeroHour, result.GameType);
    }

    /// <summary>
    /// Tests that <see cref="ExecutableFileClassifier.DetectPlatform(System.ReadOnlySpan{byte})"/> correctly identifies header magic bytes.
    /// </summary>
    [Fact]
    public void ExecutableFileClassifier_DetectPlatform_IdentifiesMagicBytes()
    {
        // MZ
        byte[] mzHeader = [0x4D, 0x5A, 0x90, 0x00];
        Assert.Equal(ExecutablePlatform.Windows, ExecutableFileClassifier.DetectPlatform(mzHeader));

        // ELF
        byte[] elfHeader = [0x7F, 0x45, 0x4C, 0x46];
        Assert.Equal(ExecutablePlatform.Linux, ExecutableFileClassifier.DetectPlatform(elfHeader));

        // Mach-O 64-bit thin
        byte[] machO64 = [0xFE, 0xED, 0xFA, 0xCF];
        Assert.Equal(ExecutablePlatform.MacOS, ExecutableFileClassifier.DetectPlatform(machO64));
    }

    /// <summary>Native installation probing propagates cancellation before inspecting a binary.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task IdentifyNativeAsync_Cancelled_ThrowsAsync()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _identifier.IdentifyNativeAsync(Path.Combine(_scratch, "missing"), cancellation.Token));
    }

    private string WriteBinary(string fileName, byte[] header)
    {
        var path = Path.Combine(_scratch, fileName);
        var payload = new byte[64];
        Buffer.BlockCopy(header, 0, payload, 0, header.Length);
        File.WriteAllBytes(path, payload);
        return path;
    }
}
