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

            // read json file
            var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);

            BuildConfiguration config;

            // try simplified format first (sample projects)
            if (json.Contains("\"BundleItems\"", StringComparison.OrdinalIgnoreCase) ||
                json.Contains("\"BundlePacks\"", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var simplified = JsonSerializer.Deserialize<SimplifiedConfigRoot>(json, _jsonOptions);
                    if ((simplified?.BundleItems != null && simplified.BundleItems.Count > 0) ||
                        (simplified?.BundlePacks != null && simplified.BundlePacks.Count > 0))
                    {
                        logger.LogInformation("Detected simplified config format, converting...");
                        var configDir = Path.GetDirectoryName(configPath) ?? string.Empty;
                        var projectDir = configDir;
                        if (!string.IsNullOrEmpty(configDir) && Path.GetFileName(configDir).Equals(ModBuilderConstants.ConfigDir, StringComparison.OrdinalIgnoreCase))
                        {
                            projectDir = Path.GetDirectoryName(configDir) ?? configDir;
                        }

                        config = ConvertSimplifiedConfig(simplified, projectDir);
                        config.LoadedConfigFiles.Add(configPath);
                        logger.LogInformation("Loaded {ItemCount} bundle items and {PackCount} bundle packs from simplified format", config.Items.Count, config.Packs.Count);
                        return config;
                    }
                }
                catch (JsonException)
                {
                    logger.LogDebug("Failed to parse as simplified format, falling back to direct format");
                }
            }

            // try python format (with "bundles" wrapper)
            if (json.Contains("\"bundles\"", StringComparison.OrdinalIgnoreCase))
            {
                var pythonConfig = JsonSerializer.Deserialize<PythonConfigRoot>(json, _jsonOptions);
                if (pythonConfig?.Bundles != null)
                {
                    logger.LogInformation("Detected Python ModBuilder config format");
                    var configDir = Path.GetDirectoryName(configPath) ?? string.Empty;
                    var projectDir = configDir;
                    if (!string.IsNullOrEmpty(configDir) && Path.GetFileName(configDir).Equals(ModBuilderConstants.ConfigDir, StringComparison.OrdinalIgnoreCase))
                    {
                        projectDir = Path.GetDirectoryName(configDir) ?? configDir;
                    }

                    config = ConvertPythonConfig(pythonConfig.Bundles, projectDir);
                    config.LoadedConfigFiles.Add(configPath);
                    return config;
                }
            }

            // try direct c# format
            var directConfig = JsonSerializer.Deserialize<BuildConfiguration>(json, _jsonOptions);
            if (directConfig == null)
            {
                logger.LogError("Failed to deserialize configuration from: {ConfigPath}", configPath);
                throw new InvalidOperationException($"Failed to deserialize configuration from: {configPath}");
            }

            config = directConfig;

            // track loaded file
            config.LoadedConfigFiles.Add(configPath);

            logger.LogInformation("Successfully loaded configuration with {ItemCount} items and {PackCount} packs",
                config.Items.Count, config.Packs.Count);

            return config;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON parsing error in configuration file: {ConfigPath}", configPath);
            throw new InvalidOperationException($"Invalid JSON in configuration file: {configPath}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load configuration: {ConfigPath}", configPath);
            throw;
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

        // load first configuration as base
        var mergedConfig = await LoadConfigurationAsync(configPaths[0], cancellationToken).ConfigureAwait(false);

        // merge remaining configurations
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
        logger.LogInformation("Resolving wildcards in configuration");

        int totalFilesResolved = 0;

        // get project directory from loaded config files
        string projectDir = string.Empty;
        if (configuration.LoadedConfigFiles.Count > 0)
        {
            var firstConfigFile = configuration.LoadedConfigFiles[0];
            projectDir = Path.GetDirectoryName(firstConfigFile) ?? string.Empty;
            if (!string.IsNullOrEmpty(projectDir))
            {
                // go up one level if config is in a subdirectory (e.g. config/)
                var parentDir = Path.GetDirectoryName(projectDir);
                if (!string.IsNullOrEmpty(parentDir) &&
                    Path.GetFileName(projectDir).Equals(ModBuilderConstants.ConfigDir, StringComparison.OrdinalIgnoreCase))
                {
                    projectDir = parentDir;
                }
            }
        }

        logger.LogInformation("Project directory for wildcard resolution: {ProjectDir}", projectDir);

        foreach (var item in configuration.Items)
        {
            var resolvedFiles = new List<BundleFile>();

            foreach (var file in item.Files)
            {
                // check if source contains wildcard patterns
                if (ContainsWildcard(file.AbsSourceFile))
                {
                    // determine base path for wildcard resolution
                    string basePath = file.AbsSourceParent;
                    string pattern = file.AbsSourceFile;

                    // if AbsSourceParent is empty, use project directory and treat AbsSourceFile as relative pattern
                    if (string.IsNullOrEmpty(basePath))
                    {
                        basePath = projectDir;
                        logger.LogDebug("Using project directory as base path: {BasePath}", basePath);
                    }
                    else if (!Path.IsPathRooted(basePath))
                    {
                        // make relative base path absolute
                        basePath = Path.Combine(projectDir, basePath);
                        logger.LogDebug("Made base path absolute: {BasePath}", basePath);
                    }

                    logger.LogDebug("Resolving wildcard pattern: {Pattern} in {Parent}", pattern, basePath);

                    var matchedFiles = await ResolveWildcardPatternAsync(
                        pattern,
                        basePath,
                        cancellationToken).ConfigureAwait(false);

                    logger.LogDebug("Resolved {Count} files from pattern: {Pattern}", matchedFiles.Count, pattern);

                    // create BundleFile entry for each matched file
                    foreach (var matchedFile in matchedFiles)
                    {
                        var resolvedFile = new BundleFile
                        {
                            AbsSourceParent = basePath,
                            AbsSourceFile = matchedFile,
                            RelTargetFile = DetermineTargetPath(matchedFile, basePath, file.RelTargetFile),
                            Params = file.Params,
                            RegistryDef = file.RegistryDef,
                        };
                        resolvedFiles.Add(resolvedFile);
                        totalFilesResolved++;
                    }
                }
                else
                {
                    // no wildcard, keep as-is
                    resolvedFiles.Add(file);
                }
            }

            // replace files list with resolved files
            item.Files = resolvedFiles;
        }

        logger.LogInformation("Resolved {Count} files from wildcard patterns", totalFilesResolved);

        return configuration;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateConfiguration(BuildConfiguration configuration)
    {
        var errors = new List<string>();

        logger.LogInformation("Validating configuration");

        // validate bundle items
        if (configuration.Items.Count == 0)
        {
            errors.Add("Configuration must contain at least one bundle item");
        }

        var itemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in configuration.Items)
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

        // validate bundle packs reference valid items
        foreach (var pack in configuration.Packs)
        {
            if (string.IsNullOrWhiteSpace(pack.Name))
            {
                errors.Add("Bundle pack has empty name");
            }

            foreach (var itemName in pack.ItemNames)
            {
                if (!itemNames.Contains(itemName))
                {
                    errors.Add($"Bundle pack '{pack.Name}' references unknown item: {itemName}");
                }
            }
        }

        // validate folder paths exist (warnings only)
        if (!string.IsNullOrEmpty(configuration.Folders.AbsBuildDir) &&
            !Directory.Exists(configuration.Folders.AbsBuildDir))
        {
            logger.LogWarning("Build directory does not exist: {Path}", configuration.Folders.AbsBuildDir);
        }

        if (!string.IsNullOrEmpty(configuration.Folders.AbsGameDir) &&
            !Directory.Exists(configuration.Folders.AbsGameDir))
        {
            logger.LogWarning("Game directory does not exist: {Path}", configuration.Folders.AbsGameDir);
        }

        // validate tools
        foreach (var tool in configuration.Tools)
        {
            if (!string.IsNullOrEmpty(tool.Value.AbsExe) && !File.Exists(tool.Value.AbsExe))
            {
                logger.LogWarning("Tool executable not found: {Tool} at {Path}", tool.Key, tool.Value.AbsExe);
            }
        }

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
        logger.LogInformation("Loading default configuration");

        // return minimal default configuration
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
            // merge items (append)
            Items = new List<BundleItem>(baseConfig.Items),

            // merge packs (append)
            Packs = new List<BundlePack>(baseConfig.Packs),

            // override folders
            Folders = new FolderConfiguration
            {
                AbsBuildDir = string.IsNullOrEmpty(overrideConfig.Folders.AbsBuildDir)
                    ? baseConfig.Folders.AbsBuildDir
                    : overrideConfig.Folders.AbsBuildDir,
                AbsReleaseDir = string.IsNullOrEmpty(overrideConfig.Folders.AbsReleaseDir)
                    ? baseConfig.Folders.AbsReleaseDir
                    : overrideConfig.Folders.AbsReleaseDir,
                AbsGameDir = string.IsNullOrEmpty(overrideConfig.Folders.AbsGameDir)
                    ? baseConfig.Folders.AbsGameDir
                    : overrideConfig.Folders.AbsGameDir
            },

            // override runner
            Runner = new RunnerConfiguration
            {
                AbsExe = string.IsNullOrEmpty(overrideConfig.Runner.AbsExe)
                    ? baseConfig.Runner.AbsExe
                    : overrideConfig.Runner.AbsExe,
                Args = string.IsNullOrEmpty(overrideConfig.Runner.Args)
                    ? baseConfig.Runner.Args
                    : overrideConfig.Runner.Args,
                WorkingDir = string.IsNullOrEmpty(overrideConfig.Runner.WorkingDir)
                    ? baseConfig.Runner.WorkingDir
                    : overrideConfig.Runner.WorkingDir,
                ModFolder = string.IsNullOrEmpty(overrideConfig.Runner.ModFolder)
                    ? baseConfig.Runner.ModFolder
                    : overrideConfig.Runner.ModFolder,
            },

            // merge tools (override by key)
            Tools = new Dictionary<string, ToolConfiguration>(baseConfig.Tools),

            // merge loaded config files
            LoadedConfigFiles = new List<string>(baseConfig.LoadedConfigFiles)
        };

        // add override items (check for duplicates by name)
        var existingItemNames = new HashSet<string>(merged.Items.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrideConfig.Items)
        {
            if (!existingItemNames.Contains(item.Name))
            {
                merged.Items.Add(item);
            }
            else
            {
                logger.LogWarning("Skipping duplicate item during merge: {ItemName}", item.Name);
            }
        }

        // add override packs (check for duplicates by name)
        var existingPackNames = new HashSet<string>(merged.Packs.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var pack in overrideConfig.Packs)
        {
            if (!existingPackNames.Contains(pack.Name))
            {
                merged.Packs.Add(pack);
            }
            else
            {
                logger.LogWarning("Skipping duplicate pack during merge: {PackName}", pack.Name);
            }
        }

        // merge tools (override existing)
        foreach (var tool in overrideConfig.Tools)
        {
            merged.Tools[tool.Key] = tool.Value;
        }

        // merge loaded config files
        merged.LoadedConfigFiles.AddRange(overrideConfig.LoadedConfigFiles);

        return merged;
    }

    /// <inheritdoc />
    public void NormalizePaths(BuildConfiguration configuration)
    {
        logger.LogDebug("Normalizing paths in configuration");

        // normalize folder paths
        configuration.Folders.AbsBuildDir = NormalizePath(configuration.Folders.AbsBuildDir);
        configuration.Folders.AbsReleaseDir = NormalizePath(configuration.Folders.AbsReleaseDir);
        configuration.Folders.AbsGameDir = NormalizePath(configuration.Folders.AbsGameDir);

        // normalize runner paths
        configuration.Runner.AbsExe = NormalizePath(configuration.Runner.AbsExe);
        configuration.Runner.WorkingDir = NormalizePath(configuration.Runner.WorkingDir);
        configuration.Runner.ModFolder = NormalizePath(configuration.Runner.ModFolder);

        // normalize tool paths
        foreach (var tool in configuration.Tools.Values)
        {
            tool.AbsExe = NormalizePath(tool.AbsExe);
        }

        // normalize bundle file paths
        foreach (var item in configuration.Items)
        {
            foreach (var file in item.Files)
            {
                file.AbsSourceParent = NormalizePath(file.AbsSourceParent);
                file.AbsSourceFile = NormalizePath(file.AbsSourceFile);
                file.RelTargetFile = NormalizePath(file.RelTargetFile);
            }
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

        var configFiles = new List<string>();

        // 1. Check ModJsonFiles.json master list
        var modJsonFilesPath = Path.Combine(projectDir, "ModJsonFiles.json");
        if (!File.Exists(modJsonFilesPath))
        {
            modJsonFilesPath = Path.Combine(projectDir, ModBuilderConstants.ConfigDir, "ModJsonFiles.json");
        }

        if (File.Exists(modJsonFilesPath))
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(modJsonFilesPath, cancellationToken).ConfigureAwait(false);
                var masterList = JsonSerializer.Deserialize<PythonModJsonFilesConfig>(jsonContent, _jsonOptions);
                if (masterList?.Build?.Files != null)
                {
                    foreach (var file in masterList.Build.Files)
                    {
                        var resolvedPath = Path.IsPathRooted(file) ? file : Path.Combine(projectDir, file);
                        if (File.Exists(resolvedPath))
                        {
                            configFiles.Add(resolvedPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse ModJsonFiles.json at {Path}", modJsonFilesPath);
            }
        }

        // 2. Direct folder inspection
        if (configFiles.Count == 0)
        {
            var configDir = Path.Combine(projectDir, ModBuilderConstants.ConfigDir);
            if (!Directory.Exists(configDir))
            {
                configDir = Path.Combine(projectDir, "Configs");
            }

            if (Directory.Exists(configDir))
            {
                var bundleItemsPath = Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);
                var bundlePacksPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);

                if (File.Exists(bundleItemsPath))
                {
                    configFiles.Add(bundleItemsPath);
                }

                if (File.Exists(bundlePacksPath))
                {
                    configFiles.Add(bundlePacksPath);
                }

                if (configFiles.Count == 0)
                {
                    var legacyBundlesPath = Path.Combine(configDir, "bundles.json");
                    if (File.Exists(legacyBundlesPath))
                    {
                        configFiles.Add(legacyBundlesPath);
                    }
                }
            }
        }

        // 3. Fallback recursive discovery
        if (configFiles.Count == 0)
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

        if (configFiles.Count == 0)
        {
            return null;
        }

        var config = await LoadAndMergeConfigurationsAsync(configFiles, cancellationToken).ConfigureAwait(false);

        // 4. Check ModFolders.json override
        var modFoldersPath = Path.Combine(projectDir, "ModFolders.json");
        if (!File.Exists(modFoldersPath))
        {
            modFoldersPath = Path.Combine(projectDir, ModBuilderConstants.ConfigDir, "ModFolders.json");
        }

        if (File.Exists(modFoldersPath))
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(modFoldersPath, cancellationToken).ConfigureAwait(false);
                var foldersConfig = JsonSerializer.Deserialize<PythonModFoldersConfig>(jsonContent, _jsonOptions);
                if (foldersConfig?.Folders != null)
                {
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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse ModFolders.json at {Path}", modFoldersPath);
            }
        }

        config = await ResolveWildcardsAsync(config, cancellationToken).ConfigureAwait(false);
        NormalizePaths(config);
        return config;
    }

    /// <summary>
    /// Checks if a path contains wildcard characters.
    /// </summary>
    private static bool ContainsWildcard(string path)
    {
        return path.Contains('*') || path.Contains('?');
    }

    /// <summary>
    /// Resolves a wildcard pattern to a list of matching file paths.
    /// </summary>
    private async Task<List<string>> ResolveWildcardPatternAsync(
        string pattern,
        string basePath,
        CancellationToken cancellationToken)
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

            var directoryInfo = new DirectoryInfo(basePath);
            var result = matcher.Execute(new DirectoryInfoWrapper(directoryInfo));

            foreach (var file in result.Files)
            {
                var absolutePath = Path.Combine(basePath, file.Path);
                matchedFiles.Add(absolutePath);
            }

            return await Task.FromResult(matchedFiles).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving wildcard pattern: {Pattern} in {BasePath}", pattern, basePath);
            return matchedFiles;
        }
    }

    /// <summary>
    /// Determines the target path for a resolved file.
    /// </summary>
    private static string DetermineTargetPath(string sourceFile, string sourceParent, string targetTemplate)
    {
        var relativePath = Path.GetRelativePath(sourceParent, sourceFile);

        if (!string.IsNullOrEmpty(targetTemplate) && ContainsWildcard(targetTemplate))
        {
            var targetNormalized = targetTemplate.Replace('\\', '/');
            var relativeNormalized = relativePath.Replace('\\', '/');

            if (targetNormalized.Contains("**"))
            {
                return relativeNormalized;
            }

            if (targetNormalized.Contains("*"))
            {
                var targetFileName = Path.GetFileName(targetNormalized);

                if (targetFileName.Contains("*"))
                {
                    var sourceExt = Path.GetExtension(sourceFile);
                    var targetExt = Path.GetExtension(targetNormalized);

                    if (!string.IsNullOrEmpty(targetExt) && targetExt != ".*" && targetExt != sourceExt)
                    {
                        var sourceNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);
                        var relativeDir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty;

                        if (!string.IsNullOrEmpty(relativeDir))
                        {
                            return $"{relativeDir}/{sourceNameWithoutExt}{targetExt}";
                        }

                        return $"{sourceNameWithoutExt}{targetExt}";
                    }
                }

                return relativeNormalized;
            }
        }

        if (!string.IsNullOrEmpty(targetTemplate))
        {
            return targetTemplate;
        }

        return relativePath;
    }

    /// <summary>
    /// Normalizes a path to use forward slashes and removes redundant separators.
    /// </summary>
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

    /// <summary>
    /// Converts Python ModBuilder config format to C# BuildConfiguration.
    /// </summary>
    private BuildConfiguration ConvertPythonConfig(PythonBundlesConfig pythonConfig, string projectDir)
    {
        logger.LogInformation("Converting Python config format to C# format");

        var config = new BuildConfiguration();

        if (pythonConfig.Items != null)
        {
            foreach (var pythonItem in pythonConfig.Items)
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

                        if (fileGroup.SourceTargetList != null)
                        {
                            foreach (var pair in fileGroup.SourceTargetList)
                            {
                                var bundleFile = new BundleFile
                                {
                                    AbsSourceParent = sourceParent,
                                    AbsSourceFile = pair.Source,
                                    RelTargetFile = pair.Target,
                                    Params = fileGroup.Params,
                                    ExcludeMarkersList = fileGroup.ExcludeMarkersList,
                                };

                                if (fileGroup.RegistryList != null && fileGroup.RegistryList.Count > 0)
                                {
                                    var registryPaths = fileGroup.RegistryList.Select(r =>
                                        Path.IsPathRooted(r) ? r : Path.Combine(projectDir, r)).ToList();
                                    bundleFile.RegistryDef = new BundleRegistryDefinition(registryPaths);
                                }

                                item.Files.Add(bundleFile);
                            }
                        }

                        if (fileGroup.SourceList != null)
                        {
                            foreach (var source in fileGroup.SourceList)
                            {
                                var bundleFile = new BundleFile
                                {
                                    AbsSourceParent = sourceParent,
                                    AbsSourceFile = source,
                                    RelTargetFile = source,
                                    Params = fileGroup.Params,
                                    ExcludeMarkersList = fileGroup.ExcludeMarkersList,
                                };

                                if (fileGroup.RegistryList != null && fileGroup.RegistryList.Count > 0)
                                {
                                    var registryPaths = fileGroup.RegistryList.Select(r =>
                                        Path.IsPathRooted(r) ? r : Path.Combine(projectDir, r)).ToList();
                                    bundleFile.RegistryDef = new BundleRegistryDefinition(registryPaths);
                                }

                                item.Files.Add(bundleFile);
                            }
                        }

                        if (!string.IsNullOrEmpty(fileGroup.Source) && !string.IsNullOrEmpty(fileGroup.Target))
                        {
                            var bundleFile = new BundleFile
                            {
                                AbsSourceParent = sourceParent,
                                AbsSourceFile = fileGroup.Source,
                                RelTargetFile = fileGroup.Target,
                                Params = fileGroup.Params,
                                ExcludeMarkersList = fileGroup.ExcludeMarkersList,
                            };

                            if (fileGroup.RegistryList != null && fileGroup.RegistryList.Count > 0)
                            {
                                var registryPaths = fileGroup.RegistryList.Select(r =>
                                    Path.IsPathRooted(r) ? r : Path.Combine(projectDir, r)).ToList();
                                bundleFile.RegistryDef = new BundleRegistryDefinition(registryPaths);
                            }

                            item.Files.Add(bundleFile);
                        }
                    }
                }

                if (pythonItem.OnPreBuild != null)
                {
                    var scriptPath = Path.IsPathRooted(pythonItem.OnPreBuild.Script)
                        ? pythonItem.OnPreBuild.Script
                        : Path.Combine(projectDir, pythonItem.OnPreBuild.Script);
                    item.Events[BundleEventType.OnPreBuild] = new BundleEvent
                    {
                        Type = BundleEventType.OnPreBuild,
                        AbsScript = scriptPath,
                        FuncName = "OnEvent"
                    };
                }

                if (pythonItem.OnBuild != null)
                {
                    var scriptPath = Path.IsPathRooted(pythonItem.OnBuild.Script)
                        ? pythonItem.OnBuild.Script
                        : Path.Combine(projectDir, pythonItem.OnBuild.Script);
                    item.Events[BundleEventType.OnBuild] = new BundleEvent
                    {
                        Type = BundleEventType.OnBuild,
                        AbsScript = scriptPath,
                        FuncName = "OnEvent"
                    };
                }

                if (pythonItem.OnPostBuild != null)
                {
                    var scriptPath = Path.IsPathRooted(pythonItem.OnPostBuild.Script)
                        ? pythonItem.OnPostBuild.Script
                        : Path.Combine(projectDir, pythonItem.OnPostBuild.Script);
                    item.Events[BundleEventType.OnPostBuild] = new BundleEvent
                    {
                        Type = BundleEventType.OnPostBuild,
                        AbsScript = scriptPath,
                        FuncName = "OnEvent"
                    };
                }

                config.Items.Add(item);
                logger.LogDebug("Converted item '{Name}' with {FileCount} files", item.Name, item.Files.Count);
            }
        }

        if (pythonConfig.Packs != null)
        {
            foreach (var pythonPack in pythonConfig.Packs)
            {
                var pack = new BundlePack
                {
                    Name = pythonPack.Name,
                    NamePrefix = string.IsNullOrEmpty(pythonPack.NamePrefix) ? pythonConfig.PacksPrefix : pythonPack.NamePrefix,
                    NameSuffix = string.IsNullOrEmpty(pythonPack.NameSuffix) ? pythonConfig.PacksSuffix : pythonPack.NameSuffix,
                    AllowBuild = pythonPack.AllowBuild,
                    AllowInstall = pythonPack.AllowInstall,
                    SetGameLanguageOnInstall = pythonPack.SetGameLanguageOnInstall,
                    ItemNames = pythonPack.ItemNames ?? new List<string>(),
                };

                config.Packs.Add(pack);
            }
        }

        return config;
    }

    /// <summary>
    /// Converts simplified config format to C# BuildConfiguration.
    /// </summary>
    private BuildConfiguration ConvertSimplifiedConfig(SimplifiedConfigRoot simplifiedConfig, string projectDir)
    {
        logger.LogInformation("Converting simplified config format to C# format");

        var config = new BuildConfiguration();

        if (simplifiedConfig.BundleItems != null)
        {
            foreach (var simpItem in simplifiedConfig.BundleItems)
            {
                if (string.IsNullOrWhiteSpace(simpItem.Name))
                {
                    continue;
                }

                var item = new BundleItem
                {
                    Name = simpItem.Name,
                    IsBig = true,
                };

                if (simpItem.SourceFiles != null)
                {
                    foreach (var pattern in simpItem.SourceFiles)
                    {
                        var bundleFile = new BundleFile
                        {
                            AbsSourceParent = projectDir,
                            AbsSourceFile = pattern,
                            RelTargetFile = string.Empty,
                        };

                        item.Files.Add(bundleFile);
                    }
                }

                config.Items.Add(item);
            }
        }

        var packsList = simplifiedConfig.BundlePacks;
        if (packsList != null)
        {
            foreach (var simpPack in packsList)
            {
                if (string.IsNullOrWhiteSpace(simpPack.Name))
                {
                    continue;
                }

                var pack = new BundlePack
                {
                    Name = simpPack.Name,
                    ItemNames = simpPack.ItemNames ?? simpPack.Items ?? new List<string>(),
                    AllowBuild = simpPack.AllowBuild ?? true,
                    AllowInstall = simpPack.AllowInstall ?? true,
                };

                config.Packs.Add(pack);
            }
        }

        return config;
    }
}
