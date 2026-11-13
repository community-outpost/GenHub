using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="DownloadTelemetryHelper"/>.
/// </summary>
public class DownloadTelemetryHelperTests
{
    /// <summary>
    /// Verifies that manifest attribution copies publisher, content, and author identity onto the configuration.
    /// </summary>
    [Fact]
    public void ApplyManifestAttribution_WithFullManifest_PopulatesAllFields()
    {
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://example.com/files/mod.zip"),
            DestinationPath = "/tmp/mod.zip",
        };
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.cool-mod"),
            Name = "Cool Mod",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo { Name = "ModAuthor", PublisherType = PublisherTypeConstants.GitHub },
        };

        DownloadTelemetryHelper.ApplyManifestAttribution(config, manifest, PublisherTypeConstants.GitHub);

        Assert.Equal(PublisherTypeConstants.GitHub, config.PublisherId);
        Assert.Equal("ModAuthor", config.Author);
        Assert.Equal("Cool Mod", config.ContentName);
        Assert.Equal("1.0.github.mod.cool-mod", config.ContentId);
        Assert.Equal("Mod", config.ContentType);
    }

    /// <summary>
    /// Verifies that an unknown publisher type falls through to the provider name instead of shadowing it.
    /// </summary>
    [Fact]
    public void ApplyManifestAttribution_WithUnknownPublisherType_PrefersProviderName()
    {
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://example.com/files/mod.zip"),
            DestinationPath = "/tmp/mod.zip",
        };
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.aodmaps.map.cool-map"),
            Name = "Cool Map",
            ContentType = ContentType.Map,
            OriginalProviderName = PublisherTypeConstants.AODMaps,
            Publisher = new PublisherInfo { Name = "MapAuthor", PublisherType = PublisherTypeConstants.Unknown },
        };

        DownloadTelemetryHelper.ApplyManifestAttribution(config, manifest);

        Assert.Equal(PublisherTypeConstants.AODMaps, config.PublisherId);
        Assert.Equal("MapAuthor", config.Author);
    }

    /// <summary>
    /// Verifies that the provider fallback is used when the manifest carries no publisher name.
    /// </summary>
    [Fact]
    public void ApplyManifestAttribution_WithoutPublisherName_UsesProviderFallback()
    {
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://example.com/files/mod.zip"),
            DestinationPath = "/tmp/mod.zip",
        };
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.cool-mod"),
            Name = "Cool Mod",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo(),
        };

        DownloadTelemetryHelper.ApplyManifestAttribution(config, manifest, PublisherTypeConstants.GitHub);

        Assert.Equal(PublisherTypeConstants.GitHub, config.PublisherId);
        Assert.Equal(PublisherTypeConstants.GitHub, config.Author);
    }

    /// <summary>
    /// Verifies that explicitly set configuration values are preserved.
    /// </summary>
    [Fact]
    public void ApplyManifestAttribution_WithExplicitValues_PreservesThem()
    {
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://example.com/files/mod.zip"),
            DestinationPath = "/tmp/mod.zip",
            PublisherId = "explicit-publisher",
            Author = "explicit-author",
            ContentName = "Explicit Name",
            ContentId = "explicit-id",
            ContentType = "Map",
        };
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.cool-mod"),
            Name = "Cool Mod",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo { Name = "ModAuthor" },
        };

        DownloadTelemetryHelper.ApplyManifestAttribution(config, manifest, PublisherTypeConstants.GitHub);

        Assert.Equal("explicit-publisher", config.PublisherId);
        Assert.Equal("explicit-author", config.Author);
        Assert.Equal("Explicit Name", config.ContentName);
        Assert.Equal("explicit-id", config.ContentId);
        Assert.Equal("Map", config.ContentType);
    }

    /// <summary>
    /// Verifies that unrecognized hosts resolve to the unknown sentinel instead of leaking raw host names.
    /// </summary>
    /// <param name="url">The download URL.</param>
    /// <param name="expected">The expected publisher identifier.</param>
    [Theory]
    [InlineData("https://mygithubclone.example.com/test.zip", "unknown")]
    [InlineData("https://notmoddb.net/test.zip", "unknown")]
    [InlineData("https://192.168.1.10/files/test.zip", "unknown")]
    [InlineData("https://drive.google.com/uc?export=download", "googledrive")]
    [InlineData("https://drive.usercontent.google.com/download", "googledrive")]
    [InlineData("https://onedrive.live.com/download", "onedrive")]
    [InlineData("https://1drv.ms/u/abc", "onedrive")]
    [InlineData("https://gentool.net/files/test.zip", "gentool")]
    [InlineData("https://github.com/owner/repo/releases/download/v1/mod.zip", "github")]
    [InlineData("https://github.com./owner/repo/releases/download/v1/mod.zip", "github")]
    [InlineData("https://gentool.net./files/test.zip", "gentool")]
    [InlineData("https://legi.cc./downloads/test.zip", "communityoutpost")]
    public void ResolvePublisherId_MapsHostsToCanonicalPublishers(string url, string expected)
    {
        var config = new DownloadConfiguration
        {
            Url = new Uri(url),
            DestinationPath = "/tmp/mod.zip",
        };

        Assert.Equal(expected, DownloadTelemetryHelper.ResolvePublisherId(config));
    }

    /// <summary>
    /// Verifies that an empty destination path resolves to the unknown sentinel instead of an empty name.
    /// </summary>
    [Fact]
    public void ResolveContentName_WithEmptyDestinationPath_ReturnsUnknown()
    {
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://example.com/files/mod.zip"),
            DestinationPath = string.Empty,
        };

        Assert.Equal(TelemetryConstants.DownloadAttribution.Unknown, DownloadTelemetryHelper.ResolveContentName(config));
        Assert.Equal(TelemetryConstants.DownloadAttribution.Unknown, DownloadTelemetryHelper.ResolveContentId(config));
    }

    /// <summary>
    /// Verifies that completed-download tracking always emits publisher, content, and author properties.
    /// </summary>
    [Fact]
    public void TrackDownloadCompleted_WithoutAttribution_EmitsFallbackProperties()
    {
        var telemetryMock = new Mock<ITelemetryService>();
        IReadOnlyDictionary<string, object?>? trackedProps = null;
        telemetryMock.Setup(t => t.TrackEvent(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<TelemetryLevel>()))
            .Callback<string, IReadOnlyDictionary<string, object?>?, TelemetryLevel>((_, props, _) => trackedProps = props);
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://unknown-host.example/files/mod.zip"),
            DestinationPath = "/tmp/mod.zip",
        };

        DownloadTelemetryHelper.TrackDownloadCompleted(telemetryMock.Object, config, 1024, TimeSpan.FromSeconds(2));

        Assert.NotNull(trackedProps);
        Assert.Equal(TelemetryConstants.DownloadAttribution.Unknown, trackedProps[TelemetryConstants.Properties.PublisherId]);
        Assert.Equal("mod.zip", trackedProps[TelemetryConstants.Properties.ContentName]);
        Assert.Equal(TelemetryConstants.DownloadAttribution.Unknown, trackedProps[TelemetryConstants.Properties.Author]);
        Assert.Equal("mod.zip", trackedProps[TelemetryConstants.Properties.FileName]);
    }

    /// <summary>
    /// Verifies that failed-download tracking redacts raw URLs from error messages.
    /// </summary>
    /// <param name="errorMessage">The raw failure message.</param>
    /// <param name="expected">The expected redacted message.</param>
    [Theory]
    [InlineData("Download failed: 404", "Download failed: 404")]
    [InlineData("Failed fetching https://cdn.example.com/files/mod.zip?st=abc123&e=999", "Failed fetching <URL>")]
    [InlineData("http://insecure.example/mod.zip timed out after https://mirror.example/mod.zip failed", "<URL> timed out after <URL> failed")]
    public void TrackDownloadFailure_RedactsUrlsFromErrorMessage(string errorMessage, string expected)
    {
        var telemetryMock = new Mock<ITelemetryService>();
        IReadOnlyDictionary<string, object?>? trackedProps = null;
        telemetryMock.Setup(t => t.TrackEvent(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<TelemetryLevel>()))
            .Callback<string, IReadOnlyDictionary<string, object?>?, TelemetryLevel>((_, props, _) => trackedProps = props);
        var config = new DownloadConfiguration
        {
            Url = new Uri("https://unknown-host.example/files/mod.zip"),
            DestinationPath = "/tmp/mod.zip",
        };

        DownloadTelemetryHelper.TrackDownloadFailure(telemetryMock.Object, config, errorMessage, TimeSpan.FromSeconds(1));

        Assert.NotNull(trackedProps);
        Assert.Equal(expected, trackedProps[TelemetryConstants.Properties.ErrorMessage]);
    }
}
