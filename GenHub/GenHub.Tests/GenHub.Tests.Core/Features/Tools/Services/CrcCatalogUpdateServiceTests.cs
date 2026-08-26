using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for CrcCatalogUpdateService background polling and caching.
/// </summary>
public sealed class CrcCatalogUpdateServiceTests : IDisposable
{
    private readonly string _tempAppDataPath = Path.Combine(Path.GetTempPath(), "GenHubCrcTest", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="CrcCatalogUpdateServiceTests"/> class.
    /// </summary>
    public CrcCatalogUpdateServiceTests()
    {
        Directory.CreateDirectory(_tempAppDataPath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempAppDataPath))
        {
            Directory.Delete(_tempAppDataPath, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a successful remote fetch updates both the in-memory registry and local fallback cache.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_SuccessfulRemoteFetch_UpdatesRegistryAndCacheAsync()
    {
        var catalog = new CrcCatalog
        {
            SchemaVersion = 1,
            TotalEntries = 1,
            Mappings =
            [
                new()
                {
                    ExeCrc = "0x27533BB0",
                    IniCrc = "0x76B251A3",
                    ManifestId = "1.20260821.superhackers.gameclient.zerohour",
                    Publisher = "superhackers",
                    GameType = "ZeroHour",
                    Version = "2026-08-21",
                },
            ],
        };

        var jsonContent = JsonSerializer.Serialize(catalog);
        var httpHandlerMock = new Mock<HttpMessageHandler>();
        httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonContent),
            });

        var httpClient = new HttpClient(httpHandlerMock.Object);
        var registry = new CrcMappingRegistry();
        var dynamicCacheMock = new Mock<IDynamicContentCache>();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        configProviderMock.Setup(c => c.GetApplicationDataPath()).Returns(_tempAppDataPath);

        var service = new CrcCatalogUpdateService(
            httpClient,
            registry,
            dynamicCacheMock.Object,
            configProviderMock.Object,
            NullLogger<CrcCatalogUpdateService>.Instance);

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(registry.TryGetEntry("0x27533BB0", "0x76B251A3", out var found));
        Assert.NotNull(found);
        Assert.Equal("1.20260821.superhackers.gameclient.zerohour", found.ManifestId);

        // Verify local fallback file was written
        var fallbackPath = Path.Combine(_tempAppDataPath, ReplayManagerConstants.CrcCatalogLocalFileName);
        Assert.True(File.Exists(fallbackPath));
    }

    /// <summary>
    /// Verifies that when remote fetching fails, the service loads from local fallback file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_RemoteFails_LoadsLocalFallbackAsync()
    {
        // Pre-seed local fallback
        var localCatalog = new CrcCatalog
        {
            SchemaVersion = 1,
            TotalEntries = 1,
            Mappings =
            [
                new()
                {
                    ExeCrc = "0x8B75EFD4",
                    IniCrc = "0x5CB7992C",
                    ManifestId = "1.213262.generalsonline.gameclient.zerohour",
                    Publisher = "generalsonline",
                    GameType = "ZeroHour",
                    Version = "021326_QFE2",
                },
            ],
        };

        var fallbackPath = Path.Combine(_tempAppDataPath, ReplayManagerConstants.CrcCatalogLocalFileName);
        await File.WriteAllTextAsync(fallbackPath, JsonSerializer.Serialize(localCatalog));

        var httpHandlerMock = new Mock<HttpMessageHandler>();
        httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
            });

        var httpClient = new HttpClient(httpHandlerMock.Object);
        var registry = new CrcMappingRegistry();
        var dynamicCacheMock = new Mock<IDynamicContentCache>();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        configProviderMock.Setup(c => c.GetApplicationDataPath()).Returns(_tempAppDataPath);

        var service = new CrcCatalogUpdateService(
            httpClient,
            registry,
            dynamicCacheMock.Object,
            configProviderMock.Object,
            NullLogger<CrcCatalogUpdateService>.Instance);

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(registry.TryGetEntry("0x8B75EFD4", "0x5CB7992C", out var found));
        Assert.NotNull(found);
        Assert.Equal("1.213262.generalsonline.gameclient.zerohour", found.ManifestId);
    }
}
