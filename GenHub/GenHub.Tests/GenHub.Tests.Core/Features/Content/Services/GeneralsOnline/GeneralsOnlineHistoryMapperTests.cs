using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineHistoryMapper"/>.
/// </summary>
public class GeneralsOnlineHistoryMapperTests
{
    /// <summary>
    /// History entries map to resolvable search results reusing the standard pipeline.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithGeneralsOnlineEntries_MapsPortableReleases()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("082826_QFE1", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        var result = Assert.Single(results);
        Assert.Equal("GeneralsOnline_082826_QFE1", result.Id);
        Assert.Equal("082826_QFE1", result.Version);
        Assert.Equal(ContentType.GameClient, result.ContentType);
        Assert.Equal(GameType.ZeroHour, result.TargetGame);
        Assert.Equal(GeneralsOnlineConstants.PublisherType, result.ProviderName);
        Assert.Equal(GeneralsOnlineConstants.ResolverId, result.ResolverId);
        Assert.True(result.RequiresResolution);
        Assert.Equal("https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", result.SelectedDownloadUrl);
        Assert.Contains("multiplayer", result.Tags);

        var release = Assert.IsType<GeneralsOnlineRelease>(result.GetData<GeneralsOnlineRelease>());
        Assert.Equal("082826_QFE1", release.Version);
        Assert.Equal("https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", release.PortableUrl);
        Assert.Null(release.PortableSize);
        Assert.Null(release.Sha256);
    }

    /// <summary>
    /// Duplicate catalog rows for one version collapse to a single browser card.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithDuplicateVersions_KeepsSingleCard()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("032926", "https://cdn.playgenerals.online/GeneralsOnline_portable_032926.zip", iniCrc: "0xA"),
            CreateEntry("032926", "https://cdn.playgenerals.online/GeneralsOnline_portable_032926.zip", iniCrc: "0xB"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        var result = Assert.Single(results);
        Assert.Equal("GeneralsOnline_032926", result.Id);
    }

    /// <summary>
    /// Standard and EAC rows sharing a manifest component keep the standard row so
    /// installed manifest IDs cannot collide.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithEacAndStandardSharingManifestComponent_PrefersStandard()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("042826_QFE2_EAC", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2_EAC.zip"),
            CreateEntry("042826_QFE2", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        var result = Assert.Single(results);
        Assert.Equal("042826_QFE2", result.Version);
    }

    /// <summary>
    /// The CDN latest version is excluded so it is not listed twice.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithCurrentVersion_ExcludesIt()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("082826_QFE1", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip"),
            CreateEntry("081326", "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries, ["082826_QFE1"]);

        var result = Assert.Single(results);
        Assert.Equal("081326", result.Version);
    }

    /// <summary>
    /// An EAC latest release also excludes the standard row sharing its manifest component,
    /// so the current release cannot appear twice with colliding manifest IDs.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithEacLatestVersion_ExcludesSharedComponent()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("042826_QFE2", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2.zip"),
            CreateEntry("042826_QFE2_EAC", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2_EAC.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries, ["042826_QFE2_EAC"]);

        Assert.Empty(results);
    }

    /// <summary>
    /// Every current CDN variant is excluded by version and manifest component, so a
    /// future multi-card CDN cannot leak back into history.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithMultipleCurrentVersions_ExcludesAllVariants()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("082826_QFE1", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip"),
            CreateEntry("042826_QFE2", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2.zip"),
            CreateEntry("042826_QFE2_EAC", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2_EAC.zip"),
            CreateEntry("081326", "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(
            entries,
            ["082826_QFE1", "042826_QFE2"]);

        var result = Assert.Single(results);
        Assert.Equal("081326", result.Version);
    }

    /// <summary>
    /// Portable URLs carrying a query string are skipped because the delivery pipeline
    /// requires the raw download URL to end with the portable extension.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithPortableUrlQueryString_Skips()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("081326", "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip?nocache=123"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        Assert.Empty(results);
    }

    /// <summary>
    /// Non-Generals Online entries and rows without a portable zip are skipped.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithNonGeneralsOnlineOrMissingUrl_Skips()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("1.04", "https://example.com/steam.zip", publisher: "steam"),
            CreateEntry("081326", string.Empty),
            CreateEntry("081326_QFE1", "https://example.com/not-a-portable.exe"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        Assert.Empty(results);
    }

    /// <summary>
    /// Versions that cannot be encoded into a manifest ID are skipped.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithUnencodableQfe_Skips()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("101525_QFE10", "https://cdn.playgenerals.online/GeneralsOnline_portable_101525_QFE10.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        Assert.Empty(results);
    }

    /// <summary>
    /// Malformed versions that the version scheme cannot parse are skipped instead of
    /// minting cards with fabricated manifest components.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithUnparseableVersion_Skips()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("133126", "https://cdn.playgenerals.online/GeneralsOnline_portable_133126.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        Assert.Empty(results);
    }

    /// <summary>
    /// EAC-only releases that share no component with another row are kept.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithEacOnlyVersion_KeepsCard()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("042826_QFE3_EAC", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE3_EAC.zip"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        var result = Assert.Single(results);
        Assert.Equal("042826_QFE3_EAC", result.Version);
    }

    /// <summary>
    /// Timezone-less build dates resolve to midnight UTC deterministically.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithDateOnlyBuildDate_ResolvesMidnightUtc()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("081326", "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip", buildDate: "2026-08-13"),
        };

        var results = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);

        var result = Assert.Single(results);
        Assert.Equal(new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc), result.LastUpdated);
    }

    /// <summary>
    /// Null catalogs yield no history.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithNullInput_ReturnsEmpty()
    {
        Assert.Empty(GeneralsOnlineHistoryMapper.BuildHistoryResults(null));
    }

    /// <summary>
    /// Empty catalogs yield no history.
    /// </summary>
    [Fact]
    public void BuildHistoryResults_WithEmptyInput_ReturnsEmpty()
    {
        Assert.Empty(GeneralsOnlineHistoryMapper.BuildHistoryResults([]));
    }

    /// <summary>
    /// History releases resolve through the standard resolver like CDN releases.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task HistoryResult_ResolvesThroughStandardResolverAsync()
    {
        var entries = new List<CrcMappingEntry>
        {
            CreateEntry("081326", "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip"),
        };
        var history = GeneralsOnlineHistoryMapper.BuildHistoryResults(entries);
        var searchResult = history.Single();

        var factory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            CreateProviderLoader());
        var resolver = new GeneralsOnlineResolver(
            factory,
            NullLogger<GeneralsOnlineResolver>.Instance);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success);
        Assert.Equal("081326", result.Data!.Version);
        Assert.Contains(result.Data.Files, f => f.DownloadUrl == "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip");
    }

    private static CrcMappingEntry CreateEntry(
        string version,
        string cdnUrl,
        string publisher = "generalsonline",
        string iniCrc = "0x1",
        string? buildDate = "2026-08-28")
    {
        return new CrcMappingEntry
        {
            ExeCrc = "0x2",
            IniCrc = iniCrc,
            Publisher = publisher,
            GameType = "ZeroHour",
            Version = version,
            BuildDate = buildDate,
            Description = $"GeneralsOnline {version} portable release",
            ManifestId = "1.828261.generalsonline.gameclient.zerohour",
            CdnUrl = cdnUrl,
        };
    }

    private static IProviderDefinitionLoader CreateProviderLoader()
    {
        var loader = new Mock<IProviderDefinitionLoader>();
        loader.Setup(l => l.GetProvider(PublisherTypeConstants.GeneralsOnline))
            .Returns(new ProviderDefinition
            {
                ProviderId = PublisherTypeConstants.GeneralsOnline,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Endpoints = new ProviderEndpoints
                {
                    WebsiteUrl = "https://www.playgenerals.online",
                    Custom = { ["releasesUrl"] = "https://cdn.playgenerals.online/releases" },
                },
            });
        return loader.Object;
    }
}
