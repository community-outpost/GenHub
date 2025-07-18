using System;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Unit tests for <see cref="DownloadModule"/> DI registration.
/// </summary>
public class DownloadModuleTests
{
    /// <summary>
    /// Verifies that AddDownloadServices registers DownloadService and HttpClient in the DI container.
    /// </summary>
    [Fact]
    public void AddDownloadServices_RegistersDownloadServiceAndHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDownloadServices();
        var provider = services.BuildServiceProvider();

        // Assert
        var service = provider.GetService<IDownloadService>();
        Assert.NotNull(service);
        Assert.IsType<DownloadService>(service);

        // HttpClient should be resolvable for DownloadService
        var httpClient = provider.GetService<HttpClient>();
        Assert.NotNull(httpClient);
    }
}
