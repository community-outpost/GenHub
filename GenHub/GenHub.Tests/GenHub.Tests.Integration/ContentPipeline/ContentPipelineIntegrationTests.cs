using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Tests.Integration.Infrastructure;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Integration.ContentPipeline;

/// <summary>
/// Searches and acquires real content from live providers through the application's content orchestrator.
/// </summary>
[Trait("Category", "LiveNetwork")]
public sealed class ContentPipelineIntegrationTests : IDisposable
{
    private const string GenLauncherCursorPackName = "ShockWave Cursor Pack HD";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    private readonly LiveContentHost _host = new();
    private readonly CancellationTokenSource _timeout = new(TestTimeout);

    /// <inheritdoc/>
    public void Dispose()
    {
        _timeout.Dispose();
        _host.Dispose();
    }

    /// <summary>
    /// The GeneralsOnline provider returns its current release.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GeneralsOnline_Search_ReturnsCurrentReleaseAsync()
    {
        var results = await _host.SearchAsync(PublisherTypeConstants.GeneralsOnline, cancellationToken: _timeout.Token);

        Assert.Contains(results, r => !string.IsNullOrWhiteSpace(r.Version));
    }

    /// <summary>
    /// A small GenLauncher addon is acquired into the CAS pool.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GenLauncher_AcquireSmallAddon_StoresFilesInCasAsync()
    {
        var results = await _host.SearchAsync(PublisherTypeConstants.GenLauncher, cancellationToken: _timeout.Token);
        var item = results.FirstOrDefault(r => r.Name == GenLauncherCursorPackName);
        Assert.True(item is not null, $"GenLauncher catalog no longer lists '{GenLauncherCursorPackName}'.");

        var manifest = await _host.AcquireAsync(item, _timeout.Token);

        await _host.AssertStoredInCasAsync(manifest, _timeout.Token);
    }

    /// <summary>
    /// A map from AODMaps is acquired into the CAS pool.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AODMaps_AcquireMap_StoresFilesInCasAsync()
    {
        var results = await _host.SearchAsync(AODMapsConstants.DiscovererSourceName, cancellationToken: _timeout.Token);

        var manifest = await _host.AcquireAsync(results[0], _timeout.Token);

        await _host.AssertStoredInCasAsync(manifest, _timeout.Token);
    }

    /// <summary>
    /// A map from CNC Labs is acquired into the CAS pool.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CNCLabs_AcquireMap_StoresFilesInCasAsync()
    {
        var results = await _host.SearchAsync(
            CNCLabsConstants.SourceName,
            query =>
            {
                query.TargetGame = GameType.ZeroHour;
                query.ContentType = ContentType.Map;
            },
            _timeout.Token);

        var manifest = await _host.AcquireAsync(results[0], _timeout.Token);

        await _host.AssertStoredInCasAsync(manifest, _timeout.Token);
    }

    /// <summary>
    /// GeneralsGamePatch2 from TheSuperHackers is acquired as the single published archive file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TheSuperHackers_AcquireGeneralsGamePatch2_StoresVerifiedArchiveInCasAsync()
    {
        var results = await _host.SearchAsync(PublisherTypeConstants.TheSuperHackers, cancellationToken: _timeout.Token);
        var idSuffix = "." + SuperHackersConstants.GeneralsGamePatch2Repo;
        var item = results.FirstOrDefault(r => r.Id.EndsWith(idSuffix, StringComparison.OrdinalIgnoreCase));
        Assert.True(item is not null, $"TheSuperHackers search returned no id ending in '{idSuffix}'.");

        var manifest = await _host.AcquireAsync(item, _timeout.Token);

        var file = Assert.Single(manifest.Files);
        Assert.Equal(ModBuilderConstants.SampleProjects.GeneralsGamePatch2Sha256, file.Hash, ignoreCase: true);
        await _host.AssertStoredInCasAsync(manifest, _timeout.Token);
    }
}
