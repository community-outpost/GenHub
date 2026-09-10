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
public sealed class ProjectConfigService : IProjectConfigService
{
    private const string ProjectFileExtension = ".mbproj";
    private const string RecentProjectsFileName = "recent_projects.json";
    private const string ModBuilderDirName = "ModBuilder";
    private const string ProjectPathEmptyError = "Project path cannot be empty";

    private readonly ILogger<ProjectConfigService> _logger;
    private readonly string _recentProjectsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectConfigService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configurationProvider">The configuration provider service.</param>
    public ProjectConfigService(
        ILogger<ProjectConfigService> logger,
        IConfigurationProviderService? configurationProvider = null)
    {
        _logger = logger;
        var appDataPath = configurationProvider?.GetApplicationDataPath()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".genhub");

        _recentProjectsPath = Path.Combine(
            appDataPath,
            ModBuilderDirName,
            RecentProjectsFileName);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

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
            if (!projectPath.EndsWith(ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = Path.ChangeExtension(projectPath, ProjectFileExtension);
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

            _logger.LogInformation(
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
            _logger.LogError(ex, "Failed to create project at {ProjectPath}", projectPath);
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

            _logger.LogInformation(
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
            _logger.LogError(ex, "Failed to load project from {ProjectPath}", projectPath);
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

            // Serialize and write to file
            await using var stream = new FileStream(
                projectPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await JsonSerializer.SerializeAsync(
                stream,
                project,
                _jsonOptions,
                cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
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
            _logger.LogError(ex, "Failed to save project to {ProjectPath}", projectPath);
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
        var errors = new List<string>();

        try
        {
            if (project == null)
            {
                return ProjectOperationResult<bool>.CreateFailure(
                    "Project cannot be null",
                    sw.Elapsed);
            }

            // Validate project name
            if (string.IsNullOrWhiteSpace(project.Name))
            {
                errors.Add("Project name cannot be empty");
            }

            // Validate project directory
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            {
                errors.Add($"Project directory does not exist: {projectDir}");
            }
            else
            {
                // Validate required directories
                var requiredDirs = new[]
                {
                    Path.Combine(projectDir, project.Directories.Configs),
                    Path.Combine(projectDir, project.Directories.GameFilesEdited),
                    Path.Combine(projectDir, project.Directories.Build),
                    Path.Combine(projectDir, project.Directories.Release),
                };

                foreach (var dir in requiredDirs.Where(dir => !Directory.Exists(dir)))
                {
                    errors.Add($"Required directory does not exist: {dir}");
                }

                // Validate bundle config files exist
                var configsDir = Path.Combine(projectDir, project.Directories.Configs);
                foreach (var config in project.BundleConfigs)
                {
                    var configPath = Path.Combine(configsDir, config);
                    if (!File.Exists(configPath))
                    {
                        _logger.LogWarning("Bundle config file not found: {ConfigPath}", configPath);
                    }
                }
            }

            sw.Stop();
            if (errors.Count > 0)
            {
                return ProjectOperationResult<bool>.CreateFailure(errors, sw.Elapsed);
            }

            return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate project at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Validation failed: {ex.Message}",
                sw.Elapsed);
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
            var existingProjects = recentProjects
                .Where(File.Exists)
                .Take(maxCount)
                .ToList();

            // If some projects were filtered out, update the file
            if (existingProjects.Count != recentProjects.Count)
            {
                await SaveRecentProjectsAsync(existingProjects, cancellationToken).ConfigureAwait(false);
            }

            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateSuccess(existingProjects, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent projects from {Path}", _recentProjectsPath);
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
            _logger.LogError(ex, "Failed to add project to recent projects: {ProjectPath}", projectPath);
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
            _logger.LogError(ex, "Failed to remove project from recent projects: {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Failed to remove from recent projects: {ex.Message}",
                sw.Elapsed);
        }
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

            var configsDir = Path.Combine(projectDir, project.Directories.Configs);
            var bundleConfigPaths = project.BundleConfigs
                .Select(config => Path.Combine(configsDir, config))
                .Where(File.Exists)
                .ToList();

            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateSuccess(bundleConfigPaths, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get bundle configs for project at {ProjectPath}", projectPath);
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
            _logger.LogError(ex, "Failed to update last build time for project at {ProjectPath}", projectPath);
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

            foreach (var bigPath in bigList)
            {
                if (!File.Exists(bigPath))
                {
                    return ProjectOperationResult<int>.CreateFailure($"BIG file not found: {bigPath}", sw.Elapsed);
                }
            }

            var destinationDir = Path.Combine(projectDir, "GameFilesEdited");
            Directory.CreateDirectory(destinationDir);

            _logger.LogInformation("Importing {Count} BIG file(s) into project {ProjectPath} ({DestDir})", bigList.Count, projectPath, destinationDir);

            var totalExtracted = await BigFilePacker.UnpackMultipleAsync(
                bigList,
                destinationDir,
                overwrite: true,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (createBundlePackForBig)
            {
                await ConfigureBundlePacksForImportedBigsAsync(projectDir, bigList, cancellationToken).ConfigureAwait(false);
            }

            sw.Stop();
            _logger.LogInformation("Successfully imported {Count} files from {BigCount} BIG archives in {Elapsed}ms", totalExtracted, bigList.Count, sw.ElapsedMilliseconds);
            return ProjectOperationResult<int>.CreateSuccess(totalExtracted, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import BIG file(s) into project: {ProjectPath}", projectPath);
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
            _logger.LogError(ex, "Failed to create project from BIG files: {ProjectPath}", projectPath);
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
        CancellationToken cancellationToken)
    {
        var configsDir = Path.Combine(projectDir, "config");
        Directory.CreateDirectory(configsDir);

        var itemsPath = Path.Combine(configsDir, ModBuilderConstants.BundleItemsConfigFileName);
        var packsPath = Path.Combine(configsDir, ModBuilderConstants.BundlePacksConfigFileName);

        // 1. Configure ModBundleItems.json
        var itemNames = new List<string>();

        if (File.Exists(itemsPath))
        {
            try
            {
                using var stream = File.OpenRead(itemsPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("bundleItems", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemsElem.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
                        {
                            itemNames.Add(nameProp.GetString()!);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse existing ModBundleItems.json at {Path}", itemsPath);
            }
        }

        if (itemNames.Count == 0)
        {
            itemNames.Add("ImportedGameFiles");
            var bundleItemsConfig = new
            {
                BundleItems = new object[]
                {
                    new
                    {
                        Name = "ImportedGameFiles",
                        SourceFiles = new[] { "GameFilesEdited/**/*" },
                        Description = "Files extracted from imported BIG archive(s)",
                    },
                },
            };

            var itemsJson = JsonSerializer.Serialize(bundleItemsConfig, _jsonOptions);
            await File.WriteAllTextAsync(itemsPath, itemsJson, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Created ModBundleItems.json for imported BIG files at {Path}", itemsPath);
        }

        // 2. Configure ModBundlePacks.json
        var existingPacks = new List<string>();
        if (File.Exists(packsPath))
        {
            try
            {
                using var stream = File.OpenRead(packsPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("bundlePacks", out var packsElem) && packsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pack in packsElem.EnumerateArray())
                    {
                        if (pack.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
                        {
                            existingPacks.Add(nameProp.GetString()!);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse existing ModBundlePacks.json at {Path}", packsPath);
            }
        }

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

        if (packsToAdd.Count > 0)
        {
            if (!File.Exists(packsPath))
            {
                var bundlePacksConfig = new
                {
                    BundlePacks = packsToAdd.ToArray(),
                };
                var packsJson = JsonSerializer.Serialize(bundlePacksConfig, _jsonOptions);
                await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Created ModBundlePacks.json for imported BIG files at {Path}", packsPath);
            }
            else
            {
                try
                {
                    var existingContent = await File.ReadAllTextAsync(packsPath, cancellationToken).ConfigureAwait(false);
                    var node = JsonNode.Parse(existingContent);
                    if (node is JsonObject rootObj)
                    {
                        if (!rootObj.TryGetPropertyValue("bundlePacks", out var packsNode) || packsNode is not JsonArray packsArray)
                        {
                            packsArray = new JsonArray();
                            rootObj["bundlePacks"] = packsArray;
                        }

                        foreach (var pack in packsToAdd)
                        {
                            var packJson = JsonSerializer.Serialize(pack, _jsonOptions);
                            var packNode = JsonNode.Parse(packJson);
                            if (packNode != null)
                            {
                                packsArray.Add(packNode);
                            }
                        }

                        await File.WriteAllTextAsync(packsPath, rootObj.ToJsonString(_jsonOptions), cancellationToken).ConfigureAwait(false);
                        _logger.LogDebug("Updated ModBundlePacks.json with imported BIG packs at {Path}", packsPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to append new packs to ModBundlePacks.json, rewriting with new packs");
                    var bundlePacksConfig = new
                    {
                        BundlePacks = packsToAdd.ToArray(),
                    };
                    var packsJson = JsonSerializer.Serialize(bundlePacksConfig, _jsonOptions);
                    await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);
                }
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
                _logger.LogDebug("Created directory: {Directory}", dir);
            }

            return ProjectOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project directory structure at {ProjectDir}", projectDir);
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

            if (template?.Name == ProjectTemplate.CustomIcons.Name)
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
            _logger.LogWarning(ex, "Failed to create sample files");
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
            _logger.LogDebug("Created ModBundleItems.json at {Path}", itemsPath);
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
            _logger.LogDebug("Created ModBundlePacks.json at {Path}", packsPath);
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
            _logger.LogDebug("Created sample INI at {Path}", sampleIniPath);
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
            _logger.LogDebug("Created README at {Path}", readmePath);
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
        var dir = Path.GetDirectoryName(_recentProjectsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = new FileStream(
            _recentProjectsPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            IoConstants.DefaultFileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, recentProjects, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
