using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for <see cref="CompressedImageToTgaConverter"/>, focused on how it behaves
/// when the libheif native library is unavailable for the current runtime.
/// </summary>
public class CompressedImageToTgaConverterTests : IDisposable
{
    /// <summary>
    /// A minimal valid AVIF (8x8 solid colour). Embedded as base64 so the test needs no
    /// external tooling and runs identically on every platform.
    /// </summary>
    private const string TinyAvifBase64 =
        "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAADybWV0YQAAAAAAAAAoaGRscgAAAAAAAAAA"
        + "cGljdAAAAAAAAAAAAAAAAGxpYmF2aWYAAAAADnBpdG0AAAAAAAEAAAAeaWxvYwAAAABEAAABAAEAAAAB"
        + "AAABGgAAAB8AAAAoaWluZgAAAAAAAQAAABppbmZlAgAAAAABAABhdjAxQ29sb3IAAAAAamlwcnAAAABL"
        + "aXBjbwAAABRpc3BlAAAAAAAAAAgAAAAIAAAAEHBpeGkAAAAAAwgICAAAAAxhdjFDgQAMAAAAABNjb2xy"
        + "bmNseAABAA0ABgAAAAAXaXBtYQAAAAAAAAABAAEEAQKDBAAAACdtZGF0EgAKCBgIv2CAhoMCMhEXwAkk"
        + "kkQAALATVO0wKFrK0A==";

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"genhub-avif-{Guid.NewGuid():N}");

    private readonly CompressedImageToTgaConverter _converter =
        new(NullLogger<CompressedImageToTgaConverter>.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressedImageToTgaConverterTests"/> class.
    /// </summary>
    public CompressedImageToTgaConverterTests()
    {
        Directory.CreateDirectory(_tempDir);
        typeof(CompressedImageToTgaConverter)
            .GetField("_avifCapabilityState", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, 0);
    }

    /// <summary>
    /// Converting a single AVIF must either succeed, or fail with a
    /// <see cref="PlatformNotSupportedException"/> naming the runtime.
    /// <para>
    /// It must never surface the raw <see cref="DllNotFoundException"/>. That exception
    /// says "Unable to load shared library 'libheif'", which tells a user nothing about
    /// what they did or what to do. This is the failure that would otherwise reach the
    /// content pipeline on any runtime LibHeif.Native does not ship assets for.
    /// </para>
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertFileAsync_AvifOnUnsupportedRuntime_ThrowsPlatformNotSupportedAsync()
    {
        var source = Path.Combine(_tempDir, "texture.avif");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(TinyAvifBase64));
        var destination = Path.Combine(_tempDir, "texture.tga");

        var thrown = await Record.ExceptionAsync(
            () => _converter.ConvertFileAsync(source, destination));

        if (NativeAvifAssetsExpected)
        {
            Assert.Null(thrown);
            Assert.True(File.Exists(destination), "The native AVIF package produced no TGA.");
            return;
        }

        if (thrown is null)
        {
            // libheif is present on this machine, so conversion is expected to work.
            Assert.True(File.Exists(destination), "Conversion reported success but wrote no TGA.");
            return;
        }

        Assert.IsType<PlatformNotSupportedException>(thrown);
        Assert.Contains("libheif", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A directory containing an undecodable AVIF must not lose the AVIF. Deleting it
    /// would destroy content the user could still convert on a runtime that has libheif.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertDirectoryAsync_UnconvertibleAvif_IsLeftOnDiskAsync()
    {
        var source = Path.Combine(_tempDir, "texture.avif");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(TinyAvifBase64));

        await _converter.ConvertDirectoryAsync(_tempDir);

        var tga = Path.Combine(_tempDir, "texture.tga");
        var convertedSuccessfully = File.Exists(tga);

        Assert.True(
            convertedSuccessfully || File.Exists(source),
            "The AVIF was neither converted nor preserved, so the source content was lost.");
    }

    /// <summary>
    /// Concurrent first-use probes must agree on AVIF availability without exposing
    /// native loader failures to callers.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertFileAsync_ConcurrentAvifProbes_DoNotExposeNativeLoaderFailureAsync()
    {
        var tasks = Enumerable.Range(0, 8)
            .Select(
                async index =>
                {
                    var source = Path.Combine(_tempDir, $"texture-{index}.avif");
                    var destination = Path.Combine(_tempDir, $"texture-{index}.tga");
                    await File.WriteAllBytesAsync(source, Convert.FromBase64String(TinyAvifBase64));
                    return await Record.ExceptionAsync(
                        () => _converter.ConvertFileAsync(source, destination));
                });

        var exceptions = await Task.WhenAll(tasks);

        Assert.DoesNotContain(exceptions, exception => exception is DllNotFoundException);
        Assert.All(
            exceptions.Where(exception => exception is not null),
            exception => Assert.IsType<PlatformNotSupportedException>(exception));
    }

    /// <summary>
    /// Releases the temporary directory used by these tests.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static bool NativeAvifAssetsExpected =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
        && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());
}
