using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.ContentResolvers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CsvResolver"/>.
/// </summary>
public class CsvResolverTests
{
    private sealed class TempCsvFile : IDisposable
    {
        public TempCsvFile(string csvContent)
        {
            FilePath = Path.GetTempFileName();
            File.WriteAllText(FilePath, csvContent);
        }

        public string FilePath { get; }

        public void Dispose()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }

    private sealed class StubHttpMessageHandler(
        string? expectedUrl = null,
        string content = "",
        HttpStatusCode statusCode = HttpStatusCode.NotFound) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var code = expectedUrl == null || request.RequestUri?.AbsoluteUri == expectedUrl
                ? statusCode
                : HttpStatusCode.NotFound;
            var responseContent = code == HttpStatusCode.OK ? content : string.Empty;
            var response = new HttpResponseMessage(code)
            {
                RequestMessage = request,
                Content = new StringContent(responseContent),
            };

            return Task.FromResult(response);
        }
    }

    private const string SampleCsvHeader = "relativePath,size,md5,sha256,gameType,language,isRequired,metadata,downloadUrl";
    private const string SampleCsvRowAll = "game.dat,123456,md5all,sha256all,Generals,All,True,\"{}\",https://example.com/game.dat";
    private const string SampleCsvRowEn = "English.big,234567,md5en,sha256en,Generals,EN,False,\"{}\",https://example.com/English.big";
    private const string SampleCsvRowDe = "German.big,345678,md5de,sha256de,Generals,DE,False,\"{}\",https://example.com/German.big";
    private const string SampleCsvRowZh = "ZeroHour.exe,456789,md5zh,sha256zh,ZeroHour,All,True,\"{}\",https://example.com/ZeroHour.exe";

    private static readonly string FullSampleCsv = string.Join(
        Environment.NewLine,
        SampleCsvHeader,
        SampleCsvRowAll,
        SampleCsvRowEn,
        SampleCsvRowDe,
        SampleCsvRowZh);

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> returns a failure when the item is null.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithNullDiscoveredItem_ReturnsFailureAsync()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(null!);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> returns a failure when SourceUrl is empty.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithEmptySourceUrl_ReturnsFailureAsync()
    {
        var resolver = CreateResolver();
        var item = new ContentSearchResult { SourceUrl = string.Empty };

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> successfully resolves a manifest from HTTP URL.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenRemoteCsvFetchedSuccessfully_ResolvesManifestAsync()
    {
        var remoteUrl = "https://example.com/catalog.csv";
        var httpHandler = new StubHttpMessageHandler(expectedUrl: remoteUrl, content: FullSampleCsv, statusCode: HttpStatusCode.OK);
        var resolver = CreateResolver(httpHandler);

        var item = CreateDiscoveredItem(remoteUrl, GameType.Generals, CsvConstants.LanguageEn);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Files.Should().HaveCount(2); // game.dat (All) + English.big (EN)
        result.Data.Files.Should().Contain(f => f.RelativePath == "game.dat" && f.SourceType == ContentSourceType.RemoteDownload);
        result.Data.Files.Should().Contain(f => f.RelativePath == "English.big" && f.SourceType == ContentSourceType.RemoteDownload);
    }

    /// <summary>
    /// Verifies that a downloaded remote CSV remains available to a new resolver while offline.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenOffline_UsesPersistedRemoteCsvAsync()
    {
        var cacheDirectory = Directory.CreateTempSubdirectory();
        try
        {
            const string remoteUrl = "https://example.com/catalog.csv";
            var item = CreateDiscoveredItem(remoteUrl, GameType.Generals, CsvConstants.LanguageEn);
            var onlineResolver = CreateResolver(
                new StubHttpMessageHandler(remoteUrl, FullSampleCsv, HttpStatusCode.OK),
                cacheDirectory.FullName);

            var onlineResult = await onlineResolver.ResolveAsync(item);
            onlineResult.Success.Should().BeTrue();

            var offlineResolver = CreateResolver(
                new StubHttpMessageHandler(remoteUrl, statusCode: HttpStatusCode.ServiceUnavailable),
                cacheDirectory.FullName);
            var offlineResult = await offlineResolver.ResolveAsync(item);

            offlineResult.Success.Should().BeTrue();
            offlineResult.Data!.Files.Should().HaveCount(2);
        }
        finally
        {
            cacheDirectory.Delete(true);
        }
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> successfully resolves a manifest from a local file.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenLocalCsvFileExists_ResolvesManifestAsync()
    {
        using var tempCsv = new TempCsvFile(FullSampleCsv);
        var resolver = CreateResolver();

        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.Generals, CsvConstants.LanguageEn);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Files.Should().HaveCount(2);
        result.Data.Files.Should().AllSatisfy(f => f.SourceType.Should().Be(ContentSourceType.LocalFile));
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> returns a failure when local file is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenLocalCsvFileNotFound_ReturnsFailureAsync()
    {
        var resolver = CreateResolver();
        var item = CreateDiscoveredItem("C:\\nonexistent\\missing_catalog.csv", GameType.Generals, CsvConstants.LanguageEn);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> returns a failure when network request fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenNetworkFails_ReturnsFailureAsync()
    {
        var remoteUrl = "https://example.com/catalog.csv";
        var httpHandler = new StubHttpMessageHandler(expectedUrl: remoteUrl, statusCode: HttpStatusCode.InternalServerError);
        var resolver = CreateResolver(httpHandler);

        var item = CreateDiscoveredItem(remoteUrl, GameType.Generals, CsvConstants.LanguageEn);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> filters files by specific language.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithSpecificLanguageQuery_FiltersFilesByLanguageAsync()
    {
        using var tempCsv = new TempCsvFile(FullSampleCsv);
        var resolver = CreateResolver();

        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.Generals, CsvConstants.LanguageDe);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data!.Files.Should().HaveCount(2); // game.dat (All) + German.big (DE)
        result.Data.Files.Should().Contain(f => f.RelativePath == "German.big");
        result.Data.Files.Should().NotContain(f => f.RelativePath == "English.big");
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> includes all language files when language is "All".
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithAllLanguageQuery_IncludesAllFilesAsync()
    {
        using var tempCsv = new TempCsvFile(FullSampleCsv);
        var resolver = CreateResolver();

        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.Generals, CsvConstants.AllLanguagesFilter);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data!.Files.Should().HaveCount(3); // game.dat, English.big, German.big
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> filters files by game type correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithTargetGame_FiltersFilesByGameTypeAsync()
    {
        using var tempCsv = new TempCsvFile(FullSampleCsv);
        var resolver = CreateResolver();

        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.ZeroHour, CsvConstants.AllLanguagesFilter);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data!.Files.Should().HaveCount(1); // ZeroHour.exe
        result.Data.Files.First().RelativePath.Should().Be("ZeroHour.exe");
        result.Data.Files.First().IsExecutable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> returns failure when no files match.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenNoFilesMatch_ReturnsFailureAsync()
    {
        using var tempCsv = new TempCsvFile(SampleCsvHeader);
        var resolver = CreateResolver();

        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.Generals, CsvConstants.LanguageEn);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> propagates cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var resolver = CreateResolver();
        var item = CreateDiscoveredItem("https://example.com/test.csv", GameType.Generals, CsvConstants.LanguageEn);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync(item, cts.Token));
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolverId"/> returns the expected constant.
    /// </summary>
    [Fact]
    public void ResolverId_ReturnsExpectedConstant()
    {
        var resolver = CreateResolver();

        resolver.ResolverId.Should().Be(CsvConstants.ResolverId);
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> filters out traversal and rooted paths.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithTraversingRelativePaths_RejectsUnsafePathsAsync()
    {
        var maliciousCsv = string.Join(
            Environment.NewLine,
            SampleCsvHeader,
            "../../escape.dll,100,md5,sha256,Generals,All,True,\"{}\",https://example.com/escape.dll",
            "C:\\root.dll,100,md5,sha256,Generals,All,True,\"{}\",https://example.com/root.dll",
            "valid.dll,100,md5,sha256,Generals,All,True,\"{}\",https://example.com/valid.dll");

        using var tempCsv = new TempCsvFile(maliciousCsv);
        var resolver = CreateResolver();
        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.Generals, CsvConstants.AllLanguagesFilter);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data!.Files.Should().HaveCount(1);
        result.Data.Files.Single().RelativePath.Should().Be("valid.dll");
    }

    /// <summary>
    /// Verifies that <see cref="CsvResolver.ResolveAsync(ContentSearchResult, CancellationToken)"/> sets SourceType to GameInstallation when remote entry has no valid URL.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WhenRemoteFileHasNoDownloadUrl_SetsSourceTypeToGameInstallationAsync()
    {
        var csvNoUrl = string.Join(
            Environment.NewLine,
            SampleCsvHeader,
            "local_only.dat,100,md5,sha256,Generals,All,True,\"{}\",");

        var remoteUrl = "https://example.com/nourl.csv";
        var httpHandler = new StubHttpMessageHandler(expectedUrl: remoteUrl, content: csvNoUrl, statusCode: HttpStatusCode.OK);
        var resolver = CreateResolver(httpHandler);
        var item = CreateDiscoveredItem(remoteUrl, GameType.Generals, CsvConstants.AllLanguagesFilter);

        var result = await resolver.ResolveAsync(item);

        result.Success.Should().BeTrue();
        result.Data!.Files.Single().SourceType.Should().Be(ContentSourceType.GameInstallation);
        result.Data.Files.Single().DownloadUrl.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the ProviderDefinition overload delegates to the main ResolveAsync method.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithProviderDefinitionOverload_DelegatesToResolveAsync()
    {
        using var tempCsv = new TempCsvFile(FullSampleCsv);
        var resolver = CreateResolver();
        var item = CreateDiscoveredItem(tempCsv.FilePath, GameType.Generals, CsvConstants.LanguageEn);

        var result = await resolver.ResolveAsync(null, item);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    private static CsvResolver CreateResolver(HttpMessageHandler? handler = null, string? applicationDataPath = null)
    {
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory
            .Setup(o => o.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler ?? new StubHttpMessageHandler()));

        CsvCatalogCache? catalogCache = null;
        if (applicationDataPath != null)
        {
            var configurationProvider = new Mock<IConfigurationProviderService>();
            configurationProvider.Setup(provider => provider.GetApplicationDataPath()).Returns(applicationDataPath);
            catalogCache = new CsvCatalogCache(configurationProvider.Object, Mock.Of<ILogger<CsvCatalogCache>>());
        }

        return new CsvResolver(mockHttpClientFactory.Object, Mock.Of<ILogger<CsvResolver>>(), catalogCache);
    }

    private static ContentSearchResult CreateDiscoveredItem(string sourceUrl, GameType gameType, string language)
    {
        var gameTypeStr = gameType == GameType.ZeroHour ? CsvConstants.ZeroHourGameType : CsvConstants.GeneralsGameType;
        var id = ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.CsvRegistry,
            ContentType.GameInstallation,
            $"{gameTypeStr}-1.0-{language}");

        var item = new ContentSearchResult
        {
            Id = id,
            Name = $"{gameTypeStr} 1.0 ({language})",
            Description = $"Base game installation files for {gameTypeStr} 1.0",
            Version = "1.0",
            ContentType = ContentType.GameInstallation,
            TargetGame = gameType,
            ProviderName = CsvConstants.SourceName,
            ResolverId = CsvConstants.ResolverId,
            SourceUrl = sourceUrl,
            RequiresResolution = true,
        };

        item.ResolverMetadata[CsvConstants.CsvUrlMetadataKey] = sourceUrl;
        item.ResolverMetadata[CsvConstants.GameTypeMetadataKey] = gameTypeStr;
        item.ResolverMetadata[CsvConstants.LanguageMetadataKey] = language;
        item.ResolverMetadata[CsvConstants.VersionMetadataKey] = "1.0";

        return item;
    }
}
