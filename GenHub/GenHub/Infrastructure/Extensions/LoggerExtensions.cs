using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.Extensions;

/// <summary>
/// Provides extension methods for structured logging.
/// </summary>
public static class LoggerExtensions
{
    private static readonly EventId StartedEvent = new(1000, "Started");
    private static readonly EventId CompletedEvent = new(1001, "Completed");
    private static readonly EventId ErrorContextEvent = new(1002, "ErrorContext");

    /// <summary>
    /// Logs the start of an operation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="operation">The operation name.</param>
    public static void LogStarted(this ILogger logger, string operation) =>
        logger.LogInformation(
            StartedEvent,
            "Starting {Operation}…",
            operation);

    /// <summary>
    /// Logs the completion of an operation with elapsed time.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="operation">The operation name.</param>
    /// <param name="elapsed">The elapsed time.</param>
    public static void LogCompleted(this ILogger logger, string operation, TimeSpan elapsed) =>
        logger.LogInformation(
            CompletedEvent,
            "{Operation} completed in {Elapsed:mm\\:ss\\.fff}",
            operation,
            elapsed);

    /// <summary>
    /// Logs an error with context.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="context">The context string.</param>
    public static void LogError(this ILogger logger, Exception ex, string context) =>
        logger.LogError(
            ErrorContextEvent,
            ex,
            "Error in {Context}",
            context);

    /// <summary>
    /// Logs a critical error with context.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="context">The context string.</param>
    public static void LogCritical(this ILogger logger, Exception ex, string context) =>
        logger.LogCritical(
            ex,
            "Critical error in {Context}",
            context);

    /// <summary>
    /// Begins a logging scope for an operation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="operation">The operation name.</param>
    /// <returns>A disposable scope object.</returns>
    public static IDisposable BeginScopeOperation(this ILogger logger, string operation)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = operation,
            ["Timestamp"] = DateTimeOffset.UtcNow,
        })!;
    }
}