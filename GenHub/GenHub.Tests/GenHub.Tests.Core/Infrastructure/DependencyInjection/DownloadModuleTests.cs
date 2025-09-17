using System;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
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
        var configProvider = CreateMockConfigProvider();

        // Register the IConfigurationProviderService that DownloadModule expects
        services.AddSingleton(configProvider);
        services.AddSingleton<IFileHashProvider, Sha256HashProvider>();

        // Act
        services.AddDownloadServices();
        var provider = services.BuildServiceProvider();

        // Assert
        var service = provider.GetService<IDownloadService>();
        Assert.NotNull(service);
        Assert.IsType<DownloadService>(service);

        // HttpClient should be resolvable for DownloadService
        var httpClientFactory = provider.GetService<IHttpClientFactory>();
        Assert.NotNull(httpClientFactory);

        var httpClient = httpClientFactory.CreateClient(nameof(DownloadService));
        Assert.NotNull(httpClient);
    }

    private static IConfigurationProviderService CreateMockConfigProvider()
    {
        var mock = new Mock<IConfigurationProviderService>();
        mock.Setup(x => x.GetDownloadUserAgent()).Returns("TestAgent/1.0");
        mock.Setup(x => x.GetDownloadTimeoutSeconds()).Returns(120);
        return mock.Object;
    }
}
