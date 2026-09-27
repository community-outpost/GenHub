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

    /// <summary>
    /// Verifies GetLogFileName contains the current date prefix and version suffix.
    /// </summary>
    [Fact]
    public void GetLogFileName_ContainsDateAndVersionSuffix()
    {
        // Act
        var fileName = LoggingModule.GetLogFileName();
        var datePrefix = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var versionSuffix = LoggingModule.GetVersionFileSuffix();

        // Assert
        Assert.StartsWith($"genhub-{datePrefix}-", fileName);
        Assert.EndsWith(".log", fileName);
        Assert.Contains(versionSuffix, fileName);
    }

    /// <summary>
    /// Verifies GetVersionFileSuffix returns a non-empty string starting with v.
    /// </summary>
    [Fact]
    public void GetVersionFileSuffix_ReturnsFormattedSuffix()
    {
        // Act
        var suffix = LoggingModule.GetVersionFileSuffix();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(suffix));
        Assert.StartsWith("v", suffix);
    }

    private static IConfigurationProviderService CreateMockConfigProvider()
    {
        var mock = new Mock<IConfigurationProviderService>();
        mock.Setup(x => x.GetEnableDetailedLogging()).Returns(false);
        return mock.Object;
    }
}
