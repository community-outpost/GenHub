using System;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineResolver"/>.
/// </summary>
public class GeneralsOnlineResolverTests
{
    private readonly Mock<IProviderDefinitionLoader> _providerLoaderMock;
    private readonly GeneralsOnlineManifestFactory _manifestFactory;
    private readonly GeneralsOnlineResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineResolverTests"/> class.
    /// </summary>
    public GeneralsOnlineResolverTests()
    {
        _providerLoaderMock = new Mock<IProviderDefinitionLoader>();
        _providerLoaderMock
            .Setup(l => l.GetProvider(PublisherTypeConstants.GeneralsOnline))
            .Returns(new ProviderDefinition
            {
                ProviderId = PublisherTypeConstants.GeneralsOnline,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Endpoints = new ProviderEndpoints
                {
                    WebsiteUrl = "https://www.playgenerals.online",
                    DownloadBaseUrl = "https://cdn.playgenerals.online/releases",
                    Custom = { ["releasesUrl"] = "https://cdn.playgenerals.online/releases" },
                },
            });

        _manifestFactory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            _providerLoaderMock.Object);

        _resolver = new GeneralsOnlineResolver(
            _manifestFactory,
            NullLogger<GeneralsOnlineResolver>.Instance,
            _providerLoaderMock.Object);
    }

    /// <summary>
    /// Tests that resolving with a typed release payload returns success.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ResolveAsync_WithTypedReleaseData_ReturnsSuccessWithManifestAsync()
    {
        var release = new GeneralsOnlineRelease
        {
            Version = "082826_QFE1",
            VersionDate = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            ReleaseDate = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            PortableUrl = "https://cdn.playgenerals.online/releases/GeneralsOnline_portable_082826_QFE1.zip",
            PortableSize = 1000,
            Changelog = "Test release",
        };

        var searchResult = new ContentSearchResult
        {
            Id = "GeneralsOnline_082826_QFE1",
            Name = "Generals Online",
            Version = "082826_QFE1",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ResolverId = GeneralsOnlineConstants.ResolverId,
        };
        searchResult.SetData(release);

        var result = await _resolver.ResolveAsync(searchResult);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("082826_QFE1", result.Data.Version);
        Assert.Contains(result.Data.Files, f => f.DownloadUrl == release.PortableUrl);
    }

    /// <summary>
    /// Tests that resolving without a typed release payload reconstructs the release from version.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ResolveAsync_WithoutTypedReleaseData_ReconstructsFromVersion_ReturnsSuccessAsync()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "file:https://www.playgenerals.online/#download",
            Name = "Generals Online",
            Version = "082826_QFE1",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ResolverId = GeneralsOnlineConstants.ResolverId,
        };

        var result = await _resolver.ResolveAsync(searchResult);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("082826_QFE1", result.Data.Version);
        Assert.NotNull(result.Data.Files);
        Assert.Contains(result.Data.Files, f => f.DownloadUrl?.Contains("GeneralsOnline_portable_082826_QFE1.zip") == true);
    }

    /// <summary>
    /// Tests that resolving without release data and without version fails gracefully.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ResolveAsync_WithoutReleaseDataAndWithoutVersion_ReturnsFailureAsync()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "file:https://www.playgenerals.online/#download",
            Name = "Generals Online",
            Version = string.Empty,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ResolverId = GeneralsOnlineConstants.ResolverId,
        };

        var result = await _resolver.ResolveAsync(searchResult);

        Assert.False(result.Success);
        Assert.NotNull(result.FirstError);
        Assert.Contains("Release information not found", result.FirstError);
    }
}
