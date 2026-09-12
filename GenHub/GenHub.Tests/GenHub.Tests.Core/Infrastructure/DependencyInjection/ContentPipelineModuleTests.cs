using System.Linq;
using GenHub.Core.Interfaces.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// Tests for <see cref="ContentPipelineModule"/> registrations.
/// </summary>
public class ContentPipelineModuleTests
{
    /// <summary>
    /// Verifies that CSV discovery remains transient while remote data is cached on disk.
    /// </summary>
    [Fact]
    public void AddContentPipelineServices_RegistersTransientCsvDiscoverer()
    {
        var services = new ServiceCollection();
        services.AddContentPipelineServices();

        var concreteDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(CsvDiscoverer));
        var interfaceDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IContentDiscoverer));

        Assert.Equal(ServiceLifetime.Transient, concreteDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, interfaceDescriptor.Lifetime);
    }
}
