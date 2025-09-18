using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CNCLabsMapDiscoverer"/>.
/// </summary>
public class CNCLabsMapDiscovererTests
{
    /// <summary>
    /// Backing mock for verifying <see cref="ILogger{TCategoryName}"/> calls.
    /// </summary>
    private readonly Mock<ILogger<CNCLabsMapDiscoverer>> _loggerMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="CNCLabsMapDiscovererTests"/> class.
    /// </summary>
    public CNCLabsMapDiscovererTests()
    {
        _loggerMock = new Mock<ILogger<CNCLabsMapDiscoverer>>();
    }

    /// <summary>
    /// Verifies that <see cref="CNCLabsMapDiscoverer.DiscoverAsync(ContentSearchQuery, CancellationToken)"/> returns a failure result
    /// when the query is <see langword="null"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_NullQuery_ReturnsFailure()
    {
        // Arrange
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(http);

        // Act
        var result = await sut.DiscoverAsync(null!);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(CNCLabsConstants.QueryNullErrorMessage, result.AllErrors ?? string.Empty);
    }

    /// <summary>
    /// Verifies that discovery fails when both a search term and the required filters are missing
    /// (i.e., neither <see cref="ContentSearchQuery.SearchTerm"/> nor both
    /// <see cref="ContentSearchQuery.TargetGame"/> and <see cref="ContentSearchQuery.ContentType"/> are provided).
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_MissingSearchTermAndFilters_ReturnsFailure()
    {
        // Arrange: neither SearchTerm nor both TargetGame & ContentType
        var query = new ContentSearchQuery
        {
            SearchTerm = string.Empty,
            TargetGame = null,
            ContentType = null,
        };

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(http);

        // Act
        var result = await sut.DiscoverAsync(query);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(CNCLabsConstants.QueryNullErrorMessage, result.AllErrors ?? string.Empty);
    }

    /// <summary>
    /// Verifies that a canceled operation results in a failure and is logged appropriately.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_CancellationRequested_ReturnsFailure()
    {
        // Arrange
        var query = new ContentSearchQuery
        {
            TargetGame = GameType.ZeroHour,
            ContentType = GenHub.Core.Models.Enums.ContentType.Mission,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(http, _loggerMock);

        // Act
        var result = await sut.DiscoverAsync(query, cts.Token);

        // Assert: class converts exceptions to OperationResult failure and logs them
        Assert.False(result.Success);
        _loggerMock.VerifyLogErrorCalled();
    }

    /// <summary>
    /// Verifies that HTTP exceptions are surfaced as a failure result and that they are logged.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_HttpThrows_ReturnsFailure_AndLogs()
    {
        // Arrange - any request throws
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("boom")));
        var sut = CreateSut(http, _loggerMock);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.Generals,
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
        };

        // Act
        var result = await sut.DiscoverAsync(query);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("boom", result.AllErrors ?? string.Empty);
        _loggerMock.VerifyLogErrorCalled();
    }

    /// <summary>
    /// (Template) “happy path” test for the structured filters branch.
    /// Uses a small HTML snippet expected to match the CNCLabs selectors.
    /// Adjust the HTML to mirror your <see cref="CNCLabsConstants"/> selectors if they differ.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithFilters_ParsesListAndProjectsResults()
    {
        // Arrange
        var query = new ContentSearchQuery
        {
            TargetGame = GameType.Generals,
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
        };

        // IMPORTANT: Adjust this HTML to match your CNCLabsConstants.* selectors.
        // The idea is: each list item has a hidden input for the id AND a link with name + href.
        var listHtml = @"
<html><body>
  <div class=""DownloadItem"">
    <input type=""hidden"" name=""ctl00$Main$DownloadsView$ItemRepeater$ctl00$ctl00$FileIdField"" id=""ctl00_Main_DownloadsView_ItemRepeater_ctl00_ctl00_FileIdField"" value=""3239"">
    <a class=""DisplayName"" href=""/downloads/details.aspx?id=3239"">
      COOP GLA vs CHI - Call of Dragon
    </a>
    <div class=""DescriptionCell"">
      <span id=""x_DescriptionLabel"">This is another custom scripted co-op mission map. 1 or 2 humans players as GLA against 1 China…</span>
      <strong>Author:</strong> El_Chapo
    </div>
  </div>
</body></html>";

        // We return the list page HTML for the search URL
        using var http = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listHtml),
            });

        var sut = CreateSut(http);

        // Act
        var result = await sut.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data!.ToList();

        Assert.Single(items);
        var item = items[0];

        Assert.Equal(string.Format(CNCLabsConstants.MapIdFormat, 3239), item.Id);
        Assert.Equal("COOP GLA vs CHI - Call of Dragon", item.Name);
        Assert.Equal(CNCLabsConstants.MapDescriptionTemplate, item.Description);
        Assert.Equal("El_Chapo", item.AuthorName);
        Assert.Equal(GenHub.Core.Models.Enums.ContentType.Map, item.ContentType);
        Assert.Equal(GameType.Generals, item.TargetGame);
        Assert.Equal(CNCLabsConstants.ResolverId, item.ResolverId);
        Assert.True(item.RequiresResolution);
        Assert.Equal(CNCLabsConstants.SourceName, item.ProviderName);
        Assert.Equal("/downloads/details.aspx?id=3239", item.SourceUrl);
        Assert.True(item.ResolverMetadata.ContainsKey(CNCLabsConstants.MapIdMetadataKey));
        Assert.Equal("3239", item.ResolverMetadata[CNCLabsConstants.MapIdMetadataKey]);
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>
    /// Creates an <see cref="HttpClient"/> whose responses are controlled by the provided <paramref name="responder"/>.
    /// </summary>
    /// <param name="responder">A delegate that receives the outgoing <see cref="HttpRequestMessage"/> and returns the response.</param>
    /// <returns>An <see cref="HttpClient"/> configured to use the responder.</returns>
    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new DelegateHandler((req, ct) => Task.FromResult(responder(req)));
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>
    /// Creates the system under test (SUT): an instance of <see cref="CNCLabsMapDiscoverer"/>.
    /// </summary>
    /// <param name="http">The <see cref="HttpClient"/> to be used by the discoverer.</param>
    /// <param name="loggerMock">An optional mock logger. If <see langword="null"/>, a new mock is created.</param>
    /// <returns>An initialized <see cref="CNCLabsMapDiscoverer"/>.</returns>
    private static CNCLabsMapDiscoverer CreateSut(HttpClient http, Mock<ILogger<CNCLabsMapDiscoverer>>? loggerMock = null)
    {
        var logger = (loggerMock ?? new Mock<ILogger<CNCLabsMapDiscoverer>>()).Object;
        return new CNCLabsMapDiscoverer(http, logger);
    }

    // ---- test doubles ----------------------------------------------------

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that delegates sending to a provided function.
    /// Useful for crafting deterministic HTTP responses in tests.
    /// </summary>
    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _impl;

        /// <summary>
        /// Initializes a new instance of the <see cref="DelegateHandler"/> class from a synchronous responder.
        /// </summary>
        /// <param name="impl">The synchronous delegate that returns an <see cref="HttpResponseMessage"/>.</param>
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> impl)
            : this((r, _) => Task.FromResult(impl(r)))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DelegateHandler"/> class.
        /// </summary>
        /// <param name="impl">The asynchronous delegate that returns an <see cref="HttpResponseMessage"/>.</param>
        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> impl)
        {
            _impl = impl;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _impl(request, cancellationToken);
    }

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that always throws the provided exception.
    /// Useful for simulating transport-layer failures.
    /// </summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowingHandler"/> class.
        /// </summary>
        /// <param name="ex">The exception to throw when a request is sent.</param>
        public ThrowingHandler(Exception ex)
        {
            _ex = ex;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(_ex);
    }
}

// ---- Moq logger helpers ---------------------------------------------------

/// <summary>
/// Extensions for verifying that logging occurred using <see cref="Moq.Mock{T}"/>.
/// </summary>
internal static class LoggerVerifyExtensions
{
    /// <summary>
    /// Verifies that an error-level log entry was written at least once.
    /// </summary>
    /// <typeparam name="T">The logger category type.</typeparam>
    /// <param name="mock">The mocked logger.</param>
    public static void VerifyLogErrorCalled<T>(this Mock<ILogger<T>> mock)
    {
        mock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}