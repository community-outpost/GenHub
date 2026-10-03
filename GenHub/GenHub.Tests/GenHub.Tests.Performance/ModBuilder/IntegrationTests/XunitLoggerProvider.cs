// <copyright file="XunitLoggerProvider.cs" company="enowX Labs">
// Copyright (c) enowX Labs. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace GenHub.Tests.Performance.ModBuilder.IntegrationTests;

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
