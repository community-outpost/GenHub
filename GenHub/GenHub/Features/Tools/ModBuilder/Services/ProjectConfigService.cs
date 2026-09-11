using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for managing ModBuilder project configurations (.mbproj files).
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="configurationProvider">The configuration provider service.</param>
public sealed class ProjectConfigService(
    ILogger<ProjectConfigService> logger,
    IConfigurationProviderService? configurationProvider = null) : IProjectConfigService
{
    private const string ProjectPathEmptyError = "Project path cannot be empty";

    private readonly string _recentProjectsPath = Path.Combine(
        configurationProvider?.GetApplicationDataPath()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".genhub"),
        ModBuilderConstants.ModBuilderDirName,
        ModBuilderConstants.RecentProjectsFileName);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public async Task<ProjectOperationResult<ModBuilderProject>> CreateProjectAsync(
        string projectPath,
        string projectName,
        string? gameInstallationId = null,
        ProjectTemplate? template = null,
        ContentType contentType = ContentType.Mod,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    ProjectPathEmptyError,
                    sw.Elapsed);
            }

            if (string.IsNullOrWhiteSpace(projectName))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Project name cannot be empty",
                    sw.Elapsed);
            }

            // Ensure the path has the correct extension
            if (!projectPath.EndsWith(ModBuilderConstants.ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = Path.ChangeExtension(projectPath, ModBuilderConstants.ProjectFileExtension);
            }

            // Check if project already exists
            if (File.Exists(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    $"Project file already exists at: {projectPath}",
                    sw.Elapsed);
            }

            // Use template or default
            template ??= ProjectTemplate.Empty;

            // Create project object
            var project = new ModBuilderProject
            {
                Name = projectName,
                Description = template.Description ?? string.Empty,
                GameInstallationId = gameInstallationId,
                ContentType = contentType,
                Directories = new ProjectDirectories(),
                BundleConfigs = new List<string>(template.DefaultBundleConfigs),
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
            };

            // Create project directory structure
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Invalid project path",
                    sw.Elapsed);
            }

            var createDirResult = await CreateProjectDirectoryStructureAsync(
                projectDir,
                project.Directories,
                cancellationToken)
                .ConfigureAwait(false);

            if (!createDirResult.Success)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    createDirResult.Errors,
                    sw.Elapsed);
            }

            // Save project file
            var saveResult = await SaveProjectAsync(projectPath, project, cancellationToken).ConfigureAwait(false);
            if (!saveResult.Success)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    saveResult.Errors,
                    sw.Elapsed);
            }

            // Create sample files if requested
            if (template.CreateSampleFiles)
            {
                await CreateSampleFilesAsync(projectDir, project.Directories, template, cancellationToken).ConfigureAwait(false);
            }

            // Add to recent projects
            await AddToRecentProjectsAsync(projectPath, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Created ModBuilder project '{ProjectName}' at {ProjectPath}",
                projectName,
                projectPath);

            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateSuccess(project, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create project at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                $"Failed to create project: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<ModBuilderProject>> LoadProjectAsync(
        string projectPath,
        bool validateIntegrity = true,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    ProjectPathEmptyError,
                    sw.Elapsed);
            }

            if (!File.Exists(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    $"Project file not found at: {projectPath}",
                    sw.Elapsed);
            }

            // Read and deserialize project file
            await using var stream = new FileStream(
                projectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var project = await JsonSerializer.DeserializeAsync<ModBuilderProject>(
                stream,
                _jsonOptions,
                cancellationToken)
                .ConfigureAwait(false);

            if (project == null)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Failed to deserialize project file",
                    sw.Elapsed);
            }

            // Set project directory
            project.ProjectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;

            // Validate integrity if requested
            if (validateIntegrity)
            {
                var validationResult = await ValidateProjectAsync(projectPath, project, cancellationToken).ConfigureAwait(false);
                if (!validationResult.Success)
                {
                    return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                        validationResult.Errors,
                        sw.Elapsed);
                }
            }

            // Add to recent projects
            await AddToRecentProjectsAsync(projectPath, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Loaded ModBuilder project '{ProjectName}' from {ProjectPath}",
                project.Name,
                projectPath);

            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateSuccess(project, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load project from {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                $"Failed to load project: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<ModBuilderProject>> SaveProjectAsync(
        string projectPath,
        ModBuilderProject project,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    ProjectPathEmptyError,
                    sw.Elapsed);
            }

            if (project == null)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Project cannot be null",
                    sw.Elapsed);
            }

            // Ensure directory exists
            var projectDir = Path.GetDirectoryName(projectPath);
            if (!string.IsNullOrEmpty(projectDir) && !Directory.Exists(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            // Update last modified timestamp
            project.LastModified = DateTime.UtcNow;

            await AtomicWriteJsonFileAsync(projectPath, project, _jsonOptions, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Saved ModBuilder project '{ProjectName}' to {ProjectPath}",
                project.Name,
                projectPath);

            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateSuccess(project, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save project to {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                $"Failed to save project: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<bool>> ValidateProjectAsync(
        string projectPath,
        ModBuilderProject project,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var sw = Stopwatch.StartNew();

        try
        {
            if (project == null)
            {
                return ProjectOperationResult<bool>.CreateFailure(
                    "Project cannot be null",
                    sw.Elapsed);
            }

            var errors = new List<string>();
            ValidateProjectProperties(projectPath, project, errors);

            sw.Stop();
            if (errors.Count > 0)
            {
                return ProjectOperationResult<bool>.CreateFailure(errors, sw.Elapsed);
            }

            return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate project at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Validation failed: {ex.Message}",
                sw.Elapsed);
        }
    }

    private void ValidateProjectProperties(string projectPath, ModBuilderProject project, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            errors.Add("Project name cannot be empty");
        }

        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
        {
            errors.Add($"Project directory does not exist: {projectDir}");
            return;
        }

        EnsureOutputDirectoriesExist(projectDir, project);
        var effectiveConfigs = ResolveAndValidateConfigsDir(projectDir, project.Directories.Configs, errors);
        ValidateGameFilesEditedDir(projectDir, project.Directories.GameFilesEdited, errors);
        ValidateBundleConfigsExist(projectDir, effectiveConfigs, project.BundleConfigs);
    }

    private void EnsureOutputDirectoriesExist(string projectDir, ModBuilderProject project)
    {
        var outputDirs = new[]
        {
            Path.Combine(projectDir, project.Directories.Build),
            Path.Combine(projectDir, project.Directories.Release),
        };

        foreach (var dir in outputDirs.Where(dir => !Directory.Exists(dir)))
        {
            try
            {
                Directory.CreateDirectory(dir);
                logger.LogDebug("Created output directory: {Directory}", dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not create output directory: {Directory}", dir);
            }
        }
    }

    private static string ResolveConfigsDir(string projectDir, string? configuredConfigs)
    {
        if (!string.IsNullOrWhiteSpace(configuredConfigs))
        {
            if (Directory.Exists(Path.Combine(projectDir, configuredConfigs)))
            {
                return configuredConfigs;
            }

            if (configuredConfigs.Equals(ModBuilderConstants.LowercaseConfigDir, StringComparison.OrdinalIgnoreCase) ||
                configuredConfigs.Equals(ModBuilderConstants.ConfigDir, StringComparison.OrdinalIgnoreCase))
            {
                var altConfig = configuredConfigs.Equals(ModBuilderConstants.LowercaseConfigDir, StringComparison.OrdinalIgnoreCase)
                    ? ModBuilderConstants.ConfigDir
                    : ModBuilderConstants.LowercaseConfigDir;
                if (Directory.Exists(Path.Combine(projectDir, altConfig)))
                {
                    return altConfig;
                }
            }

            return configuredConfigs;
        }

        if (Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.ConfigDir)))
        {
            return ModBuilderConstants.ConfigDir;
        }

        if (Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir)))
        {
            return ModBuilderConstants.LowercaseConfigDir;
        }

        return ModBuilderConstants.ConfigDir;
    }

    private static string ResolveConfigsDir(string projectDir, ModBuilderProject? project)
    {
        var configFolder = ResolveConfigsDir(projectDir, project?.Directories?.Configs);
        return Path.Combine(projectDir, configFolder);
    }

    private static string ResolveAndValidateConfigsDir(string projectDir, string configuredConfigs, List<string> errors)
    {
        var effectiveConfigs = ResolveConfigsDir(projectDir, configuredConfigs);
        var configsDir = Path.Combine(projectDir, effectiveConfigs);
        if (Directory.Exists(configsDir))
        {
            return effectiveConfigs;
        }

        errors.Add($"Required directory does not exist: {configsDir}");
        return effectiveConfigs;
    }

    private static void ValidateGameFilesEditedDir(string projectDir, string gameFilesDir, List<string> errors)
    {
        var gameFilesEditedDir = Path.Combine(projectDir, gameFilesDir);
        if (!Directory.Exists(gameFilesEditedDir))
        {
            errors.Add($"Required directory does not exist: {gameFilesEditedDir}");
        }
    }

    private void ValidateBundleConfigsExist(string projectDir, string effectiveConfigs, IEnumerable<string> bundleConfigs)
    {
        foreach (var config in bundleConfigs)
        {
            var configPath = ResolveBundleConfigPath(projectDir, effectiveConfigs, config);
            if (!File.Exists(configPath))
            {
                logger.LogWarning("Bundle config file not found: {ConfigPath}", configPath);
            }
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<List<string>>> GetRecentProjectsAsync(
        int maxCount = 10,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(_recentProjectsPath))
            {
                sw.Stop();
                return ProjectOperationResult<List<string>>.CreateSuccess(new List<string>(), sw.Elapsed);
            }

            await using var stream = new FileStream(
                _recentProjectsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var recentProjects = await JsonSerializer.DeserializeAsync<List<string>>(
                stream,
                _jsonOptions,
                cancellationToken)
                .ConfigureAwait(false);

            recentProjects ??= new List<string>();

            // Filter out projects that no longer exist
            var validProjects = recentProjects
                .Where(File.Exists)
                .ToList();

            // If some projects were filtered out because they no longer exist on disk, update the file
            if (validProjects.Count != recentProjects.Count)
            {
                await SaveRecentProjectsAsync(validProjects, cancellationToken).ConfigureAwait(false);
            }

            var resultProjects = validProjects.Take(maxCount).ToList();

            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateSuccess(resultProjects, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent projects from {Path}", _recentProjectsPath);
            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateFailure(
                $"Failed to get recent projects: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<bool>> AddToRecentProjectsAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<bool>.CreateFailure(
                    ProjectPathEmptyError,
                    sw.Elapsed);
            }

            var recentProjectsResult = await GetRecentProjectsAsync(100, cancellationToken).ConfigureAwait(false);
            var recentProjects = recentProjectsResult.Success && recentProjectsResult.Data != null
                ? recentProjectsResult.Data
                : new List<string>();

            // Remove if already exists (to move to front)
            recentProjects.Remove(projectPath);

            // Add to front
            recentProjects.Insert(0, projectPath);

            // Keep only top 20
            if (recentProjects.Count > 20)
            {
                recentProjects = recentProjects.Take(20).ToList();
            }

            await SaveRecentProjectsAsync(recentProjects, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add project to recent projects: {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Failed to add to recent projects: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<bool>> RemoveFromRecentProjectsAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<bool>.CreateFailure(
                    ProjectPathEmptyError,
                    sw.Elapsed);
            }

            var recentProjectsResult = await GetRecentProjectsAsync(100, cancellationToken).ConfigureAwait(false);
            if (!recentProjectsResult.Success || recentProjectsResult.Data == null)
            {
                sw.Stop();
                return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
            }

            var recentProjects = recentProjectsResult.Data;
            recentProjects.Remove(projectPath);

            await SaveRecentProjectsAsync(recentProjects, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove project from recent projects: {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Failed to remove from recent projects: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <summary>
    /// Resolves the absolute path for a bundle configuration file, handling both
    /// project-relative (e.g. "config/ModBundleItems.json") and configs-dir-relative (e.g. "ModBundleItems.json") paths.
    /// </summary>
    /// <param name="projectDir">The project directory.</param>
    /// <param name="configsDirName">The configs directory name.</param>
    /// <param name="config">The bundle configuration path or filename.</param>
    /// <returns>The resolved absolute path.</returns>
    public static string ResolveBundleConfigPath(string projectDir, string configsDirName, string config)
    {
        if (Path.IsPathRooted(config))
        {
            return config;
        }

        var normalizedConfig = config.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var effectiveConfigsDirName = string.IsNullOrWhiteSpace(configsDirName) ? "config" : configsDirName;
        var configsDir = Path.Combine(projectDir, effectiveConfigsDirName);

        // 1. Direct match under configsDir (e.g. "ModBundleItems.json")
        var pathInConfigs = Path.Combine(configsDir, normalizedConfig);
        if (File.Exists(pathInConfigs))
        {
            return pathInConfigs;
        }

        // 2. Direct match under projectDir (e.g. "config/ModBundleItems.json")
        var pathInProject = Path.Combine(projectDir, normalizedConfig);
        if (File.Exists(pathInProject))
        {
            return pathInProject;
        }

        // 3. If normalizedConfig has a directory prefix, check candidate configs folders for the relative file
        var prefixedCandidate = TryResolvePrefixedConfigPath(projectDir, configsDir, effectiveConfigsDirName, normalizedConfig);
        if (prefixedCandidate != null)
        {
            return prefixedCandidate;
        }

        // 4. Default fallback: if it contains a separator, prefer project-relative, otherwise configsDir-relative
        return normalizedConfig.Contains(Path.DirectorySeparatorChar) ? pathInProject : pathInConfigs;
    }

    private static string? TryResolvePrefixedConfigPath(string projectDir, string configsDir, string effectiveConfigsDirName, string normalizedConfig)
    {
        var subPath = ExtractConfigSubPath(normalizedConfig, effectiveConfigsDirName);
        if (subPath == null)
        {
            return null;
        }

        var candidateInConfigDir = Path.Combine(configsDir, subPath);
        if (File.Exists(candidateInConfigDir))
        {
            return candidateInConfigDir;
        }

        var candidateInAltConfig = Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir, subPath);
        if (File.Exists(candidateInAltConfig))
        {
            return candidateInAltConfig;
        }

        var candidateInAltConfigs = Path.Combine(projectDir, ModBuilderConstants.ConfigDir, subPath);
        if (File.Exists(candidateInAltConfigs))
        {
            return candidateInAltConfigs;
        }

        return FallbackPrefixedConfigPath(projectDir, candidateInConfigDir, candidateInAltConfig, candidateInAltConfigs);
    }

    private static string? ExtractConfigSubPath(string normalizedConfig, string effectiveConfigsDirName)
    {
        if (normalizedConfig.StartsWith(ModBuilderConstants.LowercaseConfigDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedConfig.Substring(ModBuilderConstants.LowercaseConfigDir.Length + 1);
        }

        if (normalizedConfig.StartsWith(ModBuilderConstants.ConfigDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedConfig.Substring(ModBuilderConstants.ConfigDir.Length + 1);
        }

        if (!string.IsNullOrWhiteSpace(effectiveConfigsDirName) &&
            normalizedConfig.StartsWith(effectiveConfigsDirName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedConfig.Substring(effectiveConfigsDirName.Length + 1);
        }

        return null;
    }

    private static string FallbackPrefixedConfigPath(string projectDir, string inConfigDir, string inAltConfig, string inAltConfigs)
    {
        if (Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir)))
        {
            return inAltConfig;
        }

        if (Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.ConfigDir)))
        {
            return inAltConfigs;
        }

        return inConfigDir;
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<List<string>>> GetBundleConfigsAsync(
        string projectPath,
        ModBuilderProject project,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var sw = Stopwatch.StartNew();

        try
        {
            if (project == null)
            {
                return ProjectOperationResult<List<string>>.CreateFailure(
                    "Project cannot be null",
                    sw.Elapsed);
            }

            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return ProjectOperationResult<List<string>>.CreateFailure(
                    "Invalid project path",
                    sw.Elapsed);
            }

            var effectiveConfigs = ResolveConfigsDir(projectDir, project.Directories.Configs);
            var bundleConfigPaths = project.BundleConfigs
                .Select(config => ResolveBundleConfigPath(projectDir, effectiveConfigs, config))
                .Where(File.Exists)
                .ToList();

            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateSuccess(bundleConfigPaths, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get bundle configs for project at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateFailure(
                $"Failed to get bundle configs: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<bool>> UpdateLastBuildTimeAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var loadResult = await LoadProjectAsync(projectPath, false, cancellationToken).ConfigureAwait(false);
            if (!loadResult.Success || loadResult.Data == null)
            {
                sw.Stop();
                return ProjectOperationResult<bool>.CreateFailure(
                    loadResult.Errors,
                    sw.Elapsed);
            }

            var project = loadResult.Data;
            project.LastBuild = DateTime.UtcNow;

            var saveResult = await SaveProjectAsync(projectPath, project, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            if (!saveResult.Success)
            {
                return ProjectOperationResult<bool>.CreateFailure(saveResult.Errors, sw.Elapsed);
            }

            return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update last build time for project at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Failed to update last build time: {ex.Message}",
                sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<int>> ImportBigFilesAsync(
        string projectPath,
        IEnumerable<string> bigFilePaths,
        bool createBundlePackForBig = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<int>.CreateFailure(ProjectPathEmptyError, sw.Elapsed);
            }

            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            {
                return ProjectOperationResult<int>.CreateFailure($"Project directory not found: {projectDir}", sw.Elapsed);
            }

            var bigList = bigFilePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (bigList.Count == 0)
            {
                return ProjectOperationResult<int>.CreateFailure("No valid BIG files specified for import", sw.Elapsed);
            }

            var missingBig = bigList.FirstOrDefault(p => !File.Exists(p));
            if (missingBig != null)
            {
                return ProjectOperationResult<int>.CreateFailure($"BIG file not found: {missingBig}", sw.Elapsed);
            }

            var projectLoadResult = await LoadProjectAsync(projectPath, false, cancellationToken).ConfigureAwait(false);
            var project = projectLoadResult.Data;
            var destinationDir = Path.Combine(projectDir, project?.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir);
            Directory.CreateDirectory(destinationDir);

            logger.LogInformation("Importing {Count} BIG file(s) into project {ProjectPath} ({DestDir})", bigList.Count, projectPath, destinationDir);

            var totalExtracted = await BigFilePacker.UnpackMultipleAsync(
                bigList,
                destinationDir,
                overwrite: true,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (createBundlePackForBig)
            {
                await ConfigureBundlePacksForImportedBigsAsync(projectDir, bigList, cancellationToken, project).ConfigureAwait(false);
            }

            sw.Stop();
            logger.LogInformation("Successfully imported {TotalExtracted} files from {BigCount} BIG archives in {Elapsed}ms", totalExtracted, bigList.Count, sw.ElapsedMilliseconds);
            return ProjectOperationResult<int>.CreateSuccess(totalExtracted, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import BIG file(s) into project: {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<int>.CreateFailure($"Failed to import BIG file(s): {ex.Message}", sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectOperationResult<ModBuilderProject>> CreateProjectFromBigFilesAsync(
        string projectPath,
        string projectName,
        IEnumerable<string> bigFilePaths,
        string? gameInstallationId = null,
        ContentType contentType = ContentType.Mod,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(ProjectPathEmptyError, sw.Elapsed);
            }

            var bigList = bigFilePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (bigList.Count == 0)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure("No BIG files specified to create project from", sw.Elapsed);
            }

            // 1. Create base project with ImportedBig template
            var createResult = await CreateProjectAsync(
                projectPath,
                projectName,
                gameInstallationId,
                template: ProjectTemplate.ImportedBig,
                contentType: contentType,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!createResult.Success || createResult.Data == null)
            {
                sw.Stop();
                return createResult;
            }

            // 2. Import the BIG files and auto-configure bundle packs
            var importResult = await ImportBigFilesAsync(
                projectPath,
                bigList,
                createBundlePackForBig: true,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!importResult.Success)
            {
                sw.Stop();
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(importResult.Errors, sw.Elapsed);
            }

            // 3. Reload project to return fresh project state
            var loadResult = await LoadProjectAsync(projectPath, validateIntegrity: false, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return loadResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create project from BIG files: {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateFailure($"Failed to create project from BIG files: {ex.Message}", sw.Elapsed);
        }
    }

    /// <summary>
    /// Configures ModBundleItems.json and ModBundlePacks.json for imported BIG archives.
    /// </summary>
    private async Task ConfigureBundlePacksForImportedBigsAsync(
        string projectDir,
        List<string> bigFilePaths,
        CancellationToken cancellationToken,
        ModBuilderProject? project = null)
    {
        var configsDir = ResolveConfigsDir(projectDir, project);
        Directory.CreateDirectory(configsDir);

        var itemsPath = Path.Combine(configsDir, ModBuilderConstants.BundleItemsConfigFileName);
        var packsPath = Path.Combine(configsDir, ModBuilderConstants.BundlePacksConfigFileName);

        var itemNames = await EnsureImportedBundleItemsAsync(itemsPath, project, cancellationToken).ConfigureAwait(false);
        await EnsureImportedBundlePacksAsync(packsPath, bigFilePaths, itemNames, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<string>> EnsureImportedBundleItemsAsync(
        string itemsPath,
        ModBuilderProject? project,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(itemsPath))
        {
            await CreateDefaultImportedBundleItemsFileAsync(itemsPath, project, cancellationToken).ConfigureAwait(false);
            return new List<string> { ModBuilderConstants.DefaultImportedGameFilesItemName };
        }

        var itemNames = await ReadExistingBundleItemNamesAsync(itemsPath, cancellationToken).ConfigureAwait(false);
        if (itemNames.Contains(ModBuilderConstants.DefaultImportedGameFilesItemName, StringComparer.OrdinalIgnoreCase))
        {
            return itemNames;
        }

        if (itemNames.Count == 0)
        {
            logger.LogWarning("Existing ModBundleItems.json at {Path} yielded no bundle item names; ensuring default imported bundle item is defined", itemsPath);
        }

        var appended = await EnsureDefaultImportedItemInFileAsync(itemsPath, project, cancellationToken).ConfigureAwait(false);
        if (appended)
        {
            itemNames.Add(ModBuilderConstants.DefaultImportedGameFilesItemName);
        }

        return itemNames;
    }

    private async Task<List<string>> ReadExistingBundleItemNamesAsync(string itemsPath, CancellationToken cancellationToken)
    {
        var itemNames = new List<string>();
        if (!File.Exists(itemsPath))
        {
            return itemNames;
        }

        try
        {
            var jsonDocOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            using var stream = File.OpenRead(itemsPath);
            using var doc = await JsonDocument.ParseAsync(stream, jsonDocOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

            var itemsElem = ExtractNamedArrayProperty(doc.RootElement, "bundleItems");
            ExtractNamedEntries(itemsElem, itemNames);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not parse existing ModBundleItems.json at {Path}", itemsPath);
        }

        return itemNames;
    }

    private async Task<bool> EnsureDefaultImportedItemInFileAsync(
        string itemsPath,
        ModBuilderProject? project,
        CancellationToken cancellationToken)
    {
        var gameFilesDirName = project?.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
        var defaultItem = new
        {
            Name = ModBuilderConstants.DefaultImportedGameFilesItemName,
            SourceFiles = new[] { $"{gameFilesDirName}/**/*" },
            Description = "Files extracted from imported BIG archive(s)",
        };

        try
        {
            var content = await File.ReadAllTextAsync(itemsPath, cancellationToken).ConfigureAwait(false);
            var jsonNodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
            var jsonDocumentOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            var node = JsonNode.Parse(content, jsonNodeOptions, jsonDocumentOptions);
            if (node is JsonObject rootObj)
            {
                return await AppendDefaultItemToObjectRootAsync(itemsPath, rootObj, defaultItem, cancellationToken).ConfigureAwait(false);
            }

            if (node is JsonArray rootArr)
            {
                return await AppendDefaultItemToArrayRootAsync(itemsPath, rootArr, defaultItem, cancellationToken).ConfigureAwait(false);
            }

            logger.LogWarning("Existing ModBundleItems.json at {Path} is neither an object nor an array; preserving file without changes", itemsPath);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to append default bundle item to existing {Path}; preserving file without overwriting", itemsPath);
            return false;
        }
    }

    private async Task<bool> AppendDefaultItemToObjectRootAsync(
        string itemsPath,
        JsonObject rootObj,
        object defaultItem,
        CancellationToken cancellationToken)
    {
        var existingKey = rootObj.Select(kvp => kvp.Key)
            .FirstOrDefault(k => string.Equals(k, "bundleItems", StringComparison.OrdinalIgnoreCase)) ?? "BundleItems";

        if (!rootObj.TryGetPropertyValue(existingKey, out var itemsNode) || itemsNode is not JsonArray itemsArr)
        {
            itemsArr = new JsonArray();
            rootObj[existingKey] = itemsArr;
        }

        if (!ContainsNamedItem(itemsArr, ModBuilderConstants.DefaultImportedGameFilesItemName))
        {
            itemsArr.Add(JsonNode.Parse(JsonSerializer.Serialize(defaultItem, _jsonOptions)));
            await AtomicWriteJsonFileAsync(itemsPath, rootObj, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> AppendDefaultItemToArrayRootAsync(
        string itemsPath,
        JsonArray rootArr,
        object defaultItem,
        CancellationToken cancellationToken)
    {
        if (!ContainsNamedItem(rootArr, ModBuilderConstants.DefaultImportedGameFilesItemName))
        {
            rootArr.Add(JsonNode.Parse(JsonSerializer.Serialize(defaultItem, _jsonOptions)));
            await AtomicWriteJsonFileAsync(itemsPath, rootArr, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task CreateDefaultImportedBundleItemsFileAsync(
        string itemsPath,
        ModBuilderProject? project,
        CancellationToken cancellationToken)
    {
        var gameFilesDirName = project?.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
        var bundleItemsConfig = new
        {
            BundleItems = new object[]
            {
                new
                {
                    Name = ModBuilderConstants.DefaultImportedGameFilesItemName,
                    SourceFiles = new[] { $"{gameFilesDirName}/**/*" },
                    Description = "Files extracted from imported BIG archive(s)",
                },
            },
        };

        await AtomicWriteJsonFileAsync(itemsPath, bundleItemsConfig, _jsonOptions, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Created ModBundleItems.json for imported BIG files at {Path}", itemsPath);
    }

    private async Task EnsureImportedBundlePacksAsync(
        string packsPath,
        List<string> bigFilePaths,
        List<string> itemNames,
        CancellationToken cancellationToken)
    {
        var existingPacks = await ReadExistingBundlePackNamesAsync(packsPath, cancellationToken).ConfigureAwait(false);
        var packsToAdd = BuildPacksToAdd(bigFilePaths, itemNames, existingPacks);

        if (packsToAdd.Count == 0)
        {
            return;
        }

        if (!File.Exists(packsPath))
        {
            await CreateBundlePacksFileAsync(packsPath, packsToAdd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AppendPacksToExistingBundleFileAsync(packsPath, packsToAdd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<string>> ReadExistingBundlePackNamesAsync(string packsPath, CancellationToken cancellationToken)
    {
        var existingPacks = new List<string>();
        if (!File.Exists(packsPath))
        {
            return existingPacks;
        }

        try
        {
            var jsonDocOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            using var stream = File.OpenRead(packsPath);
            using var doc = await JsonDocument.ParseAsync(stream, jsonDocOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

            var packsElem = ExtractNamedArrayProperty(doc.RootElement, "bundlePacks");
            ExtractNamedEntries(packsElem, existingPacks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not parse existing ModBundlePacks.json at {Path}", packsPath);
        }

        return existingPacks;
    }

    private static bool ContainsNamedItem(JsonArray array, string itemName)
    {
        return array.Any(n => n is JsonObject itemObj &&
            itemObj.TryGetPropertyValue("name", out var nameVal) &&
            string.Equals(nameVal?.ToString(), itemName, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement ExtractNamedArrayProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            var prop = root.EnumerateObject().FirstOrDefault(p =>
                string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            return prop.Value;
        }

        return default;
    }

    private static void ExtractNamedEntries(JsonElement arrayElem, List<string> targetList)
    {
        if (arrayElem.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in arrayElem.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nameProp = item.EnumerateObject().FirstOrDefault(p =>
                string.Equals(p.Name, "name", StringComparison.OrdinalIgnoreCase));
            var nameVal = nameProp.Value.ValueKind == JsonValueKind.String ? nameProp.Value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(nameVal))
            {
                targetList.Add(nameVal);
            }
        }
    }

    private static List<object> BuildPacksToAdd(List<string> bigFilePaths, List<string> itemNames, List<string> existingPacks)
    {
        var packsToAdd = new List<object>();
        foreach (var bigPath in bigFilePaths)
        {
            var bigFileName = Path.GetFileName(bigPath);
            var packName = Path.GetFileNameWithoutExtension(bigPath).Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(packName))
            {
                packName = "ImportedMod";
            }

            if (!existingPacks.Contains(packName, StringComparer.OrdinalIgnoreCase))
            {
                packsToAdd.Add(new
                {
                    Name = packName,
                    Items = itemNames.ToArray(),
                    ItemNames = itemNames.ToArray(),
                    AllowBuild = true,
                    AllowInstall = true,
                    Big = true,
                    OutputFile = $".Release/{bigFileName}",
                    Description = $"Build pack to generate {bigFileName} from edited mod files",
                });
                existingPacks.Add(packName);
            }
        }

        return packsToAdd;
    }

    private async Task CreateBundlePacksFileAsync(string packsPath, List<object> packsToAdd, CancellationToken cancellationToken)
    {
        var bundlePacksConfig = new
        {
            BundlePacks = packsToAdd.ToArray(),
        };
        await AtomicWriteJsonFileAsync(packsPath, bundlePacksConfig, _jsonOptions, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Created ModBundlePacks.json for imported BIG files at {Path}", packsPath);
    }

    private async Task AppendPacksToExistingBundleFileAsync(string packsPath, List<object> packsToAdd, CancellationToken cancellationToken)
    {
        try
        {
            var existingContent = await File.ReadAllTextAsync(packsPath, cancellationToken).ConfigureAwait(false);
            var jsonNodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
            var jsonDocumentOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            var node = JsonNode.Parse(existingContent, jsonNodeOptions, jsonDocumentOptions);
            if (node is JsonObject rootObj)
            {
                AppendPacksToJsonObject(rootObj, packsToAdd);
                await AtomicWriteJsonFileAsync(packsPath, rootObj, _jsonOptions, cancellationToken).ConfigureAwait(false);
                logger.LogDebug("Updated ModBundlePacks.json with imported BIG packs at {Path}", packsPath);
            }
            else if (node is JsonArray rootArray)
            {
                AppendPacksToJsonArray(rootArray, packsToAdd);
                await AtomicWriteJsonFileAsync(packsPath, rootArray, _jsonOptions, cancellationToken).ConfigureAwait(false);
                logger.LogDebug("Updated array-root ModBundlePacks.json with imported BIG packs at {Path}", packsPath);
            }
            else
            {
                logger.LogError("Existing ModBundlePacks.json at {Path} is neither an object nor an array", packsPath);
                throw new InvalidDataException($"Existing ModBundlePacks.json at '{packsPath}' has an unsupported JSON root type ({node?.GetType().Name ?? "null"}).");
            }
        }
        catch (Exception ex) when (ex is not InvalidDataException and not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to append new packs to existing ModBundlePacks.json at '{packsPath}': {ex.Message}", ex);
        }
    }

    private void AppendPacksToJsonObject(JsonObject rootObj, List<object> packsToAdd)
    {
        var existingKey = rootObj.Select(kvp => kvp.Key)
            .FirstOrDefault(k => string.Equals(k, "bundlePacks", StringComparison.OrdinalIgnoreCase)) ?? "BundlePacks";

        if (!rootObj.TryGetPropertyValue(existingKey, out var packsNode) || packsNode is not JsonArray packsArray)
        {
            packsArray = new JsonArray();
            rootObj[existingKey] = packsArray;
        }

        AppendPacksToJsonArray(packsArray, packsToAdd);
    }

    private void AppendPacksToJsonArray(JsonArray targetArray, List<object> packsToAdd)
    {
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in targetArray)
        {
            if (node is JsonObject obj &&
                obj.TryGetPropertyValue("name", out var nameProp) &&
                nameProp != null &&
                !string.IsNullOrWhiteSpace(nameProp.ToString()))
            {
                existingNames.Add(nameProp.ToString());
            }
        }

        foreach (var pack in packsToAdd)
        {
            var packJson = JsonSerializer.Serialize(pack, _jsonOptions);
            var packNode = JsonNode.Parse(packJson);
            if (packNode is JsonObject packObj &&
                packObj.TryGetPropertyValue("name", out var packNameProp) &&
                packNameProp != null &&
                !string.IsNullOrWhiteSpace(packNameProp.ToString()))
            {
                var packName = packNameProp.ToString();
                if (existingNames.Add(packName))
                {
                    targetArray.Add(packNode);
                }
                else
                {
                    logger.LogWarning("Duplicate pack name '{PackName}' already exists in bundle packs file; skipping", packName);
                }
            }
            else if (packNode != null)
            {
                targetArray.Add(packNode);
            }
        }
    }

    /// <summary>
    /// Creates the project directory structure.
    /// </summary>
    /// <param name="projectDir">The project directory path.</param>
    /// <param name="directories">The directory configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    private async Task<ProjectOperationResult<bool>> CreateProjectDirectoryStructureAsync(
        string projectDir,
        ProjectDirectories directories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        try
        {
            var dirsToCreate = new[]
            {
                projectDir,
                Path.Combine(projectDir, directories.Configs),
                Path.Combine(projectDir, directories.GameFilesEdited),
                Path.Combine(projectDir, directories.Build),
                Path.Combine(projectDir, directories.Release),
            };

            foreach (var dir in dirsToCreate.Where(dir => !Directory.Exists(dir)))
            {
                Directory.CreateDirectory(dir);
                logger.LogDebug("Created directory: {Directory}", dir);
            }

            return ProjectOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create project directory structure at {ProjectDir}", projectDir);
            return ProjectOperationResult<bool>.CreateFailure(
                $"Failed to create directory structure: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates sample files for a new project based on the specified template.
    /// </summary>
    /// <param name="projectDir">The project directory path.</param>
    /// <param name="directories">The directory configuration.</param>
    /// <param name="template">The project template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateSampleFilesAsync(
        string projectDir,
        ProjectDirectories directories,
        ProjectTemplate? template,
        CancellationToken cancellationToken)
    {
        try
        {
            var configsDir = Path.Combine(projectDir, directories.Configs);
            Directory.CreateDirectory(configsDir);

            if (template?.Name == ProjectTemplate.Hotkeys.Name || template?.Name == "CustomIcons")
            {
                await CreateCustomIconsSampleFilesAsync(projectDir, directories, configsDir, cancellationToken).ConfigureAwait(false);
            }
            else if (template?.Name == ProjectTemplate.ImprovedMenus.Name)
            {
                await CreateImprovedMenusSampleFilesAsync(projectDir, directories, configsDir, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CreateBasicModSampleFilesAsync(projectDir, directories, configsDir, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create sample files");
        }
    }

    private async Task CreateBasicModSampleFilesAsync(
        string projectDir,
        ProjectDirectories directories,
        string configsDir,
        CancellationToken cancellationToken)
    {
        var itemsPath = Path.Combine(configsDir, ModBuilderConstants.BundleItemsConfigFileName);
        if (!File.Exists(itemsPath))
        {
            var bundleItemsConfig = new
            {
                BundleItems = new object[]
                {
                    new
                    {
                        Name = "ModifiedINI",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/Data/INI/**/*.ini" },
                        OutputFormat = "INI",
                        Description = "Custom INI game settings and unit tweaks",
                    },
                },
            };

            var itemsJson = JsonSerializer.Serialize(bundleItemsConfig, _jsonOptions);
            await File.WriteAllTextAsync(itemsPath, itemsJson, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Created ModBundleItems.json at {Path}", itemsPath);
        }

        var packsPath = Path.Combine(configsDir, ModBuilderConstants.BundlePacksConfigFileName);
        if (!File.Exists(packsPath))
        {
            var bundlePacksConfig = new
            {
                BundlePacks = new[]
                {
                    new
                    {
                        Name = Path.GetFileNameWithoutExtension(projectDir) ?? "MyMod",
                        Items = new[] { "ModifiedINI" },
                        ItemNames = new[] { "ModifiedINI" },
                        AllowBuild = true,
                        AllowInstall = true,
                        OutputFile = $"{directories.Release}/{Path.GetFileNameWithoutExtension(projectDir) ?? "MyMod"}.big",
                        Description = "Default mod bundle pack",
                    },
                },
            };

            var packsJson = JsonSerializer.Serialize(bundlePacksConfig, _jsonOptions);
            await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Created ModBundlePacks.json at {Path}", packsPath);
        }

        // Create sample INI file
        var iniDir = Path.Combine(projectDir, directories.GameFilesEdited, "Data", "INI");
        Directory.CreateDirectory(iniDir);
        var sampleIniPath = Path.Combine(iniDir, "SampleTank.ini");
        if (!File.Exists(sampleIniPath))
        {
            var sampleIniContent = "; Sample ModBuilder INI file\n" +
                                   "; Edit unit properties or game settings here\n\n" +
                                   "Object AmericaTankCrusader\n" +
                                   "  MaxHealth = 1000.0\n" +
                                   "  InitialHealth = 1000.0\n" +
                                   "End\n";
            await File.WriteAllTextAsync(sampleIniPath, sampleIniContent, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Created sample INI at {Path}", sampleIniPath);
        }

        // Create a README in GameFilesEdited
        var gameFilesDir = Path.Combine(projectDir, directories.GameFilesEdited);
        var readmePath = Path.Combine(gameFilesDir, "README.txt");

        if (!File.Exists(readmePath))
        {
            var readmeContent = "Place your modified game files in this directory.\n" +
                              "Maintain the same folder structure as the game (e.g. Data/INI/, Art/Textures/).\n" +
                              "ModBuilder will automatically pack them into .BIG files when you click Execute Build.\n";
            await File.WriteAllTextAsync(readmePath, readmeContent, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Created README at {Path}", readmePath);
        }
    }

    private async Task CreateCustomIconsSampleFilesAsync(
        string projectDir,
        ProjectDirectories directories,
        string configsDir,
        CancellationToken cancellationToken)
    {
        var itemsPath = Path.Combine(configsDir, ModBuilderConstants.BundleItemsConfigFileName);
        if (!File.Exists(itemsPath))
        {
            var bundleItemsConfig = new
            {
                BundleItems = new object[]
                {
                    new
                    {
                        Name = "CustomIconTextures",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/Art/Textures/**/*.tga" },
                        OutputFormat = "DDS",
                        Compression = "DXT5",
                        GenerateMipmaps = true,
                        Description = "Custom unit cameo icons and hotkey overlay textures",
                    },
                    new
                    {
                        Name = "CustomIconINIs",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/Data/INI/**/*.ini" },
                        OutputFormat = "INI",
                        Description = "Command button assignments, command sets, and mapped image coordinates",
                    },
                    new
                    {
                        Name = "CustomIconStrings",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/Data/English/**/*.str" },
                        OutputFormat = "STR",
                        Description = "Tooltip string overrides showing hotkey keybindings",
                    },
                },
            };

            var itemsJson = JsonSerializer.Serialize(bundleItemsConfig, _jsonOptions);
            await File.WriteAllTextAsync(itemsPath, itemsJson, cancellationToken).ConfigureAwait(false);
        }

        var packsPath = Path.Combine(configsDir, ModBuilderConstants.BundlePacksConfigFileName);
        if (!File.Exists(packsPath))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectDir) ?? "CustomIcons";
            var bundlePacksConfig = new
            {
                BundlePacks = new[]
                {
                    new
                    {
                        Name = projectName,
                        Items = new[] { "CustomIconTextures", "CustomIconINIs", "CustomIconStrings" },
                        ItemNames = new[] { "CustomIconTextures", "CustomIconINIs", "CustomIconStrings" },
                        AllowBuild = true,
                        AllowInstall = true,
                        OutputFile = $"{directories.Release}/!{projectName}.big",
                        Description = "Addon package containing custom cameo icons and Legionnaire hotkeys",
                    },
                },
            };

            var packsJson = JsonSerializer.Serialize(bundlePacksConfig, _jsonOptions);
            await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);
        }

        var mappedDir = Path.Combine(projectDir, directories.GameFilesEdited, "Data", "INI", "MappedImages", "HandMade");
        Directory.CreateDirectory(mappedDir);
        var sampleMappedPath = Path.Combine(mappedDir, "CustomIcons.ini");
        if (!File.Exists(sampleMappedPath))
        {
            var content = "; Custom Icons Mapped Image Definition\nMappedImage SACrusaderCustom\n  Texture = CustomUnitIcons.tga\n  TextureWidth = 64\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:31 Bottom:31\n  Status = NONE\nEnd\n";
            await File.WriteAllTextAsync(sampleMappedPath, content, cancellationToken).ConfigureAwait(false);
        }

        var iniDir = Path.Combine(projectDir, directories.GameFilesEdited, "Data", "INI");
        Directory.CreateDirectory(iniDir);
        var sampleBtnPath = Path.Combine(iniDir, "CommandButton.ini");
        if (!File.Exists(sampleBtnPath))
        {
            var content = "; Custom CommandButton with Legionnaire-style hotkey\nCommandButton Command_ConstructAmericaVehicleCrusader\n  Command = UNIT_BUILD\n  Object = AmericaVehicleCrusader\n  TextLabel = CONTROLBAR:ConstructAmericaVehicleCrusader\n  ButtonImage = SACrusaderCustom\n  ButtonBorderType = ACTION\n  DescriptLabel = CONTROLBAR:ToolTipAmericaVehicleCrusaderHotkey\n  KeyBinding = KEY_Q\nEnd\n";
            await File.WriteAllTextAsync(sampleBtnPath, content, cancellationToken).ConfigureAwait(false);
        }

        var texDir = Path.Combine(projectDir, directories.GameFilesEdited, "Art", "Textures");
        Directory.CreateDirectory(texDir);
        var texReadme = Path.Combine(texDir, "README.txt");
        if (!File.Exists(texReadme))
        {
            await File.WriteAllTextAsync(texReadme, "Place your 32-bit RGBA .tga icon sheets here.\nModBuilder will automatically compress them to DXT5 DDS during build.\n", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateImprovedMenusSampleFilesAsync(
        string projectDir,
        ProjectDirectories directories,
        string configsDir,
        CancellationToken cancellationToken)
    {
        var itemsPath = Path.Combine(configsDir, ModBuilderConstants.BundleItemsConfigFileName);
        if (!File.Exists(itemsPath))
        {
            var bundleItemsConfig = new
            {
                BundleItems = new object[]
                {
                    new
                    {
                        Name = "MenuWindows",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/window/Menus/**/*.wnd" },
                        OutputFormat = "WINDOW",
                        Description = "Widescreen adapted .wnd menu layout definitions",
                    },
                    new
                    {
                        Name = "MenuMappedImages",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/Data/INI/MappedImages/**/*.ini" },
                        OutputFormat = "INI",
                        Description = "MappedImage coordinate definitions for widescreen menu textures",
                    },
                    new
                    {
                        Name = "MenuTextures",
                        SourceFiles = new[] { $"{directories.GameFilesEdited}/Data/English/Art/Textures/**/*.tga" },
                        OutputFormat = "DDS",
                        Compression = "DXT5",
                        GenerateMipmaps = false,
                        Description = "High resolution menu backdrops and UI frame textures",
                    },
                },
            };

            var itemsJson = JsonSerializer.Serialize(bundleItemsConfig, _jsonOptions);
            await File.WriteAllTextAsync(itemsPath, itemsJson, cancellationToken).ConfigureAwait(false);
        }

        var packsPath = Path.Combine(configsDir, ModBuilderConstants.BundlePacksConfigFileName);
        if (!File.Exists(packsPath))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectDir) ?? "ImprovedMenus";
            var bundlePacksConfig = new
            {
                BundlePacks = new[]
                {
                    new
                    {
                        Name = projectName,
                        Items = new[] { "MenuWindows", "MenuMappedImages", "MenuTextures" },
                        ItemNames = new[] { "MenuWindows", "MenuMappedImages", "MenuTextures" },
                        AllowBuild = true,
                        AllowInstall = true,
                        OutputFile = $"{directories.Release}/!{projectName}.big",
                        Description = "Widescreen 16:9 menu overhaul package",
                    },
                },
            };

            var packsJson = JsonSerializer.Serialize(bundlePacksConfig, _jsonOptions);
            await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);
        }

        var menusDir = Path.Combine(projectDir, directories.GameFilesEdited, "window", "Menus");
        Directory.CreateDirectory(menusDir);
        var sampleWndPath = Path.Combine(menusDir, "MainMenu.wnd");
        if (!File.Exists(sampleWndPath))
        {
            var content = "FILE_VERSION = 2;\nSTARTLAYOUTBLOCK\n  LAYOUTINIT = W3DMainMenuInit;\nENDLAYOUTBLOCK\nWINDOW\n  WINDOWTYPE = USER;\n  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n  NAME = \"MainMenu.wnd:MainMenuParent\";\n  STATUS = ENABLED;\nEND\n";
            await File.WriteAllTextAsync(sampleWndPath, content, cancellationToken).ConfigureAwait(false);
        }

        var artDir = Path.Combine(projectDir, directories.GameFilesEdited, "Data", "English", "Art", "Textures");
        Directory.CreateDirectory(artDir);
        var artReadme = Path.Combine(artDir, "README.txt");
        if (!File.Exists(artReadme))
        {
            await File.WriteAllTextAsync(artReadme, "Place your 16:9 widescreen menu textures here (.tga).\n", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Saves the recent projects list to disk.
    /// </summary>
    /// <param name="recentProjects">The list of recent project paths.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SaveRecentProjectsAsync(
        List<string> recentProjects,
        CancellationToken cancellationToken)
    {
        await AtomicWriteJsonFileAsync(_recentProjectsPath, recentProjects, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AtomicWriteJsonFileAsync<T>(
        string filePath,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }
    }
}
