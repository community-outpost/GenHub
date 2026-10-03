using GenHub.Features.AppUpdate.Services;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Unit tests for <see cref="VelopackBundleExtractor"/>.
/// </summary>
public class VelopackBundleExtractorTests : IDisposable
{
    private static readonly byte[] SquirrelBundleSignature =
    [
        0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
        0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
        0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
        0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d,
    ];

    private static readonly byte[] FakeZipHeader = [0x50, 0x4b, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];

    private readonly string _tempDirectory;

    public VelopackBundleExtractorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"genhub-bundle-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup failures
        }
    }

    [Fact]
    public void TryExtractBundle_ValidBundle_ExtractsExactNupkg()
    {
        var exePath = Path.Combine(_tempDirectory, "Setup.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "Extracted.nupkg");

        var payload = new byte[1024];
        Array.Copy(FakeZipHeader, payload, FakeZipHeader.Length);
        for (var i = FakeZipHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        CreateSyntheticBundle(exePath, payload, prefixPaddingBytes: 500);

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.True(success);
        Assert.Equal(payload.Length, extractedBytes);
        Assert.True(File.Exists(nupkgPath));
        var extractedData = File.ReadAllBytes(nupkgPath);
        Assert.Equal(payload, extractedData);
    }

    [Fact]
    public void TryExtractBundle_MissingSignature_ReturnsFalse()
    {
        var exePath = Path.Combine(_tempDirectory, "NotABundle.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "Extracted.nupkg");

        File.WriteAllBytes(exePath, new byte[2048]);

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.False(success);
        Assert.Equal(0, extractedBytes);
        Assert.False(File.Exists(nupkgPath));
    }

    [Fact]
    public void TryExtractBundle_InvalidOffset_ReturnsFalse()
    {
        var exePath = Path.Combine(_tempDirectory, "InvalidOffset.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "Extracted.nupkg");

        using (var fs = File.Create(exePath))
        using (var writer = new BinaryWriter(fs))
        {
            writer.Write(new byte[100]); // padding
            writer.Write(-50L); // invalid negative offset
            writer.Write(100L); // length
            writer.Write(SquirrelBundleSignature);
        }

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.False(success);
        Assert.Equal(0, extractedBytes);
    }

    [Fact]
    public void TryExtractBundle_InvalidLength_ReturnsFalse()
    {
        var exePath = Path.Combine(_tempDirectory, "InvalidLength.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "Extracted.nupkg");

        using (var fs = File.Create(exePath))
        using (var writer = new BinaryWriter(fs))
        {
            writer.Write(new byte[100]); // padding
            writer.Write(10L); // offset
            writer.Write(1000000L); // length beyond file stream
            writer.Write(SquirrelBundleSignature);
        }

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.False(success);
        Assert.Equal(0, extractedBytes);
    }

    [Fact]
    public void TryExtractBundle_NonZipHeader_ReturnsFalse()
    {
        var exePath = Path.Combine(_tempDirectory, "CorruptedZip.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "Extracted.nupkg");

        var payload = Encoding.UTF8.GetBytes("THIS IS NOT A VALID ZIP FILE HEADER AT ALL");

        CreateSyntheticBundle(exePath, payload, prefixPaddingBytes: 300);

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.False(success);
        Assert.Equal(0, extractedBytes);
    }

    [Fact]
    public void TryExtractBundle_NonExistentFile_ReturnsFalse()
    {
        var exePath = Path.Combine(_tempDirectory, "DoesNotExist.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "Extracted.nupkg");

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.False(success);
        Assert.Equal(0, extractedBytes);
    }

    [Fact]
    public void TryExtractBundle_SignatureSpansBufferBoundary_ExtractsSuccessfully()
    {
        var exePath = Path.Combine(_tempDirectory, "BoundarySetup.exe");
        var nupkgPath = Path.Combine(_tempDirectory, "BoundaryExtracted.nupkg");

        var payload = new byte[512];
        Array.Copy(FakeZipHeader, payload, FakeZipHeader.Length);

        // Buffer size in VelopackBundleExtractor is 64KB (65536 bytes).
        // Position signature so that it straddles the 65536-byte mark.
        var targetSignaturePos = 65536 - 10;
        var prefixPadding = targetSignaturePos - 16; // 16 bytes for offset and length

        CreateSyntheticBundle(exePath, payload, prefixPaddingBytes: prefixPadding);

        var success = VelopackBundleExtractor.TryExtractBundle(exePath, nupkgPath, out var extractedBytes);

        Assert.True(success);
        Assert.Equal(payload.Length, extractedBytes);
        Assert.True(File.Exists(nupkgPath));
        var extractedData = File.ReadAllBytes(nupkgPath);
        Assert.Equal(payload, extractedData);
    }

    private static void CreateSyntheticBundle(string exePath, byte[] payload, int prefixPaddingBytes)
    {
        using var fs = File.Create(exePath);
        using var writer = new BinaryWriter(fs);

        // 1. Prefix padding (simulates PE executable code)
        writer.Write(new byte[prefixPaddingBytes]);

        // 2. Header: offset (long), length (long), signature (32 bytes)
        var offsetPosition = fs.Position;
        var placeholder = new byte[16 + SquirrelBundleSignature.Length];
        writer.Write(placeholder);

        // 3. Middle padding
        writer.Write(new byte[200]);

        // 4. Payload (simulates embedded .nupkg)
        var payloadOffset = fs.Position;
        writer.Write(payload);

        // 5. Seek back and write real offset and length into the header
        fs.Seek(offsetPosition, SeekOrigin.Begin);
        writer.Write(payloadOffset);
        writer.Write((long)payload.Length);
        writer.Write(SquirrelBundleSignature);
    }
}
