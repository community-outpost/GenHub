using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for loading and managing ModBuilder configuration files.
/// Supports JSON configuration loading, wildcard resolution, and configuration merging.
/// </summary>
public class ConfigurationLoaderService(ILogger<ConfigurationLoaderService> logger) : IConfigurationLoaderService
{
    private const string ConfigDirLower = "config";
    private const string ConfigsDirLower = "configs";
    private const string ModFoldersFileName = "ModFolders.json";
    private const string ModJsonFilesFileName = "ModJsonFiles.json";
    private const string BundlesConfigFileName = "bundles.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <inheritdoc />
    public async Task<BuildConfiguration> LoadConfigurationAsync(string configPath, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Loading configuration from: {ConfigPath}", configPath);

            if (!File.Exists(configPath))
            {
                logger.LogError("Configuration file not found: {ConfigPath}", configPath);
                throw new FileNotFoundException($"Configuration file not found: {configPath}");
            }

            var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);

            if (TryLoadSimplifiedConfig(json, configPath, out var simplifiedConfig) && simplifiedConfig != null)
            {
                return simplifiedConfig;
            }

            if (TryLoadPythonConfig(json, configPath, out var pythonConfig) && pythonConfig != null)
            {
                return pythonConfig;
            }

            return LoadDirectConfig(json, configPath);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON parsing error in configuration file: {ConfigPath}", configPath);
            throw new InvalidOperationException($"Invalid JSON in configuration file: {configPath}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not FileNotFoundException)
        {
            throw new InvalidOperationException($"Failed to load configuration: {configPath}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<BuildConfiguration> LoadAndMergeConfigurationsAsync(IReadOnlyList<string> configPaths, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Loading and merging {Count} configuration files", configPaths.Count);

        if (configPaths.Count == 0)
        {
            logger.LogWarning("No configuration files provided, returning empty configuration");
            return new BuildConfiguration();
        }

        var mergedConfig = await LoadConfigurationAsync(configPaths[0], cancellationToken).ConfigureAwait(false);

        for (int i = 1; i < configPaths.Count; i++)
        {
            var config = await LoadConfigurationAsync(configPaths[i], cancellationToken).ConfigureAwait(false);
            mergedConfig = MergeConfigurations(mergedConfig, config);
        }

        logger.LogInformation("Successfully merged configurations with {ItemCount} items and {PackCount} packs",
            mergedConfig.Items.Count, mergedConfig.Packs.Count);

        return mergedConfig;
    }

    /// <inheritdoc />
    public async Task<BuildConfiguration> ResolveWildcardsAsync(BuildConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectDir = ResolveProjectDirForWildcards(configuration);
        logger.LogInformation("Resolving wildcards in configuration (ProjectDir: {ProjectDir})", projectDir);

        int totalFilesResolved = 0;
        foreach (var item in configuration.Items)
        {
            totalFilesResolved += await ResolveItemFilesAsync(item, projectDir, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Resolved {Count} files from wildcard patterns", totalFilesResolved);
        return configuration;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateConfiguration(BuildConfiguration configuration)
    {
        var errors = new List<string>();
        logger.LogInformation("Validating configuration");

        if (configuration.Items.Count == 0)
        {
            errors.Add("Configuration must contain at least one bundle item");
        }

        var itemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateBundleItems(configuration.Items, itemNames, errors);
        ValidateBundlePacks(configuration.Packs, itemNames, errors);
        ValidateDirectoriesAndTools(configuration);

        if (errors.Count > 0)
        {
            logger.LogError("Configuration validation failed with {Count} errors", errors.Count);
        }
        else
        {
            logger.LogInformation("Configuration validation passed");
        }

        return errors;
    }

    /// <inheritdoc />
    public async Task<BuildConfiguration> LoadDefaultConfigurationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Loading default configuration");

        var config = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = Path.Combine(Directory.GetCurrentDirectory(), ModBuilderConstants.DefaultBuildDir),
                AbsReleaseDir = Path.Combine(Directory.GetCurrentDirectory(), ModBuilderConstants.DefaultReleaseDir),
            }
        };

        logger.LogInformation("Default configuration created");
        return await Task.FromResult(config).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public BuildConfiguration MergeConfigurations(BuildConfiguration baseConfig, BuildConfiguration overrideConfig)
    {
        logger.LogDebug("Merging configurations");

        var merged = new BuildConfiguration
        {
            Items = new List<BundleItem>(baseConfig.Items),
            Packs = new List<BundlePack>(baseConfig.Packs),
            Folders = MergeFolderConfig(baseConfig.Folders, overrideConfig.Folders),
            Runner = MergeRunnerConfig(baseConfig.Runner, overrideConfig.Runner),
            Tools = new Dictionary<string, ToolConfiguration>(baseConfig.Tools),
            LoadedConfigFiles = new List<string>(baseConfig.LoadedConfigFiles)
        };

        MergeItems(merged, overrideConfig.Items);
        MergePacks(merged, overrideConfig.Packs);

        foreach (var tool in overrideConfig.Tools)
        {
            merged.Tools[tool.Key] = tool.Value;
        }

        merged.LoadedConfigFiles.AddRange(overrideConfig.LoadedConfigFiles);
        return merged;
    }

    /// <inheritdoc />
    public void NormalizePaths(BuildConfiguration configuration)
    {
        logger.LogDebug("Normalizing paths in configuration");

        configuration.Folders.AbsBuildDir = NormalizePath(configuration.Folders.AbsBuildDir);
        configuration.Folders.AbsReleaseDir = NormalizePath(configuration.Folders.AbsReleaseDir);
        configuration.Folders.AbsGameDir = NormalizePath(configuration.Folders.AbsGameDir);

        configuration.Runner.AbsExe = NormalizePath(configuration.Runner.AbsExe);
        configuration.Runner.WorkingDir = NormalizePath(configuration.Runner.WorkingDir);
        configuration.Runner.ModFolder = NormalizePath(configuration.Runner.ModFolder);

        foreach (var tool in configuration.Tools.Values)
        {
            tool.AbsExe = NormalizePath(tool.AbsExe);
        }

        foreach (var file in configuration.Items.SelectMany(item => item.Files))
        {
            file.AbsSourceParent = NormalizePath(file.AbsSourceParent);
            file.AbsSourceFile = NormalizePath(file.AbsSourceFile);
            file.RelTargetFile = NormalizePath(file.RelTargetFile);
        }

        logger.LogDebug("Path normalization complete");
    }

    /// <inheritdoc />
    public async Task<BuildConfiguration?> LoadProjectConfigurationAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var projectDir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
        {
            return null;
        }

        var configFiles = await DiscoverProjectConfigFilesAsync(projectDir, cancellationToken).ConfigureAwait(false);
        if (configFiles.Count == 0)
        {
            return null;
        }

        var config = await LoadAndMergeConfigurationsAsync(configFiles, cancellationToken).ConfigureAwait(false);
        await ApplyModFoldersOverrideAsync(config, projectDir, cancellationToken).ConfigureAwait(false);

        config = await ResolveWildcardsAsync(config, cancellationToken).ConfigureAwait(false);
        NormalizePaths(config);
        return config;
    }

    private static string ResolveProjectDirFromConfig(string configPath)
    {
        var configDir = Path.GetDirectoryName(configPath) ?? string.Empty;
        var folderName = Path.GetFileName(configDir);
        if (!string.IsNullOrEmpty(configDir) && IsConfigDirectoryName(folderName))
        {
            return Path.GetDirectoryName(configDir) ?? configDir;
        }

        return configDir;
    }

    private static bool IsConfigDirectoryName(string folderName)
    {
        return folderName.Equals(ModBuilderConstants.ConfigDir, StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals(ConfigDirLower, StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals(ConfigsDirLower, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryLoadSimplifiedConfig(string json, string configPath, out BuildConfiguration? config)
    {
        config = null;
        if (!json.Contains("\"BundleItems\"", StringComparison.OrdinalIgnoreCase) &&
            !json.Contains("\"BundlePacks\"", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var simplified = JsonSerializer.Deserialize<SimplifiedConfigRoot>(json, _jsonOptions);
            if ((simplified?.BundleItems != null && simplified.BundleItems.Count > 0) ||
                (simplified?.BundlePacks != null && simplified.BundlePacks.Count > 0))
            {
                logger.LogInformation("Detected simplified config format, converting...");
                var projectDir = ResolveProjectDirFromConfig(configPath);
                config = ConvertSimplifiedConfig(simplified, projectDir);
                config.LoadedConfigFiles.Add(configPath);
                logger.LogInformation("Loaded {ItemCount} bundle items and {PackCount} bundle packs from simplified format",
                    config.Items.Count, config.Packs.Count);
                return true;
            }
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse as simplified format, falling back to direct format");
        }

        return false;
    }

    private bool TryLoadPythonConfig(string json, string configPath, out BuildConfiguration? config)
    {
        config = null;
        if (!json.Contains("\"bundles\"", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var pythonConfig = JsonSerializer.Deserialize<PythonConfigRoot>(json, _jsonOptions);
            if (pythonConfig?.Bundles != null)
            {
                logger.LogInformation("Detected Python ModBuilder config format");
                var projectDir = ResolveProjectDirFromConfig(configPath);
                config = ConvertPythonConfig(pythonConfig.Bundles, projectDir);
                config.LoadedConfigFiles.Add(configPath);
                return true;
            }
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse as Python format, falling back to direct format");
        }

        return false;
    }

    private BuildConfiguration LoadDirectConfig(string json, string configPath)
    {
        var directConfig = JsonSerializer.Deserialize<BuildConfiguration>(json, _jsonOptions);
        if (directConfig == null)
        {
            logger.LogError("Failed to deserialize configuration from: {ConfigPath}", configPath);
            throw new InvalidOperationException($"Failed to deserialize configuration from: {configPath}");
        }

        directConfig.LoadedConfigFiles.Add(configPath);
        logger.LogInformation("Successfully loaded configuration with {ItemCount} items and {PackCount} packs",
            directConfig.Items.Count, directConfig.Packs.Count);
        return directConfig;
    }

    private static string ResolveProjectDirForWildcards(BuildConfiguration configuration)
    {
        if (configuration.LoadedConfigFiles.Count > 0)
        {
            var firstConfigFile = configuration.LoadedConfigFiles[0];
            var resolved = ResolveProjectDirFromConfig(firstConfigFile);
            if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
            {
                return resolved;
            }
        }

        if (!string.IsNullOrEmpty(configuration.Folders.AbsBuildDir))
        {
            var buildParent = Path.GetDirectoryName(configuration.Folders.AbsBuildDir);
            if (!string.IsNullOrEmpty(buildParent) && Directory.Exists(buildParent))
            {
                return buildParent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private async Task<int> ResolveItemFilesAsync(BundleItem item, string projectDir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedFiles = new List<BundleFile>();
        int filesResolved = 0;

        foreach (var file in item.Files)
        {
            if (ContainsWildcard(file.AbsSourceFile))
            {
                var basePath = DetermineBasePath(file.AbsSourceParent, projectDir);
                var pattern = file.AbsSourceFile;

                logger.LogDebug("Resolving wildcard pattern: {Pattern} in {Parent}", pattern, basePath);
                var matchedFiles = await ResolveWildcardPatternAsync(pattern, basePath).ConfigureAwait(false);

                foreach (var matchedFile in matchedFiles)
                {
                    resolvedFiles.Add(new BundleFile
                    {
                        AbsSourceFile = matchedFile,
                        RelTargetFile = DetermineTargetPath(matchedFile, basePath, file.RelTargetFile),
                        AbsSourceParent = basePath,
                        Params = file.Params != null ? new Dictionary<string, object>(file.Params) : null,
                        RegistryDef = file.RegistryDef,
                    });
                    filesResolved++;
                }
            }
            else
            {
                resolvedFiles.Add(file);
            }
        }

        item.Files = resolvedFiles;
        return filesResolved;
    }

    private static string DetermineBasePath(string sourceParent, string projectDir)
    {
        if (string.IsNullOrEmpty(sourceParent))
        {
            return projectDir;
        }

        return Path.IsPathRooted(sourceParent) ? sourceParent : Path.Combine(projectDir, sourceParent);
    }

    private static void ValidateBundleItems(IEnumerable<BundleItem> items, HashSet<string> itemNames, List<string> errors)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add("Bundle item has empty name");
            }
            else if (!itemNames.Add(item.Name))
            {
                errors.Add($"Duplicate bundle item name: {item.Name}");
            }

            if (item.Files.Count == 0)
            {
                errors.Add($"Bundle item '{item.Name}' has no files");
            }
        }
    }

    private static void ValidateBundlePacks(IEnumerable<BundlePack> packs, HashSet<string> itemNames, List<string> errors)
    {
        foreach (var pack in packs)
        {
            if (string.IsNullOrWhiteSpace(pack.Name))
            {
                errors.Add("Bundle pack has empty name");
            }

            foreach (var itemName in pack.ItemNames.Where(itemName => !itemNames.Contains(itemName)))
            {
                errors.Add($"Bundle pack '{pack.Name}' references unknown item: {itemName}");
            }
        }
    }

    private void ValidateDirectoriesAndTools(BuildConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration.Folders.AbsBuildDir) && !Directory.Exists(configuration.Folders.AbsBuildDir))
        {
            logger.LogWarning("Build directory does not exist: {Path}", configuration.Folders.AbsBuildDir);
        }

        if (!string.IsNullOrEmpty(configuration.Folders.AbsGameDir) && !Directory.Exists(configuration.Folders.AbsGameDir))
        {
            logger.LogWarning("Game directory does not exist: {Path}", configuration.Folders.AbsGameDir);
        }

        foreach (var tool in configuration.Tools.Where(tool => !string.IsNullOrEmpty(tool.Value.AbsExe) && !File.Exists(tool.Value.AbsExe)))
        {
            logger.LogWarning("Tool executable not found: {Tool} at {Path}", tool.Key, tool.Value.AbsExe);
        }
    }

    private static FolderConfiguration MergeFolderConfig(FolderConfiguration baseFolders, FolderConfiguration overrideFolders)
    {
        return new FolderConfiguration
        {
            AbsBuildDir = string.IsNullOrEmpty(overrideFolders.AbsBuildDir) ? baseFolders.AbsBuildDir : overrideFolders.AbsBuildDir,
            AbsReleaseDir = string.IsNullOrEmpty(overrideFolders.AbsReleaseDir) ? baseFolders.AbsReleaseDir : overrideFolders.AbsReleaseDir,
            AbsGameDir = string.IsNullOrEmpty(overrideFolders.AbsGameDir) ? baseFolders.AbsGameDir : overrideFolders.AbsGameDir
        };
    }

    private static RunnerConfiguration MergeRunnerConfig(RunnerConfiguration baseRunner, RunnerConfiguration overrideRunner)
    {
        return new RunnerConfiguration
        {
            AbsExe = string.IsNullOrEmpty(overrideRunner.AbsExe) ? baseRunner.AbsExe : overrideRunner.AbsExe,
            Args = string.IsNullOrEmpty(overrideRunner.Args) ? baseRunner.Args : overrideRunner.Args,
            WorkingDir = string.IsNullOrEmpty(overrideRunner.WorkingDir) ? baseRunner.WorkingDir : overrideRunner.WorkingDir,
            ModFolder = string.IsNullOrEmpty(overrideRunner.ModFolder) ? baseRunner.ModFolder : overrideRunner.ModFolder,
        };
    }

    private void MergeItems(BuildConfiguration merged, IEnumerable<BundleItem> overrideItems)
    {
        var existingNames = new HashSet<string>(merged.Items.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrideItems)
        {
            if (existingNames.Add(item.Name))
            {
                merged.Items.Add(item);
            }
            else
            {
                logger.LogWarning("Skipping duplicate item during merge: {ItemName}", item.Name);
            }
        }
    }

    private void MergePacks(BuildConfiguration merged, IEnumerable<BundlePack> overridePacks)
    {
        var existingNames = new HashSet<string>(merged.Packs.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var pack in overridePacks)
        {
            if (existingNames.Add(pack.Name))
            {
                merged.Packs.Add(pack);
            }
            else
            {
                logger.LogWarning("Skipping duplicate pack during merge: {PackName}", pack.Name);
            }
        }
    }

    private async Task<List<string>> DiscoverProjectConfigFilesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var configFiles = await TryDiscoverFromModJsonFilesAsync(projectDir, cancellationToken).ConfigureAwait(false);
        if (configFiles.Count > 0)
        {
            return configFiles;
        }

        DiscoverFromCandidateDirs(projectDir, configFiles);
        if (configFiles.Count > 0)
        {
            return configFiles;
        }

        DiscoverFromDirectoryFallback(projectDir, configFiles);
        return configFiles;
    }

    private async Task<List<string>> TryDiscoverFromModJsonFilesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var modJsonFilesPath = Path.Combine(projectDir, ModJsonFilesFileName);
        if (!File.Exists(modJsonFilesPath))
        {
            modJsonFilesPath = Path.Combine(projectDir, ModBuilderConstants.ConfigDir, ModJsonFilesFileName);
        }

        if (!File.Exists(modJsonFilesPath))
        {
            return result;
        }

        try
        {
            var jsonContent = await File.ReadAllTextAsync(modJsonFilesPath, cancellationToken).ConfigureAwait(false);
            var masterList = JsonSerializer.Deserialize<PythonModJsonFilesConfig>(jsonContent, _jsonOptions);
            if (masterList?.Build?.Files != null)
            {
                var fullProjectDir = Path.GetFullPath(projectDir);
                var prefix = fullProjectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var file in masterList.Build.Files)
                {
                    var resolved = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(projectDir, file));
                    if (resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
                    {
                        result.Add(resolved);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse ModJsonFiles.json at {Path}", modJsonFilesPath);
        }

        return result;
    }

    private static void DiscoverFromCandidateDirs(string projectDir, List<string> configFiles)
    {
        var candidateDirs = new[]
        {
            Path.Combine(projectDir, ModBuilderConstants.ConfigDir),
            Path.Combine(projectDir, ConfigsDirLower),
            Path.Combine(projectDir, ConfigDirLower),
        };

        foreach (var configDir in candidateDirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var bundleItemsPath = Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);
            var bundlePacksPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);

            if (File.Exists(bundleItemsPath) && !configFiles.Contains(bundleItemsPath, StringComparer.OrdinalIgnoreCase))
            {
                configFiles.Add(bundleItemsPath);
            }

            if (File.Exists(bundlePacksPath) && !configFiles.Contains(bundlePacksPath, StringComparer.OrdinalIgnoreCase))
            {
                configFiles.Add(bundlePacksPath);
            }

            if (configFiles.Count == 0)
            {
                var legacyBundlesPath = Path.Combine(configDir, BundlesConfigFileName);
                if (File.Exists(legacyBundlesPath))
                {
                    configFiles.Add(legacyBundlesPath);
                }
            }

            if (configFiles.Count > 0)
            {
                break;
            }
        }
    }

    private void DiscoverFromDirectoryFallback(string projectDir, List<string> configFiles)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(projectDir, "*.json", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file).ToLowerInvariant();
                if (fileName.StartsWith('.') || fileName.StartsWith('$'))
                {
                    continue;
                }

                if (fileName.Contains("bundle") && (fileName.Contains("items") || fileName.Contains("packs")))
                {
                    configFiles.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Recursive config discovery completed with non-fatal warnings");
        }
    }

    private async Task ApplyModFoldersOverrideAsync(BuildConfiguration config, string projectDir, CancellationToken cancellationToken)
    {
        var candidatePaths = new[]
        {
            Path.Combine(projectDir, ModFoldersFileName),
            Path.Combine(projectDir, ModBuilderConstants.ConfigDir, ModFoldersFileName),
            Path.Combine(projectDir, ConfigsDirLower, ModFoldersFileName),
            Path.Combine(projectDir, ConfigDirLower, ModFoldersFileName),
        };

        var modFoldersPath = candidatePaths.FirstOrDefault(File.Exists);
        if (string.IsNullOrEmpty(modFoldersPath))
        {
            return;
        }

        try
        {
            var jsonContent = await File.ReadAllTextAsync(modFoldersPath, cancellationToken).ConfigureAwait(false);
            var foldersConfig = JsonSerializer.Deserialize<PythonModFoldersConfig>(jsonContent, _jsonOptions);
            if (foldersConfig?.Folders == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(foldersConfig.Folders.BuildDir))
            {
                config.Folders.AbsBuildDir = Path.IsPathRooted(foldersConfig.Folders.BuildDir)
                    ? foldersConfig.Folders.BuildDir
                    : Path.Combine(projectDir, foldersConfig.Folders.BuildDir);
            }

            if (!string.IsNullOrEmpty(foldersConfig.Folders.ReleaseDir))
            {
                config.Folders.AbsReleaseDir = Path.IsPathRooted(foldersConfig.Folders.ReleaseDir)
                    ? foldersConfig.Folders.ReleaseDir
                    : Path.Combine(projectDir, foldersConfig.Folders.ReleaseDir);
            }

            if (!string.IsNullOrEmpty(foldersConfig.Folders.GameDir))
            {
                config.Folders.AbsGameDir = foldersConfig.Folders.GameDir;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse ModFolders.json at {Path}", modFoldersPath);
        }
    }

    private static bool ContainsWildcard(string path)
    {
        return path.Contains('*') || path.Contains('?');
    }

    private async Task<List<string>> ResolveWildcardPatternAsync(string pattern, string basePath)
    {
        var matchedFiles = new List<string>();

        try
        {
            logger.LogDebug("Resolving pattern '{Pattern}' in base path '{BasePath}'", pattern, basePath);

            if (!Directory.Exists(basePath))
            {
                logger.LogWarning("Base path does not exist: {BasePath}", basePath);
                return matchedFiles;
            }

            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            var normalizedPattern = pattern;
            if (Path.IsPathRooted(normalizedPattern) && !string.IsNullOrEmpty(basePath) && normalizedPattern.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPattern = Path.GetRelativePath(basePath, normalizedPattern);
            }

            normalizedPattern = normalizedPattern.TrimStart('/', '\\').Replace('\\', '/');
            matcher.AddInclude(normalizedPattern);

            var gameFilesDir = Path.Combine(basePath, ModBuilderConstants.GameFilesEditedDir);
            if (!normalizedPattern.StartsWith($"{ModBuilderConstants.GameFilesEditedDir}/", StringComparison.OrdinalIgnoreCase) &&
                !normalizedPattern.Equals(ModBuilderConstants.GameFilesEditedDir, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(gameFilesDir))
            {
                matcher.AddInclude($"{ModBuilderConstants.GameFilesEditedDir}/{normalizedPattern}");
            }

            var directoryInfo = new DirectoryInfo(basePath);
            var result = matcher.Execute(new DirectoryInfoWrapper(directoryInfo));

            foreach (var file in result.Files)
            {
                var absolutePath = Path.Combine(basePath, file.Path);
                if (!matchedFiles.Contains(absolutePath, StringComparer.OrdinalIgnoreCase))
                {
                    matchedFiles.Add(absolutePath);
                }
            }

            return await Task.FromResult(matchedFiles).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving wildcard pattern: {Pattern} in {BasePath}", pattern, basePath);
            return matchedFiles;
        }
    }

    private static string DetermineTargetPath(string sourceFile, string sourceParent, string targetTemplate)
    {
        var relativePath = Path.GetRelativePath(sourceParent, sourceFile);
        var normalizedRel = StripGameFilesEditedPrefix(relativePath.Replace('\\', '/'));

        if (string.IsNullOrEmpty(targetTemplate))
        {
            return normalizedRel;
        }

        var targetNormalized = StripGameFilesEditedPrefix(targetTemplate.Replace('\\', '/'));
        if (!ContainsWildcard(targetNormalized))
        {
            return targetNormalized;
        }

        if (targetNormalized.Contains("**"))
        {
            return normalizedRel;
        }

        return ResolveTargetExtension(sourceFile, normalizedRel, targetNormalized);
    }

    private static string StripGameFilesEditedPrefix(string path)
    {
        if (path.StartsWith($"{ModBuilderConstants.GameFilesEditedDir}/", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(ModBuilderConstants.GameFilesEditedDir.Length + 1);
        }

        if (path.Equals(ModBuilderConstants.GameFilesEditedDir, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return path;
    }

    private static string ResolveTargetExtension(string sourceFile, string normalizedRel, string targetNormalized)
    {
        var targetFileName = Path.GetFileName(targetNormalized);
        if (!targetFileName.Contains('*'))
        {
            return normalizedRel;
        }

        var sourceExt = Path.GetExtension(sourceFile);
        var targetExt = Path.GetExtension(targetNormalized);

        if (string.IsNullOrEmpty(targetExt) || targetExt == ".*" || targetExt == sourceExt)
        {
            return normalizedRel;
        }

        var sourceNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);
        var relativeDir = Path.GetDirectoryName(normalizedRel)?.Replace('\\', '/') ?? string.Empty;

        return string.IsNullOrEmpty(relativeDir) ? $"{sourceNameWithoutExt}{targetExt}" : $"{relativeDir}/{sourceNameWithoutExt}{targetExt}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var normalized = path.Replace('\\', '/');
        while (normalized.Contains("//"))
        {
            normalized = normalized.Replace("//", "/");
        }

        return normalized;
    }

    private BuildConfiguration ConvertPythonConfig(PythonBundlesConfig pythonConfig, string projectDir)
    {
        logger.LogInformation("Converting Python config format to C# format");
        var config = new BuildConfiguration();

        if (pythonConfig.Items != null)
        {
            foreach (var pythonItem in pythonConfig.Items)
            {
                var item = ConvertPythonItem(pythonItem, pythonConfig, projectDir);
                config.Items.Add(item);
                logger.LogDebug("Converted item '{Name}' with {FileCount} files", item.Name, item.Files.Count);
            }
        }

        if (pythonConfig.Packs != null)
        {
            foreach (var pythonPack in pythonConfig.Packs)
            {
                config.Packs.Add(new BundlePack
                {
                    Name = pythonPack.Name,
                    NamePrefix = string.IsNullOrEmpty(pythonPack.NamePrefix) ? pythonConfig.PacksPrefix : pythonPack.NamePrefix,
                    NameSuffix = string.IsNullOrEmpty(pythonPack.NameSuffix) ? pythonConfig.PacksSuffix : pythonPack.NameSuffix,
                    AllowBuild = pythonPack.AllowBuild,
                    AllowInstall = pythonPack.AllowInstall,
                    SetGameLanguageOnInstall = pythonPack.SetGameLanguageOnInstall,
                    ItemNames = pythonPack.ItemNames ?? new List<string>(),
                });
            }
        }

        return config;
    }

    private static BundleItem ConvertPythonItem(PythonBundleItem pythonItem, PythonBundlesConfig pythonConfig, string projectDir)
    {
        var item = new BundleItem
        {
            Name = pythonItem.Name,
            NamePrefix = string.IsNullOrEmpty(pythonItem.NamePrefix) ? pythonConfig.ItemsPrefix : pythonItem.NamePrefix,
            NameSuffix = string.IsNullOrEmpty(pythonItem.NameSuffix) ? pythonConfig.ItemsSuffix : pythonItem.NameSuffix,
            IsBig = pythonItem.Big,
            BigSuffix = pythonItem.BigSuffix,
            SetGameLanguageOnInstall = pythonItem.SetGameLanguageOnInstall,
        };

        if (pythonItem.Files != null)
        {
            foreach (var fileGroup in pythonItem.Files)
            {
                var sourceParent = Path.IsPathRooted(fileGroup.SourceParent)
                    ? fileGroup.SourceParent
                    : Path.Combine(projectDir, fileGroup.SourceParent);

                ProcessFileGroup(item, fileGroup, sourceParent, projectDir);
            }
        }

        AddBundleEvents(item, pythonItem, projectDir);
        return item;
    }

    private static void AddBundleFileWithRegistry(BundleItem item, BundleFile bundleFile, List<string>? registryList, string projectDir)
    {
        if (registryList is { Count: > 0 })
        {
            var registryPaths = registryList.Select(r => Path.IsPathRooted(r) ? r : Path.Combine(projectDir, r)).ToList();
            bundleFile.RegistryDef = new BundleRegistryDefinition(registryPaths);
        }

        item.Files.Add(bundleFile);
    }

    private static void ProcessFileGroup(BundleItem item, PythonBundleFileGroup fileGroup, string sourceParent, string projectDir)
    {
        if (fileGroup.SourceTargetList != null)
        {
            foreach (var pair in fileGroup.SourceTargetList)
            {
                AddBundleFileWithRegistry(item, new BundleFile
                {
                    AbsSourceParent = sourceParent,
                    AbsSourceFile = pair.Source,
                    RelTargetFile = pair.Target,
                    Params = fileGroup.Params,
                    ExcludeMarkersList = fileGroup.ExcludeMarkersList,
                }, fileGroup.RegistryList, projectDir);
            }
        }

        if (fileGroup.SourceList != null)
        {
            foreach (var source in fileGroup.SourceList)
            {
                AddBundleFileWithRegistry(item, new BundleFile
                {
                    AbsSourceParent = sourceParent,
                    AbsSourceFile = source,
                    RelTargetFile = source,
                    Params = fileGroup.Params,
                    ExcludeMarkersList = fileGroup.ExcludeMarkersList,
                }, fileGroup.RegistryList, projectDir);
            }
        }

        if (!string.IsNullOrEmpty(fileGroup.Source) && !string.IsNullOrEmpty(fileGroup.Target))
        {
            AddBundleFileWithRegistry(item, new BundleFile
            {
                AbsSourceParent = sourceParent,
                AbsSourceFile = fileGroup.Source,
                RelTargetFile = fileGroup.Target,
                Params = fileGroup.Params,
                ExcludeMarkersList = fileGroup.ExcludeMarkersList,
            }, fileGroup.RegistryList, projectDir);
        }
    }

    private static void AddBundleEvents(BundleItem item, PythonBundleItem pythonItem, string projectDir)
    {
        if (pythonItem.OnPreBuild != null)
        {
            item.Events[BundleEventType.OnPreBuild] = new BundleEvent
            {
                Type = BundleEventType.OnPreBuild,
                AbsScript = Path.IsPathRooted(pythonItem.OnPreBuild.Script) ? pythonItem.OnPreBuild.Script : Path.Combine(projectDir, pythonItem.OnPreBuild.Script),
                FuncName = "OnEvent"
            };
        }

        if (pythonItem.OnBuild != null)
        {
            item.Events[BundleEventType.OnBuild] = new BundleEvent
            {
                Type = BundleEventType.OnBuild,
                AbsScript = Path.IsPathRooted(pythonItem.OnBuild.Script) ? pythonItem.OnBuild.Script : Path.Combine(projectDir, pythonItem.OnBuild.Script),
                FuncName = "OnEvent"
            };
        }

        if (pythonItem.OnPostBuild != null)
        {
            item.Events[BundleEventType.OnPostBuild] = new BundleEvent
            {
                Type = BundleEventType.OnPostBuild,
                AbsScript = Path.IsPathRooted(pythonItem.OnPostBuild.Script) ? pythonItem.OnPostBuild.Script : Path.Combine(projectDir, pythonItem.OnPostBuild.Script),
                FuncName = "OnEvent"
            };
        }
    }

    private BuildConfiguration ConvertSimplifiedConfig(SimplifiedConfigRoot simplifiedConfig, string projectDir)
    {
        logger.LogInformation("Converting simplified config format to C# format");
        var config = new BuildConfiguration();

        if (simplifiedConfig.BundleItems != null)
        {
            foreach (var simpItem in simplifiedConfig.BundleItems.Where(i => !string.IsNullOrWhiteSpace(i.Name)))
            {
                var item = new BundleItem
                {
                    Name = simpItem.Name,
                    IsBig = true,
                };

                if (simpItem.SourceFiles != null)
                {
                    foreach (var pattern in simpItem.SourceFiles)
                    {
                        item.Files.Add(new BundleFile
                        {
                            AbsSourceParent = projectDir,
                            AbsSourceFile = pattern,
                            RelTargetFile = string.Empty,
                        });
                    }
                }

                config.Items.Add(item);
            }
        }

        if (simplifiedConfig.BundlePacks != null)
        {
            foreach (var simpPack in simplifiedConfig.BundlePacks.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            {
                config.Packs.Add(new BundlePack
                {
                    Name = simpPack.Name,
                    ItemNames = simpPack.ItemNames ?? simpPack.Items ?? new List<string>(),
                    AllowBuild = simpPack.AllowBuild ?? true,
                    AllowInstall = simpPack.AllowInstall ?? true,
                });
            }
        }

        return config;
    }
}
