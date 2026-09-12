using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Tests.Core.Infrastructure;
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
    public async Task DiscoverAsync_NullQuery_ReturnsFailureAsync()
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
    public async Task DiscoverAsync_MissingSearchTermAndFilters_ReturnsFailureAsync()
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
    public async Task DiscoverAsync_CancellationRequested_ReturnsFailureAsync()
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
    public async Task DiscoverAsync_HttpThrows_ReturnsFailure_AndLogsAsync()
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
    public async Task DiscoverAsync_WithFilters_ParsesListAndProjectsResultsAsync()
    {
        // Arrange
        var query = new ContentSearchQuery
        {
            TargetGame = GameType.Generals,
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
        };

        // IMPORTANT: This HTML mirrors the 2026 Bootstrap redesign of cnclabs.com list pages.
        var listHtml = @"
<html><body>
  <div class=""list-group-item list-group-item-action"">
    <div class=""download-list-row d-flex"">
      <div class=""flex-grow-1"">
        <div class=""d-flex align-items-center mb-1"">
          <h5 class=""mb-0 me-3""><a class=""text-decoration-none"" href=""/downloads/details/3239/"">COOP GLA vs CHI - Call of Dragon</a></h5>
          <span class=""badge bg-info me-1"">Multiplayer-only</span>
          <span class=""badge bg-success"">2 Players</span>
        </div>
        <div class=""mb-1 text-muted small""><p>This is another custom scripted co-op mission map. 1 or 2 humans players as GLA against 1 China…</p></div>
        <div class=""d-flex align-items-center flex-wrap small text-muted"">
          <span class=""me-3""><i class=""bi bi-person""></i> El_Chapo</span>
          <span class=""me-3""><i class=""bi bi-download""></i> 2397 downloads</span>
        </div>
      </div>
      <div class=""ms-3 text-nowrap text-center"">
        <div class=""small text-muted mt-1"">234.2 KB</div>
      </div>
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
        var items = result.Data!.Items.ToList();

        Assert.Single(items);
        var item = items[0];

        Assert.Equal(string.Format(CNCLabsConstants.MapIdFormat, 3239), item.Id);
        Assert.Equal("COOP GLA vs CHI - Call of Dragon", item.Name);
        Assert.Equal("El_Chapo", item.AuthorName);
        Assert.Equal(GenHub.Core.Models.Enums.ContentType.Map, item.ContentType);
        Assert.Equal(GameType.Generals, item.TargetGame);
        Assert.Equal(CNCLabsConstants.ResolverId, item.ResolverId);
        Assert.True(item.RequiresResolution);
        Assert.Equal(CNCLabsConstants.SourceName, item.ProviderName);
        Assert.Equal("https://www.cnclabs.com/downloads/details/3239/", item.SourceUrl);
        Assert.True(item.ResolverMetadata.ContainsKey(CNCLabsConstants.MapIdMetadataKey));
        Assert.Equal("3239", item.ResolverMetadata[CNCLabsConstants.MapIdMetadataKey]);
        Assert.Equal("This is another custom scripted co-op mission map. 1 or 2 humans players as GLA against 1 China…", item.Description);

        // Badges parsed from span.badge become tags, and the player-count badge is promoted
        // into badge metadata by PromoteFromTags.
        Assert.Contains("Multiplayer-only", item.Tags);
        Assert.Contains("2 Players", item.Tags);
        Assert.Equal("2", item.ResolverMetadata[GenHub.Core.Constants.ContentConstants.PlayerCountMetadataKey]);
    }

    /// <summary>
    /// Verifies that <see cref="CNCLabsMapDiscoverer.DiscoverAsync(ContentSearchQuery, CancellationToken)"/>
    /// correctly identifies more items when the "Next" link is present.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithNextLink_SetsHasMoreItemsTrueAsync()
    {
        // Arrange
        var query = new ContentSearchQuery
        {
            TargetGame = GameType.Generals,
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
            Page = 1,
        };

        var html = @"
<html><body>
  <div class=""list-group-item"">
    <div class=""flex-grow-1"">
      <h5 class=""mb-0""><a href=""/downloads/details/123/"">Test Map</a></h5>
    </div>
  </div>
  <nav aria-label=""Page navigation"">
    <ul class=""pagination justify-content-center"">
      <li class=""page-item active""><span class=""page-link"">1</span></li>
      <li class=""page-item""><a class=""page-link"" href=""/maps/generals/zerohour-maps.aspx?page=2"">2</a></li>
      <li class=""page-item""><a class=""page-link"" href=""?page=2"">Next</a></li>
    </ul>
  </nav>
</body></html>";

        using var http = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html),
            });

        var sut = CreateSut(http);

        // Act
        var result = await sut.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data!.HasMoreItems);
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
    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> impl) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _impl = impl;

        /// <summary>
        /// Initializes a new instance of the <see cref="DelegateHandler"/> class from a synchronous responder.
        /// </summary>
        /// <param name="implSync">The synchronous delegate that returns an <see cref="HttpResponseMessage"/>.</param>
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> implSync)
            : this((r, _) => Task.FromResult(implSync(r)))
        {
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _impl(request, cancellationToken);
    }

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that always throws the provided exception.
    /// Useful for simulating transport-layer failures.
    /// </summary>
    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        private readonly Exception _ex = ex;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(_ex);
    }
}
