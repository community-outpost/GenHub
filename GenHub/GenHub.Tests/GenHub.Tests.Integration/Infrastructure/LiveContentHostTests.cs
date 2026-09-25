using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Tests.Integration.Infrastructure;

/// <summary>
/// Verifies host cleanup without contacting live providers.
/// </summary>
public sealed class LiveContentHostTests
{
    /// <summary>
    /// Verifies that failed initialization restores process state and removes temporary data.
    /// </summary>
    [Fact]
    public void Constructor_ServiceConfigurationFails_RestoresEnvironment()
    {
        var original = CaptureEnvironment();
        var originalLog = LoggingModule.ActiveLogFilePath;
        string? appDataPath = null;

        Assert.Throws<InvalidOperationException>(() => new LiveContentHost(_ =>
        {
            appDataPath = Path.GetDirectoryName(Path.GetDirectoryName(LoggingModule.ActiveLogFilePath));
            throw new InvalidOperationException("Injected configuration failure");
        }));

        AssertRestored(original, originalLog, appDataPath!);
    }

    /// <summary>
    /// Verifies that either scope or provider disposal failure still restores process state.
    /// </summary>
    /// <param name="singleton">Whether the throwing service belongs to the provider.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispose_ServiceThrows_RestoresEnvironment(bool singleton)
    {
        var original = CaptureEnvironment();
        var originalLog = LoggingModule.ActiveLogFilePath;
        var host = new LiveContentHost(services =>
        {
            if (singleton)
            {
                services.AddSingleton<ThrowingService>();
            }
            else
            {
                services.AddScoped<ThrowingService>();
            }
        });

        try
        {
            host.Services.GetRequiredService<ThrowingService>();
            Assert.Throws<InvalidOperationException>(() => host.Dispose());
            AssertRestored(original, originalLog, host.AppDataPath);
        }
        finally
        {
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies normal disposal restores the previous log destination and temporary paths.
    /// </summary>
    [Fact]
    public void Dispose_RestoresEnvironment()
    {
        var original = CaptureEnvironment();
        var originalLog = LoggingModule.ActiveLogFilePath;
        var host = new LiveContentHost();
        host.Dispose();
        AssertRestored(original, originalLog, host.AppDataPath);
        host.Dispose();
    }

    private static Dictionary<string, string?> CaptureEnvironment() =>
        new[] { "TMPDIR", "TMP", "TEMP", "HOME", "APPDATA", "LOCALAPPDATA", "USERPROFILE", "XDG_CONFIG_HOME", "XDG_DATA_HOME" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);

    private static void AssertRestored(Dictionary<string, string?> original, string originalLog, string appDataPath)
    {
        foreach (var pair in original)
        {
            Assert.Equal(pair.Value, Environment.GetEnvironmentVariable(pair.Key));
        }

        Assert.Equal(originalLog, LoggingModule.ActiveLogFilePath);
        Assert.False(Directory.Exists(appDataPath));
    }

    private sealed class ThrowingService : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("Injected disposal failure");
    }
}
