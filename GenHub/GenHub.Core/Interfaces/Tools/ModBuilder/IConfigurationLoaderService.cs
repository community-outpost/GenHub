using GenHub.Core.Models.Tools.ModBuilder;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for loading and managing ModBuilder configuration files.
/// </summary>
public interface IConfigurationLoaderService
{
    /// <summary>
    /// Loads a single configuration file from the specified path.
    /// </summary>
    /// <param name="configPath">The absolute path to the configuration JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded build configuration.</returns>
    Task<BuildConfiguration> LoadConfigurationAsync(string configPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads and merges multiple configuration files.
    /// Later configurations override earlier ones.
    /// </summary>
    /// <param name="configPaths">The read-only list of configuration file paths to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merged build configuration.</returns>
    Task<BuildConfiguration> LoadAndMergeConfigurationsAsync(IReadOnlyList<string> configPaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves wildcard patterns in bundle file paths.
    /// </summary>
    /// <param name="configuration">The configuration containing wildcard patterns.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The configuration with resolved file paths.</returns>
    Task<BuildConfiguration> ResolveWildcardsAsync(BuildConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the configuration for correctness and completeness.
    /// </summary>
    /// <param name="configuration">The configuration to validate.</param>
    /// <returns>A list of validation errors, or empty if valid.</returns>
    IReadOnlyList<string> ValidateConfiguration(BuildConfiguration configuration);

    /// <summary>
    /// Loads the default embedded configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The default build configuration.</returns>
    Task<BuildConfiguration> LoadDefaultConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges two configurations, with the second overriding the first.
    /// </summary>
    /// <param name="baseConfig">The base configuration.</param>
    /// <param name="overrideConfig">The configuration to merge on top.</param>
    /// <returns>The merged configuration.</returns>
    BuildConfiguration MergeConfigurations(BuildConfiguration baseConfig, BuildConfiguration overrideConfig);

    /// <summary>
    /// Normalizes all paths in the configuration to use consistent separators.
    /// </summary>
    /// <param name="configuration">The configuration to normalize.</param>
    void NormalizePaths(BuildConfiguration configuration);

    /// <summary>
    /// Auto-discovers and loads configuration from standard project locations.
    /// </summary>
    /// <param name="projectPath">The path to the project file (.mbproj).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded build configuration, or null if no config found.</returns>
    Task<BuildConfiguration?> LoadProjectConfigurationAsync(string projectPath, CancellationToken cancellationToken = default);
}
