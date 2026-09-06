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
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace GenHub.Features.Content.Services.Common;

/// <summary>
/// Service for safely extracting archives and normalizing payload directory structures for game workspaces.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Payload normalization handles diverse archive topologies, legacy SIM installers, NSIS installers, and directory hierarchies.")]
public class ArchivePayloadProcessor(ILogger<ArchivePayloadProcessor> logger) : IArchivePayloadProcessor
{
    private const int MaxNestedExtractionDepth = 5;
    private const string ExtractingFilesStageDescription = "Extracting files";
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] RarSignature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];
    private static readonly byte[] Rar5Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];
    private static readonly byte[] SmartInstallMakerSignature = [0x77, 0x77, 0x67, 0x54, 0x29, 0x48, 0x35, 0x14];

    /// <inheritdoc />
    public Task ExtractArchivesSafelyAsync(
        string extractedDirectory,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        return ExtractArchivesSafelyAsync(extractedDirectory, contentType, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task ExtractArchivesSafelyAsync(
        string extractedDirectory,
        ContentType? contentType,
        IProgress<ContentAcquisitionProgress>? progress,
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

                    for (var archiveIdx = 0; archiveIdx < archiveFiles.Count; archiveIdx++)
                    {
                        var archivePath = archiveFiles[archiveIdx];
                        cancellationToken.ThrowIfCancellationRequested();

                        EnsureValidArchivePayload(archivePath);
                        logger.LogInformation("Extracting archive safely: {ArchivePath}", archivePath);

                        ExtractSingleArchive(archivePath, extractedDirectory, progress, logger, cancellationToken);
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
        return NormalizeDirectoryStructureAsync(extractedDirectory, contentType, targetGame, normalizeInactiveArchives: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task NormalizeDirectoryStructureAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        bool normalizeInactiveArchives,
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

                // 5. Normalize inactive .gib and .ctr mod archive files to .big if requested
                if (normalizeInactiveArchives)
                {
                    NormalizeInactiveBigExtensions(extractedDirectory, contentType);
                }

                // 6. Cleanup empty directories
                CleanupEmptyDirectories(extractedDirectory);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ProcessPayloadAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        return ProcessPayloadAsync(extractedDirectory, contentType, targetGame, normalizeInactiveArchives: true, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task ProcessPayloadAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        bool normalizeInactiveArchives,
        CancellationToken cancellationToken = default)
    {
        return ProcessPayloadAsync(extractedDirectory, contentType, targetGame, normalizeInactiveArchives, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ProcessPayloadAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        bool normalizeInactiveArchives,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        await ExtractArchivesSafelyAsync(extractedDirectory, contentType, progress, cancellationToken);
        await NormalizeDirectoryStructureAsync(extractedDirectory, contentType, targetGame, normalizeInactiveArchives, cancellationToken);
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
            // Not a standard ZIP SFX
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
            var overlayOffset = GetPeOverlayOffset(stream);

            // Fast path: check overlay offset first if PE executable
            if (overlayOffset > 0 &&
                overlayOffset < stream.Length &&
                (FindSignatureOffset(stream, SmartInstallMakerSignature, overlayOffset, maxScanBytes: 1024) >= 0 ||
                 FindSignatureOffset(stream, SevenZipSignature, overlayOffset, maxScanBytes: 1024) >= 0 ||
                 FindSignatureOffset(stream, RarSignature, overlayOffset, maxScanBytes: 1024) >= 0 ||
                 FindSignatureOffset(stream, Rar5Signature, overlayOffset, maxScanBytes: 1024) >= 0))
            {
                return true;
            }

            // Fallback scan: search stream for known SFX / installer signatures
            if (FindSignatureOffset(stream, SmartInstallMakerSignature) >= 0 ||
                FindSignatureOffset(stream, SevenZipSignature) >= 0 ||
                FindSignatureOffset(stream, RarSignature) >= 0 ||
                FindSignatureOffset(stream, Rar5Signature) >= 0)
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

    private static long GetPeOverlayOffset(Stream stream)
    {
        if (stream.Length < 64)
        {
            return -1;
        }

        try
        {
            stream.Position = 0;
            Span<byte> dosHeader = stackalloc byte[64];
            if (stream.Read(dosHeader) < 64 || dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
            {
                return -1;
            }

            var eLfanew = BitConverter.ToInt32(dosHeader[0x3C..0x40]);
            if (eLfanew <= 0 || eLfanew + 24 > stream.Length)
            {
                return -1;
            }

            stream.Position = eLfanew;
            Span<byte> peHeader = stackalloc byte[24];
            if (stream.Read(peHeader) < 24 ||
                peHeader[0] != (byte)'P' || peHeader[1] != (byte)'E' || peHeader[2] != 0 || peHeader[3] != 0)
            {
                return -1;
            }

            var numberOfSections = BitConverter.ToUInt16(peHeader[6..8]);
            var sizeOfOptionalHeader = BitConverter.ToUInt16(peHeader[20..22]);

            var sectionTableOffset = eLfanew + 24 + sizeOfOptionalHeader;
            if (numberOfSections == 0 || sectionTableOffset + (numberOfSections * 40) > stream.Length)
            {
                return -1;
            }

            var maxRawEnd = 0L;
            var sectionBuffer = new byte[40];
            for (var i = 0; i < numberOfSections; i++)
            {
                stream.Position = sectionTableOffset + (i * 40);
                if (stream.Read(sectionBuffer, 0, 40) < 40)
                {
                    return -1;
                }

                var sizeOfRawData = BitConverter.ToUInt32(sectionBuffer, 16);
                var pointerToRawData = BitConverter.ToUInt32(sectionBuffer, 20);
                var rawEnd = (long)pointerToRawData + sizeOfRawData;
                if (rawEnd > maxRawEnd)
                {
                    maxRawEnd = rawEnd;
                }
            }

            return maxRawEnd > 0 && maxRawEnd <= stream.Length ? maxRawEnd : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static long FindSignatureOffset(
        Stream stream,
        ReadOnlySpan<byte> signature,
        long startOffset = 0,
        long maxScanBytes = -1)
    {
        if (signature.IsEmpty || stream.Length == 0 || startOffset >= stream.Length)
        {
            return -1;
        }

        stream.Position = startOffset;
        const int bufferSize = 65536;
        var buffer = new byte[bufferSize];
        var streamOffset = startOffset;
        var overlap = 0;
        var totalScanned = 0L;
        var sigLength = signature.Length;

        while (true)
        {
            var bytesToRead = buffer.Length - overlap;
            if (maxScanBytes > 0 && totalScanned + bytesToRead > maxScanBytes)
            {
                bytesToRead = (int)(maxScanBytes - totalScanned);
                if (bytesToRead <= 0)
                {
                    break;
                }
            }

            var read = stream.Read(buffer, overlap, bytesToRead);
            if (read <= 0)
            {
                break;
            }

            var totalBytesInBuffer = overlap + read;
            totalScanned += read;

            var span = new ReadOnlySpan<byte>(buffer, 0, totalBytesInBuffer);
            var index = span.IndexOf(signature);
            if (index >= 0)
            {
                return streamOffset + index;
            }

            overlap = Math.Min(sigLength - 1, totalBytesInBuffer);
            if (overlap > 0)
            {
                Buffer.BlockCopy(buffer, totalBytesInBuffer - overlap, buffer, 0, overlap);
                streamOffset += totalBytesInBuffer - overlap;
            }
            else
            {
                streamOffset += totalBytesInBuffer;
            }

            if (read < bytesToRead)
            {
                break;
            }
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
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var isExe = Path.GetExtension(archivePath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        if (isExe)
        {
            if (TryExtractZipArchive(archivePath, extractPath, progress, logger, cancellationToken))
            {
                return;
            }

            if (TryExtractSubStreamArchive(archivePath, extractPath, progress, logger, cancellationToken))
            {
                return;
            }

            if (TryExtractSmartInstallMakerArchive(archivePath, extractPath, progress, logger, cancellationToken))
            {
                return;
            }
        }

        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            ExtractSharpCompressArchive(archive, extractPath, progress, logger, cancellationToken);
        }
        catch when (isExe)
        {
            throw new InvalidDataException($"Executable is not a supported self-extracting archive: {archivePath}");
        }
    }

    private static void ExtractSharpCompressArchive(
        IArchive archive,
        string extractPath,
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var entryCount = 0;
        long totalUncompressedSize = 0;
        var extractRoot = Path.GetFullPath(extractPath);
        var entries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
        var totalEntries = entries.Count;

        for (var i = 0; i < totalEntries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            var entryKey = entry.Key ?? string.Empty;

            entryCount++;
            if (entryCount > CatalogConstants.MaxZipEntryCount)
            {
                throw new InvalidDataException(
                    $"Archive exceeds maximum entry count of {CatalogConstants.MaxZipEntryCount}");
            }

            if (Path.IsPathRooted(entryKey))
            {
                throw new InvalidDataException($"Archive entry has an unsafe path: {entryKey}");
            }

            var pathResult = ContentPathPolicy.ResolveContainedFile(extractRoot, entryKey);
            if (!pathResult.Success)
            {
                throw new InvalidDataException($"Archive entry has an unsafe path: {entryKey}");
            }

            var destinationPath = pathResult.Data;
            if (destinationPath == null)
            {
                throw new InvalidDataException($"Archive entry could not be resolved: {entryKey}");
            }

            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            var stageProgress = (double)(i + 1) / totalEntries * 100;
            var fileName = Path.GetFileName(entryKey);
            progress?.Report(new ContentAcquisitionProgress
            {
                CurrentStage = 3,
                TotalStages = 5,
                StageDescription = ExtractingFilesStageDescription,
                CurrentOperation = $"Extracting {fileName}",
                FilesProcessed = i + 1,
                TotalFiles = totalEntries,
                StageProgress = stageProgress,
            });

            using var entryStream = entry.OpenEntryStream();
            CopyEntryWithCap(entryStream, destinationPath, ref totalUncompressedSize, cancellationToken);
            logger.LogInformation("Extracted archive entry {Current}/{Total}: {EntryName} ({Size} bytes)", i + 1, totalEntries, entryKey, entry.Size);
        }
    }

    private static bool IsBigArchiveFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 16)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) < 4)
            {
                return false;
            }

            return header[0] == (byte)'B' && header[1] == (byte)'I' && header[2] == (byte)'G' &&
                   (header[3] == (byte)'4' || header[3] == (byte)'F' || header[3] == (byte)'E' || header[3] == 0);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExecutableFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 2)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[2];
            if (stream.Read(header) < 2)
            {
                return false;
            }

            return header[0] == (byte)'M' && header[1] == (byte)'Z';
        }
        catch
        {
            return false;
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
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
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

            var validEntries = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
                .ToList();
            var totalEntries = validEntries.Count;
            if (totalEntries > CatalogConstants.MaxZipEntryCount)
            {
                throw new InvalidDataException(
                    $"Archive exceeds maximum entry count of {CatalogConstants.MaxZipEntryCount}");
            }

            long totalUncompressedSize = 0;
            var extractRoot = Path.GetFullPath(extractPath);

            for (var i = 0; i < totalEntries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExtractSingleZipEntry(validEntries[i], extractRoot, i, totalEntries, ref totalUncompressedSize, progress, logger, cancellationToken);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Zip entry extraction requires stream coordinates, cancellation, and progress reporting.")]
    private static void ExtractSingleZipEntry(
        ZipArchiveEntry entry,
        string extractRoot,
        int index,
        int totalEntries,
        ref long totalUncompressedSize,
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(entry.FullName))
        {
            throw new InvalidDataException($"Archive entry has an unsafe path: {entry.FullName}");
        }

        var pathResult = ContentPathPolicy.ResolveContainedFile(extractRoot, entry.FullName);
        if (!pathResult.Success)
        {
            throw new InvalidDataException($"Archive entry has an unsafe path: {entry.FullName}");
        }

        var destinationPath = pathResult.Data ?? string.Empty;
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var stageProgress = totalEntries > 0 ? (double)(index + 1) / totalEntries * 100 : 100;
        var fileName = Path.GetFileName(entry.FullName);
        progress?.Report(new ContentAcquisitionProgress
        {
            CurrentStage = 3,
            TotalStages = 5,
            StageDescription = ExtractingFilesStageDescription,
            CurrentOperation = $"Extracting {fileName}",
            FilesProcessed = index + 1,
            TotalFiles = totalEntries,
            StageProgress = stageProgress,
        });

        using var entryStream = entry.Open();
        CopyEntryWithCap(entryStream, destinationPath, ref totalUncompressedSize, cancellationToken);
        logger.LogInformation("Extracted zip entry {Current}/{Total}: {EntryName} ({Size} bytes)", index + 1, totalEntries, entry.FullName, entry.Length);
    }

    private static bool TryExtractSubStreamArchive(
        string archivePath,
        string extractPath,
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            var overlayOffset = GetPeOverlayOffset(stream);
            long offset = -1;

            if (overlayOffset > 0 && overlayOffset < stream.Length)
            {
                offset = FindSignatureOffset(stream, SevenZipSignature, overlayOffset, maxScanBytes: 1024);
                if (offset < 0)
                {
                    offset = FindSignatureOffset(stream, RarSignature, overlayOffset, maxScanBytes: 1024);
                }

                if (offset < 0)
                {
                    offset = FindSignatureOffset(stream, Rar5Signature, overlayOffset, maxScanBytes: 1024);
                }
            }

            if (offset < 0)
            {
                offset = FindSignatureOffset(stream, SevenZipSignature);
            }

            if (offset < 0)
            {
                offset = FindSignatureOffset(stream, RarSignature);
            }

            if (offset < 0)
            {
                offset = FindSignatureOffset(stream, Rar5Signature);
            }

            if (offset < 0)
            {
                return false;
            }

            stream.Position = offset;
            using var subStream = new SubStream(stream, offset, stream.Length - offset);
            using var archive = ArchiveFactory.OpenArchive(subStream);
            ExtractSharpCompressArchive(archive, extractPath, progress, logger, cancellationToken);
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
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var stagingDir = Path.Combine(extractPath, "_sim_staging_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = File.OpenRead(archivePath);
            var sigOffset = LocateSmartInstallMakerSignature(stream);
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

            Directory.CreateDirectory(stagingDir);
            var stagingRoot = Path.GetFullPath(stagingDir);

            return TryExtractSimPayload(stream, payloadOffset, fileTableData, stagingRoot, stagingDir, extractPath, progress, logger, cancellationToken);
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

    private static long LocateSmartInstallMakerSignature(Stream stream)
    {
        var overlayOffset = GetPeOverlayOffset(stream);
        long sigOffset = -1;

        if (overlayOffset > 0 && overlayOffset < stream.Length)
        {
            sigOffset = FindSignatureOffset(stream, SmartInstallMakerSignature, overlayOffset, maxScanBytes: 1024);
        }

        if (sigOffset < 0)
        {
            sigOffset = FindSignatureOffset(stream, SmartInstallMakerSignature);
        }

        return sigOffset;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Internal extraction helper needing unpack context.")]
    private static bool TryExtractSimPayload(
        Stream stream,
        long payloadOffset,
        byte[] fileTableData,
        string stagingRoot,
        string stagingDir,
        string extractPath,
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var legacyRecords = ParseSmartInstallMakerFileTable(fileTableData, stream, payloadOffset);
        if (legacyRecords.Count > 0)
        {
            var extractedCount = ExtractSmartInstallMakerPayload(stream, payloadOffset, legacyRecords, stagingRoot, progress, logger, cancellationToken);
            if (extractedCount == 0)
            {
                throw new InvalidDataException("Smart Install Maker extraction produced no valid files.");
            }

            PromoteDirectoryContents(stagingDir, extractPath);
            return true;
        }

        var modernFileNames = ParseModernSmartInstallMakerFileTable(fileTableData);
        if (modernFileNames.Count > 0)
        {
            var extractedCount = ExtractModernSmartInstallMakerPayload(stream, payloadOffset, modernFileNames, stagingRoot, progress, logger, cancellationToken);
            if (extractedCount == 0)
            {
                throw new InvalidDataException("Smart Install Maker modern extraction yielded 0 files.");
            }

            PromoteDirectoryContents(stagingDir, extractPath);
            return true;
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2589:Boolean expressions should not be gratuitous", Justification = "Required for C# compiler nullable struct value analysis.")]
    private static (byte[]? TableData, long PayloadOffset) ReadSmartInstallMakerMetadata(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        (long Pos, uint CompSize, byte CompType, long DataStart)? secondToLastBlock = null;
        (long Pos, uint CompSize, byte CompType, long DataStart)? lastBlock = null;
        var blockCount = 0;
        const int MaxBlockWalkCount = 100_000;

        while (stream.Position < stream.Length - 13 && blockCount < MaxBlockWalkCount)
        {
            var pos = stream.Position;
            _ = blockCount == 0 ? reader.ReadInt16() : reader.ReadInt32();
            var compSize = reader.ReadUInt32();
            _ = reader.ReadInt32();
            var compType = reader.ReadByte();
            var dataLength = compSize >= 5 ? (long)compSize - 5 : 0;
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

        if (secondToLastBlock == null || lastBlock == null)
        {
            return (null, -1);
        }

        var payloadOffset = lastBlock.Value.DataStart;
        var tableBlock = secondToLastBlock.Value;

        // Deflate / Zlib
        if (tableBlock.CompType == 1)
        {
            stream.Position = tableBlock.DataStart;
            var b0 = stream.ReadByte();
            var b1 = stream.ReadByte();
            var hasZlibHeader = b0 == 0x78 && (((b0 * 256) + b1) % 31 == 0);
            stream.Position = hasZlibHeader
                ? tableBlock.DataStart + 2
                : tableBlock.DataStart;

            try
            {
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

                return (ms.ToArray(), payloadOffset);
            }
            catch
            {
                // Fallback
            }
        }
        else if (tableBlock.CompType == 2)
        {
            // BZip2
            stream.Position = tableBlock.DataStart;
            try
            {
                using var bz2 = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                    stream,
                    SharpCompress.Compressors.CompressionMode.Decompress,
                    decompressConcatenated: false,
                    leaveOpen: true);
                using var ms = new MemoryStream();
                var buf = new byte[8192];
                var r = 0;
                var totalDecompressed = 0L;
                while ((r = bz2.Read(buf, 0, buf.Length)) > 0)
                {
                    totalDecompressed += r;
                    if (totalDecompressed > CatalogConstants.MaxCatalogSizeBytes)
                    {
                        throw new InvalidDataException("Smart Install Maker metadata table exceeds maximum allowed size.");
                    }

                    ms.Write(buf, 0, r);
                }

                return (ms.ToArray(), payloadOffset);
            }
            catch
            {
                // Fallback
            }
        }
        else if (tableBlock.CompType == 0)
        {
            // Raw
            stream.Position = tableBlock.DataStart;
            var len = (int)Math.Min(tableBlock.CompSize >= 5 ? tableBlock.CompSize - 5 : 0, CatalogConstants.MaxCatalogSizeBytes);
            var buf = new byte[len];
            var read = stream.Read(buf, 0, len);
            return (buf[..read], payloadOffset);
        }

        return (null, payloadOffset);
    }

    private static List<string> ParseModernSmartInstallMakerFileTable(byte[] tableData)
    {
        var names = new List<string>();
        var pos = 0;

        // Modern SIM 5.x header is at least 36 bytes + 120 uninstaller block (156 bytes).
        if (tableData.Length > 156)
        {
            pos = 156;
        }

        while (pos < tableData.Length - 4)
        {
            var nullIdx = Array.IndexOf(tableData, (byte)0, pos);
            if (nullIdx < 0)
            {
                break;
            }

            var strStart = nullIdx - 1;
            while (strStart >= pos && tableData[strStart] >= 32 && tableData[strStart] <= 126)
            {
                strStart--;
            }

            strStart++;

            var strLen = nullIdx - strStart;
            if (strLen >= 3)
            {
                var s = Encoding.Latin1.GetString(tableData, strStart, strLen);
                if (IsValidSimEntryName(s))
                {
                    names.Add(s);
                }
            }

            pos = nullIdx + 1;
        }

        return names;
    }

    private static int ExtractModernSmartInstallMakerPayload(
        Stream stream,
        long payloadOffset,
        IReadOnlyList<string> fileNames,
        string extractRoot,
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var extractedCount = 0;
        stream.Position = payloadOffset;

        // Skip stream 0 (uninstaller info script)
        try
        {
            var nonDisp = new NonDisposingStream(stream);
            var z0 = new SharpCompress.Compressors.Deflate.ZlibStream(nonDisp, SharpCompress.Compressors.CompressionMode.Decompress);
            var buf0 = new byte[8192];
            while (z0.Read(buf0, 0, buf0.Length) > 0)
            {
                // Discard decompressed uninstaller info script stream bytes until EOF.
            }

            stream.Position = payloadOffset + z0.TotalIn;
        }
        catch (Exception ex)
        {
            // If stream 0 decompression fails, reset to payloadOffset
            logger.LogDebug(ex, "Failed to decompress Smart Install Maker stream 0 script");
            stream.Position = payloadOffset;
        }

        var copyBuffer = new byte[65536];
        var totalFiles = fileNames.Count;
        for (var fileIdx = 0; fileIdx < totalFiles && stream.Position < stream.Length - 4; fileIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = fileNames[fileIdx];
            var pathResult = ContentPathPolicy.ResolveContainedFile(extractRoot, fileName);
            if (!pathResult.Success)
            {
                throw new InvalidDataException($"Smart Install Maker modern entry has an unsafe path: {fileName}");
            }

            var destinationPath = pathResult.Data ?? string.Empty;
            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            var stageProgress = totalFiles > 0 ? (double)(fileIdx + 1) / totalFiles * 100 : 100;
            var shortName = Path.GetFileName(fileName);
            progress?.Report(new ContentAcquisitionProgress
            {
                CurrentStage = 3,
                TotalStages = 5,
                StageDescription = ExtractingFilesStageDescription,
                CurrentOperation = $"Extracting {shortName}",
                FilesProcessed = fileIdx + 1,
                TotalFiles = totalFiles,
                StageProgress = stageProgress,
            });

            var streamStartPos = stream.Position;
            var byte0 = stream.ReadByte();
            if (byte0 < 0)
            {
                break;
            }

            var byte1 = stream.ReadByte();
            if (byte1 < 0)
            {
                break;
            }

            stream.Position = streamStartPos;
            using var outStream = File.Create(destinationPath);

            if (byte0 == 0x78)
            {
                // ZLib stream
                var nonDisp = new NonDisposingStream(stream);
                var z = new SharpCompress.Compressors.Deflate.ZlibStream(nonDisp, SharpCompress.Compressors.CompressionMode.Decompress);

                var rZ = 0;
                while ((rZ = z.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                {
                    outStream.Write(copyBuffer, 0, rZ);
                }

                stream.Position = streamStartPos + z.TotalIn;
            }
            else if (byte0 == 0x42 && byte1 == 0x5A)
            {
                // BZip2 stream ('BZ')
                using var bz = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                    stream,
                    SharpCompress.Compressors.CompressionMode.Decompress,
                    decompressConcatenated: false,
                    leaveOpen: true);

                var rBz = 0;
                while ((rBz = bz.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                {
                    outStream.Write(copyBuffer, 0, rBz);
                }
            }
            else if (byte0 == 2)
            {
                // Legacy SIM BZip2 with prefix
                stream.Position = streamStartPos + 1;
                using var bz = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                    stream,
                    SharpCompress.Compressors.CompressionMode.Decompress,
                    decompressConcatenated: false,
                    leaveOpen: true);

                var rBz = 0;
                while ((rBz = bz.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                {
                    outStream.Write(copyBuffer, 0, rBz);
                }
            }
            else if (byte0 == 1)
            {
                // Legacy SIM ZLib with prefix
                stream.Position = streamStartPos + 1;
                var nonDisp = new NonDisposingStream(stream);
                var z = new SharpCompress.Compressors.Deflate.ZlibStream(nonDisp, SharpCompress.Compressors.CompressionMode.Decompress);

                var rZ = 0;
                while ((rZ = z.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                {
                    outStream.Write(copyBuffer, 0, rZ);
                }

                stream.Position = streamStartPos + 1 + z.TotalIn;
            }
            else
            {
                // Raw uncompressed copy
                var rRaw = stream.Read(copyBuffer, 0, copyBuffer.Length);
                outStream.Write(copyBuffer, 0, rRaw);
            }

            outStream.Flush();
            var fileLength = new FileInfo(destinationPath).Length;
            if (fileLength > 0)
            {
                extractedCount++;
            }

            logger.LogInformation("Extracted installer file {Current}/{Total}: {FileName} ({Size} bytes)", fileIdx + 1, totalFiles, fileName, fileLength);
        }

        return extractedCount;
    }

    private static int ExtractSmartInstallMakerPayload(
        Stream stream,
        long payloadOffset,
        List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> records,
        string extractRoot,
        IProgress<ContentAcquisitionProgress>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var extractedCount = 0;
        var copyBuffer = new byte[65536];
        var totalRecords = records.Count;

        for (var i = 0; i < totalRecords; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rec = records[i];
            var stageProgress = (double)(i + 1) / totalRecords * 100;
            var shortName = Path.GetFileName(rec.Name);
            progress?.Report(new ContentAcquisitionProgress
            {
                CurrentStage = 3,
                TotalStages = 5,
                StageDescription = ExtractingFilesStageDescription,
                CurrentOperation = $"Extracting {shortName}",
                FilesProcessed = i + 1,
                TotalFiles = totalRecords,
                StageProgress = stageProgress,
            });

            try
            {
                ExtractSingleSmartInstallMakerRecord(stream, payloadOffset, rec, extractRoot, copyBuffer);
                extractedCount++;
                logger.LogInformation("Extracted installer file {Current}/{Total}: {FileName} ({Size} bytes)", i + 1, totalRecords, rec.Name, rec.UncompressedSize);
            }
            catch (Exception)
            {
                // If a non-essential file or uninstaller descriptor failed, keep extracting remaining files
                if (records.Count > 1 &&
                    GameContentConstants.DocumentationExtensions.Contains(Path.GetExtension(rec.Name), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw;
            }
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
        if (!pathResult.Success)
        {
            throw new InvalidDataException($"Smart Install Maker entry has an unsafe path: {rec.Name}");
        }

        var destinationPath = pathResult.Data ?? string.Empty;
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var filePos = payloadOffset + rec.StreamOffset;
        if (filePos < 0 || filePos > stream.Length || filePos + rec.CompressedSize > stream.Length + 4)
        {
            throw new InvalidDataException($"Smart Install Maker entry '{rec.Name}' compressed range exceeds stream bounds.");
        }

        stream.Position = filePos;
        var header = new byte[4];
        var headerRead = stream.Read(header, 0, 4);
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
        long written = 0;

        if (headerRead >= 2 && header[0] == (byte)'B' && header[1] == (byte)'Z')
        {
            written = TryDecompressBZip2(stream, destinationPath, uncompressedSize, copyBuffer);
        }
        else if (headerRead >= 2 && header[0] == 0x78 && (((header[0] * 256) + header[1]) % 31 == 0))
        {
            written = TryDecompressDeflate(stream, filePos, destinationPath, uncompressedSize, copyBuffer);
        }
        else if (headerRead >= 2 && header[0] == 0x1F && header[1] == 0x8B)
        {
            written = TryDecompressGZip(stream, filePos, destinationPath, uncompressedSize, copyBuffer);
        }

        if (written == 0)
        {
            written = TryCopyRawStream(stream, filePos, destinationPath, uncompressedSize, copyBuffer);
        }

        return written;
    }

    private static long CopyStreamUpToCap(Stream input, Stream output, uint maxBytes, byte[] copyBuffer)
    {
        long written = 0;
        while (written < maxBytes)
        {
            var toRead = (int)Math.Min(copyBuffer.Length, maxBytes - written);
            var readBytes = input.Read(copyBuffer, 0, toRead);
            if (readBytes <= 0)
            {
                break;
            }

            output.Write(copyBuffer, 0, readBytes);
            written += readBytes;
        }

        return written;
    }

    private static long TryDecompressBZip2(Stream stream, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        try
        {
            using var bz2 = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                stream,
                SharpCompress.Compressors.CompressionMode.Decompress,
                decompressConcatenated: false,
                leaveOpen: true);

            using var outStream = File.Create(destinationPath);
            return CopyStreamUpToCap(bz2, outStream, uncompressedSize, copyBuffer);
        }
        catch
        {
            return 0;
        }
    }

    private static long TryDecompressDeflate(Stream stream, long filePos, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        try
        {
            stream.Position = filePos + 2; // skip zlib header
            using var def = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var outStream = File.Create(destinationPath);
            return CopyStreamUpToCap(def, outStream, uncompressedSize, copyBuffer);
        }
        catch
        {
            return 0;
        }
    }

    private static long TryDecompressGZip(Stream stream, long filePos, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        try
        {
            stream.Position = filePos;
            using var gz = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var outStream = File.Create(destinationPath);
            return CopyStreamUpToCap(gz, outStream, uncompressedSize, copyBuffer);
        }
        catch
        {
            return 0;
        }
    }

    private static long TryCopyRawStream(Stream stream, long filePos, string destinationPath, uint uncompressedSize, byte[] copyBuffer)
    {
        try
        {
            stream.Position = filePos;
            using var outStream = File.Create(destinationPath);
            return CopyStreamUpToCap(stream, outStream, uncompressedSize, copyBuffer);
        }
        catch
        {
            return 0;
        }
    }

    private static List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)> ParseSmartInstallMakerFileTable(
        byte[] tableData,
        Stream stream,
        long payloadOffset)
    {
        var records = new List<(string Name, uint UncompressedSize, uint StreamOffset, uint CompressedSize)>();
        var cumulativeUncompressedSize = 0L;

        var i = 0;
        while (i < tableData.Length - 4)
        {
            if (tableData[i] != '.' || i < 40)
            {
                i++;
                continue;
            }

            if (!TryExtractSimCandidateName(tableData, i, out var name, out var nextIndex, out var startOffset))
            {
                i++;
                continue;
            }

            i = nextIndex + 1;

            if (!IsValidSimEntryName(name))
            {
                continue;
            }

            if (TryReadSimRecord(tableData, startOffset, name, stream, payloadOffset, records, out var record))
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
        }

        return records;
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
            name.EndsWith("intrnl.exe", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("uninstaller.exe", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("uninst.exe", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("unwise", StringComparison.OrdinalIgnoreCase))
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
            payloadOffset + streamOffset + compSize > stream.Length + 4)
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
            .OfType<string>();

        if (subDirs.Any(name => GameContentConstants.RecognizedGameDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetExtension)
            .OfType<string>();

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
                MoveFileToPromotedDestination(subFile, tempStaging, targetDirectory);
            }
        }
        catch
        {
            RollbackPromoteStaging(tempStaging, sourceDirectory);
            throw;
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                try
                {
                    Directory.Delete(tempStaging, recursive: true);
                }
                catch
                {
                    // Ignore temp staging cleanup failures
                }
            }
        }
    }

    private static void MoveFileToPromotedDestination(string subFile, string tempStaging, string targetDirectory)
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

    private static void RollbackPromoteStaging(string tempStaging, string sourceDirectory)
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

    private static string GetNonCollidingDestinationPath(string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
        var ext = Path.GetExtension(destinationPath);
        var counter = 1;
        var newDestPath = string.Empty;
        do
        {
            newDestPath = Path.Combine(dir, $"{fileNameWithoutExt}_{counter}{ext}");
            counter++;
        }
        while (File.Exists(newDestPath));

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

        var bytesRead1 = 0;
        while ((bytesRead1 = s1.Read(buffer1, 0, bufferSize)) > 0)
        {
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
                         .Where(subDir => Directory.Exists(subDir) && !Directory.EnumerateFileSystemEntries(subDir).Any())
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

    private void NormalizeInactiveBigExtensions(string extractedDirectory, ContentType contentType)
    {
        if (contentType is ContentType.ModdingTool or ContentType.Executable or ContentType.GameClient or ContentType.GameInstallation)
        {
            return;
        }

        try
        {
            foreach (var extension in GenLauncherConstants.InactiveBigExtensions)
            {
                var searchPattern = "*" + extension;
                foreach (var inactiveFile in Directory.GetFiles(extractedDirectory, searchPattern, SearchOption.AllDirectories))
                {
                    if (IsExecutableFile(inactiveFile))
                    {
                        var exeFile = Path.ChangeExtension(inactiveFile, ".exe");
                        if (!File.Exists(exeFile))
                        {
                            File.Move(inactiveFile, exeFile);
                            logger.LogInformation("Normalized disguised executable '{InactiveFile}' to '{ExeFile}'", inactiveFile, exeFile);
                        }

                        continue;
                    }

                    if (!IsBigArchiveFile(inactiveFile))
                    {
                        logger.LogDebug("Skipping non-BIG inactive file '{InactiveFile}' during archive normalization", inactiveFile);
                        continue;
                    }

                    var bigFile = Path.ChangeExtension(inactiveFile, GenLauncherConstants.BigExtension);
                    if (File.Exists(bigFile))
                    {
                        if (FilesHaveIdenticalContent(inactiveFile, bigFile))
                        {
                            File.Delete(inactiveFile);
                            logger.LogInformation("Removed duplicate identical inactive file '{InactiveFile}' as '{BigFile}' already exists", inactiveFile, bigFile);
                        }
                        else
                        {
                            var nonCollidingBigPath = GetNonCollidingDestinationPath(bigFile);
                            File.Move(inactiveFile, nonCollidingBigPath);
                            logger.LogInformation("Preserved differing inactive file '{InactiveFile}' by renaming to '{NewBigFile}'", inactiveFile, nonCollidingBigPath);
                        }
                    }
                    else
                    {
                        File.Move(inactiveFile, bigFile);
                        logger.LogInformation("Normalized inactive mod archive '{InactiveFile}' to '{BigFile}'", inactiveFile, bigFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to normalize inactive mod archive file extensions in: {Directory}", extractedDirectory);
        }
    }

    private sealed class SubStream(Stream baseStream, long startOffset, long length) : Stream
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
            baseStream.Position = startOffset + _position;
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

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // Do not dispose the inner stream.
        }
    }
}
