using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Helpers;
using GenHub.Core.Models.GameInstallations;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Tests.Core.Collections;
using GenHub.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for <see cref="AppDataPathHelper"/> and the paths resolved through it.
/// </summary>
[Collection(StorageMigrationStaticStateCollection.Name)]
public sealed class AppDataPathHelperTests : IDisposable
{
    private readonly string? _originalOverride = Environment.GetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar);
    private readonly string _overrideRoot = Path.Combine(Path.GetTempPath(), $"GenHub.AppDataPathHelperTests.{Guid.NewGuid():N}");

    /// <inheritdoc/>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, _originalOverride);
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);
        AppDataPathHelper.SetLegacyRoamingRootOverrideForTesting(null);
        if (Directory.Exists(_overrideRoot))
        {
            Directory.Delete(_overrideRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the test assembly runs against its own data root rather than the user's.
    /// </summary>
    [Fact]
    public void GetDataRoot_InTestAssembly_UsesIsolatedRoot()
    {
        Assert.Equal(TestDataRootInitializer.DataRoot, _originalOverride);
        Assert.Equal(TestDataRootInitializer.DataRoot, AppDataPathHelper.GetDataRoot());
    }

    /// <summary>
    /// Verifies a valid override replaces the LocalApplicationData default.
    /// </summary>
    [Fact]
    public void GetDataRoot_WithOverride_ReturnsOverride()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, $"  \"{_overrideRoot}\"  ");

        Assert.Equal(_overrideRoot, AppDataPathHelper.GetDataRoot());
    }

    /// <summary>
    /// Verifies a relative override is rejected in favour of the LocalApplicationData default.
    /// </summary>
    /// <remarks>
    /// Uses the explicit overload so the process never points at the user's real data root.
    /// </remarks>
    [Fact]
    public void ResolveDataRoot_WithRelativeOverride_FallsBackToLocalApplicationData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppName);

        Assert.Equal(expected, AppDataPathHelper.ResolveDataRoot(Path.Combine("relative", "root")));
    }

    /// <summary>
    /// Verifies the storage default data root and the helper agree when nothing else is configured.
    /// </summary>
    [Fact]
    public void GetDefaultDataRoot_WithoutConfiguredResolver_MatchesHelper()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, _overrideRoot);

        Assert.Equal(_overrideRoot, StorageMigrationService.GetDefaultDataRoot());
    }

    /// <summary>
    /// Verifies the log file is written under the configured data root outside a custom install root.
    /// </summary>
    [Fact]
    public void GetLogFilePath_WithOverride_ResolvesUnderOverride()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, _overrideRoot);
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);

        var logPath = LoggingModule.GetLogFilePath();

        Assert.Equal(Path.Combine(_overrideRoot, DirectoryNames.Logs), Path.GetDirectoryName(logPath));
    }

    /// <summary>
    /// Verifies action set completion markers are stored under the configured data root.
    /// </summary>
    [Fact]
    public void GetMarkerPath_WithOverride_ResolvesUnderOverride()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, _overrideRoot);

        var markerPath = MarkerProbe.Resolve("probe.done");

        Assert.Equal(Path.Combine(_overrideRoot, ActionSetConstants.Paths.SubActionSetMarkers, "probe.done"), markerPath);
    }

    /// <summary>
    /// Verifies legacy roaming data is never migrated while the data root is overridden, so an isolated
    /// or disposable root cannot absorb the user's real markers, backups or tokens.
    /// </summary>
    [Fact]
    public void GetLegacyRoamingRoot_WithOverride_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, _overrideRoot);

        Assert.Null(AppDataPathHelper.GetLegacyRoamingRoot());
    }

    /// <summary>
    /// Verifies a legacy roaming marker still migrates into the data root when a legacy root applies.
    /// </summary>
    [Fact]
    public void GetMarkerPath_WithLegacyRoot_MigratesLegacyMarker()
    {
        Environment.SetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar, _overrideRoot);
        var legacyRoot = Path.Combine(_overrideRoot, "Roaming");
        var legacyMarker = Path.Combine(legacyRoot, ActionSetConstants.Paths.SubActionSetMarkers, "probe.done");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyMarker)!);
        File.WriteAllText(legacyMarker, "legacy");
        AppDataPathHelper.SetLegacyRoamingRootOverrideForTesting(legacyRoot);

        var markerPath = MarkerProbe.Resolve("probe.done");

        Assert.Equal("legacy", File.ReadAllText(markerPath));
        Assert.False(File.Exists(legacyMarker));
    }

    private sealed class MarkerProbe() : BaseActionSet(NullLogger.Instance)
    {
        public override string Id => nameof(MarkerProbe);

        public override string Title => nameof(MarkerProbe);

        public override bool IsCoreFix => false;

        public override bool IsCrucialFix => false;

        public static string Resolve(string markerFileName) => GetMarkerPath(markerFileName);

        protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct) => Task.FromResult(Success());

        protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct) => Task.FromResult(Success());
    }
}
