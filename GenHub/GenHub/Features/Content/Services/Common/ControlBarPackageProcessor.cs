using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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

    private const string DirArt = "Art";
    private const string DirData = "Data";
    private const string DirZh = "ZH";
    private const string DirCcg = "CCG";
    private const string Variant720p = "720p";
    private const string Variant900p = "900p";
    private const string Variant1080p = "1080p";
    private const string Variant1440p = "1440p";
    private const string Variant2160p = "2160p";
    private const string Variant4k = "4k";
    private const string Resolution720 = "720";
    private const string Resolution900 = "900";
    private const string Resolution1080 = "1080";
    private const string Resolution1440 = "1440";
    private const string Resolution2160 = "2160";
    private const string BigFileExtension = ".big";
    private const string WindowFileExtension = ".wnd";
    private const string ControlBarToken = "controlbar";
    private const string DirTextures = "Textures";
    private const string TokenCbpr = "cbpr";
    private const string TokenCbpx = "cbpx";
    private const string TokenCbpro = "cbpro";

    private static readonly string[] KnownResolutionVariants = [Variant720p, Variant900p, Variant1080p, Variant1440p, Variant4k, Variant2160p];
    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex WordVariantRegex = new(@"\b(720p?|900p?|1080p?|1440p?|2160p?|4k)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexMatchTimeout);
    private static readonly Regex InlineVariantRegex = new(@"(?<!\d)(720p?|900p?|1080p?|1440p?|2160p?|4k)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexMatchTimeout);
    private static readonly Regex ControlBarImageStemRegex = new(@"^cb([-_]?(720p?|900p?|1080p?|1440p?|2160p?|4k))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexMatchTimeout);

    /// <inheritdoc/>
    public bool IsControlBarContent(string extractedDirectory, ContentManifest manifest)
    {
        if (manifest.ContentType != ContentType.Addon)
        {
            return false;
        }

        if (HasUnrelatedAssets(extractedDirectory))
        {
            return false;
        }

        return HasControlBarManifestMetadata(manifest) || HasControlBarFiles(extractedDirectory);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ProcessAndRepackControlBarAsync(
        string extractedDirectory,
        ContentManifest manifest,
        string? requestedVariant = null,
        bool cleanupSources = true,
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
            await ProcessVariantBigRootAsync(variantBigRoot, extractedDirectory, variantId, variantSuffix, repackedOutputs, cancellationToken);
        }
        else
        {
            CollectFlatPrebuiltBigs(extractedDirectory, variantSuffix, repackedOutputs);
        }

        if (repackedOutputs.Count == 0)
        {
            logger.LogInformation(
                "No Control Bar variant assets found for variant {VariantId} in {Directory}",
                variantId,
                extractedDirectory);
            return [];
        }

        await EnsureMetadataBigIncludedAsync(extractedDirectory, variantId, repackedOutputs, cancellationToken);

        if (cleanupSources)
        {
            CleanupSourceDirectories(extractedDirectory, repackedOutputs);
        }

        return [.. repackedOutputs];
    }

    /// <inheritdoc/>
    public void CleanupSourceDirectories(string extractedDirectory, IEnumerable<string> repackedOutputs)
    {
        ArgumentNullException.ThrowIfNull(repackedOutputs);
        var outputSet = repackedOutputs as HashSet<string> is { Comparer: var comp } && comp == StringComparer.OrdinalIgnoreCase
            ? (HashSet<string>)repackedOutputs
            : new HashSet<string>(repackedOutputs, StringComparer.OrdinalIgnoreCase);
        CleanupSourceDirectories(extractedDirectory, outputSet);
    }

    /// <inheritdoc/>
    public string? FindControlBarVariantBigRoot(string extractedDirectory, string variantId)
    {
        var candidate = FindVariantBigRootCandidate(extractedDirectory, variantId);
        if (candidate != null)
        {
            return candidate;
        }

        if (!HasLooseControlBarDirectories(extractedDirectory))
        {
            return null;
        }

        // If the package contains structured variant subdirectories, do not fall back to root for missing variants
        if (HasAnyVariantCandidate(extractedDirectory))
        {
            return null;
        }

        // For flat layout, only match if the flat directory actually corresponds to the requested variant
        var flatVariant = DetectFlatLayoutVariant(extractedDirectory);
        if (string.Equals(NormalizeVariantId(variantId), NormalizeVariantId(flatVariant), StringComparison.OrdinalIgnoreCase))
        {
            return extractedDirectory;
        }

        return null;
    }

    /// <inheritdoc/>
    public string GetControlBarVariantSuffix(string variantId)
    {
        if (variantId.Equals(Variant2160p, StringComparison.OrdinalIgnoreCase) ||
            variantId.Equals(Resolution2160, StringComparison.OrdinalIgnoreCase) ||
            variantId.Equals(Variant4k, StringComparison.OrdinalIgnoreCase))
        {
            return "4K";
        }

        if (variantId.EndsWith("p", StringComparison.OrdinalIgnoreCase))
        {
            return variantId[..^1];
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
            || fileName.Equals(GameContentConstants.ControlBarProBaseFileName, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEditionArt{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEditionData{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEdition{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProLemonEdition-Fix{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(GameContentConstants.ControlBarProLemonBaseFileName, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarHDEnglishZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarProCoreZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarHDBaseZH.big", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public bool IsMetadataOnlyBig(string fileName)
    {
        return fileName.Equals(GameContentConstants.ControlBarProBaseFileName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(GameContentConstants.ControlBarProLemonBaseFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCandidatePath(string baseDir, string prefix, string token, string subDir)
    {
        var path = string.IsNullOrEmpty(prefix)
            ? Path.Combine(baseDir, token)
            : Path.Combine(baseDir, prefix, token);

        return string.IsNullOrEmpty(subDir) ? path : Path.Combine(path, subDir);
    }

    private static bool HasLooseControlBarDirectories(string directory)
    {
        return Directory.Exists(Path.Combine(directory, GameContentConstants.WindowDirectoryName)) ||
               Directory.Exists(Path.Combine(directory, DirArt)) ||
               Directory.Exists(Path.Combine(directory, DirData)) ||
               Directory.Exists(Path.Combine(directory, GameContentConstants.GenToolDirectoryName));
    }

    private static bool HasControlBarManifestMetadata(ContentManifest manifest)
    {
        if (manifest.ContentType != ContentType.Addon)
        {
            return false;
        }

        var id = manifest.Id.Value;
        if (id.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase) ||
            id.Contains("cbpr", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("cbpx", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = manifest.Name;
        if (name.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase) ||
            name.Contains("control bar", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("control-bar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return manifest.Metadata?.Tags != null &&
            manifest.Metadata.Tags.Any(t =>
                t.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase) ||
                t.Contains("control-bar", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasControlBarFiles(string extractedDirectory)
    {
        if (!Directory.Exists(extractedDirectory))
        {
            return false;
        }

        return Directory.EnumerateFiles(extractedDirectory, "*ControlBar*.big", SearchOption.AllDirectories).Any() ||
               Directory.EnumerateFiles(extractedDirectory, "*ControlBar*.wnd", SearchOption.AllDirectories).Any();
    }

    private static bool HasUnrelatedAssets(string extractedDirectory)
    {
        if (!Directory.Exists(extractedDirectory))
        {
            return false;
        }

        if (HasDisallowedTopLevelDirectories(extractedDirectory) || HasDisallowedSubdirectories(extractedDirectory))
        {
            return true;
        }

        return Directory.EnumerateFiles(extractedDirectory, "*", SearchOption.AllDirectories)
            .Any(file => IsUnrelatedFile(file, extractedDirectory));
    }

    private static bool HasDisallowedTopLevelDirectories(string extractedDirectory)
    {
        var allowedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DirZh, DirCcg, DirArt, DirData,
            GameContentConstants.WindowDirectoryName,
            GameContentConstants.GenToolDirectoryName,
            Variant720p, Variant900p, Variant1080p, Variant1440p, Variant2160p, Variant4k,
            Resolution720, Resolution900, Resolution1080, Resolution1440, Resolution2160,
        };

        foreach (var dir in Directory.EnumerateDirectories(extractedDirectory))
        {
            var dirName = Path.GetFileName(dir);
            if (!allowedDirs.Contains(dirName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDisallowedSubdirectories(string extractedDirectory)
    {
        if (HasInvalidArtSubdirectories(Path.Combine(extractedDirectory, DirArt)) ||
            HasInvalidDataSubdirectories(Path.Combine(extractedDirectory, DirData)))
        {
            return true;
        }

        return new[] { DirZh, DirCcg }.Any(gamePrefix =>
            HasInvalidArtSubdirectories(Path.Combine(extractedDirectory, gamePrefix, DirArt)) ||
            HasInvalidDataSubdirectories(Path.Combine(extractedDirectory, gamePrefix, DirData)));
    }

    private static bool HasInvalidArtSubdirectories(string artDir)
    {
        if (!Directory.Exists(artDir))
        {
            return false;
        }

        return Directory.EnumerateDirectories(artDir).Any(dir =>
        {
            var dirName = Path.GetFileName(dir);
            return !dirName.Equals(DirTextures, StringComparison.OrdinalIgnoreCase) ||
                Directory.EnumerateDirectories(dir).Any();
        });
    }

    private static bool HasInvalidDataSubdirectories(string dataDir)
    {
        if (!Directory.Exists(dataDir))
        {
            return false;
        }

        return Directory.EnumerateDirectories(dataDir).Any(dir =>
            !Path.GetFileName(dir).Equals(GameContentConstants.WindowDirectoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnrelatedFile(string file, string extractedDirectory)
    {
        var fileName = Path.GetFileName(file);
        var extension = Path.GetExtension(file);

        if (extension.Equals(BigFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase) &&
                   !fileName.Contains(TokenCbpr, StringComparison.OrdinalIgnoreCase) &&
                   !fileName.Contains(TokenCbpx, StringComparison.OrdinalIgnoreCase);
        }

        if (extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.Equals("fullviewport.dat", StringComparison.OrdinalIgnoreCase);
        }

        if (extension.Equals(WindowFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase);
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var relPath = Path.GetRelativePath(extractedDirectory, file);
            return relPath.Contains(Path.DirectorySeparatorChar) || relPath.Contains(Path.AltDirectorySeparatorChar);
        }

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return !IsAllowedDocFile(fileName);
        }

        if (IsImageExtension(extension))
        {
            return !IsRecognizedControlBarImage(file, extractedDirectory);
        }

        return true;
    }

    private static bool IsImageExtension(string extension)
    {
        return extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedDocFile(string fileName)
    {
        return fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("license", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("$ControlBarPro.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("changelog", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecognizedControlBarImage(string file, string extractedDirectory)
    {
        var relPath = Path.GetRelativePath(extractedDirectory, file);
        var segments = relPath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var parent = segments[^2];
        var inWindow = parent.Equals(GameContentConstants.WindowDirectoryName, StringComparison.OrdinalIgnoreCase);
        var inArt = parent.Equals(DirArt, StringComparison.OrdinalIgnoreCase);
        var inArtTextures = parent.Equals(DirTextures, StringComparison.OrdinalIgnoreCase) &&
                            segments.Length >= 3 &&
                            segments[^3].Equals(DirArt, StringComparison.OrdinalIgnoreCase);

        if (!inWindow && !inArt && !inArtTextures)
        {
            return false;
        }

        var fileName = Path.GetFileName(file);
        return IsRecognizedControlBarImageName(fileName);
    }

    private static bool IsRecognizedControlBarImageName(string fileName)
    {
        if (fileName.Contains(ControlBarToken, StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(TokenCbpr, StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(TokenCbpx, StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(TokenCbpro, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return ControlBarImageStemRegex.IsMatch(stem);
    }

    private static string DetectFlatLayoutVariant(string extractedDirectory)
    {
        var files = Directory.EnumerateFiles(extractedDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Prioritize Control Bar BIG files
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(BigFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                var token = ExtractVariantToken(fileName);
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }
            }
        }

        // Prioritize Control Bar window files (.wnd)
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(WindowFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                var token = ExtractVariantToken(fileName);
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }
            }
        }

        // Fall back to remaining files in deterministic order
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var token = ExtractVariantToken(fileName);
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }
        }

        return GameContentConstants.DefaultControlBarVariant;
    }

    private static string NormalizeVariantId(string variantId)
    {
        if (variantId.Equals(Variant2160p, StringComparison.OrdinalIgnoreCase) ||
            variantId.Equals(Resolution2160, StringComparison.OrdinalIgnoreCase) ||
            variantId.Equals(Variant4k, StringComparison.OrdinalIgnoreCase))
        {
            return Variant4k;
        }

        var lower = variantId.ToLowerInvariant();
        if (!lower.EndsWith("p", StringComparison.OrdinalIgnoreCase) && int.TryParse(lower, out _))
        {
            return lower + "p";
        }

        return lower;
    }

    private static void CopySourceDirectoriesToPacks(string variantBigRoot, string artPackRoot, string dataPackRoot)
    {
        var artSource = Path.Combine(variantBigRoot, DirArt);
        var dataSource = Path.Combine(variantBigRoot, DirData);
        var windowSource = Path.Combine(variantBigRoot, GameContentConstants.WindowDirectoryName);
        var genToolSource = Path.Combine(variantBigRoot, GameContentConstants.GenToolDirectoryName);

        if (Directory.Exists(artSource))
        {
            CopyDirectory(artSource, Path.Combine(artPackRoot, DirArt));
        }

        if (Directory.Exists(dataSource))
        {
            CopyDirectory(dataSource, Path.Combine(dataPackRoot, DirData));
        }

        if (Directory.Exists(windowSource))
        {
            CopyDirectory(windowSource, Path.Combine(dataPackRoot, GameContentConstants.WindowDirectoryName));
        }

        if (Directory.Exists(genToolSource))
        {
            CopyDirectory(genToolSource, Path.Combine(dataPackRoot, GameContentConstants.GenToolDirectoryName));
        }
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

        var existingResolution = KnownResolutionVariants.FirstOrDefault(candidate =>
            Directory.Exists(Path.Combine(extractedDirectory, DirZh, candidate)) ||
            Directory.Exists(Path.Combine(extractedDirectory, candidate)));

        return existingResolution ?? GameContentConstants.DefaultControlBarVariant;
    }

    private static string? ExtractVariantToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = WordVariantRegex.Match(input);
        if (match.Success)
        {
            return NormalizeResolutionToken(match.Value);
        }

        var inlineMatch = InlineVariantRegex.Match(input);
        return inlineMatch.Success ? NormalizeResolutionToken(inlineMatch.Value) : null;
    }

    private static string NormalizeResolutionToken(string token)
    {
        var lower = token.ToLowerInvariant();
        return lower switch
        {
            Resolution720 or Variant720p => Variant720p,
            Resolution900 or Variant900p => Variant900p,
            Resolution1080 or Variant1080p => Variant1080p,
            Resolution1440 or Variant1440p => Variant1440p,
            Resolution2160 or Variant2160p or Variant4k => Variant4k,
            _ => lower,
        };
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
                    ex,
                    "File copy attempt {Attempt}/{MaxRetries} failed for {Source}: {Message}. Retrying...",
                    attempt,
                    maxRetries,
                    Path.GetFileName(source),
                    ex.Message);
                await Task.Delay(delayMs);
            }
        }
    }

    private bool HasAnyVariantCandidate(string extractedDirectory) =>
        KnownResolutionVariants.Any(variant => FindVariantBigRootCandidate(extractedDirectory, variant) != null);

    private string? FindVariantBigRootCandidate(string extractedDirectory, string variantId)
    {
        var tokens = GetVariantCandidateTokens(variantId);
        var prefixes = new[] { DirZh, DirCcg, string.Empty };
        var subDirs = new[] { GameContentConstants.BigEnDirectoryName, GameContentConstants.BigDirectoryName, string.Empty };

        foreach (var prefix in prefixes)
        {
            foreach (var token in tokens)
            {
                foreach (var subDir in subDirs)
                {
                    var candidate = BuildCandidatePath(extractedDirectory, prefix, token, subDir);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private string[] GetVariantCandidateTokens(string variantId)
    {
        var rawSuffix = GetControlBarVariantSuffix(variantId);
        if (variantId.Equals(Variant4k, StringComparison.OrdinalIgnoreCase) ||
            variantId.Equals(Variant2160p, StringComparison.OrdinalIgnoreCase) ||
            variantId.Equals(Resolution2160, StringComparison.OrdinalIgnoreCase))
        {
            return [Variant4k, rawSuffix, Variant2160p, Resolution2160];
        }

        return string.Equals(variantId, rawSuffix, StringComparison.Ordinal)
            ? [variantId]
            : [variantId, rawSuffix];
    }

    private async Task ProcessVariantBigRootAsync(
        string variantBigRoot,
        string extractedDirectory,
        string variantId,
        string variantSuffix,
        HashSet<string> repackedOutputs,
        CancellationToken cancellationToken)
    {
        var prebuiltBigs = Directory.GetFiles(variantBigRoot, "*.big", SearchOption.TopDirectoryOnly)
            .Where(path => IsAllowedControlBarBig(Path.GetFileName(path), variantSuffix))
            .ToArray();

        if (prebuiltBigs.Length > 0)
        {
            await CopyPrebuiltBigsAsync(prebuiltBigs, extractedDirectory, repackedOutputs);
        }
        else
        {
            await RepackArtAndDataBigsAsync(variantBigRoot, extractedDirectory, variantId, variantSuffix, repackedOutputs, cancellationToken);
        }
    }

    private async Task CopyPrebuiltBigsAsync(
        IReadOnlyList<string> prebuiltBigs,
        string extractedDirectory,
        HashSet<string> repackedOutputs)
    {
        logger.LogInformation("Using prebuilt Control Bar BIG files");
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

    private async Task RepackArtAndDataBigsAsync(
        string variantBigRoot,
        string extractedDirectory,
        string variantId,
        string variantSuffix,
        HashSet<string> repackedOutputs,
        CancellationToken cancellationToken)
    {
        var artBigName = $"340_ControlBarProArt{variantSuffix}ZH.big";
        var dataBigName = $"340_ControlBarProData{variantSuffix}ZH.big";

        var artBigPath = Path.Combine(extractedDirectory, artBigName);
        var dataBigPath = Path.Combine(extractedDirectory, dataBigName);

        if (!File.Exists(artBigPath) || !File.Exists(dataBigPath))
        {
            await BuildAndPackArtAndDataBigsAsync(variantBigRoot, extractedDirectory, variantId, artBigPath, dataBigPath, cancellationToken);
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

    private async Task BuildAndPackArtAndDataBigsAsync(
        string variantBigRoot,
        string extractedDirectory,
        string variantId,
        string artBigPath,
        string dataBigPath,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Repacking Control Bar variant {Variant} into Art/Data BIG files", variantId);

        var tempRoot = Path.Combine(extractedDirectory, $"cbpro-pack-{variantId}");
        var artPackRoot = Path.Combine(tempRoot, "ArtPack");
        var dataPackRoot = Path.Combine(tempRoot, "DataPack");

        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        Directory.CreateDirectory(artPackRoot);
        Directory.CreateDirectory(dataPackRoot);

        CopySourceDirectoriesToPacks(variantBigRoot, artPackRoot, dataPackRoot);

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

    private void CollectFlatPrebuiltBigs(
        string extractedDirectory,
        string variantSuffix,
        HashSet<string> repackedOutputs)
    {
        logger.LogInformation("Control Bar has flat structure, searching for prebuilt BIG files in root");
        var prebuiltCandidates = Directory.GetFiles(extractedDirectory, "*ControlBarPro*ZH.big", SearchOption.TopDirectoryOnly)
            .Where(path => IsAllowedControlBarBig(Path.GetFileName(path), variantSuffix))
            .ToArray();

        var hasArtDataSplit = prebuiltCandidates.Any(p =>
            Path.GetFileName(p).Contains(DirArt, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(p).Contains(DirData, StringComparison.OrdinalIgnoreCase));

        if (hasArtDataSplit)
        {
            prebuiltCandidates = [.. prebuiltCandidates.Where(p =>
            {
                var name = Path.GetFileName(p);
                return name.Contains(DirArt, StringComparison.OrdinalIgnoreCase) ||
                       name.Contains(DirData, StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("-Fix", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals(GameContentConstants.ControlBarProBaseFileName, StringComparison.OrdinalIgnoreCase) ||
                       name.Equals(GameContentConstants.ControlBarProLemonBaseFileName, StringComparison.OrdinalIgnoreCase);
            })];
        }

        foreach (var candidate in prebuiltCandidates)
        {
            repackedOutputs.Add(Path.GetFileName(candidate));
        }
    }

    private async Task EnsureMetadataBigIncludedAsync(
        string extractedDirectory,
        string variantId,
        HashSet<string> repackedOutputs,
        CancellationToken cancellationToken)
    {
        var existingMetadataFileName = repackedOutputs.FirstOrDefault(IsMetadataOnlyBig);

        if (existingMetadataFileName != null)
        {
            logger.LogInformation("Using existing Control Bar metadata file {FileName}", existingMetadataFileName);
            return;
        }

        var metadataFileName = GameContentConstants.ControlBarProBaseFileName;
        var metadataTargetPath = Path.Combine(extractedDirectory, metadataFileName);

        if (!File.Exists(metadataTargetPath))
        {
            await TryLocateAndCopyMetadataBigAsync(extractedDirectory, variantId, metadataFileName, metadataTargetPath);
        }

        if (File.Exists(metadataTargetPath))
        {
            repackedOutputs.Add(metadataFileName);
            logger.LogInformation("Including Control Bar metadata file {FileName} in outputs", metadataFileName);
            return;
        }

        await WriteFallbackMetadataBigAsync(metadataTargetPath, metadataFileName, repackedOutputs, cancellationToken);
    }

    private async Task TryLocateAndCopyMetadataBigAsync(
        string extractedDirectory,
        string variantId,
        string metadataFileName,
        string metadataTargetPath)
    {
        var metadataSearchPaths = new[]
        {
            Path.Combine(extractedDirectory, DirZh, metadataFileName),
            Path.Combine(extractedDirectory, DirCcg, metadataFileName),
            Path.Combine(extractedDirectory, DirZh, variantId, metadataFileName),
            Path.Combine(extractedDirectory, DirCcg, variantId, metadataFileName),
            Path.Combine(extractedDirectory, DirZh, variantId, GameContentConstants.BigEnDirectoryName, metadataFileName),
            Path.Combine(extractedDirectory, DirZh, variantId, GameContentConstants.BigDirectoryName, metadataFileName),
            Path.Combine(extractedDirectory, DirCcg, variantId, GameContentConstants.BigEnDirectoryName, metadataFileName),
            Path.Combine(extractedDirectory, DirCcg, variantId, GameContentConstants.BigDirectoryName, metadataFileName),
        };

        var foundSearchPath = metadataSearchPaths.FirstOrDefault(File.Exists);
        if (foundSearchPath != null)
        {
            logger.LogInformation("Found Control Bar metadata file at {SourcePath}, copying to root", foundSearchPath);
            await TryCopyFileWithRetryAsync(foundSearchPath, metadataTargetPath, logger);
        }
    }

    private async Task WriteFallbackMetadataBigAsync(
        string metadataTargetPath,
        string metadataFileName,
        HashSet<string> repackedOutputs,
        CancellationToken cancellationToken)
    {
        logger.LogWarning("Control Bar metadata file not found, writing embedded fallback");
        try
        {
            var metadataBytes = Convert.FromBase64String(ControlBarMetadataBigBase64);
            await File.WriteAllBytesAsync(metadataTargetPath, metadataBytes, cancellationToken);
            repackedOutputs.Add(metadataFileName);
            logger.LogInformation("Created Control Bar metadata file {FileName} from fallback", metadataFileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create fallback Control Bar metadata file");
        }
    }

    private void CleanupSourceDirectories(string extractedDirectory, HashSet<string> repackedOutputs)
    {
        // Destructive cleanup must never run when only the fallback metadata BIG was
        // produced; otherwise source content that failed to package would be deleted.
        var hasPackagedContent = repackedOutputs.Any(name => !IsMetadataOnlyBig(name));

        if (!hasPackagedContent)
        {
            logger.LogWarning(
                "Skipping Control Bar source cleanup because no content BIG files were produced for {Directory}",
                extractedDirectory);
            return;
        }

        try
        {
            var targetSourceDirNames = new[] { DirZh, DirCcg, DirArt, DirData, GameContentConstants.WindowDirectoryName, GameContentConstants.GenToolDirectoryName, Variant720p, Variant900p, Variant1080p, Variant1440p, Variant2160p, Variant4k };
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
                if (repackedOutputs.Contains(fileName) || IsMetadataOnlyBig(fileName))
                {
                    continue;
                }

                var ext = Path.GetExtension(file);
                if (ext.Equals(BigFileExtension, StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(WindowFileExtension, StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".dat", StringComparison.OrdinalIgnoreCase) ||
                    IsImageExtension(ext))
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
