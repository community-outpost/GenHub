using GenHub.Core.Interfaces.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// Tests for <see cref="ContentPipelineModule"/> registrations.
/// </summary>
public class ContentPipelineModuleTests
{
    /// <summary>
    /// Verifies that CSV discovery remains transient while remote data is cached on disk.
    /// CSV is the only transient discoverer; every other pipeline registers singletons.
    /// </summary>
    [Fact]
    public void AddContentPipelineServices_RegistersTransientCsvDiscoverer()
    {
        var services = new ServiceCollection();
        services.AddContentPipelineServices();

        var concreteDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(CsvDiscoverer));
        var transientForwards = services.Count(descriptor =>
            descriptor.ServiceType == typeof(IContentDiscoverer) && descriptor.Lifetime == ServiceLifetime.Transient);

        Assert.Equal(ServiceLifetime.Transient, concreteDescriptor.Lifetime);
        Assert.Equal(1, transientForwards);
    }
}
