using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace GenHub.Features.Content.Services.Common;

/// <summary>
/// Service for safely extracting archives and normalizing payload directory structures for game workspaces.
/// </summary>
public class ArchivePayloadProcessor(ILogger<ArchivePayloadProcessor> logger) : IArchivePayloadProcessor
{
    private const int MaxNestedExtractionDepth = 5;
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] RarSignature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];
    private static readonly byte[] SmartInstallMakerSignature = [0x77, 0x77, 0x67, 0x54, 0x29, 0x48, 0x35, 0x14];

    /// <inheritdoc />
    public Task ExtractArchivesSafelyAsync(
        string extractedDirectory,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(extractedDirectory))
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            () =>
            {
                var depth = 0;
                while (depth < MaxNestedExtractionDepth)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    depth++;

                    var archiveFiles = FindArchiveFiles(extractedDirectory, contentType);
                    if (archiveFiles.Count == 0)
                    {
                        break;
                    }

                    logger.LogInformation(
                        "Found {Count} archive(s) to extract in payload directory: {Directory} (pass {Pass})",
                        archiveFiles.Count,
                        extractedDirectory,
                        depth);

                    foreach (var archivePath in archiveFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        EnsureValidArchivePayload(archivePath);
                        logger.LogInformation("Extracting archive safely: {ArchivePath}", archivePath);

                        ExtractSingleArchive(archivePath, extractedDirectory, cancellationToken);
                        File.Delete(archivePath);
                        logger.LogInformation("Extracted archive and removed archive source: {ArchivePath}", archivePath);
                    }
                }

                var remainingArchives = FindArchiveFiles(extractedDirectory, contentType);
                if (remainingArchives.Count > 0)
                {
                    throw new InvalidDataException(
                        $"Payload contains nested archives exceeding maximum extraction depth of {MaxNestedExtractionDepth}: {string.Join(", ", remainingArchives.Select(Path.GetFileName))}");
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task NormalizeDirectoryStructureAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(extractedDirectory))
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Purge system junk files and folders
                PurgeSystemJunk(extractedDirectory);

                // 2. Iteratively strip single wrapper directories
                StripSingleWrapperDirectories(extractedDirectory, contentType, cancellationToken);

                // 3. Handle game-specific subdirectories (e.g. ZH, Zero Hour, Generals, CCG)
                RouteGameSpecificSubdirectories(extractedDirectory, targetGame, cancellationToken);

                // 4. Heuristic root content detection (single mod directory alongside loose documentation files)
                ReconcileContentRootWithDocumentation(extractedDirectory, contentType, cancellationToken);

                // 5. Normalize inactive .gib mod archive files to .big
                NormalizeGibExtensions(extractedDirectory, contentType);

                // 6. Cleanup empty directories
                CleanupEmptyDirectories(extractedDirectory);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ProcessPayloadAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        await ExtractArchivesSafelyAsync(extractedDirectory, contentType, cancellationToken);
        await NormalizeDirectoryStructureAsync(extractedDirectory, contentType, targetGame, cancellationToken);
    }

    private static bool ShouldAttemptExecutableExtraction(ContentType? contentType)
    {
        if (!contentType.HasValue)
        {
            return false;
        }

        return contentType.Value switch
        {
            ContentType.ModdingTool => false,
            ContentType.Executable => false,
            ContentType.GameClient => false,
            ContentType.GameInstallation => false,
            _ => true,
        };
    }

    private static bool IsArchiveFile(string filePath, ContentType? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length == 0)
            {
                return false;
            }

            var extension = Path.GetExtension(filePath);

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".gz", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tgz", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".bz2", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xz", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFactory.IsArchive(filePath, out _) || ZipValidation.IsValidZipFile(filePath);
            }

            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!ShouldAttemptExecutableExtraction(contentType))
                {
                    return false;
                }

                return IsSelfExtractingArchive(filePath);
            }

            if (string.IsNullOrEmpty(extension))
            {
                return ArchiveFactory.IsArchive(filePath, out _) || ZipValidation.IsValidZipFile(filePath);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSelfExtractingArchive(string filePath)
    {
        try
        {
            using var zipArchive = ZipFile.OpenRead(filePath);
            if (zipArchive.Entries.Count > 0)
            {
                return true;
            }
        }
        catch
        {
            // Not a zip sfx
        }

        try
        {
            if (ArchiveFactory.IsArchive(filePath, out _) || ZipValidation.IsValidZipFile(filePath))
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            if (FindSignatureOffset(stream, SevenZipSignature) >= 0)
            {
                return true;
            }

            stream.Position = 0;
            if (FindSignatureOffset(stream, RarSignature) >= 0)
            {
                return true;
            }

            stream.Position = 0;
            if (FindSignatureOffset(stream, SmartInstallMakerSignature) >= 0)
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    private static long FindSignatureOffset(Stream stream, byte[] signature)
    {
        if (signature.Length == 0)
        {
            return -1;
        }

        // Keep the last partial match across chunk boundaries so a signature
        // split between two reads is still detected.
        var overlap = signature.Length - 1;
        var buffer = new byte[IoConstants.SignatureScanBufferSize];
        long streamOffset = 0;
        var buffered = 0;
        var read = 0;

        while ((read = stream.Read(buffer.AsSpan(buffered))) > 0)
        {
            var available = buffered + read;
            var index = buffer.AsSpan(0, available).IndexOf(signature);
            if (index >= 0)
            {
                return streamOffset + index;
            }

            buffered = Math.Min(available, overlap);
            buffer.AsSpan(available - buffered, buffered).CopyTo(buffer);
            streamOffset += available - buffered;
        }

        return -1;
    }

    private static IReadOnlyList<string> FindArchiveFiles(string rootDirectory, ContentType? contentType = null)
    {
        return Directory.GetFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(file => IsArchiveFile(file, contentType))
            .ToList();
    }

    private static void EnsureValidArchivePayload(string archivePath)
    {
        var info = new FileInfo(archivePath);
        if (!info.Exists || info.Length == 0)
        {
            throw new InvalidDataException($"Archive file is missing or empty: {archivePath}");
        }

        Span<byte> header = stackalloc byte[16];
        using (var stream = File.OpenRead(archivePath))
        {
            var read = stream.Read(header);
            if (read == 0)
            {
                throw new InvalidDataException($"Archive file is empty: {archivePath}");
            }

            header = header[..read];
        }

        if (LooksLikeHtml(header))
        {
            var preview = ReadTextPreview(archivePath, maxChars: 120);
            throw new InvalidDataException(
                $"Downloaded file is HTML, not an archive (likely a broken download URL or HTTP error page): {archivePath}. Preview: {preview}");
        }
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
        {
            header = header[3..];
        }

        while (header.Length > 0 && (header[0] == (byte)' ' || header[0] == (byte)'\t' || header[0] == (byte)'\r' || header[0] == (byte)'\n'))
        {
            header = header[1..];
        }

        if (header.Length < 5)
        {
            return false;
        }

        Span<char> ascii = stackalloc char[Math.Min(header.Length, 9)];
        for (var i = 0; i < ascii.Length; i++)
        {
            ascii[i] = (char)header[i];
        }

        ReadOnlySpan<char> prefix = ascii;
        return prefix.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadTextPreview(string path, int maxChars)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxChars];
            var read = reader.Read(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= maxChars ? text : text[..maxChars];
        }
        catch
        {
            return "(unavailable)";
        }
    }

    private static void ExtractSingleArchive(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var isExe = Path.GetExtension(archivePath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        if (isExe)
        {
            if (TryExtractZipArchive(archivePath, extractPath, cancellationToken))
            {
                return;
            }

            if (TryExtractSubStreamArchive(archivePath, extractPath, cancellationToken))
            {
                return;
            }

            if (TryExtractSmartInstallMakerArchive(archivePath, extractPath, cancellationToken))
            {
                return;
            }
        }

        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            ExtractSharpCompressArchive(archive, extractPath, cancellationToken);
        }
        catch when (isExe)
        {
            throw new InvalidDataException($"Executable is not a supported self-extracting archive: {archivePath}");
        }
    }

    private static void ExtractSharpCompressArchive(
        IArchive archive,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var entryCount = 0;
        long totalUncompressedSize = 0;
        var extractRoot = Path.GetFullPath(extractPath);

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }

            entryCount++;
            if (entryCount > CatalogConstants.MaxZipEntryCount)
            {
                throw new InvalidDataException(
                    $"Archive exceeds maximum entry count of {CatalogConstants.MaxZipEntryCount}");
            }

            if (Path.IsPathRooted(entry.Key))
            {
                throw new InvalidDataException($"Archive entry has an unsafe path: {entry.Key}");
            }

            var pathResult = ContentPathPolicy.ResolveContainedFile(extractRoot, entry.Key);
            if (!pathResult.Success || string.IsNullOrEmpty(pathResult.Data))
            {
                throw new InvalidDataException($"Archive entry has an unsafe path: {entry.Key}");
            }

            var destinationPath = pathResult.Data;
            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var entryStream = entry.OpenEntryStream();
            CopyEntryWithCap(entryStream, destinationPath, ref totalUncompressedSize, cancellationToken);
        }
    }

    private static void CopyEntryWithCap(
        Stream source,
        string destinationPath,
        ref long totalBytesWritten,
        CancellationToken cancellationToken)
    {
        using var dest = File.Create(destinationPath);
        var buffer = new byte[81920];
        var read = 0;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalBytesWritten += read;
            if (totalBytesWritten > CatalogConstants.MaxZipUncompressedSizeBytes)
            {
                throw new InvalidDataException(
                    $"Archive exceeds maximum uncompressed size of {CatalogConstants.MaxZipUncompressedSizeBytes} bytes");
            }

            dest.Write(buffer, 0, read);
        }
    }

    private static bool TryExtractZipArchive(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        if (!ZipValidation.IsValidZipFile(archivePath))
        {
            return false;
        }

        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            if (zip.Entries.Count == 0)
            {
                return false;
            }

            var entryCount = 0;
            long totalUncompressedSize = 0;
            var extractRoot = Path.GetFullPath(extractPath);

            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                entryCount++;
                if (entryCount > CatalogConstants.MaxZipEntryCount)
                {
                    throw new InvalidDataException(
                        $"Archive exceeds maximum entry count of {CatalogConstants.MaxZipEntryCount}");
                }

                ExtractSingleZipEntry(entry, extractRoot, ref totalUncompressedSize, cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractSingleZipEntry(
        ZipArchiveEntry entry,
        string extractRoot,
        ref long totalUncompressedSize,
        CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(entry.FullName))
        {
            throw new InvalidDataException($"Archive entry has an unsafe path: {entry.FullName}");
        }

        var pathResult = ContentPathPolicy.ResolveContainedFile(extractRoot, entry.FullName);
        if (!pathResult.Success || string.IsNullOrEmpty(pathResult.Data))
        {
            throw new InvalidDataException($"Archive entry has an unsafe path: {entry.FullName}");
        }

        var destinationPath = pathResult.Data;
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        using var entryStream = entry.Open();
        CopyEntryWithCap(entryStream, destinationPath, ref totalUncompressedSize, cancellationToken);
    }

    private static bool TryExtractSubStreamArchive(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            var offset = FindSignatureOffset(stream, SevenZipSignature);
            if (offset < 0)
            {
                stream.Position = 0;
                offset = FindSignatureOffset(stream, RarSignature);
            }

            if (offset < 0)
            {
                return false;
            }

            stream.Position = offset;
            using var subStream = new SubStream(stream, offset, stream.Length - offset);
            using var archive = ArchiveFactory.OpenArchive(subStream);
            ExtractSharpCompressArchive(archive, extractPath, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractSmartInstallMakerArchive(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var stagingDir = Path.Combine(extractPath, "_sim_staging_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = File.OpenRead(archivePath);
            var sigOffset = FindSignatureOffset(stream, SmartInstallMakerSignature);
            if (sigOffset < 0)
            {
                return false;
            }

            stream.Position = sigOffset + SmartInstallMakerSignature.Length;
            var (fileTableData, payloadOffset) = ReadSmartInstallMakerMetadata(stream);
            if (fileTableData == null || fileTableData.Length == 0 || payloadOffset < 0)
            {
                return false;
            }

            var records = ParseSmartInstallMakerFileTable(fileTableData, stream, payloadOffset);
            if (records.Count == 0)
            {
                return false;
            }

            Directory.CreateDirectory(stagingDir);
            var stagingRoot = Path.GetFullPath(stagingDir);
            var extractedCount = ExtractSmartInstallMakerPayload(stream, payloadOffset, records, stagingRoot, cancellationToken);
            if (extractedCount != records.Count)
            {
                throw new InvalidDataException(
                    $"Smart Install Maker extraction incomplete: extracted {extractedCount} of {records.Count} entries.");
            }

            PromoteDirectoryContents(stagingDir, extractPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDir))
                {
                    Directory.Delete(stagingDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static (byte[]? TableData, long PayloadOffset) ReadSmartInstallMakerMetadata(Stream stream)
    {
        var (secondToLastBlock, lastBlock) = WalkSmartInstallMakerBlocks(stream);
        if (secondToLastBlock == null || lastBlock == null)
        {
            return (null, -1);
        }

        var payloadOffset = lastBlock.Value.DataStart;
        var tableBlock = secondToLastBlock.Value;
        if (tableBlock.CompType == 1)
        {
            var tableData = DecompressSimTableBlock(stream, tableBlock.DataStart);
            return (tableData, payloadOffset);
        }

        return (null, payloadOffset);
    }

    private static ((long Pos, int CompSize, byte CompType, long DataStart)? SecondToLast, (long Pos, int CompSize, byte CompType, long DataStart)? Last) WalkSmartInstallMakerBlocks(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        (long Pos, int CompSize, byte CompType, long DataStart)? secondToLastBlock = null;
        (long Pos, int CompSize, byte CompType, long DataStart)? lastBlock = null;
        var blockCount = 0;
        const int MaxBlockWalkCount = 100_000;

        while (stream.Position < stream.Length - 13 && blockCount < MaxBlockWalkCount)
        {
            var pos = stream.Position;
            _ = blockCount == 0 ? reader.ReadInt16() : reader.ReadInt32();
            var compSize = reader.ReadInt32();
            _ = reader.ReadInt32();
            var compType = reader.ReadByte();
            var dataLength = compSize - 5;
            var dataStart = stream.Position;

            secondToLastBlock = lastBlock;
            lastBlock = (pos, compSize, compType, dataStart);
            blockCount++;

            if (dataLength > 0 && stream.Position + dataLength <= stream.Length)
            {
                stream.Position += dataLength;
            }
            else
            {
                break;
            }
        }

        return (secondToLastBlock, lastBlock);
    }

    private static byte[] DecompressSimTableBlock(Stream stream, long dataStart)
    {
        stream.Position = dataStart + 2; // skip zlib 78-DA header
        using var def = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        var r = 0;
        var totalDecompressed = 0L;
        while ((r = def.Read(buf, 0, buf.Length)) > 0)
        {
            totalDecompressed += r;
            if (totalDecompressed > CatalogConstants.MaxCatalogSizeBytes)
            {
                throw new InvalidDataException("Smart Install Maker metadata table exceeds maximum allowed size.");
            }

            ms.Write(buf, 0, r);
        }

        return ms.ToArray();
    }

    private static int ExtractSmartInstallMakerPayload(
        Stream stream,
        long payloadOffset,
        List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> records,
        string extractRoot,
        CancellationToken cancellationToken)
    {
        var extractedCount = 0;
        var copyBuffer = new byte[65536];

        foreach (var rec in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractSingleSmartInstallMakerRecord(stream, payloadOffset, rec, extractRoot, copyBuffer);
            extractedCount++;
        }

        return extractedCount;
    }

    private static void ExtractSingleSmartInstallMakerRecord(
        Stream stream,
        long payloadOffset,
        (string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize) rec,
        string extractRoot,
        byte[] copyBuffer)
    {
        var pathResult = ContentPathPolicy.ResolveContainedFile(extractRoot, rec.Name);
        if (!pathResult.Success || string.IsNullOrEmpty(pathResult.Data))
        {
            throw new InvalidDataException($"Smart Install Maker entry has an unsafe path: {rec.Name}");
        }

        var destinationPath = pathResult.Data;
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var filePos = payloadOffset + rec.StreamOffset;
        if (filePos < 0 || filePos + rec.CompressedSize > stream.Length)
        {
            throw new InvalidDataException($"Smart Install Maker entry '{rec.Name}' compressed range exceeds stream bounds.");
        }

        stream.Position = filePos;
        var header = new byte[2];
        var headerRead = stream.Read(header, 0, 2);
        stream.Position = filePos;

        var written = TryDecompressSmartInstallMakerRecord(stream, filePos, header, headerRead, destinationPath, rec.UncompressedSize, copyBuffer);

        if (written != rec.UncompressedSize && filePos + rec.UncompressedSize <= stream.Length)
        {
            // Fallback to raw copy if sniffed decompressor failed but raw payload is available
            stream.Position = filePos;
            using var outStream = File.Create(destinationPath);
            written = 0;
            while (written < rec.UncompressedSize)
            {
                var toRead = (int)Math.Min(copyBuffer.Length, rec.UncompressedSize - written);
                var readBytes = stream.Read(copyBuffer, 0, toRead);
                if (readBytes <= 0)
                {
                    break;
                }

                outStream.Write(copyBuffer, 0, readBytes);
                written += readBytes;
            }
        }

        if (written != rec.UncompressedSize)
        {
            throw new InvalidDataException(
                $"Smart Install Maker entry '{rec.Name}' decompressed size mismatch: expected {rec.UncompressedSize} bytes, got {written} bytes.");
        }
    }

    private static long TryDecompressSmartInstallMakerRecord(
        Stream stream,
        long filePos,
        byte[] header,
        int headerRead,
        string destinationPath,
        uint uncompressedSize,
        byte[] copyBuffer)
    {
        if (headerRead >= 2 && header[0] == 'B' && header[1] == 'Z')
        {
            return DecompressBz2SmartInstallMakerRecord(stream, destinationPath, uncompressedSize, copyBuffer);
        }

        if (headerRead >= 2 && header[0] == 0x78 && (header[1] == 0xDA || header[1] == 0x9C || header[1] == 0x01 || header[1] == 0x5E))
        {
            return DecompressDeflateSmartInstallMakerRecord(stream, filePos, destinationPath, uncompressedSize, copyBuffer);
        }

        return DecompressRawSmartInstallMakerRecord(stream, destinationPath, uncompressedSize, copyBuffer);
    }

    private static long DecompressBz2SmartInstallMakerRecord(Stream stream, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        using var bz2 = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
            stream,
            SharpCompress.Compressors.CompressionMode.Decompress,
            decompressConcatenated: false,
            leaveOpen: true);

        using var outStream = File.Create(destinationPath);
        long written = 0;
        while (written < uncompressedSize)
        {
            var toRead = (int)Math.Min(copyBuffer.Length, uncompressedSize - written);
            var readBytes = bz2.Read(copyBuffer, 0, toRead);
            if (readBytes <= 0)
            {
                break;
            }

            outStream.Write(copyBuffer, 0, readBytes);
            written += readBytes;
        }

        return written;
    }

    private static long DecompressDeflateSmartInstallMakerRecord(Stream stream, long filePos, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        stream.Position = filePos + 2; // skip zlib header
        using var def = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var outStream = File.Create(destinationPath);
        long written = 0;
        while (written < uncompressedSize)
        {
            var toRead = (int)Math.Min(copyBuffer.Length, uncompressedSize - written);
            var readBytes = def.Read(copyBuffer, 0, toRead);
            if (readBytes <= 0)
            {
                break;
            }

            outStream.Write(copyBuffer, 0, readBytes);
            written += readBytes;
        }

        return written;
    }

    private static long DecompressRawSmartInstallMakerRecord(Stream stream, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        using var outStream = File.Create(destinationPath);
        long written = 0;
        while (written < uncompressedSize)
        {
            var toRead = (int)Math.Min(copyBuffer.Length, uncompressedSize - written);
            var readBytes = stream.Read(copyBuffer, 0, toRead);
            if (readBytes <= 0)
            {
                break;
            }

            outStream.Write(copyBuffer, 0, readBytes);
            written += readBytes;
        }

        return written;
    }

    private static List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> ParseSmartInstallMakerFileTable(
        byte[] tableData,
        Stream stream,
        long payloadOffset)
    {
        var records = new List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)>();
        var cumulativeUncompressedSize = 0L;
        var index = 0;

        while (index < tableData.Length - 4)
        {
            index = ProcessNextSimCandidate(tableData, index, stream, payloadOffset, records, ref cumulativeUncompressedSize);
        }

        return records;
    }

    private static int ProcessNextSimCandidate(
        byte[] tableData,
        int index,
        Stream stream,
        long payloadOffset,
        List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> records,
        ref long cumulativeUncompressedSize)
    {
        if (tableData[index] != '.' || index < 40)
        {
            return index + 1;
        }

        if (!TryExtractSimCandidateName(tableData, index, out var name, out var nextIndex, out var startOffset))
        {
            return index + 1;
        }

        if (IsValidSimEntryName(name) &&
            TryReadSimRecord(tableData, startOffset, name, stream, payloadOffset, records, out var record))
        {
            ValidateAndAddSimRecord(record, records, ref cumulativeUncompressedSize);
        }

        return nextIndex;
    }

    private static void ValidateAndAddSimRecord(
        (string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize) record,
        List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> records,
        ref long cumulativeUncompressedSize)
    {
        if (records.Count >= CatalogConstants.MaxZipEntryCount)
        {
            throw new InvalidDataException(
                $"Smart Install Maker archive exceeds maximum entry count of {CatalogConstants.MaxZipEntryCount}");
        }

        cumulativeUncompressedSize += record.UncompressedSize;
        if (cumulativeUncompressedSize > CatalogConstants.MaxZipUncompressedSizeBytes)
        {
            throw new InvalidDataException(
                $"Smart Install Maker archive exceeds maximum uncompressed size of {CatalogConstants.MaxZipUncompressedSizeBytes} bytes");
        }

        records.Add(record);
    }

    private static bool TryExtractSimCandidateName(
        byte[] tableData,
        int dotIndex,
        out string name,
        out int nextIndex,
        out int startOffset)
    {
        var start = dotIndex;
        while (start > 0 && tableData[start - 1] != 0 && tableData[start - 1] >= 32 && tableData[start - 1] <= 126)
        {
            start--;
        }

        var end = dotIndex;
        while (end < tableData.Length && tableData[end] != 0 && tableData[end] >= 32 && tableData[end] <= 126)
        {
            end++;
        }

        startOffset = start;
        nextIndex = end;

        if (start < 40 || end - start <= 3)
        {
            name = string.Empty;
            return false;
        }

        name = Encoding.Latin1.GetString(tableData, start, end - start);
        return true;
    }

    private static bool IsValidSimEntryName(string name)
    {
        if (!name.Contains('.') || name.StartsWith(' ') || name.Length <= 3 || name.Contains(".."))
        {
            return false;
        }

        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Intrnl.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var invalidChars = Path.GetInvalidPathChars().Concat([':', '"', '<', '>', '|', '*', '?']).ToArray();
        if (name.IndexOfAny(invalidChars) >= 0)
        {
            return false;
        }

        var ext = Path.GetExtension(name);
        return !string.IsNullOrEmpty(ext) && ext.Length <= 5;
    }

    private static bool TryReadSimRecord(
        byte[] tableData,
        int startOffset,
        string name,
        Stream stream,
        long payloadOffset,
        List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> existingRecords,
        out (string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize) record)
    {
        record = default;
        var uncompSize = BitConverter.ToUInt32(tableData, startOffset - 40);
        var streamOffset = BitConverter.ToUInt32(tableData, startOffset - 36);
        var compSize = BitConverter.ToUInt32(tableData, startOffset - 32);

        if (uncompSize == 0 || compSize == 0)
        {
            return false;
        }

        if ((ulong)uncompSize > (ulong)CatalogConstants.MaxZipUncompressedSizeBytes ||
            payloadOffset + streamOffset + compSize > stream.Length)
        {
            return false;
        }

        if (existingRecords.Exists(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        record = (name, uncompSize, streamOffset, compSize);
        return true;
    }

    private static void PurgeSystemJunk(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var subDir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                if (!Directory.Exists(subDir))
                {
                    continue;
                }

                var dirName = Path.GetFileName(subDir);
                if (GameContentConstants.SystemJunkNames.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                {
                    Directory.Delete(subDir, recursive: true);
                }
            }

            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var fileName = Path.GetFileName(file);
                if (GameContentConstants.SystemJunkNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Ignore system junk removal failures
        }
    }

    private static bool ContainsRecognizedGameContent(string directory)
    {
        var subDirs = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name));

        if (subDirs.Any(name => GameContentConstants.RecognizedGameDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetExtension)
            .Where(ext => !string.IsNullOrEmpty(ext));

        return files.Any(ext => GameContentConstants.RecognizedGameFileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
    }

    private static bool DirectoryContainsMapFilesDirectly(string directory)
    {
        return Directory.GetFiles(directory, "*.map", SearchOption.TopDirectoryOnly).Length > 0;
    }

    private static void PromoteDirectoryContents(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory) ||
            string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tempStaging = Path.Combine(Path.GetDirectoryName(sourceDirectory) ?? targetDirectory, Path.GetFileName(sourceDirectory))
            + "_staging_" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.Move(sourceDirectory, tempStaging);

            foreach (var subFile in Directory.GetFiles(tempStaging, "*", SearchOption.AllDirectories))
            {
                PromoteSingleStagedFile(subFile, tempStaging, targetDirectory);
            }
        }
        catch
        {
            RollbackStaging(tempStaging, sourceDirectory);
            throw;
        }
        finally
        {
            CleanupStaging(tempStaging);
        }
    }

    private static void PromoteSingleStagedFile(string subFile, string tempStaging, string targetDirectory)
    {
        var relativePath = Path.GetRelativePath(tempStaging, subFile);
        var destinationPath = Path.Combine(targetDirectory, relativePath);
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destinationPath))
        {
            var destInfo = new FileInfo(destinationPath);
            var srcInfo = new FileInfo(subFile);

            if (destInfo.Length == srcInfo.Length && FilesHaveIdenticalContent(subFile, destinationPath))
            {
                File.Delete(subFile);
                return;
            }

            var newDestPath = GetNonCollidingDestinationPath(destinationPath);
            File.Move(subFile, newDestPath);
        }
        else
        {
            File.Move(subFile, destinationPath);
        }
    }

    private static void RollbackStaging(string tempStaging, string sourceDirectory)
    {
        try
        {
            if (!Directory.Exists(tempStaging))
            {
                return;
            }

            if (!Directory.Exists(sourceDirectory))
            {
                Directory.Move(tempStaging, sourceDirectory);
                return;
            }

            foreach (var remainingFile in Directory.GetFiles(tempStaging, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(tempStaging, remainingFile);
                var backPath = Path.Combine(sourceDirectory, rel);
                var dir = Path.GetDirectoryName(backPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Move(remainingFile, backPath, overwrite: true);
            }
        }
        catch
        {
            // Best effort rollback
        }
    }

    private static void CleanupStaging(string tempStaging)
    {
        if (Directory.Exists(tempStaging))
        {
            try
            {
                Directory.Delete(tempStaging, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static string GetNonCollidingDestinationPath(string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
        var ext = Path.GetExtension(destinationPath);
        var counter = 1;
        var newDestPath = Path.Combine(dir, $"{fileNameWithoutExt}_{counter}{ext}");
        while (File.Exists(newDestPath))
        {
            counter++;
            newDestPath = Path.Combine(dir, $"{fileNameWithoutExt}_{counter}{ext}");
        }

        return newDestPath;
    }

    private static bool FilesHaveIdenticalContent(string file1, string file2)
    {
        const int bufferSize = 65536;
        var buffer1 = new byte[bufferSize];
        var buffer2 = new byte[bufferSize];

        using var s1 = File.OpenRead(file1);
        using var s2 = File.OpenRead(file2);

        if (s1.Length != s2.Length)
        {
            return false;
        }

        while (true)
        {
            var bytesRead1 = s1.Read(buffer1, 0, bufferSize);
            if (bytesRead1 <= 0)
            {
                break;
            }

            var bytesRead2 = s2.Read(buffer2, 0, bufferSize);
            if (bytesRead1 != bytesRead2)
            {
                return false;
            }

            if (!buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
            {
                return false;
            }
        }

        return true;
    }

    private static void CleanupEmptyDirectories(string rootDirectory)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                .Where(d => Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                .OrderByDescending(d => d.Length))
            {
                Directory.Delete(subDir);
            }
        }
        catch
        {
            // Ignore directory cleanup exceptions
        }
    }

    private void StripSingleWrapperDirectories(
        string extractedDirectory,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        var depth = 0;
        while (depth < GameContentConstants.MaxWrapperNormalizationDepth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            depth++;

            var rootFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.TopDirectoryOnly);
            var rootDirs = Directory.GetDirectories(extractedDirectory, "*", SearchOption.TopDirectoryOnly);

            if (rootFiles.Length != 0 || rootDirs.Length != 1)
            {
                break;
            }

            var singleDir = rootDirs[0];
            var dirName = Path.GetFileName(singleDir);

            // For map content, if the single directory contains .map files directly, preserve this directory
            if (contentType is ContentType.Map or ContentType.MapPack && DirectoryContainsMapFilesDirectly(singleDir))
            {
                logger.LogInformation("Preserving map folder structure for: {MapDir}", singleDir);
                break;
            }

            // If the single directory is a canonical game directory (e.g. Data, Art, Window, Maps, Audio),
            // it is already at the game root level (e.g. /Data/INI/...) and should NOT be flattened.
            if (GameContentConstants.IsRecognizedGameDirectory(dirName))
            {
                logger.LogInformation("Preserving canonical game root directory: {SingleDir}", singleDir);
                break;
            }

            logger.LogInformation("Flattening single wrapper directory: {SingleDir} into {Root}", singleDir, extractedDirectory);
            PromoteDirectoryContents(singleDir, extractedDirectory);
        }
    }

    private void RouteGameSpecificSubdirectories(
        string extractedDirectory,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootDirs = Directory.GetDirectories(extractedDirectory, "*", SearchOption.TopDirectoryOnly);
        if (rootDirs.Length == 0)
        {
            return;
        }

        var matchingAliases = targetGame switch
        {
            GameType.ZeroHour => GameContentConstants.ZeroHourSubfolderAliases,
            GameType.Generals => GameContentConstants.GeneralsSubfolderAliases,
            _ => null,
        };

        if (matchingAliases == null)
        {
            return;
        }

        foreach (var subDir in rootDirs)
        {
            var dirName = Path.GetFileName(subDir);
            if (matchingAliases.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Detected matching game-specific subdirectory '{DirName}' for game {Game}. Promoting contents to root.",
                    dirName,
                    targetGame);

                PromoteDirectoryContents(subDir, extractedDirectory);
            }
        }
    }

    private void ReconcileContentRootWithDocumentation(
        string extractedDirectory,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (contentType is ContentType.Map or ContentType.MapPack)
        {
            return;
        }

        var rootFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.TopDirectoryOnly);
        var rootDirs = Directory.GetDirectories(extractedDirectory, "*", SearchOption.TopDirectoryOnly);

        if (rootDirs.Length != 1)
        {
            return;
        }

        var singleDir = rootDirs[0];
        var dirName = Path.GetFileName(singleDir);

        // If the single directory is already a canonical game directory (e.g. Data), it should remain as is
        if (GameContentConstants.IsRecognizedGameDirectory(dirName))
        {
            return;
        }

        // Check if all files at the root level are loose documentation/metadata files
        var allRootFilesAreDocs = rootFiles.All(file =>
        {
            var ext = Path.GetExtension(file);
            return GameContentConstants.DocumentationExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        });

        if (!allRootFilesAreDocs)
        {
            return;
        }

        // Check if the single directory contains recognizable game root folders or files
        if (ContainsRecognizedGameContent(singleDir))
        {
            logger.LogInformation(
                "Promoting game content root from wrapper '{SingleDir}' to payload root alongside documentation",
                singleDir);

            PromoteDirectoryContents(singleDir, extractedDirectory);
        }
    }

    private void NormalizeGibExtensions(string extractedDirectory, ContentType contentType)
    {
        if (contentType is ContentType.ModdingTool or ContentType.Executable or ContentType.GameClient or ContentType.GameInstallation)
        {
            return;
        }

        try
        {
            foreach (var gibFile in Directory.GetFiles(extractedDirectory, "*.gib", SearchOption.AllDirectories))
            {
                var bigFile = Path.ChangeExtension(gibFile, ".big");
                if (File.Exists(bigFile))
                {
                    if (FilesHaveIdenticalContent(gibFile, bigFile))
                    {
                        File.Delete(gibFile);
                        logger.LogInformation("Removed duplicate identical inactive file '{GibFile}' as '{BigFile}' already exists", gibFile, bigFile);
                    }
                    else
                    {
                        var nonCollidingBigPath = GetNonCollidingDestinationPath(bigFile);
                        File.Move(gibFile, nonCollidingBigPath);
                        logger.LogInformation("Preserved differing inactive file '{GibFile}' by renaming to '{NewBigFile}'", gibFile, nonCollidingBigPath);
                    }
                }
                else
                {
                    File.Move(gibFile, bigFile);
                    logger.LogInformation("Normalized inactive mod archive '{GibFile}' to '{BigFile}'", gibFile, bigFile);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to normalize .gib file extensions in: {Directory}", extractedDirectory);
        }
    }

    private sealed class SubStream(Stream baseStream, long streamOffset, long length) : Stream
    {
        private long _position;

        public override bool CanRead => baseStream.CanRead;

        public override bool CanSeek => baseStream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, length);
                _position = value;
            }
        }

        public override void Flush() => baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= length)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, length - _position);
            baseStream.Position = streamOffset + _position;
            var read = baseStream.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = target;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
