using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Integration.Infrastructure;

/// <summary>
/// Builds the real application container against a disposable data root, CAS pool and temp directory.
/// </summary>
internal sealed class LiveContentHost : IDisposable
{
    private static readonly string[] TempVariables = ["TMPDIR", "TMP", "TEMP"];

    private readonly IDisposable _environment;
    private readonly Dictionary<string, string?> _originalTempValues = [];
    private readonly string _originalLogFilePath = LoggingModule.ActiveLogFilePath;
    private readonly ServiceProvider? _provider;
    private readonly IServiceScope? _scope;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveContentHost"/> class.
    /// </summary>
    /// <param name="configureServices">Optional test-specific service registrations.</param>
    internal LiveContentHost(Action<IServiceCollection>? configureServices = null)
    {
        var environment = new TemporaryApplicationEnvironment();
        _environment = environment;
        AppDataPath = environment.AppDataPath;
        CasPath = environment.CasPath;
        TempPath = Path.Combine(AppDataPath, "tmp");

        try
        {
            Directory.CreateDirectory(TempPath);
            foreach (var name in TempVariables)
            {
                _originalTempValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, TempPath);
            }

            LoggingModule.ActiveLogFilePath = Path.Combine(AppDataPath, "Logs", "genhub-integration.log");

            var services = new ServiceCollection();
            services.ConfigureApplicationServices(AddPlatformNeutralServices);
            configureServices?.Invoke(services);
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the isolated application data path.
    /// </summary>
    internal string AppDataPath { get; }

    /// <summary>
    /// Gets the isolated CAS pool path.
    /// </summary>
    internal string CasPath { get; }

    /// <summary>
    /// Gets the isolated temp directory that staging and downloads use.
    /// </summary>
    internal string TempPath { get; }

    /// <summary>
    /// Gets the scoped service provider.
    /// </summary>
    internal IServiceProvider Services => _scope!.ServiceProvider;

    /// <summary>
    /// Gets the content orchestrator.
    /// </summary>
    internal IContentOrchestrator Orchestrator => Services.GetRequiredService<IContentOrchestrator>();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _scope?.Dispose();
        }
        finally
        {
            try
            {
                _provider?.Dispose();
            }
            finally
            {
                RestoreEnvironment();
            }
        }
    }

    /// <summary>
    /// Computes the lowercase hex SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The hash.</returns>
    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Searches one provider and fails the test when the search fails or finds nothing.
    /// </summary>
    /// <param name="providerName">The provider source name.</param>
    /// <param name="configure">Optional provider-specific query settings.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The search results.</returns>
    internal async Task<IReadOnlyList<ContentSearchResult>> SearchAsync(
        string providerName,
        Action<ContentSearchQuery>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var query = new ContentSearchQuery { ProviderName = providerName };
        configure?.Invoke(query);

        var result = await Orchestrator.SearchAsync(query, cancellationToken);

        Assert.True(result.Success, $"{providerName} search failed: {result.FirstError}");
        var items = result.Data!.ToList();
        Assert.True(items.Count > 0, $"{providerName} search returned no results.");
        Assert.All(items, item => Assert.Equal(providerName, item.ProviderName, ignoreCase: true));
        return items;
    }

    /// <summary>
    /// Acquires a search result and fails the test when acquisition fails.
    /// </summary>
    /// <param name="item">The search result to acquire.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The acquired manifest.</returns>
    internal async Task<ContentManifest> AcquireAsync(ContentSearchResult item, CancellationToken cancellationToken = default)
    {
        var result = await Orchestrator.AcquireContentAsync(item, cancellationToken: cancellationToken);
        Assert.True(result.Success, $"Acquiring '{item.Name}' from {item.ProviderName} failed: {result.FirstError}");
        return result.Data!;
    }

    /// <summary>
    /// Asserts every content-addressable file in the manifest is stored in the isolated CAS pool with a matching SHA-256.
    /// </summary>
    /// <param name="manifest">The acquired manifest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every file is verified.</returns>
    internal async Task AssertStoredInCasAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        var casFiles = manifest.Files.Where(f => f.SourceType == ContentSourceType.ContentAddressable).ToList();
        Assert.NotEmpty(casFiles);

        var cas = Services.GetRequiredService<ICasService>();
        foreach (var file in casFiles)
        {
            var pathResult = await cas.GetContentPathAsync(file.Hash, manifest.ContentType, cancellationToken);
            Assert.True(pathResult.Success, $"CAS has no object for {file.RelativePath}: {pathResult.FirstError}");

            var objectPath = pathResult.Data!;
            Assert.StartsWith(CasPath, objectPath, StringComparison.Ordinal);
            Assert.Equal(file.Hash, await ComputeSha256Async(objectPath, cancellationToken), ignoreCase: true);
        }
    }

    private static IServiceCollection AddPlatformNeutralServices(IServiceCollection services)
    {
        // Windows acquisition uses the base file service; this host does not support
        // Windows workspace/hard-link tests, which require the Windows platform module.
        if (!OperatingSystem.IsWindows())
        {
            services.AddUnixFileOperations();
        }

        return services;
    }

    private void RestoreEnvironment()
    {
        LoggingModule.ActiveLogFilePath = _originalLogFilePath;
        try
        {
            foreach (var pair in _originalTempValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
        finally
        {
            _environment.Dispose();
        }
    }
}
