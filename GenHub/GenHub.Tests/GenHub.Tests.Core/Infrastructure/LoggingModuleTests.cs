namespace GenHub.Tests.Core.Infrastructure;

using System.Collections.Generic;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

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
}
