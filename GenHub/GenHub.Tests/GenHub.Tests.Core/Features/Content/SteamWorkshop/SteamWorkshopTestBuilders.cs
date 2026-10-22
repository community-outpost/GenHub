using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Shared builders for Steam Workshop tests.
/// </summary>
internal static class SteamWorkshopTestBuilders
{
    /// <summary>
    /// Creates a real manifest builder backed by mocked ID generation.
    /// </summary>
    /// <returns>A content manifest builder.</returns>
    internal static IContentManifestBuilder CreateBuilder()
    {
        var manifestIdService = new Mock<IManifestIdService>();
        manifestIdService
            .Setup(service => service.ValidateAndCreateManifestId(It.IsAny<string>()))
            .Returns((string id) => OperationResult<ManifestId>.CreateSuccess(ManifestId.Create(id)));
        manifestIdService
            .Setup(service => service.GeneratePublisherContentId(
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Returns((string publisherId, ContentType contentType, string contentName, int version) =>
                OperationResult<ManifestId>.CreateSuccess(
                    ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                        publisherId,
                        contentType,
                        contentName,
                        version))));

        return new ContentManifestBuilder(
            Mock.Of<ILogger<ContentManifestBuilder>>(),
            Mock.Of<IFileHashProvider>(),
            manifestIdService.Object,
            Mock.Of<IDownloadService>(),
            Mock.Of<IConfigurationProviderService>());
    }

    /// <summary>
    /// Creates a JSON HTTP response for stubbed requests.
    /// </summary>
    /// <param name="json">The JSON payload.</param>
    /// <returns>The response message.</returns>
    internal static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }

    /// <summary>
    /// Creates an HTML HTTP response for stubbed requests.
    /// </summary>
    /// <param name="html">The HTML payload.</param>
    /// <returns>The response message.</returns>
    internal static HttpResponseMessage HtmlResponse(string html)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
    }

    /// <summary>
    /// Creates an HTTP client factory stub routing requests to the responder.
    /// Created clients are tracked for disposal by the owning test class.
    /// </summary>
    /// <param name="responder">The request handler.</param>
    /// <param name="trackedClients">The client tracker owned by the test class.</param>
    /// <returns>The factory stub.</returns>
    internal static IHttpClientFactory CreateHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ICollection<HttpClient> trackedClients)
    {
        var handler = new DelegateHandler((request, _) => Task.FromResult(responder(request)));
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        trackedClients.Add(httpClient);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return factoryMock.Object;
    }

    /// <summary>
    /// Routes HTTP requests to a test-provided responder.
    /// </summary>
    /// <param name="implementation">The request handler.</param>
    internal sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> implementation) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return implementation(request, cancellationToken);
        }
    }
}
