using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for managing ModBuilder project configurations (.mbproj files).
/// </summary>
public sealed class ProjectConfigService : IProjectConfigService
{
    private const string ProjectFileExtension = ".mbproj";
    private const string RecentProjectsFileName = "recent_projects.json";
    private readonly ILogger<ProjectConfigService> _logger;
    private readonly string _recentProjectsPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, bool> _fileExistsCache = new();

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
            "ModBuilder",
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
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Project path cannot be empty",
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
            if (FileExistsCached(projectPath))
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
                GameInstallationId = gameInstallationId,
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
                await CreateSampleFilesAsync(projectDir, project.Directories, cancellationToken).ConfigureAwait(false);
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
                    "Project path cannot be empty",
                    sw.Elapsed);
            }

            if (!FileExistsCached(projectPath))
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    $"Project file not found: {projectPath}",
                    sw.Elapsed);
            }

            // Read and deserialize project file using streaming
            await using var stream = new FileStream(
                projectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var project = await JsonSerializer.DeserializeAsync<ModBuilderProject>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);

            if (project == null)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Failed to deserialize project file",
                    sw.Elapsed);
            }

            // Validate integrity if requested
            if (validateIntegrity)
            {
                var validationResult = await ValidateProjectAsync(projectPath, project, cancellationToken).ConfigureAwait(false);
                if (!validationResult.Success)
                {
                    return ProjectOperationResult<ModBuilderProject>.CreateValidationFailure(
                        "Project validation failed",
                        validationResult.Errors,
                        sw.Elapsed);
                }
            }

            // Add to recent projects
            await AddToRecentProjectsAsync(projectPath, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Loaded ModBuilder project from {ProjectPath}", projectPath);

            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateSuccess(project, sw.Elapsed);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse project file at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                $"Invalid project file format: {ex.Message}",
                sw.Elapsed);
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
                    "Project path cannot be empty",
                    sw.Elapsed);
            }

            if (project == null)
            {
                return ProjectOperationResult<ModBuilderProject>.CreateFailure(
                    "Project cannot be null",
                    sw.Elapsed);
            }

            // Update last modified timestamp
            project.LastModified = DateTime.UtcNow;

            // Ensure directory exists
            var projectDir = Path.GetDirectoryName(projectPath);
            if (!string.IsNullOrEmpty(projectDir) && !Directory.Exists(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            // Serialize and save using streaming
            await using var stream = new FileStream(
                projectPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await JsonSerializer.SerializeAsync(stream, project, _jsonOptions, cancellationToken).ConfigureAwait(false);

            // Invalidate cache for the saved file
            InvalidateFileExistsCache(projectPath);

            _logger.LogInformation("Saved ModBuilder project to {ProjectPath}", projectPath);

            sw.Stop();
            return ProjectOperationResult<ModBuilderProject>.CreateSuccess(project, sw.Elapsed);
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
        var validationErrors = new List<string>();

        try
        {
            if (project == null)
            {
                return ProjectOperationResult<bool>.CreateFailure(
                    "Project cannot be null",
                    sw.Elapsed);
            }

            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                validationErrors.Add("Invalid project path");
            }
            else
            {
                // Validate directory structure
                var requiredDirs = new[]
                {
                    Path.Combine(projectDir, project.Directories.Configs),
                    Path.Combine(projectDir, project.Directories.GameFilesEdited),
                    Path.Combine(projectDir, project.Directories.Build),
                    Path.Combine(projectDir, project.Directories.Release),
                };

                foreach (var dir in requiredDirs)
                {
                    if (!Directory.Exists(dir))
                    {
                        validationErrors.Add($"Missing directory: {Path.GetFileName(dir)}");
                    }
                }

                // Validate bundle configs exist
                foreach (var bundleConfig in project.BundleConfigs)
                {
                    var configPath = Path.Combine(projectDir, project.Directories.Configs, bundleConfig);
                    if (!FileExistsCached(configPath))
                    {
                        validationErrors.Add($"Missing bundle config: {bundleConfig}");
                    }
                }
            }

            sw.Stop();

            if (validationErrors.Count > 0)
            {
                return ProjectOperationResult<bool>.CreateValidationFailure(
                    "Project validation failed",
                    validationErrors,
                    sw.Elapsed);
            }

            return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate project at {ProjectPath}", projectPath);
            sw.Stop();
            return ProjectOperationResult<bool>.CreateFailure(
                $"Failed to validate project: {ex.Message}",
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
            if (!FileExistsCached(_recentProjectsPath))
            {
                sw.Stop();
                return ProjectOperationResult<List<string>>.CreateSuccess(new List<string>(), sw.Elapsed);
            }

            var jsonContent = await File.ReadAllTextAsync(_recentProjectsPath, cancellationToken).ConfigureAwait(false);
            var recentProjects = JsonSerializer.Deserialize<List<string>>(jsonContent, _jsonOptions)
                ?? new List<string>();

            // Filter out non-existent projects and limit count
            var validProjects = recentProjects
                .Where(FileExistsCached)
                .Take(maxCount)
                .ToList();

            // Update the file if we filtered out any invalid projects
            if (validProjects.Count != recentProjects.Count)
            {
                await SaveRecentProjectsAsync(validProjects, cancellationToken).ConfigureAwait(false);
            }

            sw.Stop();
            return ProjectOperationResult<List<string>>.CreateSuccess(validProjects, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent projects");
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
                    "Project path cannot be empty",
                    sw.Elapsed);
            }

            var recentProjectsResult = await GetRecentProjectsAsync(100, cancellationToken).ConfigureAwait(false);
            var recentProjects = recentProjectsResult.Success
                ? recentProjectsResult.Data!
                : new List<string>();

            // Remove if already exists (to move it to the top)
            recentProjects.Remove(projectPath);

            // Add to the beginning
            recentProjects.Insert(0, projectPath);

            // Save updated list
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
                    "Project path cannot be empty",
                    sw.Elapsed);
            }

            var recentProjectsResult = await GetRecentProjectsAsync(100, cancellationToken).ConfigureAwait(false);
            if (!recentProjectsResult.Success)
            {
                sw.Stop();
                return ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed);
            }

            var recentProjects = recentProjectsResult.Data!;
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
                .Where(FileExistsCached)
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
            if (!loadResult.Success)
            {
                sw.Stop();
                return ProjectOperationResult<bool>.CreateFailure(
                    loadResult.Errors,
                    sw.Elapsed);
            }

            var project = loadResult.Data!;
            project.LastBuild = DateTime.UtcNow;

            var saveResult = await SaveProjectAsync(projectPath, project, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            return saveResult.Success
                ? ProjectOperationResult<bool>.CreateSuccess(true, sw.Elapsed)
                : ProjectOperationResult<bool>.CreateFailure(saveResult.Errors, sw.Elapsed);
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

    /// <summary>
    /// Invalidates the entire file existence cache.
    /// </summary>
    public void InvalidateFileExistsCache()
    {
        _fileExistsCache.Clear();
    }

    /// <summary>
    /// Invalidates a specific file path in the file existence cache.
    /// </summary>
    /// <param name="path">The file path to invalidate.</param>
    public void InvalidateFileExistsCache(string path)
    {
        _fileExistsCache.TryRemove(path, out _);
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

            foreach (var dir in dirsToCreate)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    _logger.LogDebug("Created directory: {Directory}", dir);
                }
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
    /// Creates sample files for a new project.
    /// </summary>
    /// <param name="projectDir">The project directory path.</param>
    /// <param name="directories">The directory configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateSampleFilesAsync(
        string projectDir,
        ProjectDirectories directories,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create a sample bundles.json file
            var configsDir = Path.Combine(projectDir, directories.Configs);
            var sampleBundlePath = Path.Combine(configsDir, "bundles.json");

            if (!FileExistsCached(sampleBundlePath))
            {
                var sampleBundle = new
                {
                    bundles = new[]
                    {
                        new
                        {
                            name = "MyMod",
                            description = "Sample mod bundle",
                            items = Array.Empty<object>(),
                        }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(sampleBundle, _jsonOptions);
                await File.WriteAllTextAsync(sampleBundlePath, jsonContent, cancellationToken).ConfigureAwait(false);
                InvalidateFileExistsCache(sampleBundlePath);
                _logger.LogDebug("Created sample bundle config at {Path}", sampleBundlePath);
            }

            // Create a README in GameFilesEdited
            var gameFilesDir = Path.Combine(projectDir, directories.GameFilesEdited);
            var readmePath = Path.Combine(gameFilesDir, "README.txt");

            if (!FileExistsCached(readmePath))
            {
                var readmeContent = "Place your modified game files in this directory.\n" +
                                  "Maintain the same folder structure as the game's Data folder.";
                await File.WriteAllTextAsync(readmePath, readmeContent, cancellationToken).ConfigureAwait(false);
                InvalidateFileExistsCache(readmePath);
                _logger.LogDebug("Created README at {Path}", readmePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create sample files");
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

    /// <summary>
    /// Checks if a file exists using cached results to reduce filesystem I/O.
    /// </summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the file exists; otherwise, false.</returns>
    private bool FileExistsCached(string path)
    {
        return _fileExistsCache.GetOrAdd(path, p => File.Exists(p));
    }
}
