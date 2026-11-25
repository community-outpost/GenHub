using GenHub.Core.Constants;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace GenHub.Tests.Shared;

/// <summary>
/// Redirects application storage and platform home paths to a disposable test tree.
/// </summary>
internal sealed class TemporaryApplicationEnvironment : IDisposable
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, string?> _originalValues = [];

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryApplicationEnvironment"/> class.
    /// </summary>
    internal TemporaryApplicationEnvironment()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"GenHub.Tests.{Guid.NewGuid():N}");
        AppDataPath = Path.Combine(RootPath, "AppData");
        CasPath = Path.Combine(RootPath, DirectoryNames.CasPool);
        Directory.CreateDirectory(AppDataPath);

        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration
            {
                CasRootPath = CasPath,
            },
        };
        var settingsJson = JsonSerializer.Serialize(settings, SettingsJsonOptions);

        File.WriteAllText(Path.Combine(AppDataPath, FileTypes.SettingsFileName), settingsJson);

        SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, AppDataPath);
        SetEnvironmentVariable("APPDATA", Path.Combine(RootPath, "RoamingAppData"));
        SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(RootPath, "LocalAppData"));
        SetEnvironmentVariable("USERPROFILE", RootPath);
        SetEnvironmentVariable("HOME", RootPath);
        SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(RootPath, "Config"));
        SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(RootPath, "Data"));
    }

    /// <summary>
    /// Gets the isolated application data path.
    /// </summary>
    internal string AppDataPath { get; }

    /// <summary>
    /// Gets the isolated content-addressable storage path.
    /// </summary>
    internal string CasPath { get; }

    /// <summary>
    /// Gets the root of the disposable test tree.
    /// </summary>
    internal string RootPath { get; }

    /// <summary>
    /// Restores the environment and deletes the test tree on a best-effort basis.
    /// Later calls do nothing.
    /// </summary>
    /// <remarks>
    /// Services resolved during a test can keep writing under the tree after the test body ends.
    /// A recursive delete that races with them fails, so cleanup retries and then leaves the tree in place.
    /// </remarks>
    void IDisposable.Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pair in _originalValues)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        for (var attempt = 1; attempt <= TestEnvironmentConstants.DeleteAttempts; attempt++)
        {
            if (TryDeleteRoot(out var failure))
            {
                return;
            }

            if (attempt == TestEnvironmentConstants.DeleteAttempts)
            {
                Trace.TraceWarning($"Leaving test tree {RootPath} in place: {failure}");
                return;
            }

            Trace.TraceInformation($"Retrying delete of test tree {RootPath}: {failure}");
            Thread.Sleep(TestEnvironmentConstants.DeleteRetryDelay * attempt);
        }
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        _originalValues[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private bool TryDeleteRoot(out string? failure)
    {
        failure = null;
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return true;
        }
        catch (IOException ex)
        {
            failure = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            failure = ex.Message;
        }

        return false;
    }
}
