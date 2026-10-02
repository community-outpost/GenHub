using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
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
    private readonly ILogger<GenHubBuildInspector> _logger = logger ?? NullLogger<GenHubBuildInspector>.Instance;

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

        if (File.Exists(path))
        {
            var ext = Path.GetExtension(normalizedPath);
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    if (IsGenHubText(versionInfo.ProductName) ||
                        IsGenHubText(versionInfo.FileDescription) ||
                        IsGenHubText(versionInfo.OriginalFilename))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Fall through to false if unreadable
                }
            }
        }

        return false;
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
            return new GenHubBuildInfo { IsGenHubBuild = false, Version = "Unknown", SuggestedCategory = GenHubBuildConstants.CategoryRelease };
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

        return new GenHubBuildInfo { IsGenHubBuild = false, Version = "Unknown", SuggestedCategory = GenHubBuildConstants.CategoryRelease };
    }

    [GeneratedRegex(@"^(genhub[\.\-_](setup|pr|dev|build|v\d|release|fork|community|windows|nightly|installer|core)|genhub\.(exe|dll)|genhub\-setup|genhub\-pr)", RegexOptions.IgnoreCase)]
    private static partial Regex GenHubNamePattern();

    [GeneratedRegex(@"^genhub[\-_]test[\-_][0-9a-fA-F]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex TestFixturePattern();

    private static bool IsGenHubText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains(GenHubBuildConstants.OfficialProductName, StringComparison.OrdinalIgnoreCase);
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

        var source = !string.IsNullOrWhiteSpace(rawVersion) ? rawVersion! : informationalVersion!;
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
            var prMatch = Regex.Match(source, @"-pr(\d+)", RegexOptions.IgnoreCase);
            if (prMatch.Success && int.TryParse(prMatch.Groups[1].Value, out var parsedPr))
            {
                prNumber = parsedPr;
            }
        }

        return (source, hash, prNumber);
    }

    private static string? ReadFixedStringCustomAttribute(MetadataReader reader, CustomAttribute attribute)
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

    private static (string? Key, string? Value) ReadKeyValueCustomAttribute(MetadataReader reader, CustomAttribute attribute)
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

        return new GenHubBuildInfo { IsGenHubBuild = false, Version = "Unknown", SuggestedCategory = GenHubBuildConstants.CategoryRelease };
    }

    private GenHubBuildInfo InspectArchive(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            // 1. Check for .nuspec (e.g. in Velopack nupkg)
            var nuspecEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            string? nuspecId = null;
            string? nuspecVersion = null;
            string? nuspecAuthors = null;
            string? nuspecTitle = null;
            string? nuspecDescription = null;

            if (nuspecEntry != null)
            {
                try
                {
                    using var stream = nuspecEntry.Open();
                    var doc = XDocument.Load(stream);
                    var metadata = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
                    if (metadata != null)
                    {
                        nuspecId = metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                        nuspecVersion = metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                        nuspecAuthors = metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("authors", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                        nuspecTitle = metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                        nuspecDescription = metadata.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse .nuspec from archive '{Path}'", archivePath);
                }
            }

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
                        return BuildResult(
                            isGenHub: true,
                            cleanVersion: cleanVersion,
                            productVersion: rawVersion,
                            fileVersion: peMetadata.FileVersion,
                            productName: productName,
                            companyName: companyName,
                            fileDescription: fileDesc,
                            gitShortHash: peMetadata.GitShortHash ?? hash,
                            pullRequestNumber: prNum,
                            buildChannel: peMetadata.BuildChannel,
                            entryPoint: targetEntry.Name,
                            fileName: fileName);
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

        return new GenHubBuildInfo { IsGenHubBuild = false, Version = "Unknown", SuggestedCategory = GenHubBuildConstants.CategoryRelease };
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
            return new GenHubBuildInfo { IsGenHubBuild = false, Version = "Unknown", SuggestedCategory = GenHubBuildConstants.CategoryRelease };
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

        return BuildResult(
            isGenHub: true,
            cleanVersion: cleanVersion,
            productVersion: productVersion,
            fileVersion: fileVersion,
            productName: productName ?? GenHubBuildConstants.OfficialProductName,
            companyName: companyName,
            fileDescription: fileDescription,
            gitShortHash: peMetadata.GitShortHash ?? hash,
            pullRequestNumber: prNum,
            buildChannel: peMetadata.BuildChannel,
            entryPoint: fileName,
            fileName: fileName);
    }

    private GenHubBuildInfo InspectFromFileNameOnly(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        // Extract PR number if present: e.g. "GenHub-PR-123-..." or "...pr123..."
        int? prNumber = null;
        var prMatch = Regex.Match(baseName, @"(?:^|[-_.])pr[-_]?(\d+)", RegexOptions.IgnoreCase);
        if (prMatch.Success && int.TryParse(prMatch.Groups[1].Value, out var pr))
        {
            prNumber = pr;
        }

        // Extract SemVer from filename:
        // Try e.g. 1.0.0-dev.45 or 1.5.2 or 1.4.0 or v2.0.0
        string? version = null;
        var verMatch = Regex.Match(baseName, @"(?:\b|[vV]|Setup-)(\d+\.\d+(?:\.\d+)?(?:-(?:dev|alpha|beta|rc|preview|pr)\.?[a-zA-Z0-9]+)?)(?:[-_.]|$)", RegexOptions.IgnoreCase);
        if (verMatch.Success)
        {
            version = verMatch.Groups[1].Value.TrimStart('v', 'V');
        }
        else
        {
            var fallbackMatch = Regex.Match(baseName, @"(\d+\.\d+(?:\.\d+)?)");
            if (fallbackMatch.Success)
            {
                version = fallbackMatch.Groups[1].Value;
            }
        }

        // Determine if fork / custom
        var isCustom = false;
        var isFork = false;
        string? forkName = null;

        if (baseName.Contains("fork", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("community", StringComparison.OrdinalIgnoreCase))
        {
            isCustom = true;
            isFork = true;
            forkName = "Community Fork";
        }
        else if (baseName.Contains("custom", StringComparison.OrdinalIgnoreCase))
        {
            isCustom = true;
        }

        // Determine channel
        string channel;
        if (prNumber.HasValue || baseName.Contains("-pr-", StringComparison.OrdinalIgnoreCase))
        {
            channel = GenHubBuildConstants.ChannelPr;
        }
        else if (baseName.Contains("dev", StringComparison.OrdinalIgnoreCase))
        {
            channel = GenHubBuildConstants.ChannelDev;
        }
        else if (baseName.Contains("test", StringComparison.OrdinalIgnoreCase) || baseName.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            channel = GenHubBuildConstants.ChannelTest;
        }
        else if (isCustom || isFork)
        {
            channel = GenHubBuildConstants.ChannelCustomFork;
        }
        else
        {
            channel = GenHubBuildConstants.ChannelRelease;
        }

        string resolvedProductName;
        string resolvedCompanyName;
        if (isFork)
        {
            resolvedProductName = forkName != null ? $"GenHub ({forkName})" : "GenHub (Community Fork)";
            resolvedCompanyName = forkName ?? "Community";
        }
        else if (isCustom)
        {
            resolvedProductName = GenHubBuildConstants.CustomBuildName;
            resolvedCompanyName = "Custom";
        }
        else
        {
            resolvedProductName = GenHubBuildConstants.OfficialProductName;
            resolvedCompanyName = GenHubBuildConstants.OfficialCompany;
        }

        return BuildResult(
            isGenHub: true,
            cleanVersion: version ?? "1.0.0",
            productVersion: version,
            fileVersion: version,
            productName: resolvedProductName,
            companyName: resolvedCompanyName,
            fileDescription: "GenHub Application Build",
            gitShortHash: null,
            pullRequestNumber: prNumber,
            buildChannel: channel,
            entryPoint: fileName,
            fileName: fileName);
    }

    private GenHubBuildInfo BuildResult(
        bool isGenHub,
        string? cleanVersion,
        string? productVersion,
        string? fileVersion,
        string? productName,
        string? companyName,
        string? fileDescription,
        string? gitShortHash,
        int? pullRequestNumber,
        string? buildChannel,
        string? entryPoint,
        string fileName)
    {
        var (isCustom, isFork, forkName) = DetermineForkAndCustom(companyName, buildChannel, productName, cleanVersion);

        // Derive Build Channel
        var channel = DetermineChannel(buildChannel, pullRequestNumber, cleanVersion, fileName, isCustom, isFork);

        // Derive Category
        var category = channel switch
        {
            GenHubBuildConstants.ChannelPr => GenHubBuildConstants.CategoryTest,
            GenHubBuildConstants.ChannelDev => GenHubBuildConstants.CategoryDev,
            GenHubBuildConstants.ChannelTest => GenHubBuildConstants.CategoryTest,
            GenHubBuildConstants.ChannelCustomFork => GenHubBuildConstants.CategoryCustomFork,
            _ => GenHubBuildConstants.CategoryRelease,
        };

        // Derive Suggested Content ID & Name
        var suggestedId = GenHubBuildConstants.OfficialContentId;
        var suggestedName = GenHubBuildConstants.OfficialProductName;
        var suggestedDesc = "Official GenHub application build.";

        if (isFork && !string.IsNullOrWhiteSpace(forkName))
        {
            var cleanSlug = Regex.Replace(forkName.ToLowerInvariant(), @"[^a-z0-9\-]+", "-").Trim('-');
            suggestedId = $"genhub-fork-{cleanSlug}";
            suggestedName = $"GenHub ({forkName})";
            suggestedDesc = $"Custom community fork of GenHub by {forkName}.";
        }
        else if (isCustom)
        {
            suggestedId = GenHubBuildConstants.CustomContentId;
            suggestedName = GenHubBuildConstants.CustomBuildName;
            suggestedDesc = "Custom third-party build of GenHub.";
        }
        else if (channel == GenHubBuildConstants.ChannelTest)
        {
            suggestedId = GenHubBuildConstants.TestContentId;
            suggestedName = GenHubBuildConstants.TestBuildName;
            suggestedDesc = "Experimental test build of GenHub.";
        }
        else if (channel == GenHubBuildConstants.ChannelPr && pullRequestNumber.HasValue)
        {
            suggestedId = $"genhub-pr{pullRequestNumber.Value}";
            suggestedName = $"GenHub PR #{pullRequestNumber.Value}";
            suggestedDesc = $"GenHub pull request build for PR #{pullRequestNumber.Value}.";
        }
        else if (channel == GenHubBuildConstants.ChannelDev)
        {
            suggestedId = GenHubBuildConstants.DevContentId;
            suggestedName = GenHubBuildConstants.DevBuildName;
            suggestedDesc = "Development branch build of GenHub.";
        }

        // Generate Suggested Tags
        var tags = new List<string> { GenHubBuildConstants.TagGenHub, GenHubBuildConstants.TagBuild };
        if (isCustom || isFork)
        {
            tags.Add(GenHubBuildConstants.TagFork);
            tags.Add(GenHubBuildConstants.TagCustom);
            if (!string.IsNullOrWhiteSpace(forkName))
            {
                var cleanSlug = Regex.Replace(forkName.ToLowerInvariant(), @"[^a-z0-9\-]+", "-").Trim('-');
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

        return new GenHubBuildInfo
        {
            IsGenHubBuild = isGenHub,
            Version = cleanVersion ?? "1.0.0",
            ProductVersion = productVersion,
            FileVersion = fileVersion,
            GitShortHash = gitShortHash,
            PullRequestNumber = pullRequestNumber,
            BuildChannel = channel,
            IsCustomBuild = isCustom,
            IsFork = isFork,
            ForkName = forkName,
            EntryPoint = entryPoint,
            SuggestedContentId = suggestedId,
            SuggestedContentName = suggestedName,
            SuggestedDescription = suggestedDesc,
            SuggestedCategory = category,
            SuggestedTags = tags,
        };
    }

    private (bool IsCustom, bool IsFork, string? ForkName) DetermineForkAndCustom(
        string? companyName,
        string? buildChannel,
        string? productName,
        string? version)
    {
        var isCustom = false;
        var isFork = false;
        string? forkName = null;

        if (!string.IsNullOrWhiteSpace(companyName) &&
            !companyName.Equals(GenHubBuildConstants.OfficialCompany, StringComparison.OrdinalIgnoreCase) &&
            !companyName.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) &&
            !companyName.Contains("EA", StringComparison.OrdinalIgnoreCase))
        {
            isCustom = true;
            isFork = true;
            forkName = companyName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(buildChannel))
        {
            var normalizedChannel = buildChannel.ToLowerInvariant();
            if (normalizedChannel.Contains("fork") || normalizedChannel.Contains("custom") || normalizedChannel.Contains("community"))
            {
                isCustom = true;
                isFork = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(productName))
        {
            var match = Regex.Match(productName, @"GenHub\s*[\(\[]([^\]\)]+)[\)\]]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var candidate = match.Groups[1].Value.Trim();
                if (!candidate.Equals("Test", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.Equals("Dev", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.Equals("PR", StringComparison.OrdinalIgnoreCase))
                {
                    isFork = true;
                    isCustom = true;
                    forkName ??= candidate;
                }
            }
            else if (productName.Contains("fork", StringComparison.OrdinalIgnoreCase))
            {
                isFork = true;
                isCustom = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(version) &&
            (version.Contains("-fork", StringComparison.OrdinalIgnoreCase) || version.Contains("-custom", StringComparison.OrdinalIgnoreCase)))
        {
            isCustom = true;
            isFork = true;
        }

        return (isCustom, isFork, forkName);
    }

    private string DetermineChannel(
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

        if (!string.IsNullOrWhiteSpace(explicitChannel))
        {
            if (explicitChannel.Equals(GenHubBuildConstants.ChannelPr, StringComparison.OrdinalIgnoreCase))
            {
                return GenHubBuildConstants.ChannelPr;
            }

            if (explicitChannel.Equals(GenHubBuildConstants.ChannelDev, StringComparison.OrdinalIgnoreCase) || explicitChannel.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                return GenHubBuildConstants.ChannelDev;
            }

            if (explicitChannel.Equals(GenHubBuildConstants.ChannelTest, StringComparison.OrdinalIgnoreCase) || explicitChannel.Equals("Beta", StringComparison.OrdinalIgnoreCase))
            {
                return GenHubBuildConstants.ChannelTest;
            }

            if (explicitChannel.Equals(GenHubBuildConstants.ChannelRelease, StringComparison.OrdinalIgnoreCase))
            {
                return GenHubBuildConstants.ChannelRelease;
            }
        }

        if (isFork || isCustom)
        {
            return GenHubBuildConstants.ChannelCustomFork;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            if (version.Contains("-pr", StringComparison.OrdinalIgnoreCase))
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
        }

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
            string? informationalVersion = null;
            string? fileVersion = null;
            string? productName = null;
            string? companyName = null;
            string? fileDescription = null;
            string? buildChannel = null;
            int? pullRequestNumber = null;
            string? gitShortHash = null;

            foreach (var customAttributeHandle in reader.CustomAttributes)
            {
                var attribute = reader.GetCustomAttribute(customAttributeHandle);
                var attributeType = attribute.Constructor;

                string? attributeTypeName = null;
                if (attributeType.Kind == HandleKind.MemberReference)
                {
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)attributeType);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        attributeTypeName = reader.GetString(typeRef.Name);
                    }
                }

                if (attributeTypeName == null)
                {
                    continue;
                }

                var value = ReadFixedStringCustomAttribute(reader, attribute);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                switch (attributeTypeName)
                {
                    case "AssemblyInformationalVersionAttribute":
                        informationalVersion = value;
                        break;
                    case "AssemblyFileVersionAttribute":
                        fileVersion = value;
                        break;
                    case "AssemblyProductAttribute":
                        productName = value;
                        break;
                    case "AssemblyCompanyAttribute":
                        companyName = value;
                        break;
                    case "AssemblyTitleAttribute" or "AssemblyDescriptionAttribute":
                        fileDescription = value;
                        break;
                    case "AssemblyMetadataAttribute":
                        var (key, metaValue) = ReadKeyValueCustomAttribute(reader, attribute);
                        if (string.Equals(key, "BuildChannel", StringComparison.OrdinalIgnoreCase))
                        {
                            buildChannel = metaValue;
                        }
                        else if (string.Equals(key, "PullRequestNumber", StringComparison.OrdinalIgnoreCase) && int.TryParse(metaValue, out var pr))
                        {
                            pullRequestNumber = pr;
                        }
                        else if ((string.Equals(key, "GitHash", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "CommitHash", StringComparison.OrdinalIgnoreCase)) && metaValue != null)
                        {
                            gitShortHash = metaValue.Length > 7 ? metaValue[..7] : metaValue;
                        }

                        break;
                    default:
                        break;
                }
            }

            var version = reader.IsAssembly ? reader.GetAssemblyDefinition().Version?.ToString() : null;

            return new PeMetadata(
                Version: version,
                InformationalVersion: informationalVersion,
                FileVersion: fileVersion,
                ProductName: productName,
                CompanyName: companyName,
                FileDescription: fileDescription,
                BuildChannel: buildChannel,
                PullRequestNumber: pullRequestNumber,
                GitShortHash: gitShortHash);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read CLI metadata from PE stream");
            return default;
        }
    }

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
