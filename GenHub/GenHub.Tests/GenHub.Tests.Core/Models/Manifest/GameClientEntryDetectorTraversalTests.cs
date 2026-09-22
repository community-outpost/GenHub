using GenHub.Core.Models.Manifest;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Verifies payload entry detection rejects executables that escape the payload
/// through intermediate directory links, while tolerating interior links that
/// legitimate bundle layouts use.
/// </summary>
public sealed class GameClientEntryDetectorTraversalTests : IDisposable
{
    private static readonly byte[] ElfHeader = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00];

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_Traversal_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientEntryDetectorTraversalTests"/> class.
    /// </summary>
    public GameClientEntryDetectorTraversalTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// A native binary reachable only through a directory link pointing outside the
    /// payload is not accepted as the entry point, even though its lexical path
    /// looks contained.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_EscapingIntermediateLink_RejectsOutsideExecutable()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_tempRoot, "outside")).FullName;
        File.WriteAllBytes(Path.Combine(outside, "evil"), ElfHeader);
        var payload = Directory.CreateDirectory(Path.Combine(_tempRoot, "payload")).FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(payload, "linkdir"), outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        var resolution = GameClientEntryDetector.DetectEntryPoint(payload);

        Assert.False(resolution.Success);
    }

    /// <summary>
    /// Interior links that stay inside the payload keep resolving: a symlinked
    /// framework layout must not break bundle entry detection.
    /// </summary>
    [Fact]
    public void DetectEntryPoint_InteriorLink_PreservesInsideExecutable()
    {
        var payload = Directory.CreateDirectory(Path.Combine(_tempRoot, "payload")).FullName;
        var macOs = Directory.CreateDirectory(Path.Combine(payload, "Game.app", "Contents", "MacOS")).FullName;
        File.WriteAllBytes(Path.Combine(macOs, "RealClient"), ElfHeader);
        File.WriteAllText(
            Path.Combine(payload, "Game.app", "Contents", "Info.plist"),
            string.Join(
                '\n',
                "<plist version=\"1.0\">",
                "<dict>",
                "<key>CFBundleExecutable</key><string>RealClient</string>",
                "</dict>",
                "</plist>"));
        var versions = Directory.CreateDirectory(Path.Combine(payload, "Game.app", "Contents", "Frameworks", "Helper.framework", "Versions", "A")).FullName;
        File.WriteAllText(Path.Combine(versions, "data.txt"), "framework payload");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(versions)!, "Current"), "A");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        var resolution = GameClientEntryDetector.DetectEntryPoint(payload);

        Assert.True(resolution.Success, resolution.ToString());
        Assert.Equal(Path.Combine("Game.app", "Contents", "MacOS", "RealClient"), resolution.RelativePath);
    }
}
