using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace GenHub.Core.Services.Tools;

/// <summary>
/// Service that inspects executables, packages, and directories to detect and extract GenHub build metadata.
/// </summary>
public sealed partial class GenHubBuildInspector(ILogger<GenHubBuildInspector>? logger = null) : IGenHubBuildInspector
{
    private const int RegexTimeoutMs = RegexConstants.BuildInspectionTimeoutMs;
    private readonly ILogger<GenHubBuildInspector> _logger = logger ?? NullLogger<GenHubBuildInspector>.Instance;

    /// <summary>
    /// Determines whether the specified content metadata represents a GenHub application build.
    /// Build classification strictly requires <see cref="ContentType.GenHubBuild"/> or the
    /// <see cref="GenHubBuildConstants.GenHubBuildTag"/> tag ("genhub-build"). Content names
    /// are not used for classification to prevent false positives from community mods or tools
    /// (e.g. "GenHub Map Builder").
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="tags">The optional tags collection.</param>
    /// <returns><c>true</c> if the metadata represents a GenHub application build; otherwise, <c>false</c>.</returns>
    public static bool IsGenHubApplicationBuild(ContentType? contentType, IEnumerable<string>? tags = null)
    {
        if (contentType == ContentType.GenHubBuild)
        {
            return true;
        }

        return tags?.Any(t => string.Equals(t, GenHubBuildConstants.GenHubBuildTag, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <inheritdoc />
    public bool IsGenHubBuildPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizePathSeparators(path);
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (TestFixturePattern().IsMatch(fileName))
        {
            return false;
        }

        if (GenHubNamePattern().IsMatch(fileName))
        {
            return true;
        }

        return InspectPeFileHeader(path, normalizedPath);
    }

    /// <inheritdoc />
    public Task<GenHubBuildInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Inspect(path), cancellationToken);
    }

    /// <inheritdoc />
    public GenHubBuildInfo Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateNonBuildInfo();
        }

        try
        {
            if (Directory.Exists(path))
            {
                var dirResult = InspectDirectory(path);
                if (dirResult.IsGenHubBuild)
                {
                    return dirResult;
                }
            }

            if (File.Exists(path))
            {
                var fileResult = InspectExistingFile(path);
                if (fileResult.IsGenHubBuild)
                {
                    return fileResult;
                }
            }

            if (IsGenHubBuildPath(path))
            {
                return InspectFromFileNameOnly(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException or BadImageFormatException or RegexMatchTimeoutException)
        {
            _logger.LogWarning(ex, "Failed to inspect potential GenHub build at path '{Path}'", path);
            if (IsGenHubBuildPath(path))
            {
                return InspectFromFileNameOnly(path);
            }
        }

        return CreateNonBuildInfo();
    }

    [GeneratedRegex(@"^(genhub[\.\-_](setup|pr|dev|build|v\d|release|fork|community|windows|nightly|installer|core)|genhub\.(exe|dll)|genhub\-setup|genhub\-pr)", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex GenHubNamePattern();

    [GeneratedRegex(@"^genhub[\-_]test[\-_][0-9a-fA-F]{8,}", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex TestFixturePattern();

    [GeneratedRegex(@"-pr(\d+)", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex PrSourcePattern();

    [GeneratedRegex(@"(?:^|[-_.])pr[-_]?(\d+)", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex PrFileNamePattern();

    [GeneratedRegex(@"(?:\b|[vV]|Setup-)(\d+\.\d+(?:\.\d+)?(?:-(?:dev|alpha|beta|rc|preview|pr)\.?[a-zA-Z0-9]+)?)(?:[-_.]|$)", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex SemVerFileNamePattern();

    [GeneratedRegex(@"(\d+\.\d+(?:\.\d+)?)", RegexOptions.None, RegexTimeoutMs)]
    private static partial Regex FallbackVersionFileNamePattern();

    [GeneratedRegex(@"[^a-z0-9\-]+", RegexOptions.None, RegexTimeoutMs)]
    private static partial Regex ForkSlugPattern();

    [GeneratedRegex(@"GenHub\s*[\(\[]([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex ProductForkNamePattern();

    [GeneratedRegex(@"(?:^|[-._])dev(?:$|[-._])", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex DevFileNameTokenPattern();

    [GeneratedRegex(@"(?:^|[-._])(?:test|beta)(?:$|[-._])", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex TestFileNameTokenPattern();

    private static GenHubBuildInfo CreateNonBuildInfo() =>
        new() { IsGenHubBuild = false, Version = GenHubBuildConstants.UnknownVersion, SuggestedCategory = GenHubBuildConstants.CategoryRelease };

    /// <summary>
    /// Normalizes path separators to forward slashes so Windows-style paths resolve file names correctly on all platforms.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The path with forward-slash separators.</returns>
    private static string NormalizePathSeparators(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Converts a fork display name into a URL-safe slug for content IDs and tags.
    /// </summary>
    /// <param name="forkName">The fork display name.</param>
    /// <returns>The slugified fork name.</returns>
    private static string ToForkSlug(string forkName)
    {
        var slug = ForkSlugPattern().Replace(forkName.ToLowerInvariant(), "-").Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(forkName)))[..8].ToLowerInvariant();
        }

        return slug;
    }

    private static bool IsGenHubText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains(GenHubBuildConstants.OfficialProductName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InspectPeFileHeader(string path, string normalizedPath)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var ext = Path.GetExtension(normalizedPath);
        if (!ext.Equals(GenHubBuildConstants.ExecutableExtension, StringComparison.OrdinalIgnoreCase) && !ext.Equals(GenHubBuildConstants.LibraryExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            return IsGenHubText(versionInfo.ProductName) ||
                   IsGenHubText(versionInfo.FileDescription) ||
                   IsGenHubText(versionInfo.OriginalFilename);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return false;
        }
    }

    private static (string? CleanVersion, string? GitHash, int? PrNumber) NormalizeVersion(
        string? rawVersion,
        string? informationalVersion,
        int? existingPrNumber)
    {
        if (string.IsNullOrWhiteSpace(rawVersion) && string.IsNullOrWhiteSpace(informationalVersion))
        {
            return (null, null, existingPrNumber);
        }

        var source = !string.IsNullOrWhiteSpace(rawVersion) ? rawVersion : informationalVersion ?? string.Empty;
        source = source.Trim();
        if (source.StartsWith("v", StringComparison.OrdinalIgnoreCase) && source.Length > 1 && char.IsDigit(source[1]))
        {
            source = source[1..];
        }

        string? hash = null;
        var plusIndex = source.IndexOf('+');
        if (plusIndex >= 0)
        {
            hash = source[(plusIndex + 1)..].Trim();
            source = source[..plusIndex].Trim();
        }

        int? prNumber = existingPrNumber;
        if (!prNumber.HasValue)
        {
            var prMatch = PrSourcePattern().Match(source);
            if (prMatch.Success && int.TryParse(prMatch.Groups[1].Value, out var parsedPr))
            {
                prNumber = parsedPr;
            }
        }

        return (source, hash, prNumber);
    }

    private static string? ReadFixedStringCustomAttribute(CustomAttribute attribute)
    {
        try
        {
            var value = attribute.DecodeValue(new DummyStringProvider());
            if (value.FixedArguments.Length > 0 && value.FixedArguments[0].Value is string strVal)
            {
                return strVal;
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            // Ignore decoding failures
        }

        return null;
    }

    private static (string? Key, string? Value) ReadKeyValueCustomAttribute(CustomAttribute attribute)
    {
        try
        {
            var value = attribute.DecodeValue(new DummyStringProvider());
            if (value.FixedArguments.Length >= 2)
            {
                var k = value.FixedArguments[0].Value as string;
                var v = value.FixedArguments[1].Value as string;
                return (k, v);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            // Ignore decoding failures
        }

        return (null, null);
    }

    private static (string? Id, string? Version, string? Authors, string? Title, string? Description) ReadNuspecMetadata(ZipArchiveEntry? nuspecEntry)
    {
        if (nuspecEntry == null || nuspecEntry.Length > GenHubBuildConstants.MaxArchiveEntrySizeBytes)
        {
            return default;
        }

        try
        {
            using var stream = nuspecEntry.Open();
            using var memoryStream = new MemoryStream();
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > GenHubBuildConstants.MaxArchiveEntrySizeBytes)
                {
                    return default;
                }

                memoryStream.Write(buffer, 0, bytesRead);
            }

            memoryStream.Position = 0;

            using var reader = XmlReader.Create(memoryStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = GenHubBuildConstants.MaxArchiveEntrySizeBytes,
            });
            var doc = XDocument.Load(reader);
            var metadata = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            if (metadata != null)
            {
                return (
                    metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value?.Trim(),
                    metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))?.Value?.Trim(),
                    metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("authors", StringComparison.OrdinalIgnoreCase))?.Value?.Trim(),
                    metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))?.Value?.Trim(),
                    metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase))?.Value?.Trim());
            }
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            // Ignore nuspec parsing errors
        }

        return default;
    }

    private static (string? Version, int? PrNumber) ParseVersionAndPrFromFileName(string baseName)
    {
        int? prNumber = null;
        var prMatch = PrFileNamePattern().Match(baseName);
        if (prMatch.Success && int.TryParse(prMatch.Groups[1].Value, out var pr))
        {
            prNumber = pr;
        }

        string? version = null;
        var verMatch = SemVerFileNamePattern().Match(baseName);
        if (verMatch.Success)
        {
            version = verMatch.Groups[1].Value;
        }
        else
        {
            var fallbackVer = FallbackVersionFileNamePattern().Match(baseName);
            if (fallbackVer.Success)
            {
                version = fallbackVer.Groups[1].Value;
            }
        }

        return (version, prNumber);
    }

    private static (bool IsCustom, bool IsFork) ParseForkFromFileName(string baseName)
    {
        var isFork = baseName.Contains("fork", StringComparison.OrdinalIgnoreCase);
        var isCustom = baseName.Contains("custom", StringComparison.OrdinalIgnoreCase) ||
                       baseName.Contains("community", StringComparison.OrdinalIgnoreCase) ||
                       isFork;
        return (isCustom, isFork);
    }

    private static string DetermineChannelFromFileName(string baseName, int? prNumber, bool isCustom, bool isFork)
    {
        if (prNumber.HasValue)
        {
            return GenHubBuildConstants.ChannelPr;
        }

        if (isFork || isCustom)
        {
            return GenHubBuildConstants.ChannelCustomFork;
        }

        if (baseName.Contains("dev", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelDev;
        }

        if (baseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelTest;
        }

        return GenHubBuildConstants.ChannelRelease;
    }

    private static (bool IsCustom, bool IsFork, string? ForkName) DetermineForkAndCustom(
        string? companyName,
        string? buildChannel,
        string? productName,
        string? version)
    {
        var isCustom = false;
        var isFork = false;
        string? forkName = null;

        if (!string.IsNullOrWhiteSpace(companyName) &&
            !companyName.Equals(GenHubBuildConstants.OfficialCompany, StringComparison.OrdinalIgnoreCase))
        {
            isCustom = true;
            isFork = true;
            forkName = companyName;
        }

        if (string.Equals(buildChannel, GenHubBuildConstants.ChannelCustomFork, StringComparison.OrdinalIgnoreCase))
        {
            isCustom = true;
            isFork = true;
        }

        if (!string.IsNullOrWhiteSpace(productName))
        {
            var match = ProductForkNamePattern().Match(productName);
            if (match.Success)
            {
                isCustom = true;
                isFork = true;
                forkName ??= match.Groups[1].Value.Trim();
            }
        }

        if (version?.Contains("fork", StringComparison.OrdinalIgnoreCase) == true)
        {
            isCustom = true;
            isFork = true;
        }

        return (isCustom, isFork, forkName);
    }

    private static string? ResolveExplicitChannel(string? explicitChannel)
    {
        if (string.IsNullOrWhiteSpace(explicitChannel))
        {
            return null;
        }

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelPr, StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelPr;
        }

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelDev, StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelDev;
        }

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelTest, StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelTest;
        }

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelCustomFork, StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelCustomFork;
        }

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelRelease, StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelRelease;
        }

        return null;
    }

    private static string? ResolveChannelFromVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        if (PrFileNamePattern().IsMatch(version))
        {
            return GenHubBuildConstants.ChannelPr;
        }

        if (version.Contains("-dev", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelDev;
        }

        if (version.Contains("-test", StringComparison.OrdinalIgnoreCase) || version.Contains("-beta", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelTest;
        }

        return null;
    }

    private static string ResolveChannelFromFileName(string fileName)
    {
        if (DevFileNameTokenPattern().IsMatch(fileName))
        {
            return GenHubBuildConstants.ChannelDev;
        }

        if (TestFileNameTokenPattern().IsMatch(fileName))
        {
            return GenHubBuildConstants.ChannelTest;
        }

        return GenHubBuildConstants.ChannelRelease;
    }

    private static string DetermineChannel(
        string? explicitChannel,
        int? prNumber,
        string? version,
        string fileName,
        bool isCustom,
        bool isFork)
    {
        if (prNumber.HasValue)
        {
            return GenHubBuildConstants.ChannelPr;
        }

        var explicitResult = ResolveExplicitChannel(explicitChannel);
        if (explicitResult != null)
        {
            return explicitResult;
        }

        if (isFork || isCustom)
        {
            return GenHubBuildConstants.ChannelCustomFork;
        }

        var versionResult = ResolveChannelFromVersion(version);
        if (versionResult != null)
        {
            return versionResult;
        }

        return ResolveChannelFromFileName(fileName);
    }

    private static string DetermineSuggestedCategory(string channel) =>
        channel switch
        {
            GenHubBuildConstants.ChannelPr => GenHubBuildConstants.CategoryTest,
            GenHubBuildConstants.ChannelDev => GenHubBuildConstants.CategoryDev,
            GenHubBuildConstants.ChannelTest => GenHubBuildConstants.CategoryTest,
            GenHubBuildConstants.ChannelCustomFork => GenHubBuildConstants.CategoryCustomFork,
            _ => GenHubBuildConstants.CategoryRelease,
        };

    private static (string Id, string Name, string Description) DetermineSuggestedIdentity(
        bool isFork,
        bool isCustom,
        string? forkName,
        string channel,
        int? pullRequestNumber)
    {
        if (isFork && !string.IsNullOrWhiteSpace(forkName))
        {
            var cleanSlug = ToForkSlug(forkName);
            return (string.Format(CultureInfo.InvariantCulture, GenHubBuildConstants.ForkContentIdFormat, cleanSlug),
                    $"GenHub ({forkName})",
                    $"{GenHubBuildConstants.ForkOfPrefix}GenHub by {forkName}.");
        }

        if (isCustom)
        {
            return (GenHubBuildConstants.CustomContentId, GenHubBuildConstants.CustomBuildName, "Custom third-party build of GenHub.");
        }

        if (channel == GenHubBuildConstants.ChannelTest)
        {
            return (GenHubBuildConstants.TestContentId, GenHubBuildConstants.TestBuildName, "Experimental test build of GenHub.");
        }

        if (channel == GenHubBuildConstants.ChannelPr)
        {
            if (pullRequestNumber.HasValue)
            {
                return (string.Format(CultureInfo.InvariantCulture, GenHubBuildConstants.PrContentIdFormat, pullRequestNumber.Value),
                        $"GenHub PR #{pullRequestNumber.Value}",
                        $"GenHub pull request build for PR #{pullRequestNumber.Value}.");
            }

            return (GenHubBuildConstants.PrBuildContentId, GenHubBuildConstants.PrBuildName, "GenHub pull request build.");
        }

        if (channel == GenHubBuildConstants.ChannelDev)
        {
            return (GenHubBuildConstants.DevContentId, GenHubBuildConstants.DevBuildName, "Development branch build of GenHub.");
        }

        return (GenHubBuildConstants.OfficialContentId, GenHubBuildConstants.OfficialProductName, "Official GenHub application build.");
    }

    private static List<string> GenerateSuggestedTags(
        bool isFork,
        bool isCustom,
        string? forkName,
        string channel,
        int? pullRequestNumber)
    {
        var tags = new List<string> { GenHubBuildConstants.TagGenHub, GenHubBuildConstants.TagBuild };
        if (isCustom || isFork)
        {
            tags.Add(GenHubBuildConstants.TagFork);
            tags.Add(GenHubBuildConstants.TagCustom);
            if (!string.IsNullOrWhiteSpace(forkName))
            {
                tags.Add($"{GenHubBuildConstants.TagPrefixFork}{ToForkSlug(forkName)}");
            }
        }
        else if (channel == GenHubBuildConstants.ChannelPr && pullRequestNumber.HasValue)
        {
            tags.Add(GenHubBuildConstants.TagPr);
            tags.Add($"{GenHubBuildConstants.TagPrefixPr}{pullRequestNumber.Value}");
        }
        else if (channel == GenHubBuildConstants.ChannelDev)
        {
            tags.Add(GenHubBuildConstants.TagDev);
            tags.Add(GenHubBuildConstants.TagChannelDev);
        }
        else if (channel == GenHubBuildConstants.ChannelTest)
        {
            tags.Add(GenHubBuildConstants.TagTest);
            tags.Add(GenHubBuildConstants.TagChannelTest);
        }

        return tags;
    }

    private static void ApplyCustomAttribute(
        CustomAttribute attribute,
        string attributeTypeName,
        ref PeMetadataAccumulator acc)
    {
        var value = ReadFixedStringCustomAttribute(attribute);
        if (string.IsNullOrWhiteSpace(value) && attributeTypeName != GenHubBuildConstants.AttrAssemblyMetadata)
        {
            return;
        }

        switch (attributeTypeName)
        {
            case GenHubBuildConstants.AttrAssemblyInformationalVersion:
                acc.InformationalVersion = value;
                break;
            case GenHubBuildConstants.AttrAssemblyFileVersion:
                acc.FileVersion = value;
                break;
            case GenHubBuildConstants.AttrAssemblyProduct:
                acc.ProductName = value;
                break;
            case GenHubBuildConstants.AttrAssemblyCompany:
                acc.CompanyName = value;
                break;
            case GenHubBuildConstants.AttrAssemblyTitle or GenHubBuildConstants.AttrAssemblyDescription:
                acc.FileDescription = value;
                break;
            case GenHubBuildConstants.AttrAssemblyMetadata:
                var (key, metaValue) = ReadKeyValueCustomAttribute(attribute);
                if (string.Equals(key, GenHubBuildConstants.MetadataKeyBuildChannel, StringComparison.OrdinalIgnoreCase))
                {
                    acc.BuildChannel = metaValue;
                }
                else if (string.Equals(key, GenHubBuildConstants.MetadataKeyPullRequestNumber, StringComparison.OrdinalIgnoreCase) && int.TryParse(metaValue, out var pr))
                {
                    acc.PullRequestNumber = pr;
                }
                else if ((string.Equals(key, GenHubBuildConstants.MetadataKeyGitHash, StringComparison.OrdinalIgnoreCase) || string.Equals(key, GenHubBuildConstants.MetadataKeyCommitHash, StringComparison.OrdinalIgnoreCase)) && metaValue != null)
                {
                    acc.GitShortHash = metaValue.Length > 7 ? metaValue[..7] : metaValue;
                }

                break;
            default:
                // Ignore unrecognized custom attributes.
                break;
        }
    }

    private static string? GetCustomAttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        var attributeType = attribute.Constructor;
        if (attributeType.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)attributeType);
            if (memberRef.Parent.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                return reader.GetString(typeRef.Name);
            }
        }

        return null;
    }

    private GenHubBuildInfo InspectExistingFile(string path)
    {
        var normalized = NormalizePathSeparators(path);
        var ext = Path.GetExtension(normalized);

        if (ext.Equals(GenHubBuildConstants.PackageExtension, StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(GenHubBuildConstants.ZipExtension, StringComparison.OrdinalIgnoreCase))
        {
            var archiveResult = InspectArchive(path);
            if (archiveResult.IsGenHubBuild)
            {
                return archiveResult;
            }
        }

        if (ext.Equals(GenHubBuildConstants.ExecutableExtension, StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(GenHubBuildConstants.LibraryExtension, StringComparison.OrdinalIgnoreCase))
        {
            var binResult = InspectBinaryFile(path);
            if (binResult.IsGenHubBuild)
            {
                return binResult;
            }
        }

        return CreateNonBuildInfo();
    }

    private GenHubBuildInfo InspectDirectory(string dirPath)
    {
        var primaryExes = new[]
        {
            GenHubBuildConstants.WindowsExecutable,
            GenHubBuildConstants.GenericExecutable,
            GenHubBuildConstants.ManagedAssembly,
            GenHubBuildConstants.SetupExecutable,
        };

        foreach (var candidate in primaryExes)
        {
            var fullPath = Path.Combine(dirPath, candidate);
            if (File.Exists(fullPath))
            {
                var info = InspectBinaryFile(fullPath);
                if (info.IsGenHubBuild)
                {
                    return info with { EntryPoint = candidate };
                }
            }
        }

        return InspectDirectorySubFiles(dirPath) ?? CreateNonBuildInfo();
    }

    private GenHubBuildInfo? InspectDirectorySubFiles(string dirPath)
    {
        // Look in root and immediate subdirectories only, avoiding full subtree recursion
        try
        {
            var topFiles = Directory.EnumerateFiles(dirPath, $"*{GenHubBuildConstants.ExecutableExtension}", SearchOption.TopDirectoryOnly).Take(10);
            var topMatch = TryFindBuildInFiles(dirPath, topFiles);
            if (topMatch != null)
            {
                return topMatch;
            }

            foreach (var subDir in Directory.EnumerateDirectories(dirPath).Take(5))
            {
                var subFiles = Directory.EnumerateFiles(subDir, $"*{GenHubBuildConstants.ExecutableExtension}", SearchOption.TopDirectoryOnly).Take(5);
                var subMatch = TryFindBuildInFiles(dirPath, subFiles);
                if (subMatch != null)
                {
                    return subMatch;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or RegexMatchTimeoutException)
        {
            _logger.LogDebug(ex, "Error enumerating directory '{Directory}' during build inspection", dirPath);
        }

        return null;
    }

    private GenHubBuildInfo? TryFindBuildInFiles(string dirPath, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var info = InspectBinaryFile(file);
            if (info.IsGenHubBuild)
            {
                var relative = Path.GetRelativePath(dirPath, file);
                return info with { EntryPoint = NormalizePathSeparators(relative) };
            }
        }

        return null;
    }

    private GenHubBuildInfo InspectArchive(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            // 1. Check for .nuspec (e.g. in Velopack nupkg)
            var nuspecEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(GenHubBuildConstants.NuspecExtension, StringComparison.OrdinalIgnoreCase));
            var (nuspecId, nuspecVersion, nuspecAuthors, nuspecTitle, nuspecDescription) = ReadNuspecMetadata(nuspecEntry);

            // 2. Look for primary GenHub executable inside archive
            var targetEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals(GenHubBuildConstants.WindowsExecutable, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(GenHubBuildConstants.GenericExecutable, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(GenHubBuildConstants.ManagedAssembly, StringComparison.OrdinalIgnoreCase));

            if (targetEntry != null)
            {
                var targetMatch = TryInspectArchiveTargetEntry(
                    targetEntry,
                    archivePath,
                    nuspecId,
                    nuspecVersion,
                    nuspecAuthors,
                    nuspecTitle,
                    nuspecDescription);

                if (targetMatch != null)
                {
                    return targetMatch;
                }
            }

            // 3. Fallback to filename inspection if archive name matches GenHub pattern
            if (IsGenHubBuildPath(archivePath))
            {
                return InspectFromFileNameOnly(archivePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException or BadImageFormatException or RegexMatchTimeoutException)
        {
            _logger.LogDebug(ex, "Failed to inspect archive '{Path}'", archivePath);
            if (IsGenHubBuildPath(archivePath))
            {
                return InspectFromFileNameOnly(archivePath);
            }
        }

        return CreateNonBuildInfo();
    }

    private GenHubBuildInfo? TryInspectArchiveTargetEntry(
        ZipArchiveEntry targetEntry,
        string archivePath,
        string? nuspecId,
        string? nuspecVersion,
        string? nuspecAuthors,
        string? nuspecTitle,
        string? nuspecDescription)
    {
        if (targetEntry.Length > GenHubBuildConstants.MaxArchiveEntrySizeBytes)
        {
            _logger.LogWarning(
                "Archive entry '{Entry}' in '{Path}' exceeds maximum safe size of {Max} bytes; skipping decompression.",
                targetEntry.FullName,
                archivePath,
                GenHubBuildConstants.MaxArchiveEntrySizeBytes);
            return null;
        }

        using var entryStream = targetEntry.Open();
        using var memStream = new MemoryStream();
        entryStream.CopyTo(memStream);
        memStream.Position = 0;

        var peMetadata = TryReadPeMetadata(memStream);

        var rawVersion = peMetadata.InformationalVersion ?? nuspecVersion ?? peMetadata.Version;
        var (cleanVersion, hash, prNum) = NormalizeVersion(rawVersion, peMetadata.InformationalVersion, peMetadata.PullRequestNumber);

        var productName = peMetadata.ProductName ?? nuspecTitle ?? nuspecId ?? GenHubBuildConstants.OfficialProductName;
        var companyName = peMetadata.CompanyName ?? nuspecAuthors;
        var fileDesc = peMetadata.FileDescription ?? nuspecDescription;

        var normalizedArchivePath = NormalizePathSeparators(archivePath);
        var fileName = Path.GetFileName(normalizedArchivePath);
        var isGenHub = IsGenHubText(productName) ||
                       IsGenHubText(fileDesc) ||
                       IsGenHubText(targetEntry.Name) ||
                       IsGenHubText(nuspecId) ||
                       (!TestFixturePattern().IsMatch(fileName) && GenHubNamePattern().IsMatch(fileName));

        if (!isGenHub)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cleanVersion))
        {
            var fallback = InspectFromFileNameOnly(archivePath);
            cleanVersion = fallback.Version;
            prNum ??= fallback.PullRequestNumber;
        }

        var ctx = new BuildMetadataContext(
            IsGenHub: true,
            CleanVersion: cleanVersion,
            ProductVersion: rawVersion,
            FileVersion: peMetadata.FileVersion,
            ProductName: productName,
            CompanyName: companyName,
            FileDescription: fileDesc,
            GitShortHash: peMetadata.GitShortHash ?? hash,
            PullRequestNumber: prNum,
            BuildChannel: peMetadata.BuildChannel,
            EntryPoint: NormalizePathSeparators(targetEntry.FullName),
            FileName: fileName);

        return BuildResult(in ctx);
    }

    private GenHubBuildInfo InspectBinaryFile(string filePath)
    {
        var normalizedFilePath = NormalizePathSeparators(filePath);
        var fileName = Path.GetFileName(normalizedFilePath);
        FileVersionInfo? versionInfo = null;

        try
        {
            versionInfo = FileVersionInfo.GetVersionInfo(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            // Handled below via PE reader
        }

        PeMetadata peMetadata = default;
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            peMetadata = TryReadPeMetadata(fileStream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException or BadImageFormatException or RegexMatchTimeoutException)
        {
            _logger.LogDebug(ex, "Failed to read PE metadata from '{Path}'", filePath);
        }

        var productName = peMetadata.ProductName ?? versionInfo?.ProductName;
        var fileDescription = peMetadata.FileDescription ?? versionInfo?.FileDescription;
        var originalFileName = versionInfo?.OriginalFilename;
        var companyName = peMetadata.CompanyName ?? versionInfo?.CompanyName;
        var productVersion = peMetadata.InformationalVersion ?? versionInfo?.ProductVersion ?? peMetadata.Version;
        var fileVersion = versionInfo?.FileVersion ?? peMetadata.FileVersion;

        var isGenHub = IsGenHubText(productName) ||
                       IsGenHubText(fileDescription) ||
                       IsGenHubText(originalFileName) ||
                       (!TestFixturePattern().IsMatch(fileName) && GenHubNamePattern().IsMatch(fileName));

        if (!isGenHub)
        {
            return CreateNonBuildInfo();
        }

        var (cleanVersion, hash, prNum) = NormalizeVersion(
            productVersion ?? fileVersion,
            peMetadata.InformationalVersion,
            peMetadata.PullRequestNumber);

        if (string.IsNullOrWhiteSpace(cleanVersion))
        {
            var fallback = InspectFromFileNameOnly(filePath);
            cleanVersion = fallback.Version;
            prNum ??= fallback.PullRequestNumber;
        }

        var ctx = new BuildMetadataContext(
            IsGenHub: true,
            CleanVersion: cleanVersion,
            ProductVersion: productVersion,
            FileVersion: fileVersion,
            ProductName: productName ?? GenHubBuildConstants.OfficialProductName,
            CompanyName: companyName,
            FileDescription: fileDescription,
            GitShortHash: peMetadata.GitShortHash ?? hash,
            PullRequestNumber: prNum,
            BuildChannel: peMetadata.BuildChannel,
            EntryPoint: fileName,
            FileName: fileName);

        return BuildResult(in ctx);
    }

    private GenHubBuildInfo InspectFromFileNameOnly(string path)
    {
        var normalized = NormalizePathSeparators(path);
        var fileName = Path.GetFileName(normalized);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        var (version, prNumber) = ParseVersionAndPrFromFileName(baseName);
        var (isCustom, isFork) = ParseForkFromFileName(baseName);
        var channel = DetermineChannelFromFileName(baseName, prNumber, isCustom, isFork);

        var resolvedProductName = GenHubBuildConstants.OfficialProductName;
        var resolvedCompanyName = GenHubBuildConstants.OfficialCompany;

        if (isFork)
        {
            resolvedProductName = GenHubBuildConstants.CustomBuildName;
            resolvedCompanyName = GenHubBuildConstants.FallbackForkCompanyName;
        }
        else if (isCustom)
        {
            resolvedProductName = GenHubBuildConstants.CustomBuildName;
            resolvedCompanyName = GenHubBuildConstants.FallbackCustomCompanyName;
        }

        var ctx = new BuildMetadataContext(
            IsGenHub: true,
            CleanVersion: version ?? GenHubBuildConstants.DefaultVersion,
            ProductVersion: version,
            FileVersion: version,
            ProductName: resolvedProductName,
            CompanyName: resolvedCompanyName,
            FileDescription: GenHubBuildConstants.DefaultFileDescription,
            GitShortHash: null,
            PullRequestNumber: prNumber,
            BuildChannel: channel,
            EntryPoint: fileName,
            FileName: fileName);

        return BuildResult(in ctx);
    }

    private GenHubBuildInfo BuildResult(in BuildMetadataContext ctx)
    {
        var (isCustom, isFork, forkName) = DetermineForkAndCustom(ctx.CompanyName, ctx.BuildChannel, ctx.ProductName, ctx.CleanVersion);
        var channel = DetermineChannel(ctx.BuildChannel, ctx.PullRequestNumber, ctx.CleanVersion, ctx.FileName, isCustom, isFork);
        var category = DetermineSuggestedCategory(channel);
        var (suggestedId, suggestedName, suggestedDesc) = DetermineSuggestedIdentity(isFork, isCustom, forkName, channel, ctx.PullRequestNumber);
        var tags = GenerateSuggestedTags(isFork, isCustom, forkName, channel, ctx.PullRequestNumber);

        return new GenHubBuildInfo
        {
            IsGenHubBuild = ctx.IsGenHub,
            Version = ctx.CleanVersion ?? GenHubBuildConstants.DefaultVersion,
            ProductVersion = ctx.ProductVersion,
            FileVersion = ctx.FileVersion,
            GitShortHash = ctx.GitShortHash,
            PullRequestNumber = ctx.PullRequestNumber,
            BuildChannel = channel,
            IsCustomBuild = isCustom,
            IsFork = isFork,
            ForkName = forkName,
            EntryPoint = ctx.EntryPoint,
            SuggestedContentId = suggestedId,
            SuggestedContentName = suggestedName,
            SuggestedCategory = category,
            SuggestedDescription = suggestedDesc,
            SuggestedTags = tags,
        };
    }

    private PeMetadata TryReadPeMetadata(Stream stream)
    {
        try
        {
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                return default;
            }

            var reader = peReader.GetMetadataReader();
            PeMetadataAccumulator accumulator = default;

            foreach (var customAttributeHandle in reader.CustomAttributes)
            {
                var attribute = reader.GetCustomAttribute(customAttributeHandle);
                var attributeTypeName = GetCustomAttributeTypeName(reader, attribute);
                if (attributeTypeName != null)
                {
                    ApplyCustomAttribute(attribute, attributeTypeName, ref accumulator);
                }
            }

            var version = reader.IsAssembly ? reader.GetAssemblyDefinition().Version?.ToString() : null;

            return new PeMetadata(
                Version: version,
                InformationalVersion: accumulator.InformationalVersion,
                FileVersion: accumulator.FileVersion,
                ProductName: accumulator.ProductName,
                CompanyName: accumulator.CompanyName,
                FileDescription: accumulator.FileDescription,
                BuildChannel: accumulator.BuildChannel,
                PullRequestNumber: accumulator.PullRequestNumber,
                GitShortHash: accumulator.GitShortHash);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or BadImageFormatException or RegexMatchTimeoutException)
        {
            _logger.LogDebug(ex, "Failed to read CLI metadata from PE stream");
            return default;
        }
    }

    private ref struct PeMetadataAccumulator
    {
        public string? InformationalVersion;
        public string? FileVersion;
        public string? ProductName;
        public string? CompanyName;
        public string? FileDescription;
        public string? BuildChannel;
        public int? PullRequestNumber;
        public string? GitShortHash;
    }

    private readonly record struct BuildMetadataContext(
        bool IsGenHub,
        string? CleanVersion,
        string? ProductVersion,
        string? FileVersion,
        string? ProductName,
        string? CompanyName,
        string? FileDescription,
        string? GitShortHash,
        int? PullRequestNumber,
        string? BuildChannel,
        string? EntryPoint,
        string FileName);

    private readonly record struct PeMetadata(
        string? Version,
        string? InformationalVersion,
        string? FileVersion,
        string? ProductName,
        string? CompanyName,
        string? FileDescription,
        string? BuildChannel,
        int? PullRequestNumber,
        string? GitShortHash);

    private sealed class DummyStringProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetSystemType() => "System.Type";

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}
