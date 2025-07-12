using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GenHub.Tests.Infrastructure.Extensions;

/// <summary>
/// Tests for LoggerExtensions.
/// </summary>
public class LoggerExtensionsTests
{
    private readonly TestLogger testLogger = new();

    /// <summary>
    /// Tests logging information with correct format.
    /// </summary>
    [Fact]
    public void LogStarted_ShouldLogInformationWithCorrectFormat()
    {
        // Arrange
        const string operation = "TestOperation";

        // Act
        this.testLogger.LogStarted(operation);

        // Assert
        var logEntry = this.testLogger.Logs.Single();
        Assert.Equal(LogLevel.Information, logEntry.LogLevel);
        Assert.Contains("Starting TestOperation", logEntry.Message);
    }

    /// <summary>
    /// Tests logging information with elapsed time.
    /// </summary>
    [Fact]
    public void LogCompleted_ShouldLogInformationWithElapsedTime()
    {
        // Arrange
        const string operation = "TestOperation";
        var elapsed = TimeSpan.FromSeconds(1.5);

        // Act
        this.testLogger.LogCompleted(operation, elapsed);

        // Assert
        var logEntry = this.testLogger.Logs.Single();
        Assert.Equal(LogLevel.Information, logEntry.LogLevel);
        Assert.Contains("TestOperation completed", logEntry.Message);
        Assert.Contains("00:01.500", logEntry.Message);
    }

    /// <summary>
    /// Tests logging error with exception.
    /// </summary>
    [Fact]
    public void LogError_ShouldLogErrorWithException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        const string context = "TestContext";

        // Act
        this.testLogger.LogError(exception, context);

        // Assert
        var logEntry = this.testLogger.Logs.Single();
        Assert.Equal(LogLevel.Error, logEntry.LogLevel);
        Assert.Same(exception, logEntry.Exception);
        Assert.Contains("Error in TestContext", logEntry.Message);
    }

    /// <summary>
    /// Tests scope creation with operation and timestamp.
    /// </summary>
    [Fact]
    public void BeginScopeOperation_ShouldCreateScopeWithOperationAndTimestamp()
    {
        // Arrange
        const string operation = "TestOperation";

        // Act
        using var scope = this.testLogger.BeginScopeOperation(operation);

        // Assert
        Assert.NotNull(scope);
        var scopeEntry = this.testLogger.Scopes.Single();
        var scopeData = scopeEntry as Dictionary<string, object>;

        Assert.NotNull(scopeData);
        Assert.Equal(operation, scopeData["Operation"]);
        Assert.True(scopeData.ContainsKey("Timestamp"));
    }

    /// <summary>
    /// Tests logging critical errors.
    /// </summary>
    [Fact]
    public void LogCriticalError_ShouldLogCriticalErrorMessage()
    {
        // Arrange
        var exception = new Exception("Critical failure");
        const string context = "CriticalContext";

        // Act
        this.testLogger.LogCritical(exception, context);

        // Assert
        var logEntry = this.testLogger.Logs.Single();
        Assert.Equal(LogLevel.Critical, logEntry.LogLevel);
        Assert.Same(exception, logEntry.Exception);
        Assert.Contains("Critical error in CriticalContext", logEntry.Message);
    }

    /// <summary>
    /// Tests logging warnings.
    /// </summary>
    [Fact]
    public void LogWarning_ShouldLogWarningMessage()
    {
        // Arrange
        const string warningMessage = "This is a warning";

        // Act
        this.testLogger.LogWarning(warningMessage);

        // Assert
        var logEntry = this.testLogger.Logs.Single();
        Assert.Equal(LogLevel.Warning, logEntry.LogLevel);
        Assert.Contains(warningMessage, logEntry.Message);
    }

    private class TestLogger : ILogger
    {
        public List<LogEntry> Logs { get; } = new();

        public List<object> Scopes { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            this.Scopes.Add(state);
            return new TestScope();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.Logs.Add(new LogEntry
            {
                LogLevel = logLevel,
                EventId = eventId,
                Message = formatter(state, exception),
                Exception = exception,
            });
        }

        private class TestScope : IDisposable
        {
            public void Dispose()
            {
                // Dispose logic if needed
            }
        }
    }

    private class LogEntry
    {
        public LogLevel LogLevel { get; set; }

        public EventId EventId { get; set; }

        public string Message { get; set; } = string.Empty;

        public Exception? Exception { get; set; }
    }
}
