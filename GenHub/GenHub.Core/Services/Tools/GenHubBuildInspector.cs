using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GenHub.Core.Services.Tools;

/// <summary>
/// Service that inspects executables, packages, and directories to detect and extract GenHub build metadata.
/// </summary>
public sealed partial class GenHubBuildInspector(ILogger<GenHubBuildInspector>? logger = null) : IGenHubBuildInspector
{
    private const int RegexTimeoutMs = 250;
    private readonly ILogger<GenHubBuildInspector> _logger = logger ?? NullLogger<GenHubBuildInspector>.Instance;

    /// <summary>
    /// Determines whether the specified content metadata represents a GenHub application build.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="name">The content name.</param>
    /// <param name="tags">The optional tags collection.</param>
    /// <returns><c>true</c> if the metadata represents a GenHub application build; otherwise, <c>false</c>.</returns>
    public static bool IsGenHubApplicationBuild(ContentType? contentType, string? name, IEnumerable<string>? tags = null)
    {
        if (contentType == ContentType.GenHubBuild)
        {
            return true;
        }

        if (tags?.Any(t => string.Equals(t, "genhub", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(t, "genhub-build", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(name) &&
               name.StartsWith("GenHub", StringComparison.OrdinalIgnoreCase) &&
               contentType is ContentType.GameClient or ContentType.Executable or ContentType.ModdingTool or ContentType.UnknownContentType &&
               (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("PR #", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Build", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fork", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public bool IsGenHubBuildPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = path.Replace('\\', '/');
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
        catch (Exception ex)
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

    private static GenHubBuildInfo CreateNonBuildInfo() =>
        new() { IsGenHubBuild = false, Version = "Unknown", SuggestedCategory = GenHubBuildConstants.CategoryRelease };

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
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
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
        catch
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
        catch
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
        catch
        {
            // Ignore decoding failures
        }

        return (null, null);
    }

    private static (string? Id, string? Version, string? Authors, string? Title, string? Description) ReadNuspecMetadata(ZipArchiveEntry? nuspecEntry)
    {
        if (nuspecEntry == null)
        {
            return default;
        }

        try
        {
            using var stream = nuspecEntry.Open();
            var doc = XDocument.Load(stream);
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
        catch
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
            version = verMatch.Groups[1].Value.TrimStart('v', 'V');
        }
        else
        {
            var fallbackMatch = FallbackVersionFileNamePattern().Match(baseName);
            if (fallbackMatch.Success)
            {
                version = fallbackMatch.Groups[1].Value;
            }
        }

        return (version, prNumber);
    }

    private static (bool IsCustom, bool IsFork, string? ForkName) ParseForkFromFileName(string baseName)
    {
        if (baseName.Contains("fork", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("community", StringComparison.OrdinalIgnoreCase))
        {
            return (true, true, null);
        }

        if (baseName.Contains("custom", StringComparison.OrdinalIgnoreCase))
        {
            return (true, false, null);
        }

        return (false, false, null);
    }

    private static string DetermineChannelFromFileName(string baseName, int? prNumber, bool isCustom, bool isFork)
    {
        if (prNumber.HasValue || baseName.Contains("-pr-", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelPr;
        }

        if (baseName.Contains("dev", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelDev;
        }

        if (baseName.Contains("test", StringComparison.OrdinalIgnoreCase) || baseName.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelTest;
        }

        if (isCustom || isFork)
        {
            return GenHubBuildConstants.ChannelCustomFork;
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

        if (IsCustomCompany(companyName, out var companyFork))
        {
            isCustom = true;
            isFork = true;
            forkName = companyFork;
        }

        if (IsChannelIndicatingFork(buildChannel))
        {
            isCustom = true;
            isFork = true;
        }

        if (TryExtractProductForkName(productName, out var productFork))
        {
            isFork = true;
            isCustom = true;
            forkName ??= productFork;
        }

        if (IsVersionIndicatingFork(version))
        {
            isCustom = true;
            isFork = true;
        }

        return (isCustom, isFork, forkName);
    }

    private static bool IsCustomCompany(string? companyName, out string? forkName)
    {
        forkName = null;
        if (string.IsNullOrWhiteSpace(companyName) ||
            companyName.Equals(GenHubBuildConstants.OfficialCompany, StringComparison.OrdinalIgnoreCase) ||
            companyName.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) ||
            companyName.Equals("EA", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        forkName = companyName.Trim();
        return true;
    }

    private static bool IsChannelIndicatingFork(string? buildChannel)
    {
        if (string.IsNullOrWhiteSpace(buildChannel))
        {
            return false;
        }

        var normalized = buildChannel.ToLowerInvariant();
        return normalized.Contains("fork") || normalized.Contains("custom") || normalized.Contains("community");
    }

    private static bool TryExtractProductForkName(string? productName, out string? forkName)
    {
        forkName = null;
        if (string.IsNullOrWhiteSpace(productName))
        {
            return false;
        }

        var match = ProductForkNamePattern().Match(productName);
        if (match.Success)
        {
            var candidate = match.Groups[1].Value.Trim();
            if (!candidate.Equals("Test", StringComparison.OrdinalIgnoreCase) &&
                !candidate.Equals("Dev", StringComparison.OrdinalIgnoreCase) &&
                !candidate.Equals("PR", StringComparison.OrdinalIgnoreCase))
            {
                forkName = candidate;
                return true;
            }
        }

        return productName.Contains("fork", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionIndicatingFork(string? version) =>
        !string.IsNullOrWhiteSpace(version) &&
        (version.Contains("-fork", StringComparison.OrdinalIgnoreCase) ||
         version.Contains("-custom", StringComparison.OrdinalIgnoreCase));

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

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelDev, StringComparison.OrdinalIgnoreCase) ||
            explicitChannel.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelDev;
        }

        if (explicitChannel.Equals(GenHubBuildConstants.ChannelTest, StringComparison.OrdinalIgnoreCase) ||
            explicitChannel.Equals("Beta", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelTest;
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
        if (fileName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelTest;
        }

        if (fileName.Contains("dev", StringComparison.OrdinalIgnoreCase))
        {
            return GenHubBuildConstants.ChannelDev;
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
            var cleanSlug = ForkSlugPattern().Replace(forkName.ToLowerInvariant(), "-").Trim('-');
            return ($"genhub-fork-{cleanSlug}", $"GenHub ({forkName})", $"Custom community fork of GenHub by {forkName}.");
        }

        if (isCustom)
        {
            return (GenHubBuildConstants.CustomContentId, GenHubBuildConstants.CustomBuildName, "Custom third-party build of GenHub.");
        }

        if (channel == GenHubBuildConstants.ChannelTest)
        {
            return (GenHubBuildConstants.TestContentId, GenHubBuildConstants.TestBuildName, "Experimental test build of GenHub.");
        }

        if (channel == GenHubBuildConstants.ChannelPr && pullRequestNumber.HasValue)
        {
            return ($"genhub-pr{pullRequestNumber.Value}", $"GenHub PR #{pullRequestNumber.Value}", $"GenHub pull request build for PR #{pullRequestNumber.Value}.");
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
                var cleanSlug = ForkSlugPattern().Replace(forkName.ToLowerInvariant(), "-").Trim('-');
                tags.Add($"fork:{cleanSlug}");
            }
        }
        else if (channel == GenHubBuildConstants.ChannelPr && pullRequestNumber.HasValue)
        {
            tags.Add(GenHubBuildConstants.TagPr);
            tags.Add($"pr:{pullRequestNumber.Value}");
        }
        else if (channel == GenHubBuildConstants.ChannelDev)
        {
            tags.Add(GenHubBuildConstants.TagDev);
            tags.Add("channel:dev");
        }
        else if (channel == GenHubBuildConstants.ChannelTest)
        {
            tags.Add(GenHubBuildConstants.TagTest);
            tags.Add("channel:test");
        }

        return tags;
    }

    private static void ApplyCustomAttribute(
        CustomAttribute attribute,
        string attributeTypeName,
        ref PeMetadataAccumulator acc)
    {
        var value = ReadFixedStringCustomAttribute(attribute);
        if (string.IsNullOrWhiteSpace(value) && attributeTypeName != "AssemblyMetadataAttribute")
        {
            return;
        }

        switch (attributeTypeName)
        {
            case "AssemblyInformationalVersionAttribute":
                acc.InformationalVersion = value;
                break;
            case "AssemblyFileVersionAttribute":
                acc.FileVersion = value;
                break;
            case "AssemblyProductAttribute":
                acc.ProductName = value;
                break;
            case "AssemblyCompanyAttribute":
                acc.CompanyName = value;
                break;
            case "AssemblyTitleAttribute" or "AssemblyDescriptionAttribute":
                acc.FileDescription = value;
                break;
            case "AssemblyMetadataAttribute":
                var (key, metaValue) = ReadKeyValueCustomAttribute(attribute);
                if (string.Equals(key, "BuildChannel", StringComparison.OrdinalIgnoreCase))
                {
                    acc.BuildChannel = metaValue;
                }
                else if (string.Equals(key, "PullRequestNumber", StringComparison.OrdinalIgnoreCase) && int.TryParse(metaValue, out var pr))
                {
                    acc.PullRequestNumber = pr;
                }
                else if ((string.Equals(key, "GitHash", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "CommitHash", StringComparison.OrdinalIgnoreCase)) && metaValue != null)
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
        var normalized = path.Replace('\\', '/');
        var ext = Path.GetExtension(normalized);

        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) || ext.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            var archiveResult = InspectArchive(path);
            if (archiveResult.IsGenHubBuild)
            {
                return archiveResult;
            }
        }

        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
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

        // Look in root and immediate subdirectories only, avoiding full subtree recursion
        try
        {
            foreach (var subFile in Directory.EnumerateFiles(dirPath, "*.exe", SearchOption.TopDirectoryOnly).Take(10))
            {
                var info = InspectBinaryFile(subFile);
                if (info.IsGenHubBuild)
                {
                    var relative = Path.GetRelativePath(dirPath, subFile);
                    return info with { EntryPoint = relative.Replace('\\', '/') };
                }
            }

            foreach (var subDir in Directory.EnumerateDirectories(dirPath).Take(5))
            {
                foreach (var subFile in Directory.EnumerateFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly).Take(5))
                {
                    var info = InspectBinaryFile(subFile);
                    if (info.IsGenHubBuild)
                    {
                        var relative = Path.GetRelativePath(dirPath, subFile);
                        return info with { EntryPoint = relative.Replace('\\', '/') };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error enumerating directory '{Directory}' during build inspection", dirPath);
        }

        return CreateNonBuildInfo();
    }

    private GenHubBuildInfo InspectArchive(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            // 1. Check for .nuspec (e.g. in Velopack nupkg)
            var nuspecEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            var (nuspecId, nuspecVersion, nuspecAuthors, nuspecTitle, nuspecDescription) = ReadNuspecMetadata(nuspecEntry);

            // 2. Look for primary GenHub executable inside archive
            var targetEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals(GenHubBuildConstants.WindowsExecutable, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(GenHubBuildConstants.GenericExecutable, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(GenHubBuildConstants.ManagedAssembly, StringComparison.OrdinalIgnoreCase));

            if (targetEntry != null)
            {
                if (targetEntry.Length > GenHubBuildConstants.MaxArchiveEntrySizeBytes)
                {
                    _logger.LogWarning(
                        "Archive entry '{Entry}' in '{Path}' exceeds maximum safe size of {Max} bytes; skipping decompression.",
                        targetEntry.FullName,
                        archivePath,
                        GenHubBuildConstants.MaxArchiveEntrySizeBytes);
                }
                else
                {
                    using var entryStream = targetEntry.Open();
                    using var memStream = new MemoryStream();
                    entryStream.CopyTo(memStream);
                    memStream.Position = 0;

                    var peMetadata = TryReadPeMetadata(memStream);

                    var rawVersion = nuspecVersion ?? peMetadata.InformationalVersion ?? peMetadata.Version;
                    var (cleanVersion, hash, prNum) = NormalizeVersion(rawVersion, peMetadata.InformationalVersion, peMetadata.PullRequestNumber);

                    var productName = peMetadata.ProductName ?? nuspecTitle ?? nuspecId ?? GenHubBuildConstants.OfficialProductName;
                    var companyName = peMetadata.CompanyName ?? nuspecAuthors;
                    var fileDesc = peMetadata.FileDescription ?? nuspecDescription;

                    var normalizedArchivePath = archivePath.Replace('\\', '/');
                    var fileName = Path.GetFileName(normalizedArchivePath);
                    var isGenHub = IsGenHubText(productName) ||
                                   IsGenHubText(fileDesc) ||
                                   IsGenHubText(targetEntry.Name) ||
                                   IsGenHubText(nuspecId) ||
                                   (!TestFixturePattern().IsMatch(fileName) && GenHubNamePattern().IsMatch(fileName));

                    if (isGenHub)
                    {
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
                            EntryPoint: targetEntry.Name,
                            FileName: fileName);

                        return BuildResult(in ctx);
                    }
                }
            }

            // 3. Fallback to filename inspection if archive name matches GenHub pattern
            if (IsGenHubBuildPath(archivePath))
            {
                return InspectFromFileNameOnly(archivePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to inspect archive '{Path}'", archivePath);
            if (IsGenHubBuildPath(archivePath))
            {
                return InspectFromFileNameOnly(archivePath);
            }
        }

        return CreateNonBuildInfo();
    }

    private GenHubBuildInfo InspectBinaryFile(string filePath)
    {
        var normalizedFilePath = filePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedFilePath);
        FileVersionInfo? versionInfo = null;

        try
        {
            versionInfo = FileVersionInfo.GetVersionInfo(filePath);
        }
        catch
        {
            // Handled below via PE reader
        }

        PeMetadata peMetadata = default;
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            peMetadata = TryReadPeMetadata(fileStream);
        }
        catch (Exception ex)
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
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        var (version, prNumber) = ParseVersionAndPrFromFileName(baseName);
        var (isCustom, isFork, forkName) = ParseForkFromFileName(baseName);
        var channel = DetermineChannelFromFileName(baseName, prNumber, isCustom, isFork);

        var resolvedProductName = GenHubBuildConstants.OfficialProductName;
        var resolvedCompanyName = GenHubBuildConstants.OfficialCompany;

        if (isFork)
        {
            resolvedProductName = string.IsNullOrWhiteSpace(forkName) ? GenHubBuildConstants.CustomBuildName : $"GenHub ({forkName})";
            resolvedCompanyName = forkName ?? "Community";
        }
        else if (isCustom)
        {
            resolvedProductName = GenHubBuildConstants.CustomBuildName;
            resolvedCompanyName = "Custom";
        }

        var ctx = new BuildMetadataContext(
            IsGenHub: true,
            CleanVersion: version ?? GenHubBuildConstants.DefaultVersion,
            ProductVersion: version,
            FileVersion: version,
            ProductName: resolvedProductName,
            CompanyName: resolvedCompanyName,
            FileDescription: "GenHub Application Build",
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
        catch (Exception ex)
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
