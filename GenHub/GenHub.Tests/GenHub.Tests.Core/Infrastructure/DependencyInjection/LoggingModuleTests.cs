using System.Collections.Generic;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Tests for LoggingModule.
/// </summary>
public class LoggingModuleTests
{
    /// <summary>
    /// Verifies logger services are registered.
    /// </summary>
    [Fact]
    public void AddLoggingModule_ShouldRegisterLoggerServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProvider = CreateMockConfigProvider();
        services.AddSingleton<IConfigurationProviderService>(configProvider);

        // Act
        services.AddLoggingModule();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var logger = serviceProvider.GetService<ILogger<LoggingModuleTests>>();

        Assert.NotNull(loggerFactory);
        Assert.NotNull(logger);
    }

    /// <summary>
    /// Verifies bootstrap logger factory creation.
    /// </summary>
    [Fact]
    public void CreateBootstrapLoggerFactory_ShouldReturnValidFactory()
    {
        // Act
        using var factory = LoggingModule.CreateBootstrapLoggerFactory();
        var logger = factory.CreateLogger<LoggingModuleTests>();

        // Assert
        Assert.NotNull(factory);
        Assert.NotNull(logger);
    }

    private static IConfigurationProviderService CreateMockConfigProvider()
    {
        var mock = new Mock<IConfigurationProviderService>();
        mock.Setup(x => x.GetEnableDetailedLogging()).Returns(false);
        return mock.Object;
    }
}
