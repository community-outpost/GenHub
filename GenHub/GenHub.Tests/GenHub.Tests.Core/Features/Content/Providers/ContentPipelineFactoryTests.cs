using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Providers;

/// <summary>
/// Unit tests for <see cref="ContentPipelineFactory"/>.
/// </summary>
public class ContentPipelineFactoryTests
{
    private readonly Mock<ILogger<ContentPipelineFactory>> _loggerMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPipelineFactoryTests"/> class.
    /// </summary>
    public ContentPipelineFactoryTests()
    {
        _loggerMock = new Mock<ILogger<ContentPipelineFactory>>();
    }

    /// <summary>
    /// Verifies that GetDiscoverer returns the correct discoverer by SourceName.
    /// </summary>
    [Fact]
    public void GetDiscoverer_ReturnsCorrectDiscoverer_BySourceName()
    {
        // Arrange
        var discoverer1 = CreateMockDiscoverer("provider-a");
        var discoverer2 = CreateMockDiscoverer("provider-b");

        var factory = new ContentPipelineFactory(
            new[] { discoverer1.Object, discoverer2.Object },
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetDiscoverer("provider-a");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("provider-a", result.SourceName);
    }

    /// <summary>
    /// Verifies that GetDiscoverer is case-insensitive.
    /// </summary>
    [Fact]
    public void GetDiscoverer_IsCaseInsensitive()
    {
        // Arrange
        var discoverer = CreateMockDiscoverer("Provider-Test");

        var factory = new ContentPipelineFactory(
            new[] { discoverer.Object },
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act & Assert
        Assert.NotNull(factory.GetDiscoverer("provider-test"));
        Assert.NotNull(factory.GetDiscoverer("PROVIDER-TEST"));
        Assert.NotNull(factory.GetDiscoverer("Provider-Test"));
    }

    /// <summary>
    /// Verifies that GetDiscoverer returns null for non-existent provider.
    /// </summary>
    [Fact]
    public void GetDiscoverer_ReturnsNull_ForNonExistentProvider()
    {
        // Arrange
        var discoverer = CreateMockDiscoverer("provider-a");

        var factory = new ContentPipelineFactory(
            new[] { discoverer.Object },
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetDiscoverer("non-existent");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetDiscoverer returns null for null or empty provider ID.
    /// </summary>
    /// <param name="providerId">The provider ID to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDiscoverer_ReturnsNull_ForNullOrEmptyProviderId(string? providerId)
    {
        // Arrange
        var discoverer = CreateMockDiscoverer("provider-a");

        var factory = new ContentPipelineFactory(
            new[] { discoverer.Object },
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetDiscoverer(providerId!);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetResolver returns the correct resolver by ResolverId.
    /// </summary>
    [Fact]
    public void GetResolver_ReturnsCorrectResolver_ByResolverId()
    {
        // Arrange
        var resolver1 = CreateMockResolver("resolver-a");
        var resolver2 = CreateMockResolver("resolver-b");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            new[] { resolver1.Object, resolver2.Object },
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetResolver("resolver-a");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("resolver-a", result.ResolverId);
    }

    /// <summary>
    /// Verifies that GetResolver is case-insensitive.
    /// </summary>
    [Fact]
    public void GetResolver_IsCaseInsensitive()
    {
        // Arrange
        var resolver = CreateMockResolver("Resolver-Test");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            new[] { resolver.Object },
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act & Assert
        Assert.NotNull(factory.GetResolver("resolver-test"));
        Assert.NotNull(factory.GetResolver("RESOLVER-TEST"));
        Assert.NotNull(factory.GetResolver("Resolver-Test"));
    }

    /// <summary>
    /// Verifies that GetResolver returns null for non-existent provider.
    /// </summary>
    [Fact]
    public void GetResolver_ReturnsNull_ForNonExistentProvider()
    {
        // Arrange
        var resolver = CreateMockResolver("resolver-a");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            new[] { resolver.Object },
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetResolver("non-existent");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetDeliverer returns the correct deliverer by SourceName.
    /// </summary>
    [Fact]
    public void GetDeliverer_ReturnsCorrectDeliverer_BySourceName()
    {
        // Arrange
        var deliverer1 = CreateMockDeliverer("deliverer-a");
        var deliverer2 = CreateMockDeliverer("deliverer-b");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            Enumerable.Empty<IContentResolver>(),
            new[] { deliverer1.Object, deliverer2.Object },
            _loggerMock.Object);

        // Act
        var result = factory.GetDeliverer("deliverer-a");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("deliverer-a", result.SourceName);
    }

    /// <summary>
    /// Verifies that GetDeliverer is case-insensitive.
    /// </summary>
    [Fact]
    public void GetDeliverer_IsCaseInsensitive()
    {
        // Arrange
        var deliverer = CreateMockDeliverer("Deliverer-Test");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            Enumerable.Empty<IContentResolver>(),
            new[] { deliverer.Object },
            _loggerMock.Object);

        // Act & Assert
        Assert.NotNull(factory.GetDeliverer("deliverer-test"));
        Assert.NotNull(factory.GetDeliverer("DELIVERER-TEST"));
        Assert.NotNull(factory.GetDeliverer("Deliverer-Test"));
    }

    /// <summary>
    /// Verifies that GetDeliverer returns null for non-existent provider.
    /// </summary>
    [Fact]
    public void GetDeliverer_ReturnsNull_ForNonExistentProvider()
    {
        // Arrange
        var deliverer = CreateMockDeliverer("deliverer-a");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            Enumerable.Empty<IContentResolver>(),
            new[] { deliverer.Object },
            _loggerMock.Object);

        // Act
        var result = factory.GetDeliverer("non-existent");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetPipeline returns all three components when available.
    /// </summary>
    [Fact]
    public void GetPipeline_ReturnsAllComponents_WhenAvailable()
    {
        // Arrange
        var discoverer = CreateMockDiscoverer("test-provider");
        var resolver = CreateMockResolver("test-provider");
        var deliverer = CreateMockDeliverer("test-provider");

        var factory = new ContentPipelineFactory(
            new[] { discoverer.Object },
            new[] { resolver.Object },
            new[] { deliverer.Object },
            _loggerMock.Object);

        var provider = new ProviderDefinition { ProviderId = "test-provider" };

        // Act
        var (resultDiscoverer, resultResolver, resultDeliverer) = factory.GetPipeline(provider);

        // Assert
        Assert.NotNull(resultDiscoverer);
        Assert.NotNull(resultResolver);
        Assert.NotNull(resultDeliverer);
    }

    /// <summary>
    /// Verifies that GetPipeline returns partial components when some are missing.
    /// </summary>
    [Fact]
    public void GetPipeline_ReturnsPartialComponents_WhenSomeMissing()
    {
        // Arrange
        var discoverer = CreateMockDiscoverer("partial-provider");

        var factory = new ContentPipelineFactory(
            new[] { discoverer.Object },
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        var provider = new ProviderDefinition { ProviderId = "partial-provider" };

        // Act
        var (resultDiscoverer, resultResolver, resultDeliverer) = factory.GetPipeline(provider);

        // Assert
        Assert.NotNull(resultDiscoverer);
        Assert.Null(resultResolver);
        Assert.Null(resultDeliverer);
    }

    /// <summary>
    /// Verifies that GetPipeline throws ArgumentNullException for null provider.
    /// </summary>
    [Fact]
    public void GetPipeline_ThrowsArgumentNullException_ForNullProvider()
    {
        // Arrange
        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => factory.GetPipeline(null!));
    }

    /// <summary>
    /// Verifies that GetAllDiscoverers returns all registered discoverers.
    /// </summary>
    [Fact]
    public void GetAllDiscoverers_ReturnsAllRegisteredDiscoverers()
    {
        // Arrange
        var discoverer1 = CreateMockDiscoverer("provider-a");
        var discoverer2 = CreateMockDiscoverer("provider-b");
        var discoverer3 = CreateMockDiscoverer("provider-c");

        var factory = new ContentPipelineFactory(
            new[] { discoverer1.Object, discoverer2.Object, discoverer3.Object },
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetAllDiscoverers().ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    /// <summary>
    /// Verifies that GetAllResolvers returns all registered resolvers.
    /// </summary>
    [Fact]
    public void GetAllResolvers_ReturnsAllRegisteredResolvers()
    {
        // Arrange
        var resolver1 = CreateMockResolver("resolver-a");
        var resolver2 = CreateMockResolver("resolver-b");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            new[] { resolver1.Object, resolver2.Object },
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act
        var result = factory.GetAllResolvers().ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    /// <summary>
    /// Verifies that GetAllDeliverers returns all registered deliverers.
    /// </summary>
    [Fact]
    public void GetAllDeliverers_ReturnsAllRegisteredDeliverers()
    {
        // Arrange
        var deliverer1 = CreateMockDeliverer("deliverer-a");
        var deliverer2 = CreateMockDeliverer("deliverer-b");
        var deliverer3 = CreateMockDeliverer("deliverer-c");
        var deliverer4 = CreateMockDeliverer("deliverer-d");

        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            Enumerable.Empty<IContentResolver>(),
            new[] { deliverer1.Object, deliverer2.Object, deliverer3.Object, deliverer4.Object },
            _loggerMock.Object);

        // Act
        var result = factory.GetAllDeliverers().ToList();

        // Assert
        Assert.Equal(4, result.Count);
    }

    /// <summary>
    /// Verifies that factory handles empty collections correctly.
    /// </summary>
    [Fact]
    public void Factory_HandlesEmptyCollections_Correctly()
    {
        // Arrange
        var factory = new ContentPipelineFactory(
            Enumerable.Empty<IContentDiscoverer>(),
            Enumerable.Empty<IContentResolver>(),
            Enumerable.Empty<IContentDeliverer>(),
            _loggerMock.Object);

        // Act & Assert
        Assert.Null(factory.GetDiscoverer("any"));
        Assert.Null(factory.GetResolver("any"));
        Assert.Null(factory.GetDeliverer("any"));
        Assert.Empty(factory.GetAllDiscoverers());
        Assert.Empty(factory.GetAllResolvers());
        Assert.Empty(factory.GetAllDeliverers());
    }

    private static Mock<IContentDiscoverer> CreateMockDiscoverer(string sourceName)
    {
        var mock = new Mock<IContentDiscoverer>();
        mock.Setup(d => d.SourceName).Returns(sourceName);
        mock.Setup(d => d.Description).Returns($"Discoverer for {sourceName}");
        mock.Setup(d => d.IsEnabled).Returns(true);
        mock.Setup(d => d.Capabilities).Returns(ContentSourceCapabilities.RequiresDiscovery);
        return mock;
    }

    private static Mock<IContentResolver> CreateMockResolver(string resolverId)
    {
        var mock = new Mock<IContentResolver>();
        mock.Setup(r => r.ResolverId).Returns(resolverId);
        return mock;
    }

    private static Mock<IContentDeliverer> CreateMockDeliverer(string sourceName)
    {
        var mock = new Mock<IContentDeliverer>();
        mock.Setup(d => d.SourceName).Returns(sourceName);
        mock.Setup(d => d.Description).Returns($"Deliverer for {sourceName}");
        mock.Setup(d => d.CanDeliver(It.IsAny<ContentManifest>())).Returns(true);
        return mock;
    }
}
