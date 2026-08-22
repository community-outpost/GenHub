using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Common;

/// <summary>
/// Service for detecting, isolating, converting, and packaging Control Bar content into SAGE-compatible .big archives.
/// </summary>
public class ControlBarPackageProcessor(
    CompressedImageToTgaConverter avifConverter,
    ILogger<ControlBarPackageProcessor> logger) : IControlBarPackageProcessor
{
    private const string ControlBarMetadataBigBase64 = "QklHRngBAAAAAAACAAAAUwAAAFMAAAEkQ29udHJvbEJhclByby50eHQAAAABdwAAAAFHZW5Ub29sXGZ1bGx2aWV3cG9ydC5kYXQAAAAAAAAAAABDb250cm9sIEJhciBQcm8gZm9yIENPTU1BTkQgQU5EIENPTlFVRVIgR0VORVJBTFM6IFpFUk8gSE9VUg0KDQpBVVRIT1I6DQpFQSBHYW1lcywgRkFTLCB4ZXpvbg0KDQpPUklHSU5BTCBET1dOTE9BRCBVUkw6DQpodHRwOi8vZ2VudG9vbC5uZXQvZG93bmxvYWQvY29udHJvbGJhcnBybw0KDQpTT1VSQ0UgQ09ERSAmIEFTU0VUUzoNCmh0dHBzOi8vZ2l0aHViLmNvbS9UaGVTdXBlckhhY2tlcnMvR2VuZXJhbHNDb250cm9sQmFyDQoNCkRPTkFUSU9OIExJTks6DQpodHRwczovL3d3dy5wYXlwYWwubWUvZ2VudG9vbA0KMQ==";
    private const string BigEnDirectoryName = "BIG EN";

    private static readonly string[] KnownResolutionVariants = ["720p", "900p", "1080p", "1440p", "4k", "2160p"];

    /// <inheritdoc/>
    public bool IsControlBarContent(string extractedDirectory, ContentManifest manifest)
    {
        if (IsControlBarManifest(manifest))
        {
            return true;
        }

        return DirectoryContainsControlBarFiles(extractedDirectory);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ProcessAndRepackControlBarAsync(
        string extractedDirectory,
        ContentManifest manifest,
        string? requestedVariant = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Processing Control Bar packaging in {Directory} for manifest {ManifestId}",
            extractedDirectory,
            manifest.Id);

        var variantId = DetermineVariantId(extractedDirectory, manifest, requestedVariant);
        var variantSuffix = GetControlBarVariantSuffix(variantId);
        var repackedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var variantBigRoot = FindControlBarVariantBigRoot(extractedDirectory, variantId);

        if (!string.IsNullOrEmpty(variantBigRoot))
        {
            var prebuiltBigs = Directory.GetFiles(variantBigRoot, "*.big", SearchOption.TopDirectoryOnly)
                .Where(path => IsAllowedControlBarBig(Path.GetFileName(path), variantSuffix))
                .ToArray();

            if (prebuiltBigs.Length > 0)
            {
                logger.LogInformation("Using prebuilt Control Bar BIG files from {VariantRoot}", variantBigRoot);
                foreach (var prebuiltBig in prebuiltBigs)
                {
                    var bigName = Path.GetFileName(prebuiltBig);
                    var targetPath = Path.Combine(extractedDirectory, bigName);

                    if (!string.Equals(Path.GetFullPath(prebuiltBig), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                    {
                        await TryCopyFileWithRetryAsync(prebuiltBig, targetPath, logger);
                    }

                    repackedOutputs.Add(bigName);
                }
            }
            else
            {
                var artBigName = $"340_ControlBarProArt{variantSuffix}ZH.big";
                var dataBigName = $"340_ControlBarProData{variantSuffix}ZH.big";

                var artBigPath = Path.Combine(extractedDirectory, artBigName);
                var dataBigPath = Path.Combine(extractedDirectory, dataBigName);

                if (!File.Exists(artBigPath) || !File.Exists(dataBigPath))
                {
                    logger.LogInformation(
                        "Repacking Control Bar variant {Variant} into Art/Data BIG files: {ArtBig}, {DataBig}",
                        variantId,
                        artBigName,
                        dataBigName);

                    var artSource = Path.Combine(variantBigRoot, "Art");
                    var dataSource = Path.Combine(variantBigRoot, "Data");
                    var windowSource = Path.Combine(variantBigRoot, "Window");
                    var genToolSource = Path.Combine(variantBigRoot, "GenTool");

                    var tempRoot = Path.Combine(extractedDirectory, $"cbpro-pack-{variantId}");
                    var artPackRoot = Path.Combine(tempRoot, "ArtPack");
                    var dataPackRoot = Path.Combine(tempRoot, "DataPack");

                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }

                    Directory.CreateDirectory(artPackRoot);
                    Directory.CreateDirectory(dataPackRoot);

                    if (Directory.Exists(artSource))
                    {
                        CopyDirectory(artSource, Path.Combine(artPackRoot, "Art"));
                    }

                    if (Directory.Exists(dataSource))
                    {
                        CopyDirectory(dataSource, Path.Combine(dataPackRoot, "Data"));
                    }

                    if (Directory.Exists(windowSource))
                    {
                        CopyDirectory(windowSource, Path.Combine(dataPackRoot, "Window"));
                    }

                    if (Directory.Exists(genToolSource))
                    {
                        CopyDirectory(genToolSource, Path.Combine(dataPackRoot, "GenTool"));
                    }

                    try
                    {
                        // Convert AVIF/WebP images to TGA prior to packing
                        await avifConverter.ConvertDirectoryAsync(artPackRoot, cancellationToken);
                        await avifConverter.ConvertDirectoryAsync(dataPackRoot, cancellationToken);

                        var tempArtBig = Path.Combine(tempRoot, "temp_art.big");
                        var tempDataBig = Path.Combine(tempRoot, "temp_data.big");

                        await BigFilePacker.PackAsync(artPackRoot, tempArtBig);
                        await BigFilePacker.PackAsync(dataPackRoot, tempDataBig);

                        File.Move(tempArtBig, artBigPath, overwrite: true);
                        File.Move(tempDataBig, dataBigPath, overwrite: true);
                    }
                    finally
                    {
                        try
                        {
                            if (Directory.Exists(tempRoot))
                            {
                                Directory.Delete(tempRoot, recursive: true);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to cleanup temporary pack directory {TempRoot}", tempRoot);
                        }
                    }
                }

                if (File.Exists(artBigPath))
                {
                    repackedOutputs.Add(artBigName);
                }

                if (File.Exists(dataBigPath))
                {
                    repackedOutputs.Add(dataBigName);
                }
            }
        }
        else
        {
            // Check for flat structure prebuilt BIG files
            logger.LogInformation("Control Bar has flat structure, searching for prebuilt BIG files in root");
            var prebuiltCandidates = Directory.GetFiles(extractedDirectory, "*ControlBarPro*ZH.big", SearchOption.TopDirectoryOnly)
                .Where(path => IsAllowedControlBarBig(Path.GetFileName(path), variantSuffix))
                .ToArray();

            var hasArtDataSplit = prebuiltCandidates.Any(p =>
                Path.GetFileName(p).Contains("Art", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(p).Contains("Data", StringComparison.OrdinalIgnoreCase));

            if (hasArtDataSplit)
            {
                prebuiltCandidates = [.. prebuiltCandidates.Where(p =>
                {
                    var name = Path.GetFileName(p);
                    return name.Contains("Art", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("Data", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("-Fix", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("340_ControlBarProZH.big", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("340_ControlBarProLemonEditionZH.big", StringComparison.OrdinalIgnoreCase);
                })];
            }

            foreach (var candidate in prebuiltCandidates)
            {
                repackedOutputs.Add(Path.GetFileName(candidate));
            }
        }

        // Check if an existing metadata / base BIG file is already included in outputs
        var existingMetadataFileName = repackedOutputs.FirstOrDefault(name =>
            name.Equals("340_ControlBarProZH.big", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("340_ControlBarProLemonEditionZH.big", StringComparison.OrdinalIgnoreCase));

        if (existingMetadataFileName != null)
        {
            logger.LogInformation("Using existing Control Bar metadata file {FileName}", existingMetadataFileName);
        }
        else
        {
            // Explicitly ensure metadata BIG file (340_ControlBarProZH.big) is included
            var metadataFileName = "340_ControlBarProZH.big";
            var metadataTargetPath = Path.Combine(extractedDirectory, metadataFileName);

            if (!File.Exists(metadataTargetPath))
            {
                var metadataSearchPaths = new[]
                {
                    Path.Combine(extractedDirectory, "ZH", metadataFileName),
                    Path.Combine(extractedDirectory, "CCG", metadataFileName),
                    Path.Combine(extractedDirectory, "ZH", variantId, metadataFileName),
                    Path.Combine(extractedDirectory, "CCG", variantId, metadataFileName),
                    Path.Combine(extractedDirectory, "ZH", variantId, BigEnDirectoryName, metadataFileName),
                    Path.Combine(extractedDirectory, "ZH", variantId, "BIG", metadataFileName),
                    Path.Combine(extractedDirectory, "CCG", variantId, BigEnDirectoryName, metadataFileName),
                    Path.Combine(extractedDirectory, "CCG", variantId, "BIG", metadataFileName),
                };

                foreach (var searchPath in metadataSearchPaths)
                {
                    if (File.Exists(searchPath))
                    {
                        logger.LogInformation("Found Control Bar metadata file at {SourcePath}, copying to root", searchPath);
                        await TryCopyFileWithRetryAsync(searchPath, metadataTargetPath, logger);
                        break;
                    }
                }
            }

            if (File.Exists(metadataTargetPath))
            {
                repackedOutputs.Add(metadataFileName);
                logger.LogInformation("Including Control Bar metadata file {FileName} in outputs", metadataFileName);
            }
            else
            {
                logger.LogWarning("Control Bar metadata file not found, writing embedded fallback");
                try
                {
                    var metadataBytes = Convert.FromBase64String(ControlBarMetadataBigBase64);
                    File.WriteAllBytes(metadataTargetPath, metadataBytes);
                    repackedOutputs.Add(metadataFileName);
                    logger.LogInformation("Created Control Bar metadata file {FileName} from fallback", metadataFileName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create fallback Control Bar metadata file");
                }
            }
        }

        // Cleanup raw unpacked source directories so only the packaged files remain
        CleanupSourceDirectories(extractedDirectory, repackedOutputs);

        return [.. repackedOutputs];
    }

    /// <inheritdoc/>
    public string? FindControlBarVariantBigRoot(string extractedDirectory, string variantId)
    {
        var rawSuffix = GetControlBarVariantSuffix(variantId);
        var candidates = new[]
        {
            Path.Combine(extractedDirectory, "ZH", variantId, BigEnDirectoryName),
            Path.Combine(extractedDirectory, "ZH", variantId, "BIG"),
            Path.Combine(extractedDirectory, "ZH", variantId),
            Path.Combine(extractedDirectory, "ZH", rawSuffix, BigEnDirectoryName),
            Path.Combine(extractedDirectory, "ZH", rawSuffix, "BIG"),
            Path.Combine(extractedDirectory, "ZH", rawSuffix),
            Path.Combine(extractedDirectory, "CCG", variantId, BigEnDirectoryName),
            Path.Combine(extractedDirectory, "CCG", variantId, "BIG"),
            Path.Combine(extractedDirectory, "CCG", variantId),
            Path.Combine(extractedDirectory, "CCG", rawSuffix, BigEnDirectoryName),
            Path.Combine(extractedDirectory, "CCG", rawSuffix, "BIG"),
            Path.Combine(extractedDirectory, "CCG", rawSuffix),
            Path.Combine(extractedDirectory, variantId, BigEnDirectoryName),
            Path.Combine(extractedDirectory, variantId, "BIG"),
            Path.Combine(extractedDirectory, variantId),
            Path.Combine(extractedDirectory, rawSuffix, BigEnDirectoryName),
            Path.Combine(extractedDirectory, rawSuffix, "BIG"),
            Path.Combine(extractedDirectory, rawSuffix),
        };

        var existingCandidate = candidates.FirstOrDefault(Directory.Exists);
        if (existingCandidate != null)
        {
            return existingCandidate;
        }

        if (Directory.Exists(Path.Combine(extractedDirectory, "Window")) ||
            Directory.Exists(Path.Combine(extractedDirectory, "Art")) ||
            Directory.Exists(Path.Combine(extractedDirectory, "Data")) ||
            Directory.Exists(Path.Combine(extractedDirectory, "GenTool")))
        {
            return extractedDirectory;
        }

        return null;
    }

    /// <inheritdoc/>
    public string GetControlBarVariantSuffix(string variantId)
    {
        if (variantId.EndsWith("p", StringComparison.OrdinalIgnoreCase))
        {
            return variantId[..^1];
        }

        if (variantId.Equals("4k", StringComparison.OrdinalIgnoreCase))
        {
            return "4K";
        }

        return variantId;
    }

    /// <inheritdoc/>
    public bool IsAllowedControlBarBig(string fileName, string variantSuffix)
    {
        return fileName.Equals($"340_ControlBarProArt{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProData{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarPro{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarPro-Fix{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("340_ControlBarProZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEditionArt{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEditionData{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEdition{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEdition-Fix{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("340_ControlBarProLemonEditionZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarHDEnglishZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarProCoreZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarHDBaseZH.big", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControlBarManifest(ContentManifest manifest)
    {
        if (manifest.ContentType is not (ContentType.Addon or ContentType.Mod))
        {
            return false;
        }

        var id = manifest.Id.Value.ToLowerInvariant();
        if (id.Contains("controlbar") || id.Contains("cbpr") || id.Contains("cbpx"))
        {
            return true;
        }

        var name = manifest.Name.ToLowerInvariant();
        if (name.Contains("controlbar") || name.Contains("control bar") || name.Contains("control-bar"))
        {
            return true;
        }

        return manifest.Metadata?.Tags?.Any(t =>
            t.Contains("controlbar", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("control-bar", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool DirectoryContainsControlBarFiles(string extractedDirectory)
    {
        if (!Directory.Exists(extractedDirectory))
        {
            return false;
        }

        return Directory.GetFiles(extractedDirectory, "*ControlBar*.big", SearchOption.AllDirectories).Length > 0 ||
               Directory.GetFiles(extractedDirectory, "*ControlBar*.wnd", SearchOption.AllDirectories).Length > 0;
    }

    private static string DetermineVariantId(string extractedDirectory, ContentManifest manifest, string? requestedVariant)
    {
        if (!string.IsNullOrWhiteSpace(requestedVariant))
        {
            return requestedVariant;
        }

        var match = ExtractVariantToken(manifest.Id.Value) ?? ExtractVariantToken(manifest.Name);
        if (!string.IsNullOrEmpty(match))
        {
            return match;
        }

        if (manifest.Metadata?.Tags != null)
        {
            var tagMatch = manifest.Metadata.Tags
                .Select(ExtractVariantToken)
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));
            if (!string.IsNullOrEmpty(tagMatch))
            {
                return tagMatch;
            }
        }

        // Check if resolution subfolders exist in extracted content
        var existingVariant = KnownResolutionVariants.FirstOrDefault(candidate =>
            Directory.Exists(Path.Combine(extractedDirectory, "ZH", candidate)) ||
            Directory.Exists(Path.Combine(extractedDirectory, candidate)));

        if (!string.IsNullOrEmpty(existingVariant))
        {
            return existingVariant;
        }

        return "1080p";
    }

    private static string? ExtractVariantToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = Regex.Match(input, @"\b(720p?|900p?|1080p?|1440p?|2160p?|4k)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            var token = match.Value.ToLowerInvariant();
            return token switch
            {
                "720" => "720p",
                "900" => "900p",
                "1080" => "1080p",
                "1440" => "1440p",
                "2160" => "4k",
                _ => token,
            };
        }

        var inlineMatch = Regex.Match(input, @"(720p|900p|1080p|1440p|2160p|4k)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (inlineMatch.Success)
        {
            return inlineMatch.Value.ToLowerInvariant();
        }

        return null;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }

    private static async Task TryCopyFileWithRetryAsync(string source, string destination, ILogger logger, int maxRetries = 3, int delayMs = 100)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < maxRetries)
            {
                logger.LogWarning(
                    "File copy attempt {Attempt}/{MaxRetries} failed for {Source}: {Message}. Retrying...",
                    attempt,
                    maxRetries,
                    Path.GetFileName(source),
                    ex.Message);
                await Task.Delay(delayMs);
            }
        }
    }

    private void CleanupSourceDirectories(string extractedDirectory, HashSet<string> repackedOutputs)
    {
        if (repackedOutputs.Count == 0)
        {
            return;
        }

        try
        {
            var targetSourceDirNames = new[] { "ZH", "CCG", "Art", "Data", "Window", "GenTool", "720p", "900p", "1080p", "1440p", "2160p", "4k" };
            foreach (var dirName in targetSourceDirNames)
            {
                var dirPath = Path.Combine(extractedDirectory, dirName);
                if (Directory.Exists(dirPath))
                {
                    Directory.Delete(dirPath, recursive: true);
                }
            }

            var looseFiles = Directory.GetFiles(extractedDirectory, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in looseFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!repackedOutputs.Contains(fileName) && !fileName.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up control bar source directories in {Directory}", extractedDirectory);
        }
    }
}
