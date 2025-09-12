using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content
{
    /// <summary>
    /// CNCLabsMapDiscovererTests class.
    /// </summary>
    public class CNCLabsMapDiscovererTests
    {
        private readonly Mock<IContentDiscoverer> _discovererMock;
        private readonly Mock<IContentResolver> _resolverMock;
        private readonly Mock<IContentDeliverer> _delivererMock;
        private readonly Mock<IContentValidator> _validatorMock;
        private readonly Mock<IContentManifestBuilder> _contentManifestBuilderMock;
        private readonly Mock<ILogger<CNCLabsContentProvider>> _loggerMock;

        private readonly CNCLabsContentProvider _provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="CNCLabsMapDiscovererTests"/> class.
        /// </summary>
        public CNCLabsMapDiscovererTests()
        {
            _discovererMock = new Mock<IContentDiscoverer>();
            _resolverMock = new Mock<IContentResolver>();
            _delivererMock = new Mock<IContentDeliverer>();
            _validatorMock = new Mock<IContentValidator>();
            _contentManifestBuilderMock = new Mock<IContentManifestBuilder>(); // kept in case provider needs it elsewhere
            _loggerMock = new Mock<ILogger<CNCLabsContentProvider>>();

            // Provider requires a deliverer SourceName (used in results)
            _discovererMock.Setup(d => d.SourceName).Returns("CNC Labs");
            _resolverMock.Setup(d => d.ResolverId).Returns("CNCLabsMap");
            _delivererMock.Setup(d => d.SourceName).Returns("HTTP");

            var httpClient = new HttpClient();

            var realDiscoverer = new CNCLabsMapDiscoverer(httpClient, Mock.Of<ILogger<CNCLabsMapDiscoverer>>());

            _provider = new CNCLabsContentProvider(
                new[] { realDiscoverer },              // real discoverer
                new[] { _resolverMock.Object },
                new[] { _delivererMock.Object },
                _loggerMock.Object,
                _validatorMock.Object
            );

        }

        /// <summary>
        ///
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
        [Fact]
        public async Task SearchAsync()
        {
            // Arrange
            var query = new ContentSearchQuery { SearchTerm = "art of war" };

            // Make sure the resolver is "found" for that item
            _resolverMock.Setup(r => r.ResolverId).Returns("CNCLabsMap");

            // What resolution returns
            var resolvedManifest = new ContentManifest
            {
                Id = "1.0.gh.test.publisher.mod",
                Name = "Resolved Test Mod"
            };

            _resolverMock
                .Setup(r => r.ResolveAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(resolvedManifest));

            _validatorMock
                .Setup(v => v.ValidateManifestAsync(resolvedManifest, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(resolvedManifest.Id, new List<ValidationIssue>()));

            var result = await _provider.SearchAsync(query);
            Assert.True(result.Success);
        }
    }
}
