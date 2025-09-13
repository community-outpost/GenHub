using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content
{
    /// <summary>
    /// Unit tests for the <see cref="CNCLabsContentProvider"/> integration
    /// with a CNCLabs-style map discoverer/resolver pipeline.
    /// <para>
    /// This test is fully offline: it mocks the discoverer, resolver,
    /// deliverer, and validator so that CI remains deterministic.
    /// </para>
    /// </summary>
    public class CNCLabsMapDiscovererTests
    {
        /// <summary>Mock for the discovery layer that lists candidate items.</summary>
        private readonly Mock<IContentDiscoverer> _discovererMock;

        /// <summary>Mock for the resolution layer that turns a discovered item into a full manifest.</summary>
        private readonly Mock<IContentResolver> _resolverMock;

        /// <summary>Mock for the delivery layer (kept for provider contract completeness).</summary>
        private readonly Mock<IContentDeliverer> _delivererMock;

        /// <summary>Mock for validating the resolved manifest.</summary>
        private readonly Mock<IContentValidator> _validatorMock;

        /// <summary>Mock for a content-manifest builder (kept for potential provider needs).</summary>
        private readonly Mock<IContentManifestBuilder> _contentManifestBuilderMock;

        /// <summary>Logger mock for the provider.</summary>
        private readonly Mock<ILogger<CNCLabsContentProvider>> _loggerMock;

        /// <summary>The provider under test, wired with mocked dependencies.</summary>
        private readonly CNCLabsContentProvider _provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="CNCLabsMapDiscovererTests"/> class.
        /// Sets up all required mocks and constructs the provider using those mocks.
        /// </summary>
        public CNCLabsMapDiscovererTests()
        {
            _discovererMock = new Mock<IContentDiscoverer>();
            _resolverMock = new Mock<IContentResolver>();
            _delivererMock = new Mock<IContentDeliverer>();
            _validatorMock = new Mock<IContentValidator>();
            _contentManifestBuilderMock = new Mock<IContentManifestBuilder>();
            _loggerMock = new Mock<ILogger<CNCLabsContentProvider>>();

            // Provider relies on these metadata properties for wiring and reporting.
            // These string values are chosen to match the test case expectations.
            _discovererMock.SetupGet(d => d.SourceName).Returns("CNC Labs Maps");
            _resolverMock.SetupGet(r => r.ResolverId).Returns("CNCLabsMap");
            _delivererMock.SetupGet(d => d.SourceName).Returns("HTTP");

            _provider = new CNCLabsContentProvider(
                new[] { _discovererMock.Object },
                new[] { _resolverMock.Object },
                new[] { _delivererMock.Object },
                _loggerMock.Object,
                _validatorMock.Object
            );
        }

        /// <summary>
        /// Verifies that a search request flows through the discovery, resolution,
        /// and validation stages, resulting in a successful, resolved search result
        /// with an embedded <see cref="ContentManifest"/>.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Fact]
        public async Task SearchAsync()
        {
            // ---------- Arrange ----------
            // Input query: matches a typical CNCLabs map search term.
            var query = new ContentSearchQuery { SearchTerm = "art of war" };

            // Discovered item: Requires resolution and is handled by the "CNCLabsMap" resolver.
            var discoveredItem = new ContentSearchResult
            {
                Id = "1.0.gh.test.publisher.mod",
                Name = "Art of War",
                SourceUrl = "https://www.cnclabs.com/downloads/details.aspx?id=1462",
                ResolverId = "CNCLabsMap",
                RequiresResolution = true,
            };

            // Final resolved manifest returned by the resolver.
            var resolvedManifest = new ContentManifest
            {
                Id = "1.0.gh.test.publisher.mod",
                Name = "Resolved Test Mod",
            };

            // Discovery returns a single candidate item.
            _discovererMock
                .Setup(d => d.DiscoverAsync(query, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>
                    .CreateSuccess(new[] { discoveredItem }));

            // Resolver turns that discovered item into a manifest.
            _resolverMock
                .Setup(r => r.ResolveAsync(discoveredItem, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(resolvedManifest));

            // Validator accepts the manifest with no issues.
            _validatorMock
                .Setup(v => v.ValidateManifestAsync(resolvedManifest, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(resolvedManifest.Id, new List<ValidationIssue>()));

            // ---------- Act ----------
            var result = await _provider.SearchAsync(query);

            // ---------- Assert ----------
            Assert.True(result.Success);

            // There should be exactly one result and it should be resolved and enriched.
            var searchResult = Assert.Single(result.Data ?? Enumerable.Empty<ContentSearchResult>());
            Assert.Equal("Resolved Test Mod", searchResult.Name);
            Assert.False(searchResult.RequiresResolution); // Provider should mark it as resolved.
            Assert.NotNull(searchResult.GetData<ContentManifest>()); // Manifest embedded in the result.

            // Verify call flow.
            _discovererMock.Verify(d => d.DiscoverAsync(query, It.IsAny<CancellationToken>()), Times.Once);
            _resolverMock.Verify(r => r.ResolveAsync(discoveredItem, It.IsAny<CancellationToken>()), Times.Once);
            _validatorMock.Verify(v => v.ValidateManifestAsync(resolvedManifest, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}