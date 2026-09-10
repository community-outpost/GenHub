using System;
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
using GenHub.Core.Models.Enums;
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

        cancellationToken.ThrowIfCancellationRequested();
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

            project.ProjectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;

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

            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            {
                validationErrors.Add($"Project file not found at: {projectPath}");
            }

            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                validationErrors.Add("Invalid project path");
            }
            else
            {
                EnsureProjectDirectories(projectDir, project.Directories);
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

    private static void EnsureProjectDirectories(string projectDir, ProjectDirectories directories)
    {
        var configsDir = Path.Combine(projectDir, directories.Configs);
        if (!Directory.Exists(configsDir))
        {
            var fallbackConfigDir = Path.Combine(projectDir, "config");
            if (Directory.Exists(fallbackConfigDir))
            {
                directories.Configs = "config";
            }
            else
            {
                Directory.CreateDirectory(configsDir);
            }
        }

        var gameFilesDir = Path.Combine(projectDir, directories.GameFilesEdited);
        if (!Directory.Exists(gameFilesDir))
        {
            Directory.CreateDirectory(gameFilesDir);
        }

        var buildDir = Path.Combine(projectDir, directories.Build);
        if (!Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        var releaseDir = Path.Combine(projectDir, directories.Release);
        if (!Directory.Exists(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
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
            var discoveredProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Read stored recent projects
            if (File.Exists(_recentProjectsPath))
            {
                try
                {
                    var jsonContent = await File.ReadAllTextAsync(_recentProjectsPath, cancellationToken).ConfigureAwait(false);
                    var recentProjects = JsonSerializer.Deserialize<List<string>>(jsonContent, _jsonOptions);
                    if (recentProjects != null)
                    {
                        foreach (var path in recentProjects.Where(File.Exists))
                        {
                            discoveredProjects.Add(path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse recent projects file");
                }
            }

            // 2. Discover in common folders
            await Task.Run(() =>
            {
                var searchLocations = BuildSearchLocations();
                DiscoverProjectsInSearchLocations(searchLocations, discoveredProjects);
            }, cancellationToken).ConfigureAwait(false);

            var validProjects = discoveredProjects
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(maxCount)
                .ToList();

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

    private static List<string> BuildSearchLocations()
    {
        var searchLocations = new List<string>();

        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(myDocs) && Directory.Exists(myDocs))
        {
            searchLocations.Add(Path.Combine(myDocs, ModBuilderDirName));
            searchLocations.Add(Path.Combine(myDocs, "GenHub"));
            searchLocations.Add(Path.Combine(myDocs, "GenHub", ModBuilderDirName));
            searchLocations.Add(Path.Combine(myDocs, "GenHub", "Projects"));
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
        {
            searchLocations.Add(Path.Combine(desktop, ModBuilderDirName));
        }

        var sampleDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleProjects");
        if (Directory.Exists(sampleDir))
        {
            searchLocations.Add(sampleDir);
        }

        return searchLocations;
    }

    private void DiscoverProjectsInSearchLocations(IEnumerable<string> searchLocations, HashSet<string> discoveredProjects)
    {
        foreach (var loc in searchLocations.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var files = Directory.GetFiles(loc, "*.mbproj", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    discoveredProjects.Add(f);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Error scanning directory {Location} for projects", loc);
            }
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
