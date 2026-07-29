using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GenHub.Core.Constants;

namespace GenHub.Tests.Shared;

/// <summary>
/// Redirects application storage and platform home paths to a disposable test tree.
/// </summary>
internal sealed class TemporaryApplicationEnvironment : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryApplicationEnvironment"/> class.
    /// </summary>
    internal TemporaryApplicationEnvironment()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"GenHub.Tests.{Guid.NewGuid():N}");
        AppDataPath = Path.Combine(RootPath, "AppData");
        CasPath = Path.Combine(RootPath, DirectoryNames.CasPool);
        Directory.CreateDirectory(AppDataPath);

        var settings = JsonSerializer.Serialize(new
        {
            casConfiguration = new
            {
                casRootPath = CasPath,
            },
        });

        File.WriteAllText(Path.Combine(AppDataPath, FileTypes.SettingsFileName), settings);

        SetEnvironmentVariable("GENHUB_GenHub__AppDataPath", AppDataPath);
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
    private string RootPath { get; }

    /// <inheritdoc/>
    void IDisposable.Dispose()
    {
        foreach (var pair in _originalValues)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        Directory.Delete(RootPath, recursive: true);
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        _originalValues[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
}
