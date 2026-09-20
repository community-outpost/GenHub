using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContentManifest = GenHub.Core.Models.Manifest.ContentManifest;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Central orchestrator for the 5-stage ModBuilder build pipeline.
/// Manages change detection, event system, and build execution.
/// </summary>
/// <param name="cacheService">The build cache service.</param>
/// <param name="fileConversionService">The file conversion service.</param>
/// <param name="hashProvider">The MD5 hash provider.</param>
/// <param name="configurationLoaderService">The configuration loader service.</param>
/// <param name="archiveService">The archive service.</param>
/// <param name="serviceScopeFactory">The service scope factory for resolving scoped dependencies.</param>
/// <param name="logger">The logger instance.</param>
public sealed class BuildEngineService(
    IBuildCacheService cacheService,
    IFileConversionService fileConversionService,
    IMd5HashProvider hashProvider,
    IConfigurationLoaderService configurationLoaderService,
    IArchiveService archiveService,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<BuildEngineService> logger) : IBuildEngineService
{
    private sealed class StageProgressTracker(int totalFiles)
    {
        private int _filesDone;

        public int TotalFiles => totalFiles;

        public int IncrementDone() => Interlocked.Increment(ref _filesDone);
    }

    private sealed record PackStagingPaths(
        string BundlesDir,
        string PackStagingDir,
        string BuildDir,
        string? ProjectDir);

    private sealed record ManifestStageDirs(
        string BundlesDir,
        string BuildDir,
        string? ReleaseDir,
        string StagingRootDir);

    private sealed record ManifestPlanEntry(
        string Name,
        string Version,
        string? Publisher,
        ContentType ContentType,
        GameType TargetGame,
        IReadOnlyList<BundlePack> Packs);

    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private readonly object _abortLock = new();

    private CancellationTokenSource? _abortTokenSource;
    private bool _isRunning;
    private BuildStructure? _cachedBuildStructure;
    private string? _cachedConfigHash;
    private Dictionary<string, BundleFile>? _cachedSourceToBundleFileMap;
    private int _filesProcessed;
    private int _filesSkipped;
    private int _filesFailed;
    private string? _lastErrorMessage;

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
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        if (PathHelper.IsPathInsideAppDirectory(project.ProjectDir))
        {
            var msg = $"Cannot execute build within the application installation directory: '{project.ProjectDir}'. The project must be located in a user directory.";
            logger.LogError(msg);
            return BuildOperationResult.CreateFailure(msg);
        }

        var sw = Stopwatch.StartNew();

        if (!await _buildLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Build already in progress");
            return BuildOperationResult.CreateFailure("Build already in progress", 0, 0, 0, sw.Elapsed);
        }

        try
        {
            logger.LogInformation("ExecuteBuildAsync called for project: {ProjectName} with steps: {Steps}", project.Name, buildSteps);

            // reset counters
            _filesProcessed = 0;
            _filesSkipped = 0;
            _filesFailed = 0;
            _lastErrorMessage = null;

            var duplicateNamesError = TryGetDuplicateEntryNamesError(configuration);
            if (duplicateNamesError != null)
            {
                logger.LogError("Invalid bundle configuration: {Error}", duplicateNamesError);
                sw.Stop();
                return BuildOperationResult.CreateFailure(duplicateNamesError, 0, 0, 0, sw.Elapsed);
            }

            // get or create cached build structure
            var buildStructure = await GetOrCreateBuildStructureAsync(project, configuration, buildSteps, cancellationToken)
                .ConfigureAwait(false);

            if (buildStructure.Setup != null)
            {
                buildStructure.Setup.SelectedPacks = selectedBundlePacks;
            }

            var success = await RunAsync(buildStructure, progress, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();

            return success
                ? BuildOperationResult.CreateSuccess(_filesProcessed, _filesSkipped, _filesFailed, sw.Elapsed)
                : BuildOperationResult.CreateFailure(_lastErrorMessage ?? "Build failed", _filesProcessed, _filesSkipped, _filesFailed, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteBuildAsync failed");
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
                logger.LogInformation("Aborting build");
                _abortTokenSource.Cancel();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void InvalidateBuildStructureCache()
    {
        logger.LogDebug("Invalidating build structure cache");
        _cachedBuildStructure = null;
        _cachedConfigHash = null;
        _cachedSourceToBundleFileMap = null;
    }

    private void RecordFirstError(string message)
    {
        Interlocked.CompareExchange(ref _lastErrorMessage, message, null);
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

            logger.LogInformation("Starting ModBuilder build pipeline");

            var steps = ResolveBuildSteps(buildStructure.Setup.Step);
            if (steps == BuildStep.None)
            {
                logger.LogWarning("BuildStep is None, nothing to do");
                return true;
            }

            _lastErrorMessage = null;
            var success = await ExecutePipelineStagesAsync(buildStructure, steps, progress, _abortTokenSource.Token).ConfigureAwait(false);

            logger.LogInformation("Build pipeline completed with success={Success}", success);
            return success;
        }
        catch (OperationCanceledException)
        {
            _lastErrorMessage = "Build was cancelled by user";
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build pipeline failed with exception");
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
            (BuildStep.PostBuild, () => PostBuildAsync(progress, cancellationToken), "PostBuild stage failed"),
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

                    logger.LogError("Stage {Stage} failed, aborting pipeline", step);
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
        logger.LogInformation("PreBuild stage started (using cached build structure)");
        progress?.Report(new BuildProgress { CurrentStage = BuildStage.Loading, CurrentStep = "PreBuild: Initializing build structure" });

        FireBundleEvent(BundleEventType.OnPreBuild, null);

        if (buildStructure.Configuration == null)
        {
            logger.LogError("Project configuration is null");
            _lastErrorMessage = "Configuration is null";
            return false;
        }

        logger.LogDebug(
            "Build structure contains {ItemCount} items and {PackCount} packs",
            buildStructure.BundleItems.Count,
            buildStructure.BundlePacks.Count);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CleanAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Clean stage started");
        progress?.Report(new BuildProgress { CurrentStage = BuildStage.Loading, CurrentStep = "Cleaning build directories" });

        var buildDir = setup.Folders?.AbsBuildDir ?? (setup.ProjectDir != null ? Path.Combine(setup.ProjectDir, ModBuilderConstants.DefaultBuildDir) : null);
        var releaseDir = setup.Folders?.AbsReleaseDir ?? (setup.ProjectDir != null ? Path.Combine(setup.ProjectDir, ModBuilderConstants.DefaultReleaseDir) : null);

        try
        {
            if (!string.IsNullOrEmpty(buildDir) && Directory.Exists(buildDir))
            {
                if (IsSafeToCleanDirectory(setup.ProjectDir, buildDir))
                {
                    Directory.Delete(buildDir, true);
                    logger.LogInformation("Deleted build directory: {BuildDir}", buildDir);
                }
                else
                {
                    logger.LogWarning("Skipping clean for unsafe or external build directory: {BuildDir}", buildDir);
                    progress?.Report(new BuildProgress
                    {
                        CurrentStage = BuildStage.Loading,
                        CurrentStep = "Cleaning build directories",
                        Message = $"Skipped unsafe or external build directory: {buildDir}",
                    });
                }
            }

            if (!string.IsNullOrEmpty(releaseDir) && Directory.Exists(releaseDir))
            {
                if (IsSafeToCleanDirectory(setup.ProjectDir, releaseDir))
                {
                    Directory.Delete(releaseDir, true);
                    logger.LogInformation("Deleted release directory: {ReleaseDir}", releaseDir);
                }
                else
                {
                    logger.LogWarning("Skipping clean for unsafe or external release directory: {ReleaseDir}", releaseDir);
                    progress?.Report(new BuildProgress
                    {
                        CurrentStage = BuildStage.Loading,
                        CurrentStep = "Cleaning build directories",
                        Message = $"Skipped unsafe or external release directory: {releaseDir}",
                    });
                }
            }

            cacheService.Clear();
            logger.LogInformation("Build cache cleared");

            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to clean build directories");
            _lastErrorMessage = $"Failed to clean build directories: {ex.Message}";
            return false;
        }
    }

    internal static bool IsSafeToCleanDirectory(string? projectDir, string targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir) || PathHelper.IsPathInsideAppDirectory(targetDir))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return false;
        }

        return !PathHelper.AreSamePath(projectDir, targetDir) && PathHelper.IsPathWithinDirectory(projectDir, targetDir);
    }

    private async Task<bool> BuildAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Build stage started");

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
        logger.LogInformation("Building stage: {Stage}", stage);
        var currentBuildStage = stage switch
        {
            BuildIndex.BigBundleItem or BuildIndex.ReleaseBundlePack => BuildStage.Archiving,
            _ => BuildStage.Processing,
        };
        var filesToProcess = GetFilesForStage(stage);
        progress?.Report(new BuildProgress
        {
            CurrentStage = currentBuildStage,
            CurrentIndex = stage,
            CurrentStep = $"Building {stage}",
            ProcessedFiles = 0,
            TotalFiles = filesToProcess.Count,
            PercentComplete = 0,
            Percentage = 0,
        });

        var startEvent = GetStartBuildEvent(stage);
        FireBundleEvent(startEvent, null);

        var cachePath = GetCachePath(stage, setup);
        var cacheDir = Path.GetDirectoryName(cachePath);
        var cachingEnabled = true;
        if (!string.IsNullOrEmpty(cacheDir))
        {
            try
            {
                Directory.CreateDirectory(cacheDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cachingEnabled = false;
                logger.LogWarning(ex, "Could not create cache directory {CacheDir}, build caching disabled for this stage", cacheDir);
            }
        }

        if (cachingEnabled)
        {
            await cacheService.LoadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        }

        var initialFailed = Volatile.Read(ref _filesFailed);

        logger.LogInformation("Processing {Count} files for stage {Stage}", filesToProcess.Count, stage);

        await ExecuteStageFilesAsync(stage, setup, progress, filesToProcess, cancellationToken).ConfigureAwait(false);

        if (cachingEnabled)
        {
            await cacheService.SaveCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        }

        var finishEvent = GetFinishBuildEvent(stage);
        FireBundleEvent(finishEvent, null);

        var finalFailed = Volatile.Read(ref _filesFailed);
        return finalFailed == initialFailed;
    }

    private async Task ExecuteStageFilesAsync(
        BuildIndex stage,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        IReadOnlyList<string> filesToProcess,
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
            default:
                var dedupedFiles = DeduplicateByTarget(
                    filesToProcess,
                    filePath => GetTargetPathForFile(filePath, stage, setup),
                    filePath => filePath,
                    stage.ToString());
                var tracker = new StageProgressTracker(dedupedFiles.Count);
                await Parallel.ForEachAsync(
                    dedupedFiles,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken,
                    },
                    async (filePath, ct) =>
                        await ProcessSingleFileAsync(filePath, stage, setup, progress, tracker, ct).ConfigureAwait(false)).ConfigureAwait(false);
                break;
        }
    }

    private List<T> DeduplicateByTarget<T>(
        IReadOnlyList<T> entries,
        Func<T, string> targetSelector,
        Func<T, string> sourceSelector,
        string context)
    {
        if (entries.Count < 2)
        {
            return entries.ToList();
        }

        // Parallel writers racing on one target corrupt output and fail with
        // sharing violations. Keep the ordinal-first source so the surviving
        // copy is deterministic across machines and filesystems.
        var winnerByTarget = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.OrderBy(sourceSelector, StringComparer.Ordinal))
        {
            var targetKey = NormalizeTargetKey(targetSelector(entry));
            if (winnerByTarget.TryAdd(targetKey, entry))
            {
                continue;
            }

            logger.LogWarning(
                "Skipping {Source} for {Context}: target {Target} is already produced by {Winner}; keeping a single deterministic copy",
                sourceSelector(entry),
                context,
                targetKey,
                sourceSelector(winnerByTarget[targetKey]));
            Interlocked.Increment(ref _filesSkipped);
        }

        return winnerByTarget.Values.OrderBy(sourceSelector, StringComparer.Ordinal).ToList();
    }

    private static string NormalizeTargetKey(string targetPath)
    {
        var unified = targetPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(unified);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return unified;
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
            await BuildSingleBigBundleItemAsync(item, bundlesDir, setup.ProjectDir, progress, currentItem, totalBigItems, cancellationToken)
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
        string? projectDir,
        IProgress<BuildProgress>? progress,
        int currentItem,
        int totalBigItems,
        CancellationToken cancellationToken)
    {
        var bigFileName = GetBigFileName(item);
        var bigFilePath = Path.Combine(bundlesDir, bigFileName);

        var stagingDir = Path.Combine(bundlesDir, $"{ModBuilderConstants.StagingItemPrefix}{item.Name}");
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }

        Directory.CreateDirectory(stagingDir);

        try
        {
            var stagingFiles = DeduplicateByTarget(item.Files, GetTargetRelativePath, file => file.AbsSourceFile, item.Name);
            var totalFiles = stagingFiles.Count;
            var currentFile = 0;

            CreateStagingDirectories(stagingFiles, stagingDir);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
                CancellationToken = cancellationToken,
            };

            await Parallel.ForEachAsync(stagingFiles, parallelOptions, (file, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var sourceFile = file.AbsSourceFile;
                if (!File.Exists(sourceFile))
                {
                    logger.LogWarning("File not found for BIG bundle: {FilePath}", sourceFile);
                    Interlocked.Increment(ref _filesFailed);
                    RecordFirstError($"File not found for BIG bundle: {sourceFile}");
                    return ValueTask.CompletedTask;
                }

                var targetRelPath = GetTargetRelativePath(file);
                var targetStagedFile = Path.Combine(stagingDir, targetRelPath);
                if (PathHelper.AreSamePath(sourceFile, targetStagedFile))
                {
                    logger.LogDebug("Skipping staging where source and target are the same file: {Path}", sourceFile);
                    return ValueTask.CompletedTask;
                }

                File.Copy(sourceFile, targetStagedFile, true);

                var processed = Interlocked.Increment(ref currentFile);
                if (processed % ModBuilderConstants.StagingProgressReportInterval == 0 || processed == totalFiles)
                {
                    var fileProgress = (double)processed / totalFiles;
                    var overallProgress = ((currentItem - 1) + fileProgress) / totalBigItems;

                    progress?.Report(new BuildProgress
                    {
                        CurrentStage = BuildStage.Packing,
                        CurrentFile = Path.GetFileName(sourceFile),
                        CurrentIndex = BuildIndex.BigBundleItem,
                        CurrentStep = $"Packing {item.Name} ({processed}/{totalFiles}): {Path.GetFileName(sourceFile)}",
                        ProcessedFiles = processed,
                        TotalFiles = totalFiles,
                        PercentComplete = overallProgress * 100,
                        Percentage = overallProgress,
                    });
                }

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

            var archiveProgress = new Progress<double>(p =>
            {
                var overallProgress = ((currentItem - 1) + p) / totalBigItems;
                progress?.Report(new BuildProgress
                {
                    CurrentStage = BuildStage.Compressing,
                    CurrentFile = $"{item.Name}.big",
                    CurrentIndex = BuildIndex.BigBundleItem,
                    CurrentStep = $"Compressing {item.Name}.big ({p:P0})",
                    ProcessedFiles = Volatile.Read(ref _filesProcessed),
                    PercentComplete = overallProgress * 100,
                    Percentage = overallProgress,
                });
            });

            var manifestPath = ResolveItemManifestPath(item, projectDir);
            await CreateBigArchiveWithLoggingAsync(stagingDir, bigFilePath, item.Name, manifestPath, archiveProgress, cancellationToken).ConfigureAwait(false);

            if (File.Exists(bigFilePath))
            {
                await VerifyBuiltArchiveHashAsync(bigFilePath, manifestPath, item.ManifestFile, progress, cancellationToken).ConfigureAwait(false);
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
                    logger.LogDebug(ex, "Failed to clean up staging directory: {StagingDir}", stagingDir);
                }
            }
        }
    }

    private static void CreateStagingDirectories(IReadOnlyList<BundleFile> files, string stagingDir)
    {
        var uniqueDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var targetRelPath = GetTargetRelativePath(file);
            var targetStagedFile = Path.Combine(stagingDir, targetRelPath);
            var targetStagedDir = Path.GetDirectoryName(targetStagedFile);
            if (!string.IsNullOrEmpty(targetStagedDir))
            {
                uniqueDirs.Add(targetStagedDir);
            }
        }

        foreach (var dir in uniqueDirs.Where(dir => !Directory.Exists(dir)))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private async Task CreateBigArchiveWithLoggingAsync(
        string stagingDir,
        string bigFilePath,
        string itemName,
        string? manifestPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var archiveResult = !string.IsNullOrEmpty(manifestPath)
            ? await archiveService.CreateBigArchiveAsync(stagingDir, bigFilePath, manifestPath, progress, cancellationToken).ConfigureAwait(false)
            : await archiveService.CreateBigArchiveAsync(stagingDir, bigFilePath, progress, cancellationToken).ConfigureAwait(false);

        if (!archiveResult.Success)
        {
            Interlocked.Increment(ref _filesFailed);
            logger.LogError("Failed to create BIG archive for item {ItemName}: {Error}", itemName, archiveResult.FirstError);
            _lastErrorMessage = $"Failed to create BIG archive for item {itemName}: {archiveResult.FirstError}";
        }
        else
        {
            logger.LogInformation("Successfully created BIG archive: {Path}", bigFilePath);
        }
    }

    private static string GetTargetRelativePath(BundleFile file)
    {
        var configuredTarget = ToRelativeTargetPath(file.RelTargetFile);
        if (!string.IsNullOrEmpty(configuredTarget))
        {
            var normalized = configuredTarget.Replace('\\', '/');
            if (normalized.EndsWith('/') || string.IsNullOrEmpty(Path.GetExtension(normalized)))
            {
                return $"{normalized.TrimEnd('/')}/{Path.GetFileName(file.AbsSourceFile)}";
            }

            return configuredTarget;
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

    internal static string ToRelativeTargetPath(string? relTargetFile)
    {
        if (string.IsNullOrEmpty(relTargetFile) || !Path.IsPathRooted(relTargetFile))
        {
            return relTargetFile ?? string.Empty;
        }

        // Absolute targets reset Path.Combine and collapse copies onto their own
        // source (sharing violation). Strip the root so staging stays relative.
        var root = Path.GetPathRoot(relTargetFile);
        if (!string.IsNullOrEmpty(root) && relTargetFile.Length > root.Length)
        {
            return relTargetFile.Substring(root.Length).TrimStart('/', '\\');
        }

        return Path.GetFileName(relTargetFile);
    }

    private async Task ExecuteReleaseBundlePackStageAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;

        var candidatePacks = setup.Bundles?.Packs?.ToList() ?? new List<BundlePack>();

        if (candidatePacks.Count == 0 && setup.Bundles?.Items != null && setup.Bundles.Items.Count > 0)
        {
            var defaultPackName = !string.IsNullOrWhiteSpace(setup.ProjectDir) ? Path.GetFileName(setup.ProjectDir) : "Release";
            candidatePacks.Add(new BundlePack
            {
                Name = defaultPackName,
                AllowBuild = true,
                ItemNames = setup.Bundles.Items.Select(i => i.Name).ToList(),
            });
            logger.LogInformation("No bundle packs explicitly defined; created default release pack '{PackName}' for {ItemCount} items", defaultPackName, setup.Bundles.Items.Count);
        }

        if (candidatePacks.Count == 0)
        {
            logger.LogWarning("No bundle packs or items found to release");
            Interlocked.Increment(ref _filesFailed);
            _lastErrorMessage = "No bundle packs or items found to release. Please configure bundle items or packs in ModBuilder.";
            return;
        }

        if (!Directory.Exists(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
        }

        var packs = FilterSelectedPacks(candidatePacks.Where(p => p.AllowBuild), setup.SelectedPacks);

        if (packs.Count == 0)
        {
            logger.LogWarning("No bundle packs enabled or selected for release");
            Interlocked.Increment(ref _filesFailed);
            _lastErrorMessage = "No bundle packs are enabled or selected for release. Check 'Allow Build' in Bundle Pack settings.";
            return;
        }

        foreach (var pack in packs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BuildSingleReleaseBundlePackAsync(pack, setup, progress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task BuildSingleReleaseBundlePackAsync(
        BundlePack pack,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;
        var projectDir = setup.ProjectDir ?? Directory.GetCurrentDirectory();
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var bundlesDir = Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir);
        var items = setup.Bundles?.Items;
        var compressionLevel = setup.ZipCompressionLevel;

        var packFileName = GetPackFileName(pack);
        var packFilePath = Path.Combine(releaseDir, packFileName);

        var packStagingDir = Path.Combine(buildDir, ModBuilderConstants.StagingPackPrefix, pack.Name);
        if (Directory.Exists(packStagingDir))
        {
            Directory.Delete(packStagingDir, true);
        }

        Directory.CreateDirectory(packStagingDir);

        try
        {
            if (items != null)
            {
                var stagingPaths = new PackStagingPaths(bundlesDir, packStagingDir, buildDir, setup.ProjectDir);
                await StagePackFilesAsync(pack, items, stagingPaths, progress, cancellationToken).ConfigureAwait(false);
            }

            var stagedFiles = Directory.GetFiles(packStagingDir, "*", SearchOption.AllDirectories);
            if (stagedFiles.Length == 0)
            {
                Interlocked.Increment(ref _filesFailed);
                logger.LogError("No files were staged for pack {PackName}; release archive cannot be created.", pack.Name);
                _lastErrorMessage = $"No files were staged for pack '{pack.Name}'. Check that bundle items exist and contain files.";
                return;
            }

            progress?.Report(new BuildProgress
            {
                CurrentStage = BuildStage.Packing,
                CurrentFile = pack.Name,
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStep = $"Packaging release {pack.Name}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
            });

            var manifestPath = ResolvePackManifestPath(pack, projectDir);
            var archiveResult = await CreatePackArchiveAsync(
                pack,
                packStagingDir,
                packFilePath,
                manifestPath,
                compressionLevel,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!archiveResult.Success)
            {
                Interlocked.Increment(ref _filesFailed);
                logger.LogError("Failed to create archive for pack {PackName}: {Error}", pack.Name, archiveResult.FirstError);
                _lastErrorMessage = $"Failed to create archive for pack '{pack.Name}': {archiveResult.FirstError}";
            }
            else
            {
                Interlocked.Increment(ref _filesProcessed);
                logger.LogInformation("Successfully created archive: {Path}", packFilePath);

                if (pack.IsBigPack && File.Exists(packFilePath))
                {
                    await VerifyBuiltArchiveHashAsync(packFilePath, manifestPath, pack.ManifestFile, progress, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            CleanupPackStagingDir(packStagingDir);
        }
    }

    private static string? ResolvePackManifestPath(BundlePack pack, string projectDir)
    {
        if (string.IsNullOrEmpty(pack.ManifestFile))
        {
            return null;
        }

        var path = Path.IsPathRooted(pack.ManifestFile) || string.IsNullOrEmpty(projectDir)
            ? pack.ManifestFile
            : Path.Combine(projectDir, pack.ManifestFile);
        return Path.GetFullPath(path);
    }

    private static string? ResolveItemManifestPath(BundleItem item, string? projectDir)
    {
        if (string.IsNullOrEmpty(item.ManifestFile))
        {
            return null;
        }

        var path = Path.IsPathRooted(item.ManifestFile) || string.IsNullOrEmpty(projectDir)
            ? item.ManifestFile
            : Path.Combine(projectDir, item.ManifestFile);
        return Path.GetFullPath(path);
    }

    private async Task<OperationResult<bool>> CreatePackArchiveAsync(
        BundlePack pack,
        string packStagingDir,
        string packFilePath,
        string? manifestPath,
        System.IO.Compression.CompressionLevel compressionLevel,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var packFileName = Path.GetFileName(packFilePath);
        var archiveProgress = new Progress<double>(p =>
        {
            var percent = p * 100;
            progress?.Report(new BuildProgress
            {
                CurrentStage = BuildStage.Packing,
                CurrentFile = packFileName,
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStep = $"Packing {packFileName} ({p:P0})",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
                PercentComplete = percent,
                Percentage = p,
            });
        });

        if (pack.IsBigPack)
        {
            return !string.IsNullOrEmpty(manifestPath)
                ? await archiveService.CreateBigArchiveAsync(packStagingDir, packFilePath, manifestPath, archiveProgress, cancellationToken).ConfigureAwait(false)
                : await archiveService.CreateBigArchiveAsync(packStagingDir, packFilePath, archiveProgress, cancellationToken).ConfigureAwait(false);
        }

        return await archiveService.CreateZipArchiveAsync(packStagingDir, packFilePath, compressionLevel, archiveProgress, cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyBuiltArchiveHashAsync(
        string packFilePath,
        string? manifestPath,
        string? configuredManifest,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                if (!string.IsNullOrEmpty(configuredManifest))
                {
                    logger.LogWarning("Manifest file '{Configured}' specified but not found at resolved path: {Path}", configuredManifest, manifestPath);
                }

                return;
            }

            var manifest = await BigFilePacker.LoadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(manifest?.Sha256))
            {
                return;
            }

            var packFileName = Path.GetFileName(packFilePath);
            if (manifest.EntryOrder.Count > 0 && TryReadBigEntryCount(packFilePath, out var entryCount) && entryCount != manifest.EntryOrder.Count)
            {
                if (entryCount < manifest.EntryOrder.Count)
                {
                    // Subset archives (bundle items sharing a release manifest) can
                    // never match the release hash, so skip them without warning.
                    logger.LogDebug(
                        "Skipping hash verification for {File}: {Built} entries is a subset of {Manifest} manifest entries",
                        packFileName,
                        entryCount,
                        manifest.EntryOrder.Count);
                }
                else
                {
                    logger.LogWarning(
                        "Archive {File} contains {Built} entries but the manifest lists {Manifest}; extra files were packed beyond the release set",
                        packFileName,
                        entryCount,
                        manifest.EntryOrder.Count);
                }

                return;
            }
            progress?.Report(new BuildProgress
            {
                CurrentStage = BuildStage.Verifying,
                CurrentFile = packFileName,
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStep = $"Verifying SHA256 integrity for {packFileName}...",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
                PercentComplete = 99.0,
                Percentage = 0.99,
            });

            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(packFilePath);
            var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            var builtSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

            if (string.Equals(builtSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("BYTE-FOR-BYTE EXACT MATCH: Built BIG archive matches publisher SHA256: {Sha256}", builtSha256);
            }
            else
            {
                var mismatchMsg = $"BIG archive SHA256 mismatch for {Path.GetFileName(packFilePath)}! Expected {manifest.Sha256}, got {builtSha256}";
                logger.LogWarning("{MismatchMessage}", mismatchMsg);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to verify hash for built archive: {Path}", packFilePath);
        }
    }

    private static bool TryReadBigEntryCount(string bigPath, out int entryCount)
    {
        entryCount = 0;
        try
        {
            using var stream = File.OpenRead(bigPath);
            if (stream.Length < 12)
            {
                return false;
            }

            using var reader = new BinaryReader(stream);
            reader.ReadBytes(8);
            var countBytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(countBytes);
            }

            entryCount = (int)BitConverter.ToUInt32(countBytes, 0);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void CleanupPackStagingDir(string packStagingDir)
    {
        if (Directory.Exists(packStagingDir))
        {
            try
            {
                Directory.Delete(packStagingDir, true);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to clean up pack staging directory: {StagingDir}", packStagingDir);
            }
        }
    }

    private async Task StagePackFilesAsync(BundlePack pack, IReadOnlyList<BundleItem> items, PackStagingPaths paths, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        if (pack.IsBigPack)
        {
            StageBigPackFiles(pack, items, paths.PackStagingDir, paths.BuildDir, progress, cancellationToken);
        }
        else
        {
            await StageStandardPackFilesAsync(pack, items, paths, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    private void StageBigPackFiles(BundlePack pack, IReadOnlyList<BundleItem> items, string packStagingDir, string buildDir, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var filesToStage = new List<(BundleFile File, string ItemName)>();
        foreach (var itemName in pack.ItemNames)
        {
            var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                continue;
            }

            foreach (var file in item.Files)
            {
                filesToStage.Add((file, item.Name));
            }
        }

        var dedupedPairs = DeduplicateByTarget(
            filesToStage,
            pair => GetTargetRelativePath(pair.File),
            pair => pair.File.AbsSourceFile,
            pack.Name);

        var uniqueDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, _) in dedupedPairs)
        {
            var targetRelPath = GetTargetRelativePath(file);
            var (_, finalTargetRelPath) = ResolveStagedSource(file, targetRelPath, buildDir);
            var destPath = Path.Combine(packStagingDir, finalTargetRelPath);
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
            {
                uniqueDirs.Add(dir);
            }
        }

        foreach (var dir in uniqueDirs.Where(dir => !Directory.Exists(dir)))
        {
            Directory.CreateDirectory(dir);
        }

        var totalFiles = dedupedPairs.Count;
        var stagedCount = 0;
        progress?.Report(new BuildProgress
        {
            CurrentStage = BuildStage.Staging,
            CurrentIndex = BuildIndex.ReleaseBundlePack,
            CurrentStep = $"Staging release {pack.Name} (0/{totalFiles} files)",
            ProcessedFiles = 0,
            TotalFiles = totalFiles,
            PercentComplete = 0,
            Percentage = 0,
        });

        Parallel.ForEach(dedupedPairs, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
            CancellationToken = cancellationToken,
        }, pair =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            StageBigPackFile(pair.File, packStagingDir, pack.Name, pair.ItemName, buildDir);

            var staged = Interlocked.Increment(ref stagedCount);
            if (staged % ModBuilderConstants.StagingProgressReportInterval == 0 || staged == totalFiles)
            {
                var percent = totalFiles > 0 ? (double)staged / totalFiles * 100 : 100;
                progress?.Report(new BuildProgress
                {
                    CurrentStage = BuildStage.Staging,
                    CurrentFile = Path.GetFileName(pair.File.AbsSourceFile),
                    CurrentIndex = BuildIndex.ReleaseBundlePack,
                    CurrentStep = $"Staging release {pack.Name} ({staged}/{totalFiles} files)",
                    ProcessedFiles = staged,
                    TotalFiles = totalFiles,
                    PercentComplete = percent,
                    Percentage = percent / 100,
                });
            }
        });
    }

    private (string Path, string TargetRelPath)? ProbeConvertedOutput(
        string rawDir,
        string targetRelPath,
        string sourcePath,
        string targetExtension)
    {
        if (Path.IsPathRooted(targetRelPath))
        {
            return null;
        }

        var convertedRel = Path.ChangeExtension(targetRelPath, targetExtension);
        if (Path.IsPathRooted(convertedRel))
        {
            return null;
        }

        var subPath = Path.Combine(rawDir, convertedRel);
        if (IsSubpathOf(rawDir, subPath) && File.Exists(subPath))
        {
            return (subPath, convertedRel);
        }

        var flatSource = Path.Combine(rawDir, Path.ChangeExtension(Path.GetFileName(sourcePath), targetExtension));
        if (IsSubpathOf(rawDir, flatSource) && File.Exists(flatSource))
        {
            return (flatSource, convertedRel);
        }

        var flatTarget = Path.Combine(rawDir, Path.GetFileName(convertedRel));
        if (IsSubpathOf(rawDir, flatTarget) && File.Exists(flatTarget))
        {
            return (flatTarget, convertedRel);
        }

        logger.LogWarning("Expected converted {Extension} output not found for {SourcePath}; falling back to raw source", targetExtension.ToUpperInvariant(), sourcePath);
        return null;
    }

    private (string Path, string TargetRelPath) ResolveStagedSource(
        BundleFile file,
        string targetRelPath,
        string? buildDir)
    {
        var sourcePath = file.AbsSourceFile;
        if (string.IsNullOrEmpty(buildDir))
        {
            return (sourcePath, targetRelPath);
        }

        if (IsRawPassthrough(file))
        {
            return (sourcePath, targetRelPath);
        }

        var rawDir = Path.Combine(buildDir, ModBuilderConstants.RawBundleItemsSubdir);
        if (!Directory.Exists(rawDir))
        {
            return (sourcePath, targetRelPath);
        }

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Check if converted output exists (DDS for image, CSF for string table)
        if (ext is ModBuilderConstants.FileExtensions.Tga or ModBuilderConstants.FileExtensions.Png)
        {
            var probed = ProbeConvertedOutput(rawDir, targetRelPath, sourcePath, ModBuilderConstants.FileExtensions.Dds);
            if (probed != null)
            {
                return probed.Value;
            }
        }
        else if (ext == ModBuilderConstants.FileExtensions.Str)
        {
            var probed = ProbeConvertedOutput(rawDir, targetRelPath, sourcePath, ModBuilderConstants.FileExtensions.Csf);
            if (probed != null)
            {
                return probed.Value;
            }
        }

        // Passthrough candidate
        var candidateRel = Path.Combine(rawDir, targetRelPath);
        if (IsSubpathOf(rawDir, candidateRel) && File.Exists(candidateRel))
        {
            return (candidateRel, targetRelPath);
        }

        return (sourcePath, targetRelPath);
    }

    private void StageBigPackFile(BundleFile file, string packStagingDir, string packName, string itemName, string? buildDir = null)
    {
        var sourcePath = file.AbsSourceFile;
        if (!File.Exists(sourcePath))
        {
            logger.LogWarning("Source file {SourceFile} not found for bundle item {ItemName}", sourcePath, itemName);
            Interlocked.Increment(ref _filesFailed);
            RecordFirstError($"Source file {sourcePath} not found for bundle item {itemName}");
            return;
        }

        var targetRelPath = GetTargetRelativePath(file);
        var (actualSource, finalTargetRelPath) = ResolveStagedSource(file, targetRelPath, buildDir);

        var destPath = Path.Combine(packStagingDir, finalTargetRelPath);
        if (PathHelper.AreSamePath(actualSource, destPath))
        {
            logger.LogDebug("Skipping staging where source and target are the same file: {Path}", actualSource);
            return;
        }

        EnsureDestinationDirectory(destPath);
        File.Copy(actualSource, destPath, true);
        logger.LogDebug("Staged file {RelPath} for BIG pack {PackName}", finalTargetRelPath, packName);
    }

    private async Task StageStandardPackFilesAsync(BundlePack pack, IReadOnlyList<BundleItem> items, PackStagingPaths paths, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var totalItems = pack.ItemNames.Count;
        var stagedItems = 0;
        foreach (var itemName in pack.ItemNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                continue;
            }

            if (item.IsBig)
            {
                await StageBigBundleArchiveAsync(item, paths.BundlesDir, paths.PackStagingDir, pack.Name, paths.ProjectDir, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                StageRawBundleFiles(item, paths.PackStagingDir, pack.Name, paths.BuildDir, progress, cancellationToken);
            }

            stagedItems++;
            var percent = totalItems > 0 ? (double)stagedItems / totalItems * 100 : 100;
            progress?.Report(new BuildProgress
            {
                CurrentStage = BuildStage.Staging,
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStep = $"Staging release {pack.Name} ({stagedItems}/{totalItems} items)",
                ProcessedFiles = stagedItems,
                TotalFiles = totalItems,
                PercentComplete = percent,
                Percentage = percent / 100,
            });
        }
    }

    private async Task StageBigBundleArchiveAsync(BundleItem item, string bundlesDir, string packStagingDir, string packName, string? projectDir, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var bigFileName = GetBigFileName(item);
        var srcBig = Path.Combine(bundlesDir, bigFileName);
        var buildAttempted = false;
        if (!File.Exists(srcBig))
        {
            logger.LogInformation("BIG bundle {BigFileName} missing in bundles directory; building it on demand...", bigFileName);
            if (item.Files.Count > 0)
            {
                if (!Directory.Exists(bundlesDir))
                {
                    Directory.CreateDirectory(bundlesDir);
                }

                buildAttempted = true;
                await BuildSingleBigBundleItemAsync(item, bundlesDir, projectDir, progress, 1, 1, cancellationToken).ConfigureAwait(false);
            }
        }

        if (File.Exists(srcBig))
        {
            var destBig = Path.Combine(packStagingDir, bigFileName);
            File.Copy(srcBig, destBig, true);
            logger.LogDebug("Staged .BIG archive {BigFileName} for pack {PackName}", bigFileName, packName);
        }
        else
        {
            logger.LogError("BIG bundle {BigFileName} missing for pack {PackName} and could not be built.", bigFileName, packName);
            if (!buildAttempted)
            {
                Interlocked.Increment(ref _filesFailed);
                _lastErrorMessage = $"BIG bundle '{bigFileName}' missing for pack '{packName}'.";
            }
        }
    }

    private void StageRawBundleFiles(BundleItem item, string packStagingDir, string packName, string? buildDir = null, IProgress<BuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var totalFiles = item.Files.Count;
        var stagedCount = 0;
        foreach (var file in item.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = file.AbsSourceFile;
            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("Source file {SourceFile} not found for raw bundle item {ItemName}", sourcePath, item.Name);
                Interlocked.Increment(ref _filesFailed);
                continue;
            }

            var relPath = !string.IsNullOrEmpty(file.RelTargetFile) ? file.RelTargetFile : file.GetRelSourceFile();
            if (string.IsNullOrEmpty(relPath))
            {
                relPath = Path.GetFileName(sourcePath);
            }

            var (actualSource, finalRelPath) = ResolveStagedSource(file, relPath, buildDir);

            var destPath = Path.Combine(packStagingDir, finalRelPath);
            if (PathHelper.AreSamePath(actualSource, destPath))
            {
                logger.LogDebug("Skipping staging where source and target are the same file: {Path}", actualSource);
                continue;
            }

            EnsureDestinationDirectory(destPath);
            File.Copy(actualSource, destPath, true);
            logger.LogDebug("Staged loose file {RelPath} for pack {PackName}", finalRelPath, packName);

            stagedCount++;
            if (stagedCount % ModBuilderConstants.StagingProgressReportInterval == 0 || stagedCount == totalFiles)
            {
                var percent = totalFiles > 0 ? (double)stagedCount / totalFiles * 100 : 100;
                progress?.Report(new BuildProgress
                {
                    CurrentStage = BuildStage.Staging,
                    CurrentFile = Path.GetFileName(sourcePath),
                    CurrentIndex = BuildIndex.ReleaseBundlePack,
                    CurrentStep = $"Staging {item.Name} for release {packName} ({stagedCount}/{totalFiles} files)",
                    ProcessedFiles = stagedCount,
                    TotalFiles = totalFiles,
                    PercentComplete = percent,
                    Percentage = percent / 100,
                });
            }
        }
    }

    private static void EnsureDestinationDirectory(string filePath)
    {
        var destDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }
    }

    private async Task ProcessSingleFileAsync(
        string filePath,
        BuildIndex stage,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        StageProgressTracker tracker,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            logger.LogWarning("File not found for stage {Stage}: {FilePath}", stage, filePath);
            Interlocked.Increment(ref _filesFailed);
            RecordFirstError($"File not found: {filePath}");
            ReportStageFileProgress(progress, stage, filePath, tracker);
            return;
        }

        var currentMd5 = await cacheService.ComputeOrReuseMd5Async(filePath, cancellationToken).ConfigureAwait(false);
        var status = cacheService.DetermineFileStatus(filePath, currentMd5, null);

        if (status is BuildFileStatus.Unchanged or BuildFileStatus.Irrelevant)
        {
            logger.LogDebug("Skipping unchanged/irrelevant file: {FilePath}", filePath);
            var fileInfo = new FileInfo(filePath);
            var mtime = fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
            cacheService.AddFile(filePath, mtime, currentMd5);
            Interlocked.Increment(ref _filesSkipped);
            ReportStageFileProgress(progress, stage, filePath, tracker);
            return;
        }

        logger.LogDebug("Processing file: {FilePath} (Status: {Status})", filePath, status);

        // process file based on stage
        var success = stage switch
        {
            BuildIndex.RawBundleItem => await ProcessRawBundleItemFileAsync(filePath, setup, cancellationToken).ConfigureAwait(false),
            _ => true,
        };

        if (success)
        {
            var fileInfo = new FileInfo(filePath);
            var mtime = fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
            cacheService.AddFile(filePath, mtime, currentMd5);

            Interlocked.Increment(ref _filesProcessed);
            ReportStageFileProgress(progress, stage, filePath, tracker);
        }
        else
        {
            Interlocked.Increment(ref _filesFailed);
            logger.LogError("Failed to process file: {FilePath}", filePath);
            ReportStageFileProgress(progress, stage, filePath, tracker);
        }
    }

    private static void ReportStageFileProgress(
        IProgress<BuildProgress>? progress,
        BuildIndex stage,
        string filePath,
        StageProgressTracker tracker)
    {
        if (progress == null)
        {
            return;
        }

        var done = tracker.IncrementDone();
        var total = tracker.TotalFiles;
        var percent = total > 0 ? (double)done / total * 100 : 100;
        var fileExt = Path.GetExtension(filePath).ToLowerInvariant();
        var stageType = fileExt is ModBuilderConstants.FileExtensions.Tga or ModBuilderConstants.FileExtensions.Png or ModBuilderConstants.FileExtensions.Bmp or ModBuilderConstants.FileExtensions.Dds
            ? BuildStage.Converting
            : BuildStage.Processing;
        var stepDescription = $"{stageType}: {Path.GetFileName(filePath)} ({done}/{total})";
        progress.Report(new BuildProgress
        {
            CurrentStage = stageType,
            CurrentFile = Path.GetFileName(filePath),
            CurrentIndex = stage,
            CurrentStep = stepDescription,
            ProcessedFiles = done,
            TotalFiles = total,
            PercentComplete = percent,
            Percentage = percent / 100,
        });
    }

    private async Task<bool> ProcessRawBundleItemFileAsync(string filePath, BuildSetup setup, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var targetPath = GetTargetPathForFile(filePath, BuildIndex.RawBundleItem, setup);
        var bundleFile = FindBundleFile(filePath);

        // Honor explicit passthrough: files already in their declared output format,
        // or marked noconvert/raw, are copied verbatim so builds stay byte-for-byte
        // reproducible instead of being pointlessly re-encoded.
        if (IsRawPassthrough(bundleFile) || OutputFormatMatchesSource(bundleFile, extension))
        {
            return await CopyFileDirectlyAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false);
        }

        return extension switch
        {
            ModBuilderConstants.FileExtensions.Png or ModBuilderConstants.FileExtensions.Tga => await ConvertImageFileAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
            ModBuilderConstants.FileExtensions.Str => await ConvertStringTableFileAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
            ModBuilderConstants.FileExtensions.Ini => await ProcessIniFileAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
            _ => await CopyFileDirectlyAsync(filePath, targetPath, cancellationToken).ConfigureAwait(false),
        };
    }

    private BundleFile? FindBundleFile(string sourcePath)
    {
        if (_cachedSourceToBundleFileMap?.TryGetValue(sourcePath, out var bundleFile) == true)
        {
            return bundleFile;
        }

        return _cachedBuildStructure?.BundleItems?.Values
            .SelectMany(i => i.Files)
            .FirstOrDefault(f => string.Equals(f.AbsSourceFile, sourcePath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRawPassthrough(BundleFile? file)
    {
        if (file?.Params == null)
        {
            return false;
        }

        return file.Params.Any(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.NoConvert, StringComparison.OrdinalIgnoreCase)) ||
            file.Params.Any(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.Raw, StringComparison.OrdinalIgnoreCase)) ||
            file.Params.Any(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.OutputFormat, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kvp.Value?.ToString(), ModBuilderConstants.BundleParams.RawValue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool OutputFormatMatchesSource(BundleFile? file, string sourceExtension)
    {
        var outputFormat = file?.Params?
            .FirstOrDefault(kvp => string.Equals(kvp.Key, ModBuilderConstants.BundleParams.OutputFormat, StringComparison.OrdinalIgnoreCase)).Value?
            .ToString();
        if (string.IsNullOrWhiteSpace(outputFormat))
        {
            return false;
        }

        var normalizedFormat = outputFormat.Trim().TrimStart('.');
        var normalizedSource = sourceExtension.Trim().TrimStart('.');
        return string.Equals(normalizedFormat, normalizedSource, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ConvertImageFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var ddsTargetPath = Path.ChangeExtension(targetPath, ModBuilderConstants.FileExtensions.Dds);
        var result = await fileConversionService.ConvertFileAsync(sourcePath, ddsTargetPath, ModBuilderConstants.ConversionFormats.Dds, null, cancellationToken)
            .ConfigureAwait(false);

        return result.Success;
    }

    private async Task<bool> ConvertStringTableFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var csfTargetPath = Path.ChangeExtension(targetPath, ModBuilderConstants.FileExtensions.Csf);
        var result = await fileConversionService.ConvertFileAsync(sourcePath, csfTargetPath, ModBuilderConstants.ConversionFormats.Csf, null, cancellationToken)
            .ConfigureAwait(false);

        return result.Success;
    }

    private async Task<bool> ProcessIniFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        return await CopyFileDirectlyAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CopyFileDirectlyAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        if (PathHelper.AreSamePath(sourcePath, targetPath))
        {
            logger.LogDebug("Skipping copy where source and target are the same file: {Path}", sourcePath);
            return true;
        }

        try
        {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, ModBuilderConstants.BuildFileBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, ModBuilderConstants.BuildFileBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to copy file from {SourcePath} to {TargetPath}", sourcePath, targetPath);
            return false;
        }
    }

    private string GetTargetPathForFile(string sourcePath, BuildIndex stage, BuildSetup setup)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        string? relPath = null;

        var bundleFile = FindBundleFile(sourcePath);
        if (bundleFile != null)
        {
            relPath = GetTargetRelativePath(bundleFile);
        }

        if (string.IsNullOrEmpty(relPath) && !string.IsNullOrEmpty(setup.ProjectDir))
        {
            var rel = Path.GetRelativePath(setup.ProjectDir, sourcePath);
            if (!rel.StartsWith("..", StringComparison.Ordinal))
            {
                relPath = rel;
            }
        }

        if (string.IsNullOrEmpty(relPath))
        {
            relPath = Path.GetFileName(sourcePath);
        }

        return stage switch
        {
            BuildIndex.RawBundleItem => Path.Combine(buildDir, ModBuilderConstants.RawBundleItemsSubdir, relPath),
            _ => Path.Combine(buildDir, relPath),
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

    private async Task<bool> PostBuildAsync(IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("PostBuild stage started");
        progress?.Report(new BuildProgress { CurrentStage = BuildStage.Complete, CurrentStep = "PostBuild: Finalizing build" });

        FireBundleEvent(BundleEventType.OnPostBuild, null);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ReleaseAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Release stage started");
        progress?.Report(new BuildProgress
        {
            CurrentStage = BuildStage.Archiving,
            CurrentIndex = BuildIndex.ReleaseBundlePack,
            CurrentStep = "Creating release archives",
        });

        FireBundleEvent(BundleEventType.OnRelease, null);

        return await BuildStageAsync(BuildIndex.ReleaseBundlePack, setup, progress, cancellationToken).ConfigureAwait(false);
    }

    private static HashSet<string> ResolveManifestFilesToInclude(BuildSetup setup)
    {
        var filesToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (setup.SelectedPacks == null || setup.SelectedPacks.Count == 0 || setup.Bundles?.Items == null)
        {
            return filesToInclude;
        }

        foreach (var packOrItemName in setup.SelectedPacks)
        {
            IncludePackOrItemFiles(setup, packOrItemName, filesToInclude);
        }

        return filesToInclude;
    }

    private static void IncludePackOrItemFiles(BuildSetup setup, string packOrItemName, HashSet<string> filesToInclude)
    {
        var pack = setup.Bundles?.Packs?.FirstOrDefault(p => string.Equals(p.Name, packOrItemName, StringComparison.OrdinalIgnoreCase));
        if (pack != null)
        {
            foreach (var itemName in pack.ItemNames)
            {
                var item = setup.Bundles?.Items?.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
                AddItemFileNames(filesToInclude, item);
            }
        }
        else
        {
            var item = setup.Bundles?.Items?.FirstOrDefault(i => string.Equals(i.Name, packOrItemName, StringComparison.OrdinalIgnoreCase));
            AddItemFileNames(filesToInclude, item);
        }
    }

    private static void AddItemFileNames(HashSet<string> files, BundleItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.IsBig)
        {
            files.Add(GetBigFileName(item));
        }
        else
        {
            foreach (var file in item.Files)
            {
                var relPath = !string.IsNullOrEmpty(file.RelTargetFile) ? file.RelTargetFile : file.GetRelSourceFile();
                if (!string.IsNullOrEmpty(relPath))
                {
                    files.Add(relPath);
                    files.Add(Path.GetFileName(relPath));
                }
            }
        }
    }

    private static void StageManifestFiles(string stagingDir, string bundlesDir, HashSet<string> filesToInclude)
    {
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }

        Directory.CreateDirectory(stagingDir);

        var allFiles = Directory.GetFiles(bundlesDir, "*", SearchOption.AllDirectories);
        foreach (var file in allFiles)
        {
            var fileName = Path.GetFileName(file);
            var relPath = Path.GetRelativePath(bundlesDir, file);
            if (filesToInclude.Contains(fileName) || filesToInclude.Contains(relPath))
            {
                var destPath = Path.Combine(stagingDir, relPath);
                EnsureDestinationDirectory(destPath);
                File.Copy(file, destPath, overwrite: true);
            }
        }
    }

    private async Task<bool> CreateManifestAsync(
        BuildStructure buildStructure,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("CreateManifest stage started");
        progress?.Report(new BuildProgress
        {
            CurrentStage = BuildStage.Hashing,
            CurrentIndex = BuildIndex.CreateManifest,
            CurrentStep = "Creating local ContentManifest and storing in CAS",
        });

        FireBundleEvent(BundleEventType.OnCreateManifest, null);

        var setup = buildStructure.Setup;
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var bundlesDir = Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir);

        Directory.CreateDirectory(bundlesDir);

        if (!await EnsureBundlesPreparedAsync(setup, bundlesDir, progress, cancellationToken).ConfigureAwait(false))
        {
            logger.LogError("No bundle files found in {BundlesDir} to create manifest.", bundlesDir);
            _lastErrorMessage = $"No bundle files found in {bundlesDir}. Ensure bundle items have source files.";
            return false;
        }

        var stagingDir = Path.Combine(buildDir, ModBuilderConstants.StagingManifestPrefix);

        try
        {
            return await ExecuteCreateManifestsAsync(
                buildStructure,
                bundlesDir,
                buildDir,
                setup.Folders?.AbsReleaseDir,
                stagingDir,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupStagingDirectory(stagingDir);
        }
    }

    private async Task<bool> ExecuteCreateManifestsAsync(
        BuildStructure buildStructure,
        string bundlesDir,
        string buildDir,
        string? releaseDir,
        string stagingDir,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var planResult = ResolveManifestPlan(buildStructure);
        if (!planResult.Success || planResult.Data == null)
        {
            logger.LogError("Failed to resolve manifest plan: {Error}", planResult.FirstError);
            _lastErrorMessage = planResult.FirstError ?? "Failed to resolve manifest plan.";
            return false;
        }

        if (planResult.Data.Count == 0)
        {
            var manifestContentDir = PrepareManifestContentDirectory(buildStructure.Setup, bundlesDir, stagingDir);
            return await ExecuteCreateLocalManifestAsync(
                buildStructure,
                manifestContentDir,
                bundlesDir,
                buildDir,
                releaseDir,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        var entries = planResult.Data;
        var dirs = new ManifestStageDirs(bundlesDir, buildDir, releaseDir, stagingDir);
        var failures = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = await ExecuteManifestPlanEntryAsync(
                buildStructure,
                entries[i],
                i,
                entries.Count,
                dirs,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!created)
            {
                failures++;
            }
        }

        if (failures > 0)
        {
            _lastErrorMessage ??= $"Failed to create {failures} of {entries.Count} content manifests.";
            return false;
        }

        return true;
    }

    private OperationResult<IReadOnlyList<ManifestPlanEntry>> ResolveManifestPlan(BuildStructure buildStructure)
    {
        var projectPacks = buildStructure.Setup.Bundles?.Packs ?? [];
        if (projectPacks.Count == 0)
        {
            return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateSuccess([]);
        }

        var effectivePacks = FilterSelectedPacks(projectPacks.Where(p => p.AllowBuild), buildStructure.Setup.SelectedPacks);
        if (effectivePacks.Count == 0)
        {
            return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateFailure(
                "No bundle packs are enabled or selected for manifest creation. Check 'Allow Build' in Bundle Pack settings.");
        }

        var definitions = buildStructure.Configuration.Manifests.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
        if (definitions.Count == 0)
        {
            return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateSuccess(
                [CreateDefaultPlanEntry(buildStructure.Project, effectivePacks)]);
        }

        return ResolveDefinedManifestPlan(buildStructure.Project, definitions, projectPacks, effectivePacks);
    }

    private static ManifestPlanEntry CreateDefaultPlanEntry(ModBuilderProject project, IReadOnlyList<BundlePack> packs)
    {
        var version = ResolveManifestVersion(null, project.Version);
        var name = string.IsNullOrWhiteSpace(project.Name) ? packs[0].Name : project.Name;
        return new ManifestPlanEntry(
            name,
            version,
            null,
            ResolveProjectContentType(project.ContentType),
            ResolveProjectTargetGame(project.TargetGame),
            packs);
    }

    private OperationResult<IReadOnlyList<ManifestPlanEntry>> ResolveDefinedManifestPlan(
        ModBuilderProject project,
        IReadOnlyList<BundleManifest> definitions,
        IReadOnlyList<BundlePack> projectPacks,
        IReadOnlyList<BundlePack> effectivePacks)
    {
        var duplicateNames = FindDuplicateNames(definitions.Select(definition => definition.Name));
        if (duplicateNames.Count > 0)
        {
            return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateFailure(
                $"Duplicate bundle manifest name: '{duplicateNames[0]}'. Manifest names must be unique.");
        }

        var packsByName = projectPacks.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        var effectiveNames = new HashSet<string>(effectivePacks.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var entries = new List<ManifestPlanEntry>();
        foreach (var definition in definitions)
        {
            var unknown = definition.PackNames.Where(name => !packsByName.ContainsKey(name)).ToList();
            if (unknown.Count > 0)
            {
                return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateFailure(
                    $"Bundle manifest '{definition.Name}' references unknown pack(s): {string.Join(", ", unknown)}.");
            }

            var packs = definition.PackNames.Where(name => effectiveNames.Contains(name)).Select(name => packsByName[name]).ToList();
            if (packs.Count == 0)
            {
                logger.LogWarning(
                    "Skipping bundle manifest '{ManifestName}': none of its packs are enabled or selected.",
                    definition.Name);
                continue;
            }

            var version = ResolveManifestVersion(definition.Version, project.Version);
            var publisher = string.IsNullOrWhiteSpace(definition.Publisher) ? null : definition.Publisher;
            var contentType = ResolveManifestContentType(definition.ContentType, project.ContentType);
            var targetGame = ResolveManifestTargetGame(definition.TargetGame, project.TargetGame);
            entries.Add(new ManifestPlanEntry(definition.Name, version, publisher, contentType, targetGame, packs));
        }

        if (entries.Count == 0)
        {
            return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateFailure(
                "All bundle manifest definitions were skipped because none of their packs are enabled or selected.");
        }

        return OperationResult<IReadOnlyList<ManifestPlanEntry>>.CreateSuccess(entries);
    }

    private static ContentType ResolveManifestContentType(ContentType? definitionValue, ContentType projectValue)
    {
        if (definitionValue is { } contentType && contentType != ContentType.UnknownContentType)
        {
            return contentType;
        }

        return ResolveProjectContentType(projectValue);
    }

    private static GameType ResolveManifestTargetGame(GameType? definitionValue, GameType projectValue)
    {
        if (definitionValue is { } targetGame && targetGame != GameType.Unknown)
        {
            return targetGame;
        }

        return ResolveProjectTargetGame(projectValue);
    }

    private static string ResolveManifestVersion(string? definitionVersion, string? projectVersion)
    {
        if (!string.IsNullOrWhiteSpace(definitionVersion))
        {
            return definitionVersion;
        }

        if (!string.IsNullOrWhiteSpace(projectVersion))
        {
            return projectVersion;
        }

        return ModBuilderConstants.DefaultManifestVersion;
    }

    private static List<BundlePack> FilterSelectedPacks(IEnumerable<BundlePack> packs, IReadOnlyList<string>? selectedPacks)
    {
        var filtered = packs.ToList();
        if (selectedPacks is { Count: > 0 })
        {
            filtered = filtered.Where(p =>
                selectedPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase) ||
                p.ItemNames.Any(item => selectedPacks.Contains(item, StringComparer.OrdinalIgnoreCase))).ToList();
        }

        return filtered;
    }

    private async Task<bool> EnsureBundlesPreparedAsync(
        BuildSetup setup,
        string bundlesDir,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var allBundleFiles = Directory.GetFiles(bundlesDir, "*", SearchOption.AllDirectories);
        if (allBundleFiles.Length > 0)
        {
            return true;
        }

        if (setup.Bundles?.Items == null || setup.Bundles.Items.Count == 0)
        {
            return false;
        }

        var initialFailed = Volatile.Read(ref _filesFailed);
        logger.LogInformation("No files found in bundles directory; preparing bundle files before creating manifest...");
        foreach (var item in setup.Bundles.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Files.Count == 0)
            {
                continue;
            }

            if (item.IsBig)
            {
                await BuildSingleBigBundleItemAsync(item, bundlesDir, setup.ProjectDir, progress, 1, 1, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                StageRawBundleFiles(item, bundlesDir, item.Name, setup.Folders?.AbsBuildDir, progress, cancellationToken);
            }
        }

        if (Volatile.Read(ref _filesFailed) > initialFailed)
        {
            logger.LogError("One or more files failed to stage during bundle preparation");
            return false;
        }

        return Directory.Exists(bundlesDir) && Directory.GetFiles(bundlesDir, "*", SearchOption.AllDirectories).Length > 0;
    }

    private static string PrepareManifestContentDirectory(BuildSetup setup, string bundlesDir, string stagingDir)
    {
        var filesToInclude = ResolveManifestFilesToInclude(setup);
        if (filesToInclude.Count == 0)
        {
            return bundlesDir;
        }

        StageManifestFiles(stagingDir, bundlesDir, filesToInclude);
        var stagedCount = Directory.Exists(stagingDir) ? Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories).Length : 0;
        return stagedCount > 0 ? stagingDir : bundlesDir;
    }

    /// <summary>
    /// Adapts CAS storage progress (hashing and storing phases) to build progress reports
    /// so the manifest creation ending stays visible in the build terminal.
    /// </summary>
    /// <param name="progress">The build progress reporter.</param>
    /// <param name="manifestName">The manifest display name used in step descriptions.</param>
    /// <returns>A storage progress reporter forwarding to the build progress.</returns>
    private static IProgress<ContentStorageProgress> CreateManifestStorageProgress(
        IProgress<BuildProgress> progress,
        string manifestName)
    {
        return new Progress<ContentStorageProgress>(storageProgress =>
        {
            var isHashing = storageProgress.Phase == ContentStoragePhase.Hashing;
            progress.Report(new BuildProgress
            {
                CurrentStage = isHashing ? BuildStage.Hashing : BuildStage.Storing,
                CurrentIndex = BuildIndex.CreateManifest,
                CurrentFile = storageProgress.CurrentFileName ?? string.Empty,
                CurrentStep = isHashing
                    ? $"Hashing {manifestName} ({storageProgress.ProcessedCount}/{storageProgress.TotalCount})"
                    : $"Storing {manifestName} in CAS ({storageProgress.ProcessedCount}/{storageProgress.TotalCount})",
                ProcessedFiles = storageProgress.ProcessedCount,
                TotalFiles = storageProgress.TotalCount,
                PercentComplete = storageProgress.Percentage,
                Percentage = storageProgress.Percentage / 100,
            });
        });
    }

    private async Task<bool> ExecuteCreateLocalManifestAsync(
        BuildStructure buildStructure,
        string manifestContentDir,
        string bundlesDir,
        string buildDir,
        string? releaseDir,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var localContentService = scope.ServiceProvider.GetRequiredService<ILocalContentService>();

        try
        {
            var projectName = buildStructure.Project.Name;
            var targetGame = buildStructure.Project.TargetGame;
            var contentType = ResolveProjectContentType(buildStructure.Project.ContentType);

            var storageProgress = progress == null
                ? null
                : CreateManifestStorageProgress(progress, projectName);

            var manifestResult = await localContentService.CreateLocalContentManifestAsync(
                manifestContentDir,
                projectName,
                contentType,
                targetGame,
                sourcePath: bundlesDir,
                progress: storageProgress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!manifestResult.Success)
            {
                logger.LogError("Failed to create local content manifest: {Error}", manifestResult.FirstError);
                _lastErrorMessage = $"Failed to create manifest: {manifestResult.FirstError}";
                return false;
            }

            var manifest = manifestResult.Data;
            logger.LogInformation(
                "Successfully created local ContentManifest '{ManifestId}' for project '{ProjectName}' in CAS",
                manifest?.Id,
                projectName);

            if (manifest != null)
            {
                await PersistManifestOutputsAsync(manifest, buildDir, releaseDir, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new BuildProgress
            {
                CurrentStage = BuildStage.Complete,
                CurrentIndex = BuildIndex.CreateManifest,
                CurrentStep = $"Local manifest {manifest?.Id} created and saved to {ModBuilderConstants.ManifestFileName}",
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while creating local ContentManifest");
            _lastErrorMessage = $"Failed to create manifest: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> ExecuteManifestPlanEntryAsync(
        BuildStructure buildStructure,
        ManifestPlanEntry entry,
        int entryIndex,
        int entryCount,
        ManifestStageDirs dirs,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var safeName = PathHelper.SanitizeFileName(entry.Name);
        if (string.IsNullOrEmpty(safeName))
        {
            logger.LogError("Bundle manifest name '{ManifestName}' is not usable as a file name.", entry.Name);
            _lastErrorMessage = $"Bundle manifest name '{entry.Name}' is not usable as a file name.";
            return false;
        }

        var entryStagingDir = Path.Combine(dirs.StagingRootDir, safeName);
        if (Directory.Exists(entryStagingDir))
        {
            Directory.Delete(entryStagingDir, true);
        }

        Directory.CreateDirectory(entryStagingDir);

        var items = buildStructure.Setup.Bundles?.Items ?? [];
        var stagingPaths = new PackStagingPaths(dirs.BundlesDir, entryStagingDir, dirs.BuildDir, buildStructure.Setup.ProjectDir);
        foreach (var pack in entry.Packs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StagePackFilesAsync(pack, items, stagingPaths, progress, cancellationToken).ConfigureAwait(false);
        }

        var stagedFiles = Directory.GetFiles(entryStagingDir, "*", SearchOption.AllDirectories);
        if (stagedFiles.Length == 0)
        {
            Interlocked.Increment(ref _filesFailed);
            logger.LogError("No files were staged for bundle manifest '{ManifestName}'.", entry.Name);
            _lastErrorMessage = $"No files were staged for bundle manifest '{entry.Name}'.";
            return false;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var localContentService = scope.ServiceProvider.GetRequiredService<ILocalContentService>();

        try
        {
            var storageProgress = progress == null
                ? null
                : CreateManifestStorageProgress(progress, entry.Name);

            var manifestResult = await localContentService.CreateLocalContentManifestAsync(
                entryStagingDir,
                entry.Name,
                entry.ContentType,
                entry.TargetGame,
                sourcePath: dirs.BundlesDir,
                progress: storageProgress,
                cancellationToken: cancellationToken,
                publisherId: entry.Publisher,
                manifestVersion: entry.Version).ConfigureAwait(false);

            if (!manifestResult.Success || manifestResult.Data == null)
            {
                logger.LogError("Failed to create bundle manifest '{ManifestName}': {Error}", entry.Name, manifestResult.FirstError);
                _lastErrorMessage = $"Failed to create manifest '{entry.Name}': {manifestResult.FirstError}";
                return false;
            }

            var manifest = manifestResult.Data;
            var fileName = ResolveManifestOutputFileName(entry.Name, entryCount);
            await PersistManifestOutputsAsync(manifest, dirs.BuildDir, dirs.ReleaseDir, cancellationToken, fileName).ConfigureAwait(false);

            logger.LogInformation(
                "Successfully created local ContentManifest '{ManifestId}' for '{ManifestName}' ({Index}/{Count}) in CAS",
                manifest.Id,
                entry.Name,
                entryIndex + 1,
                entryCount);

            progress?.Report(new BuildProgress
            {
                CurrentStage = BuildStage.Complete,
                CurrentIndex = BuildIndex.CreateManifest,
                CurrentStep = $"Local manifest {manifest.Id} ({entryIndex + 1}/{entryCount}) created and saved to {fileName}",
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while creating bundle manifest '{ManifestName}'", entry.Name);
            _lastErrorMessage = $"Failed to create manifest '{entry.Name}': {ex.Message}";
            return false;
        }
    }

    private static string ResolveManifestOutputFileName(string manifestName, int entryCount)
    {
        if (entryCount == 1)
        {
            return ModBuilderConstants.ManifestFileName;
        }

        var safeName = PathHelper.SanitizeFileName(manifestName);
        var baseName = Path.GetFileNameWithoutExtension(ModBuilderConstants.ManifestFileName);
        var extension = Path.GetExtension(ModBuilderConstants.ManifestFileName);
        return $"{baseName}-{safeName}{extension}";
    }

    private static ContentType ResolveProjectContentType(ContentType contentType) =>
        contentType != ContentType.UnknownContentType ? contentType : ContentType.Mod;

    private static GameType ResolveProjectTargetGame(GameType targetGame) =>
        targetGame != GameType.Unknown ? targetGame : GameType.ZeroHour;

    private async Task PersistManifestOutputsAsync(
        ContentManifest manifest,
        string buildDir,
        string? releaseDir,
        CancellationToken cancellationToken,
        string? fileName = null)
    {
        PublishContentAcquiredSafely(manifest);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var manifestJson = JsonSerializer.Serialize(manifest, options);
        var outputFileName = fileName ?? ModBuilderConstants.ManifestFileName;
        var buildManifestPath = Path.Combine(buildDir, outputFileName);
        await File.WriteAllTextAsync(buildManifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved manifest file to {Path}", buildManifestPath);

        if (!string.IsNullOrEmpty(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
            var releaseManifestPath = Path.Combine(releaseDir, outputFileName);
            await File.WriteAllTextAsync(releaseManifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Saved manifest file to {Path}", releaseManifestPath);
        }
    }

    private void PublishContentAcquiredSafely(ContentManifest manifest)
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new ContentAcquiredMessage(manifest));
            logger.LogInformation("Published ContentAcquiredMessage for manifest {ManifestId}", manifest.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish ContentAcquiredMessage for manifest {ManifestId}", manifest.Id);
        }
    }

    private void CleanupStagingDirectory(string stagingDir)
    {
        if (!Directory.Exists(stagingDir))
        {
            return;
        }

        try
        {
            Directory.Delete(stagingDir, true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to clean up manifest staging directory: {StagingDir}", stagingDir);
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
        var buildDir = setup.Folders?.AbsBuildDir;
        if (string.IsNullOrWhiteSpace(buildDir))
        {
            buildDir = !string.IsNullOrWhiteSpace(setup.ProjectDir)
                ? Path.Combine(setup.ProjectDir, ModBuilderConstants.DefaultBuildDir)
                : Path.Combine(Path.GetTempPath(), ModBuilderConstants.FallbackTempDirName, ModBuilderConstants.DefaultBuildDir);
        }

        return Path.Combine(buildDir, $"{stage}{ModBuilderConstants.JsonExtension}");
    }

    private void FireBundleEvent(BundleEventType eventType, string? bundleName)
    {
        try
        {
            logger.LogDebug("Firing bundle event: {EventType}", eventType);
            BundleEventTriggered?.Invoke(this, new BundleEventArgs
            {
                EventType = eventType,
                BundleItemName = bundleName,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error firing bundle event {EventType}", eventType);
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
            logger.LogDebug("Reusing cached build structure (hash matches: {Hash})", configHash);
            _cachedBuildStructure.Setup.Step = buildSteps;
            _cachedBuildStructure.Setup.ZipCompressionLevel = configuration.ZipCompressionLevel;
            if (configuration.Folders != null && _cachedBuildStructure.Setup.Folders != null)
            {
                if (!string.IsNullOrEmpty(configuration.Folders.AbsBuildDir))
                {
                    _cachedBuildStructure.Setup.Folders.AbsBuildDir = configuration.Folders.AbsBuildDir;
                }

                if (!string.IsNullOrEmpty(configuration.Folders.AbsReleaseDir))
                {
                    _cachedBuildStructure.Setup.Folders.AbsReleaseDir = configuration.Folders.AbsReleaseDir;
                }

                if (!string.IsNullOrEmpty(configuration.Folders.AbsGameDir))
                {
                    _cachedBuildStructure.Setup.Folders.AbsGameDir = configuration.Folders.AbsGameDir;
                }
            }

            return _cachedBuildStructure;
        }

        logger.LogInformation("Creating new build structure (config changed or first build)");

        var buildStructure = await CreateBuildStructureAsync(project, configuration, buildSteps, cancellationToken)
            .ConfigureAwait(false);

        _cachedBuildStructure = buildStructure;
        _cachedConfigHash = configHash;
        _cachedSourceToBundleFileMap = BuildSourceToBundleFileMap(buildStructure);

        return buildStructure;
    }

    private static Dictionary<string, BundleFile> BuildSourceToBundleFileMap(BuildStructure buildStructure)
    {
        var map = new Dictionary<string, BundleFile>(StringComparer.OrdinalIgnoreCase);
        if (buildStructure.BundleItems == null)
        {
            return map;
        }

        foreach (var file in buildStructure.BundleItems.Values
                     .Where(item => item.Files != null)
                     .SelectMany(item => item.Files)
                     .Where(file => !string.IsNullOrEmpty(file.AbsSourceFile)))
        {
            map.TryAdd(file.AbsSourceFile, file);
        }

        return map;
    }

    private async Task<string> ComputeConfigHashAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hashParts = new List<string>();

        AddProjectAndConfigFileHashParts(project, configuration, hashParts);
        AddBundleAndPackHashParts(project, configuration, hashParts);
        AddSourceFileHashParts(project, hashParts);
        hashParts.Add($"ZipCompression:{configuration.ZipCompressionLevel}");
        hashParts.Add($"Folders:{configuration.Folders?.AbsBuildDir}:{configuration.Folders?.AbsReleaseDir}:{configuration.Folders?.AbsGameDir}:{project.GameDir}");

        var combinedString = string.Join("|", hashParts);
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempFile, combinedString, cancellationToken)
                .ConfigureAwait(false);
            return await hashProvider.ComputeFileHashAsync(tempFile, cancellationToken)
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

    private static void AddProjectAndConfigFileHashParts(
        ModBuilderProject project,
        BuildConfiguration configuration,
        List<string> hashParts)
    {
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
    }

    private static void AddBundleAndPackHashParts(
        ModBuilderProject project,
        BuildConfiguration configuration,
        List<string> hashParts)
    {
        foreach (var bundleConfig in project.BundleConfigs)
        {
            var absolutePath = ProjectConfigService.ResolveBundleConfigPath(
                project.ProjectDir,
                project.Directories?.Configs ?? ModBuilderConstants.LowercaseConfigDir,
                bundleConfig);

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
    }

    private static void AddSourceFileHashParts(ModBuilderProject project, List<string> hashParts)
    {
        var sourceDir = ResolveSourceDir(project);
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        var dirStack = new Stack<string>();
        dirStack.Push(sourceDir);

        while (dirStack.Count > 0)
        {
            var current = dirStack.Pop();
            try
            {
                ProcessDirectoryFilesForHash(current, hashParts);
                EnqueueSubdirectories(current, dirStack);
            }
            catch (IOException)
            {
                // Skip inaccessible folder
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible folder
            }
        }
    }

    private static string ResolveSourceDir(ModBuilderProject project)
    {
        var projectDir = !string.IsNullOrWhiteSpace(project.ProjectDir) ? project.ProjectDir : Directory.GetCurrentDirectory();
        var gameFilesEdited = !string.IsNullOrWhiteSpace(project.Directories?.GameFilesEdited) ? project.Directories.GameFilesEdited : ModBuilderConstants.GameFilesEditedDir;
        return Path.Combine(projectDir, gameFilesEdited);
    }

    private static void ProcessDirectoryFilesForHash(string current, List<string> hashParts)
    {
        foreach (var file in Directory.EnumerateFiles(current))
        {
            try
            {
                var fi = new FileInfo(file);
                hashParts.Add($"{file}:{fi.LastWriteTimeUtc.Ticks}");
            }
            catch (IOException)
            {
                // Skip locked/unreadable file for hash
            }
            catch (UnauthorizedAccessException)
            {
                // Skip locked/unreadable file for hash
            }
        }
    }

    private static void EnqueueSubdirectories(string current, Stack<string> dirStack)
    {
        foreach (var subDir in Directory.EnumerateDirectories(current))
        {
            dirStack.Push(subDir);
        }
    }

    private async Task<BuildStructure> CreateBuildStructureAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        BuildStep buildSteps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogDebug("Resolving wildcards in configuration");
        configuration = await configurationLoaderService.ResolveWildcardsAsync(configuration, cancellationToken)
            .ConfigureAwait(false);

        var projectDir = !string.IsNullOrWhiteSpace(project.ProjectDir) ? project.ProjectDir : Directory.GetCurrentDirectory();
        var defaultBuild = !string.IsNullOrWhiteSpace(project.Directories?.Build) ? project.Directories.Build : ModBuilderConstants.DefaultBuildDir;
        var defaultRelease = !string.IsNullOrWhiteSpace(project.Directories?.Release) ? project.Directories.Release : ModBuilderConstants.DefaultReleaseDir;

        if (string.IsNullOrEmpty(configuration.Folders.AbsBuildDir))
        {
            configuration.Folders.AbsBuildDir = Path.Combine(projectDir, defaultBuild);
        }

        if (string.IsNullOrEmpty(configuration.Folders.AbsReleaseDir))
        {
            configuration.Folders.AbsReleaseDir = Path.Combine(projectDir, defaultRelease);
        }

        var gameDir = !string.IsNullOrEmpty(configuration.Folders.AbsGameDir)
            ? configuration.Folders.AbsGameDir
            : project.GameDir ?? string.Empty;

        var setup = new BuildSetup
        {
            Step = buildSteps,
            ProjectDir = projectDir,
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
            ZipCompressionLevel = configuration.ZipCompressionLevel,
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

    private static string? TryGetDuplicateEntryNamesError(BuildConfiguration configuration)
    {
        // Pack references resolve by name (case-insensitive), so duplicates are
        // ambiguous. Fail fast with an actionable message instead of the
        // cryptic duplicate-key error from dictionary construction.
        var duplicateItems = FindDuplicateNames(configuration.Items.Select(item => item.Name));
        var duplicatePacks = FindDuplicateNames(configuration.Packs.Select(pack => pack.Name));
        if (duplicateItems.Count == 0 && duplicatePacks.Count == 0)
        {
            return null;
        }

        var details = new List<string>();
        if (duplicateItems.Count > 0)
        {
            details.Add($"duplicate bundle item name(s): {string.Join(", ", duplicateItems)} (see ModBundleItems.json)");
        }

        if (duplicatePacks.Count > 0)
        {
            details.Add($"duplicate bundle pack name(s): {string.Join(", ", duplicatePacks)} (see ModBundlePacks.json)");
        }

        return $"Invalid bundle configuration with {string.Join(" and ", details)}. " +
            "Rename or remove the duplicates so pack references resolve unambiguously.";
    }

    private static IReadOnlyList<string> FindDuplicateNames(IEnumerable<string> names)
    {
        return names
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<BuildIndex, List<string>> PopulateStageFiles(
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

    private List<string> CollectRawBundleItemFiles(BuildConfiguration configuration)
    {
        var existing = new List<string>();
        foreach (var sourceFile in configuration.Items.SelectMany(item => item.Files.Select(f => f.AbsSourceFile)))
        {
            if (File.Exists(sourceFile))
            {
                existing.Add(sourceFile);
            }
            else
            {
                logger.LogWarning("Configured bundle source file does not exist: {File}", sourceFile);
            }
        }

        return existing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> CollectBigBundleItemFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        return configuration.Items
            .Where(item => item.IsBig)
            .Select(item => Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, GetBigFileName(item)))
            .ToList();
    }

    private static string GetPackFileName(BundlePack pack)
    {
        if (!string.IsNullOrWhiteSpace(pack.OutputFile))
        {
            var fileName = Path.GetFileName(pack.OutputFile);
            if (pack.IsBigPack && fileName.EndsWith(ModBuilderConstants.ZipExtension, StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(fileName, ModBuilderConstants.BigExtension);
            }

            if (!pack.IsBigPack && fileName.EndsWith(ModBuilderConstants.BigExtension, StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(fileName, ModBuilderConstants.ZipExtension);
            }

            return fileName;
        }

        var extension = pack.IsBigPack ? ModBuilderConstants.BigExtension : ModBuilderConstants.ZipExtension;
        return $"{pack.GetFullName()}{extension}";
    }

    private static List<string> CollectReleaseBundlePackFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;
        return configuration.Packs
            .Where(pack => pack.AllowBuild)
            .Select(pack => Path.Combine(releaseDir, GetPackFileName(pack)))
            .ToList();
    }

    private static bool IsSubpathOf(string basePath, string candidatePath)
    {
        var fullBase = Path.GetFullPath(basePath);
        if (!fullBase.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            fullBase += Path.DirectorySeparatorChar;
        }

        var fullCandidate = Path.GetFullPath(candidatePath);
        return fullCandidate.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
    }
}
