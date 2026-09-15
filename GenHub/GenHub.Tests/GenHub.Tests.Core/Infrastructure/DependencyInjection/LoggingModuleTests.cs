using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

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

    /// <summary>
    /// Verifies bootstrap logger factory writes debug log events to file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateBootstrapLoggerFactory_WritesDebugLogToFileAsync()
    {
        var originalActiveLog = LoggingModule.ActiveLogFilePath;
        var tempFile = Path.Combine(Path.GetTempPath(), "BootstrapDebugLog_" + Guid.NewGuid().ToString("N") + ".log");

        try
        {
            LoggingModule.ActiveLogFilePath = tempFile;

            using (var factory = LoggingModule.CreateBootstrapLoggerFactory())
            {
                var logger = factory.CreateLogger<LoggingModuleTests>();
                logger.LogDebug("Bootstrap debug message test");
            }

            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("Bootstrap debug message test", content);
        }
        finally
        {
            LoggingModule.ActiveLogFilePath = originalActiveLog;
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static IConfigurationProviderService CreateMockConfigProvider()
    {
        var mock = new Mock<IConfigurationProviderService>();
        mock.Setup(x => x.GetEnableDetailedLogging()).Returns(false);
        return mock.Object;
    }
}