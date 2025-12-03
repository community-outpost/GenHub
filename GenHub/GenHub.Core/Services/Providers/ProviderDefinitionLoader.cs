namespace GenHub.Core.Services.Providers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for loading provider definitions from JSON configuration files.
/// <para>
/// Providers are loaded from two locations (in order of priority):
/// <list type="number">
/// <item><description>
/// <b>Bundled Providers</b>: <c>{AppDirectory}/Providers/*.provider.json</c>
/// - Ships with the application
/// - Read-only, updated via application updates
/// - Contains official provider definitions
/// </description></item>
/// <item><description>
/// <b>User Providers</b>: <c>{AppData}/GenHub/Providers/*.provider.json</c>
/// - Optional user-defined or customized providers
/// - User providers with matching ProviderId override bundled providers
/// - Enables power users to add custom content sources
/// </description></item>
/// </list>
/// </para>
/// </summary>
public class ProviderDefinitionLoader : IProviderDefinitionLoader
{
    /// <summary>
    /// The name of the Providers subdirectory.
    /// </summary>
    public const string ProvidersDirectoryName = "Providers";

    /// <summary>
    /// The file pattern for provider definition files.
    /// </summary>
    public const string ProviderFilePattern = "*.provider.json";

    /// <summary>
    /// The application name used for AppData folder.
    /// </summary>
    private const string AppDataFolderName = "GenHub";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };

    private readonly ILogger<ProviderDefinitionLoader> logger;
    private readonly string bundledProvidersDirectory;
    private readonly string userProvidersDirectory;
    private readonly ConcurrentDictionary<string, ProviderDefinition> providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private bool isInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderDefinitionLoader"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="bundledProvidersDirectory">Override for bundled providers directory (testing).</param>
    /// <param name="userProvidersDirectory">Override for user providers directory (testing).</param>
    public ProviderDefinitionLoader(
        ILogger<ProviderDefinitionLoader> logger,
        string? bundledProvidersDirectory = null,
        string? userProvidersDirectory = null)
    {
        this.logger = logger;
        this.bundledProvidersDirectory = bundledProvidersDirectory ?? GetBundledProvidersDirectory();
        this.userProvidersDirectory = userProvidersDirectory ?? GetUserProvidersDirectory();

        this.logger.LogDebug(
            "ProviderDefinitionLoader initialized - Bundled: {BundledPath}, User: {UserPath}",
            this.bundledProvidersDirectory,
            this.userProvidersDirectory);
    }

    /// <summary>
    /// Gets the bundled providers directory path.
    /// </summary>
    public string BundledProvidersDirectory => this.bundledProvidersDirectory;

    /// <summary>
    /// Gets the user providers directory path.
    /// </summary>
    public string UserProvidersDirectory => this.userProvidersDirectory;

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<ProviderDefinition>>> LoadProvidersAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        await this.loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.isInitialized)
            {
                return OperationResult<IEnumerable<ProviderDefinition>>.CreateSuccess(this.providers.Values.ToList(), stopwatch.Elapsed);
            }

            var result = await this.LoadAllProvidersInternalAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return OperationResult<IEnumerable<ProviderDefinition>>.CreateFailure(result, stopwatch.Elapsed);
            }

            this.isInitialized = true;

            return OperationResult<IEnumerable<ProviderDefinition>>.CreateSuccess(this.providers.Values.ToList(), stopwatch.Elapsed);
        }
        finally
        {
            this.loadLock.Release();
        }
    }

    /// <inheritdoc/>
    public ProviderDefinition? GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        // Auto-load providers if not initialized
        if (!this.isInitialized)
        {
            this.EnsureProvidersLoaded();
        }

        return this.providers.TryGetValue(providerId, out var provider) ? provider : null;
    }

    /// <inheritdoc/>
    public IEnumerable<ProviderDefinition> GetAllProviders()
    {
        // Auto-load providers if not initialized
        if (!this.isInitialized)
        {
            this.EnsureProvidersLoaded();
        }

        return this.providers.Values.Where(p => p.Enabled);
    }

    /// <inheritdoc/>
    public IEnumerable<ProviderDefinition> GetProvidersByType(ProviderType providerType)
    {
        // Auto-load providers if not initialized
        if (!this.isInitialized)
        {
            this.EnsureProvidersLoaded();
        }

        return this.providers.Values
            .Where(p => p.Enabled && p.ProviderType == providerType);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ReloadProvidersAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        await this.loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            this.providers.Clear();
            this.isInitialized = false;

            var result = await this.LoadAllProvidersInternalAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return OperationResult<bool>.CreateFailure(result, stopwatch.Elapsed);
            }

            this.isInitialized = true;

            this.logger.LogInformation("Reloaded {Count} providers", this.providers.Count);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }
        finally
        {
            this.loadLock.Release();
        }
    }

    /// <inheritdoc/>
    public OperationResult<bool> AddCustomProvider(ProviderDefinition provider)
    {
        var stopwatch = Stopwatch.StartNew();

        if (provider == null)
        {
            return OperationResult<bool>.CreateFailure("Provider definition cannot be null.", stopwatch.Elapsed);
        }

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            return OperationResult<bool>.CreateFailure("Provider ID cannot be null or empty.", stopwatch.Elapsed);
        }

        this.providers.AddOrUpdate(provider.ProviderId, provider, (_, _) => provider);
        this.logger.LogInformation("Added custom provider {ProviderId}", provider.ProviderId);

        return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
    }

    /// <inheritdoc/>
    public OperationResult<bool> RemoveCustomProvider(string providerId)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(providerId))
        {
            return OperationResult<bool>.CreateFailure("Provider ID cannot be null or empty.", stopwatch.Elapsed);
        }

        var removed = this.providers.TryRemove(providerId, out _);

        if (removed)
        {
            this.logger.LogInformation("Removed custom provider {ProviderId}", providerId);
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }

        return OperationResult<bool>.CreateFailure($"Provider '{providerId}' not found.", stopwatch.Elapsed);
    }

    /// <summary>
    /// Gets the default bundled providers directory (application directory).
    /// </summary>
    private static string GetBundledProvidersDirectory()
    {
        var appDirectory = AppContext.BaseDirectory;
        return Path.Combine(appDirectory, ProvidersDirectoryName);
    }

    /// <summary>
    /// Gets the user providers directory (AppData/Roaming/GenHub/Providers).
    /// </summary>
    private static string GetUserProvidersDirectory()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, AppDataFolderName, ProvidersDirectoryName);
    }

    /// <summary>
    /// Ensures providers are loaded synchronously. Used by synchronous accessor methods.
    /// </summary>
    private void EnsureProvidersLoaded()
    {
        // Use a synchronous load for first-time access from sync methods
        this.loadLock.Wait();
        try
        {
            if (this.isInitialized)
            {
                return;
            }

            // Perform synchronous load
            this.LoadAllProvidersSynchronous();
            this.isInitialized = true;
        }
        finally
        {
            this.loadLock.Release();
        }
    }

    /// <summary>
    /// Loads all providers synchronously from both bundled and user directories.
    /// User providers override bundled providers with the same ProviderId.
    /// </summary>
    private void LoadAllProvidersSynchronous()
    {
        // Load bundled providers first
        this.LoadProvidersFromDirectorySynchronous(this.bundledProvidersDirectory, "bundled");

        // Load user providers (override bundled if same ID)
        this.LoadProvidersFromDirectorySynchronous(this.userProvidersDirectory, "user");

        this.logger.LogInformation(
            "Loaded {Count} providers (bundled: {BundledPath}, user: {UserPath})",
            this.providers.Count,
            this.bundledProvidersDirectory,
            this.userProvidersDirectory);
    }

    /// <summary>
    /// Loads providers from a specific directory synchronously.
    /// </summary>
    private void LoadProvidersFromDirectorySynchronous(string directory, string sourceType)
    {
        if (!Directory.Exists(directory))
        {
            this.logger.LogDebug("{SourceType} providers directory not found: {Path}", sourceType, directory);
            return;
        }

        var providerFiles = Directory.GetFiles(directory, ProviderFilePattern, SearchOption.TopDirectoryOnly);

        foreach (var filePath in providerFiles)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

                if (provider == null)
                {
                    this.logger.LogWarning("Failed to deserialize provider from {Path}", filePath);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(provider.ProviderId))
                {
                    this.logger.LogWarning("Provider in {Path} has no providerId", filePath);
                    continue;
                }

                // AddOrUpdate so user providers override bundled providers
                this.providers.AddOrUpdate(provider.ProviderId, provider, (_, _) => provider);
                this.logger.LogDebug(
                    "Loaded {SourceType} provider {ProviderId} from {Path}",
                    sourceType,
                    provider.ProviderId,
                    filePath);
            }
            catch (JsonException ex)
            {
                this.logger.LogError(ex, "Failed to parse provider file {Path}", filePath);
            }
            catch (IOException ex)
            {
                this.logger.LogError(ex, "Failed to read provider file {Path}", filePath);
            }
        }
    }

    private async Task<OperationResult<bool>> LoadAllProvidersInternalAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        // Load bundled providers first
        var bundledErrors = await this.LoadProvidersFromDirectoryAsync(
            this.bundledProvidersDirectory,
            "bundled",
            cancellationToken).ConfigureAwait(false);
        errors.AddRange(bundledErrors);

        // Load user providers (override bundled if same ID)
        var userErrors = await this.LoadProvidersFromDirectoryAsync(
            this.userProvidersDirectory,
            "user",
            cancellationToken).ConfigureAwait(false);
        errors.AddRange(userErrors);

        this.logger.LogInformation(
            "Loaded {Count} providers (bundled: {BundledPath}, user: {UserPath})",
            this.providers.Count,
            this.bundledProvidersDirectory,
            this.userProvidersDirectory);

        // Return success even with some errors if we loaded at least some providers
        if (this.providers.Count > 0 || errors.Count == 0)
        {
            return OperationResult<bool>.CreateSuccess(true, stopwatch.Elapsed);
        }

        return OperationResult<bool>.CreateFailure(errors, stopwatch.Elapsed);
    }

    /// <summary>
    /// Loads providers from a specific directory asynchronously.
    /// </summary>
    private async Task<List<string>> LoadProvidersFromDirectoryAsync(
        string directory,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (!Directory.Exists(directory))
        {
            this.logger.LogDebug("{SourceType} providers directory not found: {Path}", sourceType, directory);
            return errors;
        }

        var providerFiles = Directory.GetFiles(directory, ProviderFilePattern, SearchOption.TopDirectoryOnly);

        foreach (var filePath in providerFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

                if (provider == null)
                {
                    this.logger.LogWarning("Failed to deserialize provider from {Path}", filePath);
                    errors.Add($"Failed to deserialize: {Path.GetFileName(filePath)}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(provider.ProviderId))
                {
                    this.logger.LogWarning("Provider in {Path} has no providerId", filePath);
                    errors.Add($"Missing providerId: {Path.GetFileName(filePath)}");
                    continue;
                }

                // AddOrUpdate so user providers override bundled providers
                this.providers.AddOrUpdate(provider.ProviderId, provider, (_, _) => provider);
                this.logger.LogDebug(
                    "Loaded {SourceType} provider {ProviderId} from {Path}",
                    sourceType,
                    provider.ProviderId,
                    filePath);
            }
            catch (JsonException ex)
            {
                this.logger.LogError(ex, "Failed to parse provider file {Path}", filePath);
                errors.Add($"JSON parse error in {Path.GetFileName(filePath)}: {ex.Message}");
            }
            catch (IOException ex)
            {
                this.logger.LogError(ex, "Failed to read provider file {Path}", filePath);
                errors.Add($"IO error reading {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        return errors;
    }
}
