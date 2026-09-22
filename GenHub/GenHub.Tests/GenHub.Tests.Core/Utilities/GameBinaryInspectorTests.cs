using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Unit tests for <see cref="GameBinaryInspector"/>.
/// </summary>
public sealed class GameBinaryInspectorTests : IDisposable
{
    private static readonly byte[] TitleBytes = Encoding.ASCII.GetBytes(GameBinaryConstants.ZeroHourTitle);
    private static readonly byte[] TitleUtf16Bytes = Encoding.Unicode.GetBytes(GameBinaryConstants.ZeroHourTitle);
    private static readonly byte[] ChallengeBytes = Encoding.ASCII.GetBytes(GameBinaryConstants.ChallengeMenuMarker);

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "GenHub_BinaryInspect_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="GameBinaryInspectorTests"/> class.
    /// </summary>
    public GameBinaryInspectorTests()
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
    /// A 32-bit engine with the Zero Hour title and menu token reads as a Zero Hour engine.
    /// </summary>
    [Fact]
    public void InspectBytes_EngineWithZeroHourMarkers_IdentifiesZeroHour()
    {
        var bytes = CraftPe(is64Bit: false, TextPayload(), MarkerPayload(includeTitle: true, includeChallenge: true));

        var verdict = GameBinaryInspector.InspectBytes("GeneralsXZH.exe", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.ZeroHour, verdict.GameType);
    }

    /// <summary>
    /// The title alone (launcher-style) proves nothing without the menu token.
    /// </summary>
    [Fact]
    public void InspectBytes_TitleWithoutChallengeMenu_ReturnsUnknown()
    {
        var bytes = CraftPe(is64Bit: false, TextPayload(), MarkerPayload(includeTitle: true, includeChallenge: false));

        var verdict = GameBinaryInspector.InspectBytes("GeneralsLauncher", bytes);

        Assert.Equal(GameBinaryRole.Unknown, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// The menu token alone proves nothing without the title.
    /// </summary>
    [Fact]
    public void InspectBytes_ChallengeMenuWithoutTitle_ReturnsUnknown()
    {
        var bytes = CraftPe(is64Bit: false, TextPayload(), MarkerPayload(includeTitle: false, includeChallenge: true));

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Unknown, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// A marker-less 32-bit binary with encrypted .text reads as a packed stub.
    /// </summary>
    [Fact]
    public void InspectBytes_PackedTextWithoutMarkers_IdentifiesStub()
    {
        var random = new Random(42);
        var text = new byte[GameBinaryConstants.EntropySampleSize];
        random.NextBytes(text);
        var bytes = CraftPe(is64Bit: false, text, MarkerPayload(includeTitle: false, includeChallenge: false));

        var verdict = GameBinaryInspector.InspectBytes("generals.exe", bytes);

        Assert.Equal(GameBinaryRole.PackedStub, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// .NET runtime markers identify a launcher even when title strings are present.
    /// </summary>
    [Fact]
    public void InspectBytes_DotNetMarkers_IdentifiesLauncher()
    {
        var payload = MarkerPayload(includeTitle: true, includeChallenge: true);
        var withRuntime = new byte[payload.Length + 16];
        Buffer.BlockCopy(payload, 0, withRuntime, 0, payload.Length);
        Encoding.ASCII.GetBytes("mscorlib").CopyTo(withRuntime, payload.Length);
        var bytes = CraftPe(is64Bit: false, TextPayload(), withRuntime);

        var verdict = GameBinaryInspector.InspectBytes("GeneralsOnlineZH.exe", bytes);

        Assert.Equal(GameBinaryRole.DotNetLauncher, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// Tools are excluded by file name before sniffing, even with full engine markers.
    /// </summary>
    [Fact]
    public void InspectBytes_ToolFileName_SkipsSniffing()
    {
        var bytes = CraftPe(is64Bit: false, TextPayload(), MarkerPayload(includeTitle: true, includeChallenge: true));

        var verdict = GameBinaryInspector.InspectBytes("WorldBuilder.exe", bytes);

        Assert.Equal(GameBinaryRole.Tool, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// Installers are excluded by file name before sniffing.
    /// </summary>
    /// <param name="fileName">The installer file name.</param>
    [Theory]
    [InlineData("GeneralsOnline-Setup.exe")]
    [InlineData("ZH-Installer.exe")]
    public void InspectBytes_InstallerFileName_SkipsSniffing(string fileName)
    {
        var bytes = CraftPe(is64Bit: false, TextPayload(), MarkerPayload(includeTitle: true, includeChallenge: true));

        var verdict = GameBinaryInspector.InspectBytes(fileName, bytes);

        Assert.Equal(GameBinaryRole.Installer, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// The file-name gates apply to Windows executables only: a Unix binary named
    /// like an installer still sniffs normally.
    /// </summary>
    [Fact]
    public void InspectBytes_UnixSetupName_SniffsNormally()
    {
        var bytes = CraftPe(is64Bit: false, TextPayload(), MarkerPayload(includeTitle: true, includeChallenge: true));

        var verdict = GameBinaryInspector.InspectBytes("setup.bin", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.ZeroHour, verdict.GameType);
    }

    /// <summary>
    /// A token glued to a preceding word character is not a menu marker.
    /// </summary>
    [Fact]
    public void InspectBytes_ChallengeMenuWithWordPrefix_ReturnsUnknown()
    {
        var payload = Concat(TitleBytes, [(byte)' '], Encoding.ASCII.GetBytes("XChallengeMenu"));
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Unknown, verdict.Role);
    }

    /// <summary>
    /// Trailing capitals are part of the real menu table entries and still match.
    /// </summary>
    [Fact]
    public void InspectBytes_ChallengeMenuWithSystemSuffix_IdentifiesZeroHour()
    {
        var payload = Concat(TitleBytes, [(byte)' '], Encoding.ASCII.GetBytes("ChallengeMenuSystem"));
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.ZeroHour, verdict.GameType);
    }

    /// <summary>
    /// The UTF-16LE title copy counts the same as the ANSI copy.
    /// </summary>
    [Fact]
    public void InspectBytes_Utf16Title_IdentifiesZeroHour()
    {
        var payload = Concat(TitleUtf16Bytes, ChallengeBytes);
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.ZeroHour, verdict.GameType);
    }

    /// <summary>
    /// The ampersand engine token with no Zero Hour markers reads as Generals.
    /// </summary>
    /// <param name="token">The token casing to embed.</param>
    [Theory]
    [InlineData("c&cgenerals")]
    [InlineData("C&CGENERALS")]
    [InlineData("C&cGeNeRaLs")]
    public void InspectBytes_GeneralsToken_IdentifiesGenerals(string token)
    {
        var payload = Concat([(byte)' '], Encoding.ASCII.GetBytes(token), [(byte)' ']);
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.Generals, verdict.GameType);
    }

    /// <summary>
    /// The bare forms without the ampersand appear inside Zero Hour binaries and prove nothing.
    /// </summary>
    /// <param name="token">The bare token to embed.</param>
    [Theory]
    [InlineData("ccgenerals")]
    [InlineData("ccgenzh")]
    public void InspectBytes_BareTokenWithoutAmpersand_ReturnsUnknown(string token)
    {
        var payload = Concat([(byte)' '], Encoding.ASCII.GetBytes(token), [(byte)' ']);
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Unknown, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// Full Zero Hour evidence plus the Generals token is conflicting, not Generals.
    /// </summary>
    [Fact]
    public void InspectBytes_ConflictingEvidence_ReturnsUnknownEngine()
    {
        var payload = Concat(TitleBytes, [(byte)' '], ChallengeBytes, [(byte)' '], Encoding.ASCII.GetBytes("c&cgenerals"));
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// The Generals token plus a lone Zero Hour marker is still conflicting evidence.
    /// </summary>
    [Fact]
    public void InspectBytes_GeneralsTokenWithLoneChallengeMenu_ReturnsUnknown()
    {
        var payload = Concat([(byte)' '], ChallengeBytes, [(byte)' '], Encoding.ASCII.GetBytes("c&cgenerals"));
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameType.Unknown, verdict.GameType);
    }

    /// <summary>
    /// A token glued to a preceding word character is not a Generals marker.
    /// </summary>
    [Fact]
    public void InspectBytes_GeneralsTokenWithWordPrefix_ReturnsUnknown()
    {
        var payload = Concat(Encoding.ASCII.GetBytes("Xc&cgenerals"), [(byte)' ']);
        var bytes = CraftPe(is64Bit: false, TextPayload(), payload);

        var verdict = GameBinaryInspector.InspectBytes("client.exe", bytes);

        Assert.Equal(GameBinaryRole.Unknown, verdict.Role);
    }

    /// <summary>
    /// ELF and Mach-O engines sniff with the same markers as PE engines.
    /// </summary>
    [Fact]
    public void InspectBytes_ElfMagicWithMarkers_IdentifiesZeroHour()
    {
        AssertNativeMagicIdentifiesZeroHour([0x7F, (byte)'E', (byte)'L', (byte)'F']);
    }

    /// <summary>
    /// Mach-O engines sniff with the same markers as PE engines.
    /// </summary>
    [Fact]
    public void InspectBytes_MachOMagicWithMarkers_IdentifiesZeroHour()
    {
        AssertNativeMagicIdentifiesZeroHour([0xCF, 0xFA, 0xED, 0xFE]);
    }

    /// <summary>
    /// Scripts and text are not executable bytes.
    /// </summary>
    [Fact]
    public void InspectBytes_TextFile_ReturnsNotExecutable()
    {
        var verdict = GameBinaryInspector.InspectBytes("run.sh", Encoding.ASCII.GetBytes("#!/bin/bash\necho hi\n"));

        Assert.Equal(GameBinaryRole.NotExecutable, verdict.Role);
    }

    /// <summary>
    /// Markers spanning a chunk seam are found exactly once by the streaming scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InspectAsync_MarkersAcrossChunkSeam_IdentifiesZeroHourAsync()
    {
        var path = Path.Combine(_scratch, "seamclient");
        var prefix = new byte[GameBinaryConstants.ScanChunkSize - 20];
        for (var i = 0; i < prefix.Length; i++)
        {
            prefix[i] = (byte)'A';
        }

        var payload = Concat(prefix, TitleBytes, [(byte)' '], ChallengeBytes);
        var bytes = new byte[payload.Length + 4];
        bytes[0] = 0x7F;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        Buffer.BlockCopy(payload, 0, bytes, 4, payload.Length);
        await File.WriteAllBytesAsync(path, bytes);

        var result = await GameBinaryInspector.InspectAsync(path, CancellationToken.None);

        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(GameBinaryRole.Engine, result.Data.Role);
        Assert.Equal(GameType.ZeroHour, result.Data.GameType);
    }

    /// <summary>
    /// Missing paths and directories fail instead of yielding a verdict.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InspectAsync_MissingPath_FailsAsync()
    {
        var missing = await GameBinaryInspector.InspectAsync(Path.Combine(_scratch, "absent.exe"), CancellationToken.None);
        var directory = await GameBinaryInspector.InspectAsync(_scratch, CancellationToken.None);

        Assert.False(missing.Success);
        Assert.False(directory.Success);
    }

    /// <summary>
    /// Cancellation aborts the scan cooperatively.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InspectAsync_CancelledToken_ThrowsAsync()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => GameBinaryInspector.InspectAsync(Path.Combine(_scratch, "client.exe"), cancelled.Token));
    }

    /// <summary>
    /// Verifies PE32+ (64-bit) executables are rejected by the packed-stub entropy parser,
    /// which specifically targets 32-bit legacy game engines.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InspectAsync_Pe32PlusWithHighEntropyText_DoesNotClassifyAsPackedStubAsync()
    {
        var random = new Random(42);
        var text = new byte[GameBinaryConstants.EntropySampleSize];
        random.NextBytes(text);

        var file = CraftPe(is64Bit: true, text, []);
        var path = Path.Combine(_scratch, "game64.exe");
        await File.WriteAllBytesAsync(path, file);

        var result = await GameBinaryInspector.InspectAsync(path);

        Assert.True(result.Success);
        Assert.NotEqual(GameBinaryRole.PackedStub, result.Data!.Role);
    }

    /// <summary>
    /// When a PE carries a .textbss section (raw pointer 0) before a real .text section,
    /// inspection continues scanning and resolves the valid .text section.
    /// </summary>
    [Fact]
    public void InspectBytes_TextBssBeforeValidTextSection_ScansSubsequentSection()
    {
        const int peOffset = 64;
        const int optionalSize = 224;
        const int rawPointer = 512;
        var tableOffset = peOffset + 24 + optionalSize;

        var text = TextPayload();
        var markers = MarkerPayload(includeTitle: true, includeChallenge: true);

        var file = new byte[rawPointer + text.Length + markers.Length];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(file, 0x3C);

        file[peOffset] = (byte)'P';
        file[peOffset + 1] = (byte)'E';
        const ushort numberOfSections = 2;
        BitConverter.GetBytes(numberOfSections).CopyTo(file, peOffset + 6);
        BitConverter.GetBytes((ushort)optionalSize).CopyTo(file, peOffset + 20);
        const ushort pe32Magic = 0x10B;
        BitConverter.GetBytes(pe32Magic).CopyTo(file, peOffset + 24);

        Encoding.ASCII.GetBytes(".textbss").CopyTo(file, tableOffset);
        BitConverter.GetBytes(0u).CopyTo(file, tableOffset + 16);
        BitConverter.GetBytes(0u).CopyTo(file, tableOffset + 20);

        var section2Offset = tableOffset + GameBinaryConstants.PeSectionHeaderStride;
        Encoding.ASCII.GetBytes(".text").CopyTo(file, section2Offset);
        BitConverter.GetBytes((uint)text.Length).CopyTo(file, section2Offset + 16);
        BitConverter.GetBytes((uint)rawPointer).CopyTo(file, section2Offset + 20);

        Buffer.BlockCopy(text, 0, file, rawPointer, text.Length);
        Buffer.BlockCopy(markers, 0, file, rawPointer + text.Length, markers.Length);

        var verdict = GameBinaryInspector.InspectBytes("GeneralsXZH.exe", file);
        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.ZeroHour, verdict.GameType);
    }

    private static void AssertNativeMagicIdentifiesZeroHour(byte[] magic)
    {
        var bytes = Concat(magic, new byte[64], TitleBytes, [(byte)' '], ChallengeBytes);

        var verdict = GameBinaryInspector.InspectBytes("gameclient", bytes);

        Assert.Equal(GameBinaryRole.Engine, verdict.Role);
        Assert.Equal(GameType.ZeroHour, verdict.GameType);
    }

    private static byte[] TextPayload()
    {
        var text = new byte[GameBinaryConstants.EntropySampleSize];
        for (var i = 0; i < text.Length; i++)
        {
            text[i] = (byte)'A';
        }

        return text;
    }

    private static byte[] MarkerPayload(bool includeTitle, bool includeChallenge)
    {
        var parts = new List<byte[]>();
        if (includeTitle)
        {
            parts.Add(TitleBytes);
            parts.Add([(byte)' ']);
        }

        if (includeChallenge)
        {
            parts.Add(ChallengeBytes);
        }

        return Concat(parts.ToArray());
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var merged = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, merged, offset, part.Length);
            offset += part.Length;
        }

        return merged;
    }

    private static byte[] CraftPe(bool is64Bit, byte[] text, byte[] markers)
    {
        const int peOffset = 64;
        const int optionalSize = 224;
        const int rawPointer = 512;
        var tableOffset = peOffset + 24 + optionalSize;

        var file = new byte[rawPointer + text.Length + markers.Length];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(file, 0x3C);

        file[peOffset] = (byte)'P';
        file[peOffset + 1] = (byte)'E';
        BitConverter.GetBytes((ushort)1).CopyTo(file, peOffset + 6);
        BitConverter.GetBytes((ushort)optionalSize).CopyTo(file, peOffset + 20);
        BitConverter.GetBytes((ushort)(is64Bit ? 0x20B : 0x10B)).CopyTo(file, peOffset + 24);

        Encoding.ASCII.GetBytes(".text").CopyTo(file, tableOffset);
        BitConverter.GetBytes((uint)text.Length).CopyTo(file, tableOffset + 16);
        BitConverter.GetBytes((uint)rawPointer).CopyTo(file, tableOffset + 20);
        Buffer.BlockCopy(text, 0, file, rawPointer, text.Length);
        Buffer.BlockCopy(markers, 0, file, rawPointer + text.Length, markers.Length);
        return file;
    }
}
