using System.IO;
using System.Linq;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace GenHub.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// Tests for <see cref="ContentPipelineModule"/> registrations.
/// </summary>
public class ContentPipelineModuleTests
{
    /// <summary>
    /// Verifies that CSV catalog entries remain cached across provider resolutions.
    /// </summary>
    [Fact]
    public void AddContentPipelineServices_RegistersSharedCsvDiscoverer()
    {
        var configProvider = new Mock<IConfigurationProviderService>();
        configProvider
            .Setup(provider => provider.GetCsvCatalogConfiguration())
            .Returns(new CsvCatalogConfiguration());
        configProvider
            .Setup(provider => provider.GetApplicationDataPath())
            .Returns(Path.GetTempPath());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configProvider.Object);
        services.AddContentPipelineServices();

        using var serviceProvider = services.BuildServiceProvider();
        var concreteDiscoverer = serviceProvider.GetRequiredService<CsvDiscoverer>();
        var interfaceDescriptor = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IContentDiscoverer) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        var interfaceDiscoverer = interfaceDescriptor.ImplementationFactory!(serviceProvider);

        Assert.Same(concreteDiscoverer, interfaceDiscoverer);
    }
}
