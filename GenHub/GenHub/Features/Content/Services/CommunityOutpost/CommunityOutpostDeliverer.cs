using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Specialized deliverer for Community Outpost content.
/// Downloads packages (ZIP or 7z/.dat files), extracts files, and creates manifests via factory.
/// Supports multiple download mirrors for fallback.
/// </summary>
public partial class CommunityOutpostDeliverer(
   IDownloadService downloadService,
   CompressedImageToTgaConverter avifConverter,
   ILogger<CommunityOutpostDeliverer> logger,
   IHttpClientFactory? httpClientFactory = null)
   : IContentDeliverer
{
    private static (string Code, GenPatcherContentMetadata Metadata) NormalizeContentCode(string contentCode)
    {
        // For some content (like cbprc), the code may have a language suffix (e - english)
        // Strip it if it's there and try that way too
        var actualContentCode = contentCode.ToLowerInvariant();
        var depMetadata = GenPatcherContentRegistry.GetMetadata(actualContentCode);

        if (depMetadata.ContentType == ContentType.UnknownContentType && actualContentCode.Length == 5)
        {
            var strippedCode = actualContentCode[..4];
            var strippedMetadata = GenPatcherContentRegistry.GetMetadata(strippedCode);
            if (strippedMetadata.ContentType != ContentType.UnknownContentType)
            {
                actualContentCode = strippedCode;
                depMetadata = strippedMetadata;
            }
        }

        return (actualContentCode, depMetadata);
    }

    private static ManifestFile? FindDownloadableArchive(ContentManifest packageManifest)
    {
        return packageManifest.Files.FirstOrDefault(f =>
            !string.IsNullOrEmpty(f.DownloadUrl) &&
            (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
             f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Extracts the content code from the manifest metadata.
    /// </summary>
    private static string GetContentCodeFromManifest(ContentManifest manifest)
    {
        var codeFromTag = TryExtractCodeFromTag(manifest);
        if (codeFromTag != null)
        {
            return codeFromTag;
        }

        var codeFromId = TryExtractCodeFromId(manifest);
        if (codeFromId != null)
        {
            return codeFromId;
        }

        return "unknown";
    }

    private static string? TryExtractCodeFromTag(ContentManifest manifest)
    {
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(t => t.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(contentCodeTag))
        {
            return null;
        }

        var tagValue = contentCodeTag["contentCode:".Length..];
        var directMeta = GenPatcherContentRegistry.GetMetadata(tagValue);
        if (directMeta.ContentType != ContentType.UnknownContentType)
        {
            return directMeta.ContentCode;
        }

        var dashIdx = tagValue.IndexOf('-');
        if (dashIdx > 0)
        {
            var prefix = tagValue[..dashIdx];
            var prefixMeta = GenPatcherContentRegistry.GetMetadata(prefix);
            if (prefixMeta.ContentType != ContentType.UnknownContentType)
            {
                return prefixMeta.ContentCode;
            }
        }

        return tagValue;
    }

    private static string? TryExtractCodeFromId(ContentManifest manifest)
    {
        // Format: 1.version.communityoutpost.contentType.contentName
        var idParts = manifest.Id.Value?.Split('.') ?? [];
        if (idParts.Length < 5)
        {
            return null;
        }

        var contentName = idParts[4];
        var metadata = GenPatcherContentRegistry.GetMetadata(contentName);
        if (metadata.ContentType != ContentType.UnknownContentType)
        {
            return metadata.ContentCode;
        }

        var dashIndex = contentName.IndexOf('-');
        var codePrefix = dashIndex > 0 ? contentName[..dashIndex] : contentName;
        var prefixMetadata = GenPatcherContentRegistry.GetMetadata(codePrefix);
        if (prefixMetadata.ContentType != ContentType.UnknownContentType)
        {
            return prefixMetadata.ContentCode;
        }

        foreach (var code in GenPatcherContentRegistry.GetKnownContentCodes())
        {
            if (contentName.StartsWith(code, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return codePrefix;
    }

    [GeneratedRegex(@"href=[""']([^""']*generals-?zh.*?(\d{4}-\d{2}-\d{2}|\d{2}-\d{2}-\d{4}|\d{8}|\d{6}).*?\.(?:zip|7z|rar|exe))[""']", RegexOptions.IgnoreCase)]
    private static partial Regex CommunityPatchRegex();

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

    /// <summary>
    /// Extracts an archive (ZIP, 7z, etc.) asynchronously using SharpCompress.
    /// Automatically detects format. Catalog archives are third-party input, so every entry is
    /// confined to <paramref name="extractPath"/> and the archive is held to entry-count and
    /// expansion budgets measured against the bytes actually decompressed.
    /// </summary>
    private static async Task ExtractArchiveAsync(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            async () =>
            {
                var fileInfo = new FileInfo(archivePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    throw new FileNotFoundException($"Archive file not found or empty: {archivePath}");
                }

                EnsureValidArchivePayload(archivePath);

                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var fileEntries = archive.Entries.Where(e => !e.IsDirectory).ToList();

                if (fileEntries.Count > CommunityOutpostConstants.MaxArchiveEntries)
                {
                    throw new InvalidOperationException(
                        $"Archive contains too many entries ({fileEntries.Count} > {CommunityOutpostConstants.MaxArchiveEntries}).");
                }

                long expandedBytes = 0;

                foreach (var entry in fileEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!ArchiveEntryName.IsExtractable(entry.Key))
                    {
                        throw new InvalidOperationException(
                            $"Archive entry '{entry.Key}' has a name that cannot be extracted to a file.");
                    }

                    var destinationPath = Path.GetFullPath(Path.Combine(extractPath, entry.Key));
                    if (!PathHelper.IsPathWithinDirectory(extractPath, destinationPath))
                    {
                        throw new InvalidOperationException(
                            $"Zip slip vulnerability detected: entry '{entry.Key}' attempts to extract outside target directory.");
                    }

                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    await using var entryStream = entry.OpenEntryStream();
                    expandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                        entryStream,
                        destinationPath,
                        entry.Key,
                        CommunityOutpostConstants.MaxEntryUncompressedBytes,
                        CommunityOutpostConstants.MaxAggregateUncompressedBytes - expandedBytes,
                        overwrite: true,
                        cancellationToken);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Creates a generic manifest when no specialized content types are detected.
    /// </summary>
    private static async Task<List<ContentManifest>> CreateGenericManifestAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            return [];
        }

        List<ManifestFile> manifestFiles = [];

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var relativePath = Path.GetRelativePath(extractedDirectory, file);
            var fileInfo = new FileInfo(file);

            manifestFiles.Add(new ManifestFile
            {
                RelativePath = relativePath,
                Size = fileInfo.Length,
                IsRequired = true,
                IsExecutable = ExecutableFileClassifier.RequiresExecutePermission(relativePath, file),
                SourceType = ContentSourceType.ExtractedPackage,
            });
        }

        var manifest = new ContentManifest
        {
            Id = originalManifest.Id,
            Name = originalManifest.Name,
            Version = !string.IsNullOrWhiteSpace(originalManifest.Version)
                ? originalManifest.Version
                : CommunityOutpostCatalogConstants.DefaultMetadataVersion,
            SchemaVersion = originalManifest.SchemaVersion,
            ContentType = originalManifest.ContentType,
            TargetGame = originalManifest.TargetGame,
            Publisher = originalManifest.Publisher,
            Metadata = originalManifest.Metadata,
            Dependencies = originalManifest.Dependencies,
            Files = manifestFiles,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        return await Task.FromResult(new List<ContentManifest> { manifest });
    }

    private static void AddArchiveCandidateUrls(ManifestFile archiveFile, List<string> candidateUrls)
    {
        if (string.IsNullOrEmpty(archiveFile.DownloadUrl))
        {
            return;
        }

        if (archiveFile.DownloadUrl.Contains("/patch/", StringComparison.OrdinalIgnoreCase) &&
            archiveFile.DownloadUrl.EndsWith(CommunityOutpostConstants.DatFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            candidateUrls.Add(archiveFile.DownloadUrl.Replace("/patch/", "/gp2/f/", StringComparison.OrdinalIgnoreCase));
        }

        candidateUrls.Add(archiveFile.DownloadUrl);
    }

    private static void TryCleanFailedExtraction(string extractPath, string archivePath)
    {
        if (Directory.Exists(extractPath))
        {
            try
            {
                Directory.Delete(extractPath, recursive: true);
            }
            catch (IOException)
            {
                // Ignore extraction cleanup failures
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore extraction cleanup failures
            }

            Directory.CreateDirectory(extractPath);
        }

        if (File.Exists(archivePath))
        {
            try
            {
                File.Delete(archivePath);
            }
            catch (IOException)
            {
                // Ignore archive cleanup failures
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore archive cleanup failures
            }
        }
    }

    /// <inheritdoc />
    public string SourceName => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc />
    public string Description => CommunityOutpostConstants.DelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if it's a Community Outpost manifest with a downloadable file
        // Note: PublisherType in manifest is "communityoutpost" (no hyphen)
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals(
                   CommunityOutpostConstants.PublisherType,
                   StringComparison.OrdinalIgnoreCase) == true ||
               manifest.OriginalProviderName?.Equals(
                   CommunityOutpostConstants.PublisherType,
                   StringComparison.OrdinalIgnoreCase) == true;

        return publisherMatches &&
               manifest.Files.Any(f =>
                   !string.IsNullOrEmpty(f.DownloadUrl) &&
                   (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                    f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Starting Community Outpost content delivery for {ManifestId} (v{Version})",
                packageManifest.Id,
                packageManifest.Version);

            var archiveFile = FindDownloadableArchive(packageManifest);
            if (archiveFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("No downloadable archive found in manifest");
            }

            var isSevenZip = archiveFile.SourcePath == "archive:7z" ||
                            archiveFile.DownloadUrl!.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                            archiveFile.DownloadUrl!.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

            var archiveExtension = isSevenZip ? ".7z" : ".zip";
            var archivePath = Path.Combine(targetDirectory, $"content{archiveExtension}");
            var candidateUrls = await CollectCandidateUrlsAsync(archiveFile, packageManifest, cancellationToken);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = 10,
                CurrentOperation = "Downloading Community Outpost package",
                CurrentFile = archiveFile.RelativePath,
            });

            var extractPath = Path.Combine(targetDirectory, "extracted");
            Directory.CreateDirectory(extractPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 40,
                CurrentOperation = isSevenZip
                    ? "Extracting 7z archive"
                    : "Extracting ZIP archive",
            });

            logger.LogDebug("Extracting {ArchiveType} to {Path}", isSevenZip ? "7z" : "ZIP", extractPath);

            var extractResult = await DownloadAndExtractArchiveAsync(candidateUrls, archivePath, extractPath, cancellationToken);
            if (!extractResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(extractResult);
            }

            await RepackContentIfNeededAsync(packageManifest, extractPath, cancellationToken);
            await ProcessAndMergeDependencyBigFilesAsync(packageManifest, extractPath, cancellationToken);

            var moveResult = await MoveExtractedFilesToStagingRootAsync(extractPath, targetDirectory);
            if (!moveResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(moveResult);
            }

            await CleanupTemporaryFilesAsync(archivePath, extractPath);
            return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver Community Outpost content");
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hasArchiveFile = manifest.Files.Any(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));

            return Task.FromResult(OperationResult<bool>.CreateSuccess(hasArchiveFile));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Validation failed for Community Outpost manifest {ManifestId}",
                manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    private async Task<List<string>> CollectCandidateUrlsAsync(
        ManifestFile archiveFile,
        ContentManifest packageManifest,
        CancellationToken cancellationToken)
    {
        var candidateUrls = new List<string>();
        AddArchiveCandidateUrls(archiveFile, candidateUrls);
        await AddContentCodeCandidateUrlsAsync(packageManifest, candidateUrls, cancellationToken);
        return candidateUrls;
    }

    private async Task AddContentCodeCandidateUrlsAsync(
        ContentManifest packageManifest,
        List<string> candidateUrls,
        CancellationToken cancellationToken)
    {
        var contentCode = GetContentCodeFromManifest(packageManifest);
        if (string.IsNullOrEmpty(contentCode) || string.Equals(contentCode, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (normalizedCode, _) = NormalizeContentCode(contentCode);

        if (string.Equals(normalizedCode, "community-patch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedCode, "communitypatch", StringComparison.OrdinalIgnoreCase))
        {
            await AddCommunityPatchCandidateUrlsAsync(candidateUrls, cancellationToken);
        }
        else
        {
            var fallbackGp2Url = $"{CommunityOutpostCatalogConstants.DefaultFilesBaseUrl.TrimEnd('/')}/{normalizedCode}.dat";
            if (!candidateUrls.Contains(fallbackGp2Url, StringComparer.OrdinalIgnoreCase))
            {
                candidateUrls.Add(fallbackGp2Url);
            }
        }
    }

    private async Task AddCommunityPatchCandidateUrlsAsync(
        List<string> candidateUrls,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory?.CreateClient() ?? new HttpClient();
            var pageContent = await client.GetStringAsync(CommunityOutpostConstants.PatchPageUrl, cancellationToken);
            var match = CommunityPatchRegex().Match(pageContent);
            if (match.Success)
            {
                var liveUrl = match.Groups[1].Value;
                if (!liveUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    liveUrl = $"{CommunityOutpostConstants.PatchPageUrl.TrimEnd('/')}/{liveUrl.TrimStart('/')}";
                }

                if (!candidateUrls.Contains(liveUrl, StringComparer.OrdinalIgnoreCase))
                {
                    candidateUrls.Insert(0, liveUrl);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not scrape live community patch URL from {Url}", CommunityOutpostConstants.PatchPageUrl);
        }

        var githubFallbackUrl = $"https://github.com/{SuperHackersConstants.GeneralsGameCodeOwner}/{SuperHackersConstants.GeneralsGameCodeRepo}/releases/latest/download/generalszh-latest.zip";
        if (!candidateUrls.Contains(githubFallbackUrl, StringComparer.OrdinalIgnoreCase))
        {
            candidateUrls.Add(githubFallbackUrl);
        }
    }

    private async Task<OperationResult<bool>> DownloadAndExtractArchiveAsync(
        List<string> candidateUrls,
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        string? lastError = null;

        foreach (var downloadUrl in candidateUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extractResult = await TryExtractCandidateAsync(downloadUrl, archivePath, extractPath, cancellationToken);
            if (extractResult.Success)
            {
                return extractResult;
            }

            lastError = extractResult.FirstError;
        }

        logger.LogError("Failed to extract Community Outpost archive from all attempted URLs: {Error}", lastError);
        return OperationResult<bool>.CreateFailure($"Extraction failed: {lastError}");
    }

    private async Task<OperationResult<bool>> TryExtractCandidateAsync(
        string downloadUrl,
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var downloadResult = await DownloadWithMirrorFallbackAsync(downloadUrl, archivePath, cancellationToken);
        if (!downloadResult.Success)
        {
            return downloadResult;
        }

        try
        {
            await ExtractArchiveAsync(archivePath, extractPath, cancellationToken);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract archive downloaded from {Url}, attempting fallback if available", downloadUrl);
            TryCleanFailedExtraction(extractPath, archivePath);
            return OperationResult<bool>.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Downloads a file with mirror fallback support.
    /// </summary>
    private async Task<OperationResult<bool>> DownloadWithMirrorFallbackAsync(
        string primaryUrl,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // Try primary URL first
        logger.LogDebug("Downloading from primary URL: {Url}", primaryUrl);
        var result = await downloadService.DownloadFileAsync(
            new Uri(primaryUrl),
            targetPath,
            expectedHash: null,
            progress: null,
            cancellationToken);

        if (result.Success)
        {
            if (File.Exists(targetPath))
            {
                try
                {
                    EnsureValidArchivePayload(targetPath);
                    return OperationResult<bool>.CreateSuccess(true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Downloaded file from {Url} is not a valid archive: {Message}", primaryUrl, ex.Message);
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogDebug(deleteEx, "Failed to delete invalid archive at {Path}", targetPath);
                    }

                    return OperationResult<bool>.CreateFailure($"Downloaded payload from {primaryUrl} is not a valid archive: {ex.Message}");
                }
            }

            return OperationResult<bool>.CreateSuccess(true);
        }

        logger.LogWarning("Primary download failed: {Error}", result.FirstError);

        // Note: Mirror URLs would be stored in the original search result metadata
        // For now, we only try the primary URL since we don't have easy access
        // to the original metadata here. In a future enhancement, we could
        // store mirror URLs in the manifest or pass them through.
        return OperationResult<bool>.CreateFailure($"Download failed: {result.FirstError}");
    }

    /// <summary>
    /// Moves all extracted files from the extraction subdirectory into the staging root so that
    /// downstream orchestrator stages (factory post-processing, validation) can find them.
    /// </summary>
    /// <param name="extractPath">The extraction subdirectory.</param>
    /// <param name="stagingRoot">The staging root directory.</param>
    private async Task<OperationResult<bool>> MoveExtractedFilesToStagingRootAsync(string extractPath, string stagingRoot)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(extractPath))
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            foreach (var file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
            {
                var moveResult = MoveSingleExtractedFile(file, extractPath, stagingRoot);
                if (!moveResult.Success)
                {
                    return moveResult;
                }
            }

            return OperationResult<bool>.CreateSuccess(true);
        });
    }

    private OperationResult<bool> MoveSingleExtractedFile(string file, string extractPath, string stagingRoot)
    {
        var relativePath = Path.GetRelativePath(extractPath, file);
        var pathResult = ContentPathPolicy.ResolveContainedFile(stagingRoot, relativePath);
        if (!pathResult.Success)
        {
            return OperationResult<bool>.CreateFailure(pathResult);
        }

        var targetPath = pathResult.Data;
        if (string.IsNullOrEmpty(targetPath))
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        try
        {
            File.Move(file, targetPath, overwrite: true);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to move extracted file {File} to staging root", file);
            return OperationResult<bool>.CreateFailure($"Failed to move extracted file '{relativePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up temporary files after extraction.
    /// </summary>
    private async Task CleanupTemporaryFilesAsync(string archivePath, string extractPath)
    {
        await Task.Run(() =>
        {
            // Delete archive file
            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                    logger.LogDebug("Deleted archive file: {Path}", archivePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete archive file {Path}", archivePath);
            }

            // Delete extracted directory
            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, recursive: true);
                    logger.LogDebug("Deleted extracted directory: {Path}", extractPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete extracted directory {Path}", extractPath);
            }
        });
    }

    /// <summary>
    /// Repacks extracted content into a single .big file if required by metadata.
    /// </summary>
    private async Task RepackContentIfNeededAsync(
        ContentManifest manifest,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var contentCode = GetContentCodeFromManifest(manifest);
        var (_, metadata) = NormalizeContentCode(contentCode);

        if (metadata.RequiresRepacking && !string.IsNullOrEmpty(metadata.OutputFilename))
        {
            // Variant-based output filenames (e.g., 340_ControlBarPro{variant}ZH.big)
            // must be handled later when a specific variant is selected.
            if (metadata.OutputFilename.Contains("{variant}", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(
                    "Skipping repack at delivery stage for {ContentCode} because output filename is variant-based: {OutputFilename}",
                    contentCode,
                    metadata.OutputFilename);
                return;
            }

            // If a correctly named BIG file already exists in the extracted content, do not repack.
            var existingBig = Directory.GetFiles(extractPath, metadata.OutputFilename, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(existingBig))
            {
                logger.LogInformation(
                    "Skipping repack for {ContentCode} because {OutputFilename} already exists in extracted content",
                    contentCode,
                    metadata.OutputFilename);
                return;
            }

            logger.LogInformation(
                "Repacking content for {ContentCode} into {OutputFilename}",
                contentCode,
                metadata.OutputFilename);

            // Create a temporary directory for the packed file
            var packDir = Path.Combine(Directory.GetParent(extractPath)!.FullName, "packed");
            Directory.CreateDirectory(packDir);
            var destinationPath = Path.Combine(packDir, metadata.OutputFilename);

            // Pack the files
            // GenPatcher archives often extract to nested ZH\BIG or CCG\BIG folders. We must pack the BIG folder contents,
            // not the parent folder, to avoid embedding extra path prefixes inside the .big.
            var bigDirectories = Directory.GetDirectories(extractPath, "BIG*", SearchOption.AllDirectories);
            var packSource = extractPath;

            if (bigDirectories.Length > 0)
            {
                bool IsUnder(string path, string folder)
                {
                    return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(segment => segment.Equals(folder, StringComparison.OrdinalIgnoreCase));
                }

                bool EndsWithSegment(string path, string segment)
                {
                    return path.EndsWith(segment, StringComparison.OrdinalIgnoreCase);
                }

                var preferred = bigDirectories
                    .FirstOrDefault(d => IsUnder(d, "ZH") && EndsWithSegment(d, "BIG EN"))
                    ?? bigDirectories.FirstOrDefault(d => IsUnder(d, "ZH") && EndsWithSegment(d, "BIG"))
                    ?? bigDirectories.FirstOrDefault(d => IsUnder(d, "CCG") && EndsWithSegment(d, "BIG EN"))
                    ?? bigDirectories.FirstOrDefault(d => IsUnder(d, "CCG") && EndsWithSegment(d, "BIG"))
                    ?? bigDirectories.First();

                packSource = preferred;
            }

            // Convert compressed image files (AVIF, WebP) to TGA format before packing
            // GenPatcher dat archives contain AVIF/WebP for compression, but the game requires TGA textures
            var compressedImageCount = Directory.GetFiles(packSource, "*.avif", SearchOption.AllDirectories).Length
                + Directory.GetFiles(packSource, "*.webp", SearchOption.AllDirectories).Length;
            if (compressedImageCount > 0)
            {
                logger.LogInformation(
                    "Converting {Count} compressed image files to TGA format for game compatibility",
                    compressedImageCount);

                var convertedCount = await avifConverter.ConvertDirectoryAsync(packSource, cancellationToken);
                logger.LogInformation("Converted {Converted} compressed image files to TGA", convertedCount);
            }

            await BigFilePacker.PackAsync(packSource, destinationPath);

            // Clear the ExtractPath and move the packed file there
            // This ensures the manifest factory only sees the packed file
            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                Directory.CreateDirectory(extractPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reset extract path {ExtractPath} during repacking", extractPath);
                throw new IOException($"Failed to prepare extraction directory: {ex.Message}", ex);
            }

            File.Move(destinationPath, Path.Combine(extractPath, metadata.OutputFilename));

            // Cleanup packDir
            try
            {
                if (Directory.Exists(packDir))
                {
                    Directory.Delete(packDir, true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to cleanup temporary pack directory {PackDir}", packDir);
            }

            logger.LogInformation("Repacking completed successfully");
        }
    }

    /// <summary>
    /// Processes AutoInstall dependencies by downloading, repacking them, and copying their BIG files
    /// into the main extract path so they become part of the same manifest.
    /// </summary>
    private async Task ProcessAndMergeDependencyBigFilesAsync(
        ContentManifest packageManifest,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var packageContentCode = GetContentCodeFromManifest(packageManifest);
        var (_, packageMetadata) = NormalizeContentCode(packageContentCode);
        var hasControlBarProBigs = false;

        if (packageMetadata.Category == GenPatcherContentCategory.ControlBar && packageMetadata.SupportsVariants)
        {
            hasControlBarProBigs = Directory.GetFiles(extractPath, "*ControlBarPro*ZH.big", SearchOption.AllDirectories)
                .Any(path => !Path.GetFileName(path).Contains("Core", StringComparison.OrdinalIgnoreCase));
        }

        var metadataDeps = packageMetadata.GetDependencies()
            .Where(d => d.InstallBehavior == DependencyInstallBehavior.AutoInstall);
        var manifestDeps = (packageManifest.Dependencies ?? Enumerable.Empty<ContentDependency>())
            .Where(d => d.InstallBehavior == DependencyInstallBehavior.AutoInstall);

        var autoInstallDeps = metadataDeps
            .Concat(manifestDeps)
            .GroupBy(d => d.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (autoInstallDeps.Count == 0)
        {
            logger.LogDebug("No auto-install dependencies to process");
            return;
        }

        logger.LogInformation(
            "Processing {Count} auto-install dependencies - their BIG files will be added to the main manifest",
            autoInstallDeps.Count);

        foreach (var dep in autoInstallDeps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessSingleAutoInstallDependencyAsync(
                    dep,
                    packageMetadata,
                    hasControlBarProBigs,
                    extractPath,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to process dependency {Name}", dep.Name);
            }
        }

        logger.LogInformation("Finished processing auto-install dependencies");
    }

    private async Task ProcessSingleAutoInstallDependencyAsync(
        ContentDependency dep,
        GenPatcherContentMetadata packageMetadata,
        bool hasControlBarProBigs,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var manifestIdStr = dep.Id.Value;
        var lastDotIndex = manifestIdStr.LastIndexOf('.');
        if (lastDotIndex < 0)
        {
            logger.LogWarning("Cannot extract content code from dependency ID: {Id}", manifestIdStr);
            return;
        }

        var depContentCode = manifestIdStr[(lastDotIndex + 1)..];
        var (actualContentCode, depMetadata) = NormalizeContentCode(depContentCode);

        logger.LogInformation(
            "Processing dependency: {Name} (code: {Code}) - will add its BIG file to main manifest",
            dep.Name ?? dep.Id.Value,
            actualContentCode);

        if (ShouldSkipDependency(dep, depMetadata, packageMetadata, hasControlBarProBigs, extractPath))
        {
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N");
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "DepBigFiles", uniqueId);
        var depArchive = Path.Combine(tempDir, $"{actualContentCode}.dat");
        var depExtractPath = Path.Combine(tempDir, actualContentCode);
        Directory.CreateDirectory(tempDir);

        try
        {
            var urlsToTry = new List<string>
            {
                $"{CommunityOutpostCatalogConstants.DefaultFilesBaseUrl.TrimEnd('/')}/{actualContentCode}.dat",
                $"https://legi.cc/patch/{actualContentCode}.dat",
            };

            var extracted = await DownloadAndExtractDependencyArchiveAsync(urlsToTry, depArchive, depExtractPath, cancellationToken);
            if (!extracted)
            {
                logger.LogError("Failed to download and extract dependency {Name}", dep.Name);
                return;
            }

            await avifConverter.ConvertDirectoryAsync(depExtractPath, cancellationToken);

            var depPackageManifest = new ContentManifest
            {
                Id = dep.Id,
                Name = dep.Name ?? depMetadata.DisplayName,
                Version = "1.0",
                ContentType = depMetadata.ContentType,
                TargetGame = depMetadata.TargetGame,
                Metadata = new ContentMetadata
                {
                    Tags = [$"contentCode:{actualContentCode}"],
                },
            };

            await RepackContentIfNeededAsync(depPackageManifest, depExtractPath, cancellationToken);
            CopyDependencyBigFiles(dep, depExtractPath, extractPath);
        }
        finally
        {
            try
            {
                if (File.Exists(depArchive))
                {
                    File.Delete(depArchive);
                }

                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to clean up temporary dependency archive or directory for {DependencyId}", dep.Id);
            }
        }
    }

    private bool ShouldSkipDependency(
        ContentDependency dep,
        GenPatcherContentMetadata depMetadata,
        GenPatcherContentMetadata packageMetadata,
        bool hasControlBarProBigs,
        string extractPath)
    {
        if (hasControlBarProBigs &&
            packageMetadata.Category == GenPatcherContentCategory.ControlBar &&
            (string.Equals(depMetadata.OutputFilename, "400_ControlBarProCoreZH.big", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(depMetadata.OutputFilename, "400_ControlBarHDBaseZH.big", StringComparison.OrdinalIgnoreCase)))
        {
            var outputName = depMetadata.OutputFilename;
            var payloadAlreadyPresent = !string.IsNullOrEmpty(outputName) &&
                Directory.GetFiles(extractPath, Path.GetFileName(outputName), SearchOption.AllDirectories).Length > 0;

            if (payloadAlreadyPresent)
            {
                logger.LogInformation(
                    "Skipping dependency {Name} because payload {Filename} already exists in extracted content",
                    dep.Name ?? dep.Id.Value,
                    outputName);
                return true;
            }

            logger.LogInformation(
                "Control Bar Pro BIGs exist but {Filename} is missing — downloading dependency {Name}",
                outputName,
                dep.Name ?? dep.Id.Value);
        }

        return false;
    }

    private async Task<bool> DownloadAndExtractDependencyArchiveAsync(
        List<string> urlsToTry,
        string depArchive,
        string depExtractPath,
        CancellationToken cancellationToken)
    {
        foreach (var depUrl in urlsToTry)
        {
            logger.LogDebug("Trying dependency download from {Url}", depUrl);
            var downloadResult = await DownloadWithMirrorFallbackAsync(depUrl, depArchive, cancellationToken);
            if (!downloadResult.Success)
            {
                continue;
            }

            try
            {
                if (Directory.Exists(depExtractPath))
                {
                    Directory.Delete(depExtractPath, recursive: true);
                }

                Directory.CreateDirectory(depExtractPath);
                await ExtractArchiveAsync(depArchive, depExtractPath, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract dependency archive from {Url}, trying next fallback", depUrl);
                if (File.Exists(depArchive))
                {
                    try
                    {
                        File.Delete(depArchive);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogDebug(deleteEx, "Failed to delete fallback dependency archive at {Path}", depArchive);
                    }
                }
            }
        }

        return false;
    }

    private void CopyDependencyBigFiles(ContentDependency dep, string depExtractPath, string extractPath)
    {
        var bigFiles = Directory.GetFiles(depExtractPath, "*.big", SearchOption.AllDirectories);
        if (bigFiles.Length == 0)
        {
            logger.LogWarning("No BIG files found for dependency {Name} after repacking", dep.Name);
            return;
        }

        foreach (var bigFile in bigFiles)
        {
            var bigFileName = Path.GetFileName(bigFile);
            var targetPath = Path.Combine(extractPath, bigFileName);
            File.Copy(bigFile, targetPath, overwrite: true);
            logger.LogInformation(
                "Copied dependency BIG file {FileName} to main extract path",
                bigFileName);
        }
    }
}
