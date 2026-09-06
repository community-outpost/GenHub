using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Features.Content.Services.Parsers;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.Downloads.ViewModels.Filters;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests ensuring ModDB search, filtering, and resolution consistently default to newest-first order.
/// </summary>
public sealed class ModDBOrderingTests
{
    /// <summary>
    /// Verifies that ModDBFilter with SortDateDesc (date-desc) serializes it in query string.
    /// </summary>
    [Fact]
    public void ModDBFilter_WithDateDescSort_SerializesToQueryString()
    {
        // Arrange
        var filter = new ModDBFilter
        {
            Keyword = "contra",
            Sort = ModDBConstants.DefaultSort,
        };

        // Assert
        Assert.Equal(ModDBConstants.DefaultSort, filter.Sort);
        Assert.Equal(ModDBConstants.SortDateDesc, filter.Sort);

        var queryString = filter.ToQueryString();
        Assert.Contains("sort=date-desc", queryString, StringComparison.Ordinal);
        Assert.Contains("kw=contra", queryString, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that ModDBFilterViewModel initializes with DefaultSort and applies it to ContentSearchQuery.
    /// </summary>
    [Fact]
    public void ModDBFilterViewModel_AppliesDefaultSortToQuery()
    {
        // Arrange
        var viewModel = new ModDBFilterViewModel();
        var baseQuery = new ContentSearchQuery();

        // Assert
        Assert.Equal(ModDBConstants.DefaultSort, viewModel.SelectedSort);
        Assert.NotEmpty(viewModel.SortOptions);

        var appliedQuery = viewModel.ApplyFilters(baseQuery);
        Assert.Equal(ModDBConstants.SortDateDesc, appliedQuery.Sort);
    }

    /// <summary>
    /// Verifies that ModDBResolver selects the newest release when resolving a mod page with multiple release files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ModDBResolver_SelectsNewestReleaseBinaryAsync()
    {
        // Arrange
        const string modUrl = "https://www.moddb.com/mods/test-mod";
        var oldFile = new DownloadableFile(
            Name: "TestMod_v1.0.zip",
            UploadDate: new DateTime(2010, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/101",
            FileSectionType: FileSectionType.Downloads);

        var middleFile = new DownloadableFile(
            Name: "TestMod_v2.0.zip",
            UploadDate: new DateTime(2018, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/102",
            FileSectionType: FileSectionType.Downloads);

        var newestFile = new DownloadableFile(
            Name: "TestMod_v3.0.zip",
            UploadDate: new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/103",
            FileSectionType: FileSectionType.Downloads);

        var parsedPage = new ParsedWebPage(
            Url: new Uri(modUrl),
            Context: new GlobalContext("Test Mod", "Author", new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc), "Zero Hour", "icon.png", "Description"),
            Sections: [oldFile, newestFile, middleFile],
            PageType: PageType.Detail);

        var searchResult = new ContentSearchResult
        {
            Id = "moddb-test-mod",
            Name = "Test Mod",
            SourceUrl = modUrl,
            ProviderName = "ModDB",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ParsedPageData = parsedPage,
        };

        var factory = CreateFactory(CreateManifestBuilder);

        var resolver = new ModDBResolver(
            factory,
            new ModDBPageParser(new Mock<IPlaywrightService>().Object, new Mock<ILogger<ModDBPageParser>>().Object),
            new Mock<ILogger<ModDBResolver>>().Object);

        // Act
        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        // The manifest ID contains the date of the newest file (20241101) rather than the oldest (20100501)
        Assert.Contains("20241101", result.Data.Id.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that ModDBResolver selects an Archive package over a Setup installer executable
    /// for mods (such as Contra) where both Setup and Archive packages are provided on ModDB.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ModDBResolver_PrioritizesArchiveOverSetupReleaseAsync()
    {
        // Arrange
        const string modUrl = "https://www.moddb.com/mods/contra";
        var setupFile = new DownloadableFile(
            Name: "Contra X BETA 2 (Setup)",
            Filename: "Contra_X_BETA_2_Setup.exe",
            UploadDate: new DateTime(2024, 12, 1, 12, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/201",
            FileSectionType: FileSectionType.Downloads);

        var archiveFile = new DownloadableFile(
            Name: "Contra X BETA 2 (Archive)",
            Filename: "Contra_X_BETA_2_Archive.zip",
            UploadDate: new DateTime(2024, 12, 1, 10, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/202",
            FileSectionType: FileSectionType.Downloads);

        var parsedPage = new ParsedWebPage(
            Url: new Uri(modUrl),
            Context: new GlobalContext("Contra", "Contra Team", new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc), "Zero Hour", "contra.png", "Contra Mod"),
            Sections: [setupFile, archiveFile],
            PageType: PageType.Detail);

        var searchResult = new ContentSearchResult
        {
            Id = "moddb-contra",
            Name = "Contra",
            SourceUrl = modUrl,
            ProviderName = "ModDB",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ParsedPageData = parsedPage,
        };

        var factory = CreateFactory(CreateManifestBuilder);

        var resolver = new ModDBResolver(
            factory,
            new ModDBPageParser(new Mock<IPlaywrightService>().Object, new Mock<ILogger<ModDBPageParser>>().Object),
            new Mock<ILogger<ModDBResolver>>().Object);

        // Act
        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var file = Assert.Single(result.Data.Files);
        Assert.EndsWith(".zip", file.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static ModDBManifestFactory CreateFactory(Func<IContentManifestBuilder>? manifestBuilderFactory = null)
    {
        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("content-hash");
        var payloadProcessor = new GenHub.Features.Content.Services.Common.ArchivePayloadProcessor(
            new Mock<ILogger<GenHub.Features.Content.Services.Common.ArchivePayloadProcessor>>().Object);

        return new ModDBManifestFactory(
            manifestBuilderFactory ?? (() => new Mock<IContentManifestBuilder>().Object),
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<ICasService>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPlaywrightService>().Object,
            hashProvider.Object,
            payloadProcessor,
            new Mock<ILogger<ModDBManifestFactory>>().Object);
    }

    private static IContentManifestBuilder CreateManifestBuilder()
    {
        var manifestIdService = new Mock<IManifestIdService>();
        manifestIdService
            .Setup(service => service.ValidateAndCreateManifestId(It.IsAny<string>()))
            .Returns((string id) => OperationResult<ManifestId>.CreateSuccess(ManifestId.Create(id)));
        manifestIdService
            .Setup(service => service.GeneratePublisherContentId(
                It.IsAny<string>(),
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Returns((string publisherId, GenHub.Core.Models.Enums.ContentType contentType, string contentName, int version) =>
                OperationResult<ManifestId>.CreateSuccess(
                    ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                        publisherId,
                        contentType,
                        contentName,
                        version))));

        return new GenHub.Features.Manifest.ContentManifestBuilder(
            new Mock<ILogger<GenHub.Features.Manifest.ContentManifestBuilder>>().Object,
            new Mock<IFileHashProvider>().Object,
            manifestIdService.Object,
            new Mock<IDownloadService>().Object,
            new Mock<IConfigurationProviderService>().Object);
    }
}
