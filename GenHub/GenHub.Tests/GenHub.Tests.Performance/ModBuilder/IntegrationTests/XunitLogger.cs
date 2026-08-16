// <copyright file="XunitLogger.cs" company="enowX Labs">
// Copyright (c) enowX Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Performance.ModBuilder.IntegrationTests;

using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

/// <summary>
/// XUnit logger provider for capturing logs in test output.
/// </summary>
internal sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XunitLogger(output, categoryName);

    public void Dispose()
    {
    }
}

/// <summary>
/// XUnit logger for capturing logs in test output.
/// </summary>
internal sealed class XunitLogger(ITestOutputHelper output, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            output.WriteLine($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
            if (exception != null)
            {
                output.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // Ignore errors writing to test output
        }
    }
}
