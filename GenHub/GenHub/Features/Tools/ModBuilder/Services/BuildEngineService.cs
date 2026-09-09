using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Central orchestrator for the 5-stage ModBuilder build pipeline.
/// Manages change detection, event system, and build execution.
/// </summary>
public sealed class BuildEngineService : IBuildEngineService
{
    private readonly IBuildCacheService _cacheService;
    private readonly IFileConversionService _fileConversionService;
    private readonly IMd5HashProvider _hashProvider;
    private readonly IConfigurationLoaderService _configurationLoaderService;
    private readonly IArchiveService _archiveService;
    private readonly IServiceScopeFactory? _serviceScopeFactory;
    private readonly ILocalContentService? _localContentService;
    private readonly ILogger<BuildEngineService> _logger;

    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private readonly object _abortLock = new();

    private CancellationTokenSource? _abortTokenSource;
    private bool _isRunning;
    private BuildStructure? _cachedBuildStructure;
    private string? _cachedConfigHash;
    private int _filesProcessed;
    private int _filesSkipped;
    private int _filesFailed;
    private string? _lastErrorMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildEngineService"/> class using DI scope factory.
    /// </summary>
    public BuildEngineService(
        IBuildCacheService cacheService,
        IFileConversionService fileConversionService,
        IMd5HashProvider hashProvider,
        IConfigurationLoaderService configurationLoaderService,
        IArchiveService archiveService,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<BuildEngineService> logger)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _fileConversionService = fileConversionService ?? throw new ArgumentNullException(nameof(fileConversionService));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _configurationLoaderService = configurationLoaderService ?? throw new ArgumentNullException(nameof(configurationLoaderService));
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildEngineService"/> class with direct local content service (for tests).
    /// </summary>
    public BuildEngineService(
        IBuildCacheService cacheService,
        IFileConversionService fileConversionService,
        IMd5HashProvider hashProvider,
        IConfigurationLoaderService configurationLoaderService,
        IArchiveService archiveService,
        ILocalContentService localContentService,
        ILogger<BuildEngineService> logger)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _fileConversionService = fileConversionService ?? throw new ArgumentNullException(nameof(fileConversionService));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _configurationLoaderService = configurationLoaderService ?? throw new ArgumentNullException(nameof(configurationLoaderService));
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _localContentService = localContentService ?? throw new ArgumentNullException(nameof(localContentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Event triggered when a bundle event occurs during the build process.
    /// </summary>
    public event EventHandler<BundleEventArgs>? BundleEventTriggered;

    /// <inheritdoc/>
    public async Task<BuildOperationResult> ExecuteBuildAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        List<string> selectedBundlePacks,
        BuildStep buildSteps,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();

        if (!await _buildLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Build already in progress");
            return BuildOperationResult.CreateFailure("Build already in progress", 0, 0, 0, sw.Elapsed);
        }

        try
        {
            _logger.LogInformation("ExecuteBuildAsync called for project: {ProjectName} with steps: {Steps}", project.Name, buildSteps);

            // reset counters
            _filesProcessed = 0;
            _filesSkipped = 0;
            _filesFailed = 0;
            _lastErrorMessage = null;

            // get or create cached build structure
            var buildStructure = await GetOrCreateBuildStructureAsync(project, configuration, buildSteps, cancellationToken)
                .ConfigureAwait(false);

            if (selectedBundlePacks != null && buildStructure.Setup != null)
            {
                buildStructure.Setup.SelectedPacks = selectedBundlePacks;
            }

            // wrap IProgress<string> to IProgress<BuildProgress>
            IProgress<BuildProgress>? buildProgress = null;
            if (progress != null)
            {
                buildProgress = new Progress<BuildProgress>(p => progress.Report(p.CurrentStep));
            }

            var success = await RunAsync(buildStructure, buildProgress, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();

            return success
                ? BuildOperationResult.CreateSuccess(_filesProcessed, _filesSkipped, _filesFailed, sw.Elapsed)
                : BuildOperationResult.CreateFailure(_lastErrorMessage ?? "Build failed", _filesProcessed, _filesSkipped, _filesFailed, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteBuildAsync failed");
            sw.Stop();
            return BuildOperationResult.CreateFailure($"Build failed: {ex.Message}", _filesProcessed, _filesSkipped, _filesFailed, sw.Elapsed);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task<bool> CanAbortAsync(CancellationToken cancellationToken = default)
    {
        lock (_abortLock)
        {
            return Task.FromResult(_isRunning && _abortTokenSource != null);
        }
    }

    /// <inheritdoc/>
    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        lock (_abortLock)
        {
            if (_isRunning && _abortTokenSource != null)
            {
                _logger.LogInformation("Aborting build");
                _abortTokenSource.Cancel();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void InvalidateBuildStructureCache()
    {
        _logger.LogDebug("Invalidating build structure cache");
        _cachedBuildStructure = null;
        _cachedConfigHash = null;
    }

    private async Task<bool> RunAsync(
        BuildStructure buildStructure,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_abortLock)
            {
                _isRunning = true;
                _abortTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            _logger.LogInformation("Starting ModBuilder build pipeline");

            var steps = ResolveBuildSteps(buildStructure.Setup.Step);
            if (steps == BuildStep.None)
            {
                _logger.LogWarning("BuildStep is None, nothing to do");
                return true;
            }

            _lastErrorMessage = null;
            var success = await ExecutePipelineStagesAsync(buildStructure, steps, progress, _abortTokenSource.Token).ConfigureAwait(false);

            _logger.LogInformation("Build pipeline completed with success={Success}", success);
            return success;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Build was cancelled");
            _lastErrorMessage = "Build was cancelled by user";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build pipeline failed with exception");
            _lastErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            lock (_abortLock)
            {
                _isRunning = false;
                _abortTokenSource?.Dispose();
                _abortTokenSource = null;
            }
        }
    }

    private static BuildStep ResolveBuildSteps(BuildStep steps)
    {
        if (steps == BuildStep.None)
        {
            return BuildStep.None;
        }

        if ((steps & (BuildStep.Release | BuildStep.CreateManifest)) != 0)
        {
            steps |= BuildStep.Build;
        }

        if ((steps & BuildStep.Build) != 0)
        {
            steps |= BuildStep.PostBuild;
        }

        if ((steps & (BuildStep.Clean | BuildStep.Build | BuildStep.CreateManifest)) != 0)
        {
            steps |= BuildStep.PreBuild;
        }

        return steps;
    }

    private async Task<bool> ExecutePipelineStagesAsync(
        BuildStructure buildStructure,
        BuildStep steps,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var setup = buildStructure.Setup;

        var stages = new (BuildStep Step, Func<Task<bool>> Action, string ErrorName)[]
        {
            (BuildStep.PreBuild, () => PreBuildAsync(buildStructure, progress, cancellationToken), "PreBuild stage failed"),
            (BuildStep.Clean, () => CleanAsync(setup, progress, cancellationToken), "Clean stage failed"),
            (BuildStep.Build, () => BuildAsync(setup, progress, cancellationToken), "Build stage failed"),
            (BuildStep.PostBuild, () => PostBuildAsync(setup, progress, cancellationToken), "PostBuild stage failed"),
            (BuildStep.Release, () => ReleaseAsync(setup, progress, cancellationToken), "Release stage failed"),
            (BuildStep.CreateManifest, () => CreateManifestAsync(buildStructure, progress, cancellationToken), "Create Manifest stage failed"),
        };

        foreach (var (step, action, errorName) in stages)
        {
            if ((steps & step) != 0)
            {
                var success = await action().ConfigureAwait(false);
                if (!success)
                {
                    if (string.IsNullOrEmpty(_lastErrorMessage))
                    {
                        _lastErrorMessage = errorName;
                    }

                    _logger.LogError("Stage {Stage} failed, aborting pipeline", step);
                    return false;
                }
            }
        }

        return true;
    }

    private async Task<bool> PreBuildAsync(
        BuildStructure buildStructure,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PreBuild stage started");
        progress?.Report(new BuildProgress { CurrentStep = "PreBuild: Validating configuration" });

        FireBundleEvent(BundleEventType.OnPreBuild, null);

        var validationResult = ValidateProjectConfiguration(buildStructure.Configuration);
        if (!validationResult.IsValid)
        {
            _logger.LogError("Project configuration validation failed: {Errors}", string.Join(", ", validationResult.Errors));
            _lastErrorMessage = $"Configuration validation failed: {string.Join(", ", validationResult.Errors)}";
            return false;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private static (bool IsValid, List<string> Errors) ValidateProjectConfiguration(BuildConfiguration configuration)
    {
        var errors = new List<string>();

        if (configuration == null)
        {
            errors.Add("Configuration is null");
            return (false, errors);
        }

        if (configuration.Items == null || configuration.Items.Count == 0)
        {
            errors.Add("No bundle items configured");
        }

        if (configuration.Packs == null || configuration.Packs.Count == 0)
        {
            errors.Add("No bundle packs configured");
        }

        if (configuration.Items != null)
        {
            foreach (var item in configuration.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    errors.Add("Bundle item missing name");
                }

                if (item.Files == null || item.Files.Count == 0)
                {
                    errors.Add($"Bundle item '{item.Name}' has no files");
                }
            }
        }

        return (errors.Count == 0, errors);
    }

    private async Task<bool> CleanAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Clean stage started");
        progress?.Report(new BuildProgress { CurrentStep = "Cleaning build directories" });

        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;

        try
        {
            if (Directory.Exists(buildDir))
            {
                Directory.Delete(buildDir, true);
                _logger.LogInformation("Deleted build directory: {BuildDir}", buildDir);
            }

            if (Directory.Exists(releaseDir))
            {
                Directory.Delete(releaseDir, true);
                _logger.LogInformation("Deleted release directory: {ReleaseDir}", releaseDir);
            }

            _cacheService.Clear();
            _logger.LogInformation("Build cache cleared");

            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean build directories");
            _lastErrorMessage = $"Failed to clean build directories: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> BuildAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Build stage started");

        if (!string.IsNullOrEmpty(setup.Folders?.AbsBuildDir))
        {
            Directory.CreateDirectory(setup.Folders.AbsBuildDir);
        }

        FireBundleEvent(BundleEventType.OnBuild, null);

        var success = true;
        success &= await BuildStageAsync(BuildIndex.RawBundleItem, setup, progress, cancellationToken).ConfigureAwait(false);
        success &= await BuildStageAsync(BuildIndex.BigBundleItem, setup, progress, cancellationToken).ConfigureAwait(false);

        return success;
    }

    private async Task<bool> BuildStageAsync(
        BuildIndex stage,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Building stage: {Stage}", stage);
        progress?.Report(new BuildProgress
        {
            CurrentIndex = stage,
            CurrentStep = $"Building {stage}",
        });

        var startEvent = GetStartBuildEvent(stage);
        FireBundleEvent(startEvent, null);

        var cachePath = GetCachePath(stage, setup);
        var cacheDir = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        await _cacheService.LoadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);

        var initialFailed = Volatile.Read(ref _filesFailed);
        var filesToProcess = GetFilesForStage(stage);

        _logger.LogInformation("Processing {Count} files for stage {Stage}", filesToProcess.Count, stage);

        await ExecuteStageFilesAsync(stage, setup, filesToProcess, progress, cancellationToken).ConfigureAwait(false);

        await _cacheService.SaveCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);

        var finishEvent = GetFinishBuildEvent(stage);
        FireBundleEvent(finishEvent, null);

        var finalFailed = Volatile.Read(ref _filesFailed);
        return finalFailed == initialFailed;
    }

    private async Task ExecuteStageFilesAsync(
        BuildIndex stage,
        BuildSetup setup,
        IReadOnlyList<string> filesToProcess,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        switch (stage)
        {
            case BuildIndex.BigBundleItem:
                await ExecuteBigBundleItemStageAsync(setup, progress, cancellationToken).ConfigureAwait(false);
                break;
            case BuildIndex.ReleaseBundlePack:
                await ExecuteReleaseBundlePackStageAsync(setup, progress, cancellationToken).ConfigureAwait(false);
                break;
            case BuildIndex.RawBundleItem:
                await Parallel.ForEachAsync(
                    filesToProcess,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken,
                    },
                    async (filePath, ct) =>
                    {
                        await ProcessSingleFileAsync(filePath, stage, setup, progress, ct).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                break;
            default:
                foreach (var filePath in filesToProcess)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessSingleFileAsync(filePath, stage, setup, progress, cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task ExecuteBigBundleItemStageAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var bundlesDir = Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir);

        if (setup.Bundles?.Items == null)
        {
            return;
        }

        if (!Directory.Exists(bundlesDir))
        {
            Directory.CreateDirectory(bundlesDir);
        }

        var bigItems = setup.Bundles.Items.Where(i => i.IsBig).ToList();
        var totalBigItems = bigItems.Count;
        var currentItem = 0;

        foreach (var item in bigItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentItem++;
            await BuildSingleBigBundleItemAsync(item, bundlesDir, progress, cancellationToken, currentItem, totalBigItems)
                .ConfigureAwait(false);
        }
    }

    private static string GetBigFileName(BundleItem item)
    {
        var suffix = item.BigSuffix ?? string.Empty;
        if (!suffix.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
        {
            suffix += ".big";
        }

        return $"{item.GetFullName()}{suffix}";
    }

    private async Task BuildSingleBigBundleItemAsync(
        BundleItem item,
        string bundlesDir,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken,
        int currentItem,
        int totalBigItems)
    {
        var bigFileName = GetBigFileName(item);
        var bigFilePath = Path.Combine(bundlesDir, bigFileName);

        var stagingDir = Path.Combine(bundlesDir, $".staging_{item.Name}");
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }

        Directory.CreateDirectory(stagingDir);

        try
        {
            var totalFiles = item.Files.Count;
            var currentFile = 0;

            foreach (var file in item.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentFile++;

                var sourceFile = file.AbsSourceFile;
                if (!File.Exists(sourceFile))
                {
                    _logger.LogWarning("File not found for BIG bundle: {FilePath}", sourceFile);
                    Interlocked.Increment(ref _filesFailed);
                    _lastErrorMessage = $"File not found for BIG bundle: {sourceFile}";
                    continue;
                }

                var targetRelPath = GetTargetRelativePath(file);
                var targetStagedFile = Path.Combine(stagingDir, targetRelPath);

                var targetStagedDir = Path.GetDirectoryName(targetStagedFile);
                if (!string.IsNullOrEmpty(targetStagedDir) && !Directory.Exists(targetStagedDir))
                {
                    Directory.CreateDirectory(targetStagedDir);
                }

                File.Copy(sourceFile, targetStagedFile, true);

                var fileProgress = (double)currentFile / totalFiles;
                var overallProgress = ((currentItem - 1) + fileProgress) / totalBigItems;

                progress?.Report(new BuildProgress
                {
                    CurrentIndex = BuildIndex.BigBundleItem,
                    CurrentStep = $"Packing {item.Name} ({currentFile}/{totalFiles}): {Path.GetFileName(sourceFile)}",
                    ProcessedFiles = currentFile,
                    TotalFiles = totalFiles,
                    PercentComplete = overallProgress * 100,
                    Percentage = overallProgress,
                });
            }

            var archiveProgress = new Progress<double>(p =>
            {
                var overallProgress = ((currentItem - 1) + p) / totalBigItems;
                progress?.Report(new BuildProgress
                {
                    CurrentIndex = BuildIndex.BigBundleItem,
                    CurrentStep = $"Compressing {item.Name}.big ({p:P0})",
                    ProcessedFiles = Volatile.Read(ref _filesProcessed),
                    PercentComplete = overallProgress * 100,
                    Percentage = overallProgress,
                });
            });

            var archiveResult = await _archiveService.CreateBigArchiveAsync(stagingDir, bigFilePath, archiveProgress, cancellationToken)
                .ConfigureAwait(false);

            if (!archiveResult.Success)
            {
                Interlocked.Increment(ref _filesFailed);
                _logger.LogError("Failed to create BIG archive for item {ItemName}: {Error}", item.Name, archiveResult.FirstError);
                _lastErrorMessage = $"Failed to create BIG archive for item {item.Name}: {archiveResult.FirstError}";
            }
            else
            {
                Interlocked.Increment(ref _filesProcessed);
                _logger.LogInformation("Successfully created BIG archive: {Path}", bigFilePath);
            }
        }
        finally
        {
            if (Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up staging directory: {StagingDir}", stagingDir);
                }
            }
        }
    }

    private static string GetTargetRelativePath(BundleFile file)
    {
        if (!string.IsNullOrEmpty(file.RelTargetFile))
        {
            return file.RelTargetFile;
        }

        if (!string.IsNullOrEmpty(file.AbsSourceParent))
        {
            var parentWithSep = file.AbsSourceParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (file.AbsSourceFile.StartsWith(parentWithSep, StringComparison.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(file.AbsSourceParent, file.AbsSourceFile);
                if (!rel.StartsWith("..", StringComparison.Ordinal))
                {
                    return rel;
                }
            }
        }

        return Path.GetFileName(file.AbsSourceFile);
    }

    private async Task ExecuteReleaseBundlePackStageAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var bundlesDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.BundlesSubdir);
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;

        if (setup.Bundles?.Packs == null)
        {
            return;
        }

        if (!Directory.Exists(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
        }

        var packs = setup.Bundles.Packs.Where(p => p.AllowBuild);
        if (setup.SelectedPacks != null && setup.SelectedPacks.Count > 0)
        {
            packs = packs.Where(p => setup.SelectedPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
        }

        foreach (var pack in packs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BuildSingleReleaseBundlePackAsync(pack, bundlesDir, releaseDir, buildDir, setup.Bundles.Items, progress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task BuildSingleReleaseBundlePackAsync(
        BundlePack pack,
        string bundlesDir,
        string releaseDir,
        string buildDir,
        IReadOnlyList<BundleItem>? items,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var zipFileName = $"{pack.GetFullName()}.zip";
        var zipFilePath = Path.Combine(releaseDir, zipFileName);

        var packStagingDir = Path.Combine(buildDir, ".staging_pack", pack.Name);
        if (Directory.Exists(packStagingDir))
        {
            Directory.Delete(packStagingDir, true);
        }

        Directory.CreateDirectory(packStagingDir);

        try
        {
            if (items != null)
            {
                StagePackBigFiles(pack, items, bundlesDir, packStagingDir);
            }

            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStep = $"Packaging release {pack.Name}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
            });

            var archiveResult = await _archiveService.CreateZipArchiveAsync(packStagingDir, zipFilePath, System.IO.Compression.CompressionLevel.Optimal, null, cancellationToken).ConfigureAwait(false);
            if (!archiveResult.Success)
            {
                Interlocked.Increment(ref _filesFailed);
                _logger.LogError("Failed to create ZIP archive for pack {PackName}: {Error}", pack.Name, archiveResult.FirstError);
                _lastErrorMessage = $"Failed to create ZIP archive for pack {pack.Name}: {archiveResult.FirstError}";
            }
            else
            {
                Interlocked.Increment(ref _filesProcessed);
                _logger.LogInformation("Successfully created ZIP archive: {Path}", zipFilePath);
            }
        }
        finally
        {
            if (Directory.Exists(packStagingDir))
            {
                try
                {
                    Directory.Delete(packStagingDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up pack staging directory: {StagingDir}", packStagingDir);
                }
            }
        }
    }

    private void StagePackBigFiles(BundlePack pack, IReadOnlyList<BundleItem> items, string bundlesDir, string packStagingDir)
    {
        foreach (var itemName in pack.ItemNames)
        {
            var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (item != null && item.IsBig)
            {
                var bigFileName = GetBigFileName(item);
                var srcBig = Path.Combine(bundlesDir, bigFileName);
                if (File.Exists(srcBig))
                {
                    var destBig = Path.Combine(packStagingDir, bigFileName);
                    File.Copy(srcBig, destBig, true);
                }
                else
                {
                    _logger.LogWarning("BIG bundle {BigFileName} missing when packaging pack {PackName}", bigFileName, pack.Name);
                }
            }
        }
    }

    private async Task ProcessSingleFileAsync(
        string filePath,
        BuildIndex stage,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found for stage {Stage}: {FilePath}", stage, filePath);
            Interlocked.Increment(ref _filesFailed);
            _lastErrorMessage = $"File not found: {filePath}";
            return;
        }

        var projectDir = setup.ProjectDir ?? Directory.GetCurrentDirectory();
        var cacheKey = Path.GetRelativePath(projectDir, filePath);

        // compute or reuse hash
        var currentMd5 = await _cacheService.ComputeOrReuseMd5Async(filePath, cancellationToken).ConfigureAwait(false);

        // check file status using cache
        var status = _cacheService.DetermineFileStatus(cacheKey, currentMd5, null);

        if (status is BuildFileStatus.Unchanged or BuildFileStatus.Irrelevant)
        {
            _logger.LogDebug("Skipping unchanged/irrelevant file: {FilePath}", filePath);
            Interlocked.Increment(ref _filesSkipped);
            return;
        }

        _logger.LogDebug("Processing file: {FilePath} (Status: {Status})", filePath, status);

        // process file based on stage
        var success = stage switch
        {
            BuildIndex.RawBundleItem => await ProcessRawBundleItemFileAsync(filePath, setup, cancellationToken).ConfigureAwait(false),
            _ => true,
        };

        if (success)
        {
            var fileInfo = new FileInfo(filePath);
            var mtime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
            _cacheService.AddFile(cacheKey, mtime, currentMd5);

            Interlocked.Increment(ref _filesProcessed);
            progress?.Report(new BuildProgress
            {
                CurrentIndex = stage,
                CurrentStep = $"Processed {Path.GetFileName(filePath)}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
            });
        }
        else
        {
            Interlocked.Increment(ref _filesFailed);
            _logger.LogError("Failed to process file: {FilePath}", filePath);
        }
    }

    private async Task<bool> ProcessRawBundleItemFileAsync(string filePath, BuildSetup setup, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var targetPath = GetTargetPathForFile(filePath, BuildIndex.RawBundleItem, setup);

        return extension switch
        {
            ".png" or ".tga" => await ConvertImageFileAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
            ".str" => await ConvertStringTableFileAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
            ".ini" => await ProcessIniFileAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
            _ => await CopyFileDirectlyAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<bool> ConvertImageFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var ddsTargetPath = Path.ChangeExtension(targetPath, ".dds");
        var result = await _fileConversionService.ConvertFileAsync(sourcePath, ddsTargetPath, "DDS", null, cancellationToken)
            .ConfigureAwait(false);

        return result.Success;
    }

    private async Task<bool> ConvertStringTableFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var csfTargetPath = Path.ChangeExtension(targetPath, ".csf");
        var result = await _fileConversionService.ConvertFileAsync(sourcePath, csfTargetPath, "CSF", null, cancellationToken)
            .ConfigureAwait(false);

        return result.Success;
    }

    private static async Task<bool> ProcessIniFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        return await CopyFileDirectlyAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> CopyFileDirectlyAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, IoConstants.DefaultFileBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, IoConstants.DefaultFileBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string GetTargetPathForFile(string sourcePath, BuildIndex stage, BuildSetup setup)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var fileName = Path.GetFileName(sourcePath);

        return stage switch
        {
            BuildIndex.RawBundleItem => Path.Combine(buildDir, ModBuilderConstants.RawBundleItemsSubdir, fileName),
            _ => Path.Combine(buildDir, fileName),
        };
    }

    private IReadOnlyList<string> GetFilesForStage(BuildIndex stage)
    {
        if (_cachedBuildStructure?.StageFiles.TryGetValue(stage, out var files) == true)
        {
            return files;
        }

        return [];
    }

    private async Task<bool> PostBuildAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PostBuild stage started");
        progress?.Report(new BuildProgress { CurrentStep = "PostBuild: Finalizing build" });

        FireBundleEvent(BundleEventType.OnPostBuild, null);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ReleaseAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Release stage started");
        progress?.Report(new BuildProgress
        {
            CurrentIndex = BuildIndex.ReleaseBundlePack,
            CurrentStep = "Creating release archives",
        });

        FireBundleEvent(BundleEventType.OnRelease, null);

        return await BuildStageAsync(BuildIndex.ReleaseBundlePack, setup, progress, cancellationToken).ConfigureAwait(false);
    }

    private static HashSet<string> ResolveManifestFilesToInclude(BuildSetup setup)
    {
        var filesToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (setup.SelectedPacks == null || setup.SelectedPacks.Count == 0 || setup.Bundles?.Packs == null || setup.Bundles.Items == null)
        {
            return filesToInclude;
        }

        foreach (var packName in setup.SelectedPacks)
        {
            var pack = setup.Bundles.Packs.FirstOrDefault(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));
            if (pack == null)
            {
                continue;
            }

            foreach (var itemName in pack.ItemNames)
            {
                var item = setup.Bundles.Items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
                if (item != null && item.IsBig)
                {
                    filesToInclude.Add(GetBigFileName(item));
                }
            }
        }

        return filesToInclude;
    }

    private static void StageManifestFiles(string stagingDir, string[] bigFiles, HashSet<string> filesToInclude)
    {
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }

        Directory.CreateDirectory(stagingDir);

        foreach (var file in bigFiles)
        {
            var fileName = Path.GetFileName(file);
            if (filesToInclude.Contains(fileName))
            {
                File.Copy(file, Path.Combine(stagingDir, fileName), overwrite: true);
            }
        }
    }

    private async Task<bool> CreateManifestAsync(
        BuildStructure buildStructure,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("CreateManifest stage started");
        progress?.Report(new BuildProgress
        {
            CurrentIndex = BuildIndex.CreateManifest,
            CurrentStep = "Creating local ContentManifest and storing in CAS",
        });

        FireBundleEvent(BundleEventType.OnCreateManifest, null);

        var setup = buildStructure.Setup;
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var bundlesDir = Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir);

        if (!Directory.Exists(bundlesDir))
        {
            _logger.LogError("Bundles directory does not exist: {BundlesDir}. Please run the Build step first.", bundlesDir);
            _lastErrorMessage = $"Bundles directory does not exist: {bundlesDir}";
            return false;
        }

        var bigFiles = Directory.GetFiles(bundlesDir, "*.big");
        if (bigFiles.Length == 0)
        {
            _logger.LogError("No .big bundle files found in {BundlesDir} to create manifest.", bundlesDir);
            _lastErrorMessage = $"No .big bundle files found in {bundlesDir}";
            return false;
        }

        var filesToInclude = ResolveManifestFilesToInclude(setup);
        var stagingDir = Path.Combine(buildDir, ".staging_manifest");
        var manifestContentDir = bundlesDir;

        if (filesToInclude.Count > 0)
        {
            StageManifestFiles(stagingDir, bigFiles, filesToInclude);
            manifestContentDir = stagingDir;
        }

        ILocalContentService localContentService;
        IDisposable? scopeToDispose = null;

        if (_localContentService != null)
        {
            localContentService = _localContentService;
        }
        else if (_serviceScopeFactory != null)
        {
            var scope = _serviceScopeFactory.CreateScope();
            scopeToDispose = scope;
            localContentService = scope.ServiceProvider.GetRequiredService<ILocalContentService>();
        }
        else
        {
            throw new InvalidOperationException("Neither ILocalContentService nor IServiceScopeFactory is available.");
        }

        try
        {
            var projectName = buildStructure.Project.Name;
            var targetGame = buildStructure.Project.TargetGame;

            var manifestResult = await localContentService.CreateLocalContentManifestAsync(
                manifestContentDir,
                projectName,
                ContentType.Mod,
                targetGame,
                sourcePath: bundlesDir,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!manifestResult.Success)
            {
                _logger.LogError("Failed to create local content manifest: {Error}", manifestResult.FirstError);
                _lastErrorMessage = $"Failed to create manifest: {manifestResult.FirstError}";
                return false;
            }

            var manifest = manifestResult.Data;
            _logger.LogInformation(
                "Successfully created local ContentManifest '{ManifestId}' for project '{ProjectName}' in CAS",
                manifest?.Id,
                projectName);

            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.CreateManifest,
                CurrentStep = $"Local manifest {manifest?.Id} created successfully",
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while creating local ContentManifest");
            _lastErrorMessage = $"Failed to create manifest: {ex.Message}";
            return false;
        }
        finally
        {
            scopeToDispose?.Dispose();

            if (Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up manifest staging directory: {StagingDir}", stagingDir);
                }
            }
        }
    }

    private static BundleEventType GetStartBuildEvent(BuildIndex stage)
    {
        return stage switch
        {
            BuildIndex.RawBundleItem => BundleEventType.OnStartBuildRawBundleItem,
            BuildIndex.BigBundleItem => BundleEventType.OnStartBuildBigBundleItem,
            BuildIndex.RawBundlePack => BundleEventType.OnStartBuildRawBundlePack,
            BuildIndex.ReleaseBundlePack => BundleEventType.OnStartBuildReleaseBundlePack,
            BuildIndex.CreateManifest => BundleEventType.OnStartCreateManifest,
            _ => BundleEventType.OnBuild,
        };
    }

    private static BundleEventType GetFinishBuildEvent(BuildIndex stage)
    {
        return stage switch
        {
            BuildIndex.RawBundleItem => BundleEventType.OnFinishBuildRawBundleItem,
            BuildIndex.BigBundleItem => BundleEventType.OnFinishBuildBigBundleItem,
            BuildIndex.RawBundlePack => BundleEventType.OnFinishBuildRawBundlePack,
            BuildIndex.ReleaseBundlePack => BundleEventType.OnFinishBuildReleaseBundlePack,
            BuildIndex.CreateManifest => BundleEventType.OnFinishCreateManifest,
            _ => BundleEventType.OnPostBuild,
        };
    }

    private static string GetCachePath(BuildIndex stage, BuildSetup setup)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        return Path.Combine(buildDir, $"{stage}.json");
    }

    private void FireBundleEvent(BundleEventType eventType, string? bundleName)
    {
        try
        {
            _logger.LogDebug("Firing bundle event: {EventType}", eventType);
            BundleEventTriggered?.Invoke(this, new BundleEventArgs
            {
                EventType = eventType,
                BundleItemName = bundleName,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error firing bundle event {EventType}", eventType);
        }
    }

    private async Task<BuildStructure> GetOrCreateBuildStructureAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        BuildStep buildSteps,
        CancellationToken cancellationToken)
    {
        var configHash = await ComputeConfigHashAsync(project, configuration, cancellationToken).ConfigureAwait(false);

        if (_cachedBuildStructure != null && _cachedConfigHash == configHash)
        {
            _logger.LogDebug("Reusing cached build structure (hash matches: {Hash})", configHash);
            _cachedBuildStructure.Setup.Step = buildSteps;
            return _cachedBuildStructure;
        }

        _logger.LogInformation("Creating new build structure (config changed or first build)");

        var buildStructure = await CreateBuildStructureAsync(project, configuration, buildSteps, cancellationToken)
            .ConfigureAwait(false);

        _cachedBuildStructure = buildStructure;
        _cachedConfigHash = configHash;

        return buildStructure;
    }

    private async Task<string> ComputeConfigHashAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hashParts = new List<string>();

        if (!string.IsNullOrEmpty(project.ProjectDir) && Directory.Exists(project.ProjectDir))
        {
            var projectDirInfo = new DirectoryInfo(project.ProjectDir);
            hashParts.Add($"{project.ProjectDir}:{projectDirInfo.LastWriteTimeUtc.Ticks}");
        }

        foreach (var configFile in configuration.LoadedConfigFiles.Where(File.Exists))
        {
            var fileInfo = new FileInfo(configFile);
            hashParts.Add($"{configFile}:{fileInfo.LastWriteTimeUtc.Ticks}");
        }

        foreach (var bundleConfig in project.BundleConfigs)
        {
            var absolutePath = Path.IsPathRooted(bundleConfig)
                ? bundleConfig
                : Path.Combine(project.ProjectDir, bundleConfig);

            if (File.Exists(absolutePath))
            {
                var fileInfo = new FileInfo(absolutePath);
                hashParts.Add($"{absolutePath}:{fileInfo.LastWriteTimeUtc.Ticks}");
            }
        }

        if (configuration.Items != null)
        {
            foreach (var item in configuration.Items)
            {
                hashParts.Add($"Item:{item.Name}:{item.Files.Count}");
                foreach (var f in item.Files)
                {
                    hashParts.Add($"File:{f.AbsSourceFile}");
                }
            }
        }

        if (configuration.Packs != null)
        {
            foreach (var pack in configuration.Packs)
            {
                hashParts.Add($"Pack:{pack.Name}:{string.Join(",", pack.ItemNames)}");
            }
        }

        var sourceDir = Path.Combine(project.ProjectDir, !string.IsNullOrWhiteSpace(project.Directories?.GameFilesEdited) ? project.Directories.GameFilesEdited : ModBuilderConstants.GameFilesEditedDir);
        if (Directory.Exists(sourceDir))
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(file);
                hashParts.Add($"{file}:{fi.LastWriteTimeUtc.Ticks}");
            }
        }

        var combinedString = string.Join("|", hashParts);
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempFile, combinedString, cancellationToken)
                .ConfigureAwait(false);
            return await _hashProvider.ComputeFileHashAsync(tempFile, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task<BuildStructure> CreateBuildStructureAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        BuildStep buildSteps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Resolving wildcards in configuration");
        configuration = await _configurationLoaderService.ResolveWildcardsAsync(configuration, cancellationToken)
            .ConfigureAwait(false);

        var projectDir = project.ProjectDir;
        var defaultBuild = !string.IsNullOrWhiteSpace(project.Directories?.Build) ? project.Directories.Build : ModBuilderConstants.DefaultBuildDir;
        var defaultRelease = !string.IsNullOrWhiteSpace(project.Directories?.Release) ? project.Directories.Release : ModBuilderConstants.DefaultReleaseDir;

        if (!string.IsNullOrEmpty(projectDir))
        {
            if (string.IsNullOrEmpty(configuration.Folders.AbsBuildDir))
            {
                configuration.Folders.AbsBuildDir = Path.Combine(projectDir, defaultBuild);
            }

            if (string.IsNullOrEmpty(configuration.Folders.AbsReleaseDir))
            {
                configuration.Folders.AbsReleaseDir = Path.Combine(projectDir, defaultRelease);
            }
        }

        var gameDir = !string.IsNullOrEmpty(configuration.Folders.AbsGameDir)
            ? configuration.Folders.AbsGameDir
            : project.GameDir ?? string.Empty;

        var setup = new BuildSetup
        {
            Step = buildSteps,
            ProjectDir = project.ProjectDir,
            Folders = new Folders
            {
                AbsBuildDir = configuration.Folders.AbsBuildDir,
                AbsReleaseDir = configuration.Folders.AbsReleaseDir,
                AbsGameDir = gameDir,
            },
            Bundles = new Bundles
            {
                Items = configuration.Items.ToList(),
                Packs = configuration.Packs.ToList(),
            },
            Runner = new Runner(),
            RunnerConfig = configuration.Runner,
        };

        var stageFiles = PopulateStageFiles(setup, configuration);

        var bundleItems = configuration.Items.ToDictionary(item => item.Name, item => item);
        var bundlePacks = configuration.Packs.ToDictionary(pack => pack.Name, pack => pack);

        return new BuildStructure
        {
            Project = project,
            Configuration = configuration,
            Setup = setup,
            StageFiles = stageFiles,
            BundleItems = bundleItems,
            BundlePacks = bundlePacks,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static Dictionary<BuildIndex, List<string>> PopulateStageFiles(
        BuildSetup setup,
        BuildConfiguration configuration)
    {
        var stageFiles = new Dictionary<BuildIndex, List<string>>();

        var rawBundleItemFiles = CollectRawBundleItemFiles(configuration);
        stageFiles[BuildIndex.RawBundleItem] = rawBundleItemFiles;

        var bigBundleItemFiles = CollectBigBundleItemFiles(setup, configuration);
        stageFiles[BuildIndex.BigBundleItem] = bigBundleItemFiles;

        var releaseBundlePackFiles = CollectReleaseBundlePackFiles(setup, configuration);
        stageFiles[BuildIndex.ReleaseBundlePack] = releaseBundlePackFiles;

        return stageFiles;
    }

    private static List<string> CollectRawBundleItemFiles(BuildConfiguration configuration)
    {
        var files = new List<string>();
        foreach (var item in configuration.Items)
        {
            foreach (var file in item.Files)
            {
                if (File.Exists(file.AbsSourceFile))
                {
                    files.Add(file.AbsSourceFile);
                }
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> CollectBigBundleItemFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        return configuration.Items
            .Where(item => item.IsBig)
            .Select(item => Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, GetBigFileName(item)))
            .ToList();
    }

    private static List<string> CollectReleaseBundlePackFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;
        return configuration.Packs
            .Where(pack => pack.AllowBuild)
            .Select(pack => Path.Combine(releaseDir, $"{pack.GetFullName()}.zip"))
            .ToList();
    }
}
