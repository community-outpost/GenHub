using GenHub.Core.Constants;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace GenHub.Tests.Shared;

/// <summary>
/// Points the application data root at a per-process temp directory before any test runs,
/// so logs, markers, backups and caches never land in the user's real GenHub data.
/// </summary>
internal static class TestDataRootInitializer
{
    /// <summary>
    /// Gets the per-process data root assigned to this test assembly.
    /// </summary>
    internal static string DataRoot { get; } = Path.Combine(Path.GetTempPath(), $"GenHub.TestRun.{Environment.ProcessId}.{Guid.NewGuid():N}");

    /// <summary>
    /// Redirects the data root when the test assembly loads.
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Test assemblies must isolate the data root before any test code runs.")]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, DataRoot);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete();
    }

    private static void TryDelete()
    {
        try
        {
            Directory.Delete(DataRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An open log file can keep the directory alive; the temp tree is disposable.
        }
    }
}
