using GenHub.Core.Models.Manifest;
using System;
using System.IO;
using System.Text;
using System.Threading;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Synthetic matrix for <see cref="GameClientEntryDetector"/>: bundle layouts, flat
/// layouts, and every ambiguity fail-list.
/// </summary>
public sealed class GameClientEntryDetectorTests : IDisposable
{
    private static readonly byte[] MachOHeader = [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] ElfHeader = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00];

    private readonly string _payload = Path.Combine(Path.GetTempPath(), "GenHub_EntryDetector_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientEntryDetectorTests"/> class.
    /// </summary>
    public GameClientEntryDetectorTests()
    {
        Directory.CreateDirectory(_payload);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_payload))
        {
            Directory.Delete(_payload, recursive: true);
        }
    }

    /// <summary>
    /// Bundle layout: the plist names a nested launcher, but the known game binary beside
    /// it wins.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_PlistBinaryBesideKnownGameBinary_PrefersKnownBinary()
    {
        var macOs = CreateBundle("ZeroHour.app", declaredExecutable: "GeneralsLauncher");
        WriteBinary(Path.Combine(macOs, "GeneralsLauncher"), MachOHeader);
        WriteBinary(Path.Combine(macOs, "GeneralsOnlineZH"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("ZeroHour.app", "Contents", "MacOS", "GeneralsOnlineZH"), resolution.RelativePath);
    }

    /// <summary>
    /// macOS bundle layout: a plist-declared script wins outright over sibling binaries.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_PlistDeclaredScript_WinsOutright()
    {
        var macOs = CreateBundle("MacGame.app", declaredExecutable: "run.sh");
        File.WriteAllText(Path.Combine(macOs, "run.sh"), "#!/bin/sh\nexport FOO=1\n");
        WriteBinary(Path.Combine(macOs, "GameClient"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("MacGame.app", "Contents", "MacOS", "run.sh"), resolution.RelativePath);
    }

    /// <summary>
    /// A plist-declared binary with no known alternative wins as declared.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_PlistBinaryWithoutKnownAlternative_UsesDeclaredBinary()
    {
        var macOs = CreateBundle("Custom.app", declaredExecutable: "CustomClient");
        WriteBinary(Path.Combine(macOs, "CustomClient"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("Custom.app", "Contents", "MacOS", "CustomClient"), resolution.RelativePath);
    }

    /// <summary>
    /// Wrapper-stripping may remove the .app level, leaving a bare Contents tree that must
    /// still resolve.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_BareContentsTree_ResolvesKnownBinary()
    {
        var contents = Directory.CreateDirectory(Path.Combine(_payload, "Contents")).FullName;
        WritePlist(contents, "GeneralsLauncher");
        var macOs = Directory.CreateDirectory(Path.Combine(contents, "MacOS")).FullName;
        WriteBinary(Path.Combine(macOs, "GeneralsLauncher"), MachOHeader);
        WriteBinary(Path.Combine(macOs, "GeneralsOnlineZH"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("Contents", "MacOS", "GeneralsOnlineZH"), resolution.RelativePath);
    }

    /// <summary>
    /// A bundle without a plist resolves its single native binary.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_PlistLessBundleWithSingleNativeBinary_ResolvesBinary()
    {
        var macOs = CreateBundle("Plain.app", declaredExecutable: null);
        WriteBinary(Path.Combine(macOs, "PlainClient"), MachOHeader);
        File.WriteAllText(Path.Combine(macOs, "notes.txt"), "not a binary");

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("Plain.app", "Contents", "MacOS", "PlainClient"), resolution.RelativePath);
    }

    /// <summary>
    /// Multiple bundles in one payload must fail listing the bundles.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_MultipleBundles_FailsListingBundles()
    {
        var first = CreateBundle("First.app", declaredExecutable: null);
        WriteBinary(Path.Combine(first, "FirstClient"), MachOHeader);
        var second = CreateBundle("Second.app", declaredExecutable: null);
        WriteBinary(Path.Combine(second, "SecondClient"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.False(resolution.Success);
        Assert.Contains("First.app", string.Join(",", resolution.Candidates));
        Assert.Contains("Second.app", string.Join(",", resolution.Candidates));
    }

    /// <summary>
    /// A single flat native binary wins by magic bytes whatever its extension.
    /// </summary>
    /// <param name="fileName">The binary file name.</param>
    [Theory]
    [InlineData("GeneralsOnlineZH")]
    [InlineData("game.AppImage")]
    [InlineData("setup.bin")]
    [InlineData("installer.run")]
    public void DetectEntryPoint_SingleFlatNativeBinary_ResolvesByMagicBytes(string fileName)
    {
        WriteBinary(Path.Combine(_payload, fileName), ElfHeader);
        File.WriteAllText(Path.Combine(_payload, "README"), "documentation without magic bytes");

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(fileName, resolution.RelativePath);
    }

    /// <summary>
    /// Multiple flat natives resolve to the single known primary name.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_MultipleFlatNativesWithSingleKnownName_ResolvesKnownName()
    {
        WriteBinary(Path.Combine(_payload, "helper"), MachOHeader);
        WriteBinary(Path.Combine(_payload, "GeneralsOnlineZH"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal("GeneralsOnlineZH", resolution.RelativePath);
    }

    /// <summary>
    /// Multiple unknown flat natives must fail listing every candidate.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_MultipleUnknownFlatNatives_FailsListingCandidates()
    {
        WriteBinary(Path.Combine(_payload, "alpha"), MachOHeader);
        WriteBinary(Path.Combine(_payload, "beta"), ElfHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.False(resolution.Success);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    /// <summary>
    /// The Windows path is unchanged: a single .exe wins when no native binary exists.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_SingleWindowsExecutable_ResolvesExecutable()
    {
        File.WriteAllBytes(Path.Combine(_payload, "generalszh.exe"), [0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        File.WriteAllText(Path.Combine(_payload, "PatchZH.big"), "archive");

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal("generalszh.exe", resolution.RelativePath);
    }

    /// <summary>
    /// A single Flatpak bundle in the payload root is resolved as the launch entry point.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_SingleFlatpakBundle_ResolvesFlatpak()
    {
        File.WriteAllBytes(Path.Combine(_payload, "gameclient.flatpak"), [0x01, 0x02, 0x03]);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal("gameclient.flatpak", resolution.RelativePath);
    }

    /// <summary>
    /// A plist-declared traversal is rejected and never baked as an entry point.
    /// </summary>
    /// <param name="declared">The hostile declared executable.</param>
    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("/bin/evil")]
    [InlineData("sub/evil")]
    public void DetectEntryPoint_HostileDeclaredName_FallsBackToBinaries(string declared)
    {
        var macOs = CreateBundle("Game.app", declaredExecutable: declared);
        WriteBinary(Path.Combine(macOs, "RealClient"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("Game.app", "Contents", "MacOS", "RealClient"), resolution.RelativePath);
    }

    /// <summary>
    /// A nested execution declaration inside plug-in metadata cannot hijack the entry.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_NestedPlistKey_Ignored()
    {
        var macOs = CreateBundle("Game.app", declaredExecutable: "RealClient");
        WriteBinary(Path.Combine(macOs, "RealClient"), MachOHeader);
        var plist = string.Join(
            '\n',
            "<plist version=\"1.0\">",
            "<dict>",
            "<key>CFBundleExecutable</key><string>RealClient</string>",
            "<key>Nested</key><dict><key>CFBundleExecutable</key><string>Hijack</string></dict>",
            "</dict>",
            "</plist>");
        File.WriteAllText(Path.Combine(_payload, "Game.app", "Contents", "Info.plist"), plist);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("Game.app", "Contents", "MacOS", "RealClient"), resolution.RelativePath);
    }

    /// <summary>
    /// The plist fallback never reads framework-internal declarations.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_FrameworkPlistOnly_ExcludedFromFallback()
    {
        var macOs = CreateBundle("Game.app", declaredExecutable: "RealClient");
        WriteBinary(Path.Combine(macOs, "RealClient"), MachOHeader);
        WriteBinary(Path.Combine(macOs, "Helper"), MachOHeader);
        File.Delete(Path.Combine(_payload, "Game.app", "Contents", "Info.plist"));
        var framework = Directory.CreateDirectory(Path.Combine(_payload, "Game.app", "Contents", "Frameworks", "Helper.framework")).FullName;
        WritePlist(framework, "Helper");

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.False(resolution.Success);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    /// <summary>
    /// Cancellation aborts the payload scan cooperatively.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_CancelledToken_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => GameClientEntryDetector.DetectEntryPoint(_payload, cancelled.Token));
    }

    /// <summary>
    /// A bundle root resolves to its declared executable as an absolute path.
    /// </summary>
    [Fact]
    public void ResolveBundleExecutableAbsolute_BundleRoot_ResolvesDeclaredBinary()
    {
        var macOs = CreateBundle("Game.app", declaredExecutable: "GameClient");
        WriteBinary(Path.Combine(macOs, "GameClient"), MachOHeader);

        var resolved = GameClientEntryDetector.ResolveBundleExecutableAbsolute(Path.Combine(_payload, "Game.app"));

        Assert.Equal(Path.Combine(_payload, "Game.app", "Contents", "MacOS", "GameClient"), resolved);
    }

    /// <summary>
    /// An empty bundle and a non-bundle path resolve to null.
    /// </summary>
    [Fact]
    public void ResolveBundleExecutableAbsolute_Unresolvable_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_payload, "Empty.app"));

        Assert.Null(GameClientEntryDetector.ResolveBundleExecutableAbsolute(Path.Combine(_payload, "Empty.app")));
        Assert.Null(GameClientEntryDetector.ResolveBundleExecutableAbsolute(_payload));
    }

    /// <summary>
    /// Vendored shared libraries carry native magic but are never launch candidates: a
    /// single client binary beside .so/.dylib files still resolves.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_SingleBinaryWithVendoredLibraries_ResolvesBinary()
    {
        WriteBinary(Path.Combine(_payload, "gameclient"), ElfHeader);
        WriteBinary(Path.Combine(_payload, "libvendored.so"), ElfHeader);
        WriteBinary(Path.Combine(_payload, "libvendored.so.2"), ElfHeader);
        WriteBinary(Path.Combine(_payload, "libvendored.dylib"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal("gameclient", resolution.RelativePath);
    }

    /// <summary>
    /// Helper bundles nested inside the outer .app (Sparkle updaters, plug-ins) are part
    /// of the outer bundle, not second candidates.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_NestedHelperBundle_ResolvesOutermost()
    {
        var macOs = CreateBundle("Outer.app", declaredExecutable: "OuterClient");
        WriteBinary(Path.Combine(macOs, "OuterClient"), MachOHeader);

        var frameworks = Directory.CreateDirectory(Path.Combine(_payload, "Outer.app", "Contents", "Frameworks")).FullName;
        var helperMacOs = Directory.CreateDirectory(Path.Combine(frameworks, "Helper.app", "Contents", "MacOS")).FullName;
        WritePlist(Path.Combine(frameworks, "Helper.app", "Contents"), "Helper");
        WriteBinary(Path.Combine(helperMacOs, "Helper"), MachOHeader);

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.True(resolution.Success);
        Assert.Equal(Path.Combine("Outer.app", "Contents", "MacOS", "OuterClient"), resolution.RelativePath);
    }

    /// <summary>
    /// Root-level scripts are never auto-picked: without a bundle, a lone script leaves
    /// the payload without a launchable file.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_RootLevelScriptOnly_FailsWithoutLaunchableFile()
    {
        File.WriteAllText(Path.Combine(_payload, "run.sh"), "#!/bin/sh\necho hi\n");

        var resolution = GameClientEntryDetector.DetectEntryPoint(_payload);

        Assert.False(resolution.Success);
        Assert.Empty(resolution.Candidates);
    }

    /// <summary>
    /// A missing payload directory fails instead of throwing.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_MissingDirectory_Fails()
    {
        var resolution = GameClientEntryDetector.DetectEntryPoint(Path.Combine(_payload, "absent"));

        Assert.False(resolution.Success);
    }

    private static void WritePlist(string contentsDirectory, string executableName)
    {
        var plist = new StringBuilder()
            .AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .AppendLine("<plist version=\"1.0\">")
            .AppendLine("<dict>")
            .AppendLine("    <key>CFBundleExecutable</key>")
            .AppendLine($"    <string>{executableName}</string>")
            .AppendLine("</dict>")
            .AppendLine("</plist>")
            .ToString();
        File.WriteAllText(Path.Combine(contentsDirectory, "Info.plist"), plist);
    }

    private static void WriteBinary(string path, byte[] header)
    {
        var payload = new byte[64];
        Buffer.BlockCopy(header, 0, payload, 0, header.Length);
        File.WriteAllBytes(path, payload);
    }

    private string CreateBundle(string bundleName, string? declaredExecutable)
    {
        var bundle = Directory.CreateDirectory(Path.Combine(_payload, bundleName)).FullName;
        var contents = Directory.CreateDirectory(Path.Combine(bundle, "Contents")).FullName;
        if (declaredExecutable is not null)
        {
            WritePlist(contents, declaredExecutable);
        }

        return Directory.CreateDirectory(Path.Combine(contents, "MacOS")).FullName;
    }

    /// <summary>
    /// Verifies that safe archives return the detected entry executable.
    /// </summary>
    [Fact]
    public void DetectEntryPointFromArchive_SafeArchive_ReturnsEntry()
    {
        var zipPath = Path.Combine(_payload, "safe.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("bin/generals.exe");
            using var stream = entry.Open();
            stream.Write([0x4D, 0x5A]);
        }

        var entryPoint = GameClientEntryDetector.DetectEntryPointFromArchive(zipPath);
        Assert.Equal("bin/generals.exe", entryPoint);
    }

    /// <summary>
    /// Verifies that entries with path traversal sequences or rooted paths are rejected.
    /// </summary>
    [Fact]
    public void DetectEntryPointFromArchive_ZipSlipOrRootedEntries_AreIgnored()
    {
        var zipPath = Path.Combine(_payload, "unsafe.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("../generals.exe");
            using var stream1 = entry1.Open();
            stream1.Write([0x4D, 0x5A]);

            var entry2 = archive.CreateEntry("/generals.exe");
            using var stream2 = entry2.Open();
            stream2.Write([0x4D, 0x5A]);
        }

        var entryPoint = GameClientEntryDetector.DetectEntryPointFromArchive(zipPath);
        Assert.Null(entryPoint);
    }
}
