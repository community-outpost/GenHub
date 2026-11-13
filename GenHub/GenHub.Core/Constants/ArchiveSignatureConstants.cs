using System;

namespace GenHub.Core.Constants;

/// <summary>
/// Shared archive signatures used for payload identification.
/// </summary>
public static class ArchiveSignatureConstants
{
    /// <summary>Gets the ZIP local file header signature.</summary>
    public static ReadOnlySpan<byte> ZipLocalFileHeader => [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Gets the ZIP end of central directory signature.</summary>
    public static ReadOnlySpan<byte> ZipEndOfCentralDirectory => [0x50, 0x4B, 0x05, 0x06];

    /// <summary>Gets the 7-Zip signature.</summary>
    public static ReadOnlySpan<byte> SevenZip => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    /// <summary>Gets the RAR signature.</summary>
    public static ReadOnlySpan<byte> Rar => [0x52, 0x61, 0x72, 0x21];

    /// <summary>Gets the GZIP signature.</summary>
    public static ReadOnlySpan<byte> Gzip => [0x1F, 0x8B];

    /// <summary>Gets the BZip2 signature.</summary>
    public static ReadOnlySpan<byte> Bzip2 => [0x42, 0x5A, 0x68];

    /// <summary>Gets the XZ signature.</summary>
    public static ReadOnlySpan<byte> Xz => [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];
}
