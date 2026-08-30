using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Central orchestrator for the 5-stage ModBuilder build pipeline.
/// Manages change detection, event system, and build execution.
/// </summary>
public sealed class BuildEngineService(
    IBuildCacheService cacheService,
    IFileConversionService fileConversionService,
    IMd5HashProvider hashProvider,
    IConfigurationLoaderService configurationLoaderService,
    IArchiveService archiveService,
    ILogger<BuildEngineService> logger) : IBuildEngineService
{
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private readonly object _abortLock = new();
    private readonly Dictionary<string, string?> _installedFiles = new(); // target -> backup (null if no backup)

    private CancellationTokenSource? _abortTokenSource;
    private bool _isRunning;
    private BuildStructure? _cachedBuildStructure;
    private string? _cachedConfigHash;
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
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

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
    }

    /// <summary>
    /// Internal method to run the build pipeline with BuildStructure.
    /// </summary>
    private async Task<bool> RunAsync(
        BuildStructure buildStructure,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _isRunning = true;
            _abortTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Build was cancelled");
            _lastErrorMessage = "Build was cancelled by user";
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build pipeline failed with exception");
            _lastErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            _isRunning = false;
            _abortTokenSource?.Dispose();
            _abortTokenSource = null;
        }
    }

    private static BuildStep ResolveBuildSteps(BuildStep steps)
    {
        if (steps == BuildStep.None)
        {
            return BuildStep.None;
        }

        if ((steps & BuildStep.Release) != 0)
        {
            steps |= BuildStep.Build;
        }

        if ((steps & BuildStep.Build) != 0)
        {
            steps |= BuildStep.PostBuild;
        }

        if ((steps & (BuildStep.Clean | BuildStep.Build | BuildStep.Install | BuildStep.Uninstall | BuildStep.Run)) != 0)
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
            (BuildStep.Uninstall, () => UninstallAsync(setup, progress, cancellationToken), "Uninstall stage failed"),
            (BuildStep.Install, () => InstallAsync(setup, progress, cancellationToken), "Install stage failed"),
            (BuildStep.Run, () => RunGameAsync(setup, progress, cancellationToken), "Run Game stage failed"),
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

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Executes the PreBuild stage.
    /// </summary>
    /// <param name="buildStructure">The build structure.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> PreBuildAsync(BuildStructure buildStructure, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("PreBuild stage started (using cached build structure)");
        progress?.Report(new BuildProgress { CurrentStep = "PreBuild: Initializing build structure" });

        // fire OnPreBuild events
        FireBundleEvent(BundleEventType.OnPreBuild, null);

        // build structure is already initialized and cached
        logger.LogDebug("Build structure contains {ItemCount} items and {PackCount} packs",
            buildStructure.BundleItems.Count,
            buildStructure.BundlePacks.Count);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Executes the Clean stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> CleanAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Clean stage started");
        progress?.Report(new BuildProgress { CurrentStep = "Clean: Removing build directories" });

        // delete build and release directories
        if (setup.Folders?.AbsBuildDir != null && Directory.Exists(setup.Folders.AbsBuildDir))
        {
            Directory.Delete(setup.Folders.AbsBuildDir, recursive: true);
            logger.LogInformation("Deleted build directory: {Dir}", setup.Folders.AbsBuildDir);
        }

        if (setup.Folders?.AbsReleaseDir != null && Directory.Exists(setup.Folders.AbsReleaseDir))
        {
            Directory.Delete(setup.Folders.AbsReleaseDir, recursive: true);
            logger.LogInformation("Deleted release directory: {Dir}", setup.Folders.AbsReleaseDir);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Executes the Build stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> BuildAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Build stage started");

        // ensure build directory exists
        if (!string.IsNullOrEmpty(setup.Folders?.AbsBuildDir))
        {
            Directory.CreateDirectory(setup.Folders.AbsBuildDir);
        }

        // fire OnBuild event
        FireBundleEvent(BundleEventType.OnBuild, null);

        // execute 3 build stages
        var success = true;
        success &= await BuildStageAsync(BuildIndex.RawBundleItem, setup, progress, cancellationToken).ConfigureAwait(false);
        success &= await BuildStageAsync(BuildIndex.BigBundleItem, setup, progress, cancellationToken).ConfigureAwait(false);
        success &= await BuildStageAsync(BuildIndex.RawBundlePack, setup, progress, cancellationToken).ConfigureAwait(false);

        return success;
    }

    private async Task<bool> BuildStageAsync(
        BuildIndex stage,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Building stage: {Stage}", stage);
        progress?.Report(new BuildProgress
        {
            CurrentIndex = stage,
            CurrentStep = $"Building {stage}",
        });

        // fire start event
        var startEvent = GetStartBuildEvent(stage);
        FireBundleEvent(startEvent, null);

        // load cache for this stage
        var cachePath = GetCachePath(stage, setup);

        // ensure cache directory exists
        var cacheDir = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        await cacheService.LoadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);

        var initialFailed = Volatile.Read(ref _filesFailed);

        // get files to process for this stage
        var filesToProcess = GetFilesForStage(stage);

        logger.LogInformation("Processing {Count} files for stage {Stage}", filesToProcess.Count, stage);

        if (stage == BuildIndex.BigBundleItem)
        {
            await ExecuteBigBundleItemStageAsync(setup, progress, cancellationToken).ConfigureAwait(false);
        }
        else if (stage == BuildIndex.ReleaseBundlePack)
        {
            await ExecuteReleaseBundlePackStageAsync(setup, progress, cancellationToken).ConfigureAwait(false);
        }
        else if (stage == BuildIndex.RawBundleItem)
        {
            // process files in parallel for optimum performance
            await Parallel.ForEachAsync(
                filesToProcess,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken
                },
                (file, ct) => new ValueTask(ProcessFileAsync(file, stage, setup, progress, ct)))
                .ConfigureAwait(false);
        }

        // fire finish event
        var finishEvent = GetFinishBuildEvent(stage);
        FireBundleEvent(finishEvent, null);

        // save cache
        await cacheService.SaveCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);

        var stageFailed = Volatile.Read(ref _filesFailed) > initialFailed;
        return !stageFailed;
    }

    private async Task ExecuteBigBundleItemStageAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var rawDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.RawBundleItemsSubdir);
        var bundlesDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.BundlesSubdir);

        if (setup.Bundles?.Items == null)
        {
            return;
        }

        if (!Directory.Exists(bundlesDir))
        {
            Directory.CreateDirectory(bundlesDir);
        }

        foreach (var item in setup.Bundles.Items.Where(i => i.IsBig))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = item.BigSuffix ?? string.Empty;
            var bigFileName = suffix.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                ? $"{item.GetFullName()}{suffix}"
                : $"{item.GetFullName()}{suffix}.big";
            var bigFilePath = Path.Combine(bundlesDir, bigFileName);

            var itemStagingDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ".staging", item.Name);
            if (Directory.Exists(itemStagingDir))
            {
                Directory.Delete(itemStagingDir, true);
            }

            Directory.CreateDirectory(itemStagingDir);
            StageBundleItemFiles(item, rawDir, itemStagingDir);

            var archiveResult = await archiveService.CreateBigArchiveAsync(itemStagingDir, bigFilePath, null, cancellationToken).ConfigureAwait(false);
            if (!archiveResult.Success)
            {
                logger.LogError("Failed to create BIG archive {Archive}: {Error}", bigFilePath, archiveResult.FirstError);
                Interlocked.Increment(ref _filesFailed);
            }
            else
            {
                logger.LogInformation("Created bundle: {BigFile}", bigFilePath);
                progress?.Report(new BuildProgress
                {
                    CurrentIndex = BuildIndex.BigBundleItem,
                    CurrentStage = BuildStage.Archiving,
                    CurrentFile = bigFileName,
                    CurrentStep = $"Created bundle: {bigFileName}",
                    ProcessedFiles = Volatile.Read(ref _filesProcessed)
                });
            }

            if (Directory.Exists(itemStagingDir))
            {
                try
                {
                    Directory.Delete(itemStagingDir, true);
                }
                catch
                {
                    // Ignore cleanup failure
                }
            }
        }
    }

    private static void StageBundleItemFiles(BundleItem item, string rawDir, string itemStagingDir)
    {
        var fullStagingDir = Path.GetFullPath(itemStagingDir);
        var stagingDirPrefix = fullStagingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var file in item.Files)
        {
            var targetRel = ResolveItemFileRelativePath(file);
            if (string.IsNullOrEmpty(targetRel))
            {
                continue;
            }

            var cleanRel = targetRel.TrimStart('/', '\\');
            var srcInRaw = Path.GetFullPath(Path.Combine(rawDir, cleanRel));
            var srcDirect = !string.IsNullOrEmpty(file.AbsSourceFile) && File.Exists(file.AbsSourceFile)
                ? Path.GetFullPath(file.AbsSourceFile)
                : null;
            var actualSource = File.Exists(srcInRaw) ? srcInRaw : srcDirect;
            var destInStaging = Path.GetFullPath(Path.Combine(itemStagingDir, cleanRel));

            if (!string.IsNullOrEmpty(actualSource) &&
                File.Exists(actualSource) &&
                destInStaging.StartsWith(stagingDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var destDir = Path.GetDirectoryName(destInStaging);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(actualSource, destInStaging, overwrite: true);
            }
        }
    }

    private static string ResolveItemFileRelativePath(BundleFile file)
    {
        if (!string.IsNullOrEmpty(file.RelTargetFile))
        {
            return file.RelTargetFile;
        }

        if (!string.IsNullOrEmpty(file.GetRelSourceFile()))
        {
            return file.GetRelSourceFile();
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

        foreach (var pack in setup.Bundles.Packs.Where(p => p.AllowBuild))
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

        if (items != null)
        {
            StagePackBigFiles(pack, items, bundlesDir, packStagingDir);
        }

        var archiveResult = await archiveService.CreateZipArchiveAsync(packStagingDir, zipFilePath, System.IO.Compression.CompressionLevel.Optimal, null, cancellationToken).ConfigureAwait(false);
        if (!archiveResult.Success)
        {
            logger.LogError("Failed to create ZIP archive {Archive}: {Error}", zipFilePath, archiveResult.FirstError);
            Interlocked.Increment(ref _filesFailed);
        }
        else
        {
            logger.LogInformation("Created release pack: {ZipFile}", zipFilePath);
            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStage = BuildStage.Archiving,
                CurrentFile = zipFileName,
                CurrentStep = $"Created release pack: {zipFileName}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed)
            });
        }

        if (Directory.Exists(packStagingDir))
        {
            try
            {
                Directory.Delete(packStagingDir, true);
            }
            catch
            {
                // Ignore cleanup failure
            }
        }
    }

    private static void StagePackBigFiles(BundlePack pack, IReadOnlyList<BundleItem> items, string bundlesDir, string packStagingDir)
    {
        foreach (var itemName in pack.ItemNames)
        {
            var item = items.FirstOrDefault(i => i.Name == itemName);
            if (item != null && item.IsBig)
            {
                var suffix = item.BigSuffix ?? string.Empty;
                var bigFileName = suffix.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                    ? $"{item.GetFullName()}{suffix}"
                    : $"{item.GetFullName()}{suffix}.big";
                var bigFilePath = Path.Combine(bundlesDir, bigFileName);

                if (File.Exists(bigFilePath))
                {
                    var destBig = Path.Combine(packStagingDir, bigFileName);
                    File.Copy(bigFilePath, destBig, overwrite: true);
                }
            }
        }
    }

    /// <summary>
    /// Process a single file for the given build stage.
    /// </summary>
    private async Task ProcessFileAsync(
        string filePath,
        BuildIndex stage,
        BuildSetup setup,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                logger.LogWarning("Source file not found: {FilePath}", filePath);
                return;
            }

            var currentMd5 = await cacheService.ComputeOrReuseMd5Async(filePath, cancellationToken)
                .ConfigureAwait(false);

            var fileStatus = cacheService.DetermineFileStatus(filePath, currentMd5, null);

            if (fileStatus == BuildFileStatus.Unchanged || fileStatus == BuildFileStatus.Irrelevant)
            {
                logger.LogDebug("Skipping unchanged file: {FilePath}", filePath);

                var fileInfo = new FileInfo(filePath);
                var unixTime = fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
                cacheService.AddFile(filePath, unixTime, currentMd5, null);

                Interlocked.Increment(ref _filesSkipped);
                return;
            }

            var targetPath = GetTargetPathForFile(filePath, stage, setup);
            if (string.IsNullOrEmpty(targetPath))
            {
                logger.LogWarning("Could not determine target path for: {FilePath}", filePath);
                return;
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            logger.LogDebug("Processing file: {Source} -> {Target}", filePath, targetPath);

            var conversionResult = await fileConversionService.ConvertFileAsync(
                filePath,
                targetPath,
                conversionType: null,
                progress: null,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!conversionResult.Success)
            {
                logger.LogError("File conversion failed: {Error}", conversionResult.FirstError);
                Interlocked.Increment(ref _filesFailed);
                return;
            }

            var fileInfoFinal = new FileInfo(filePath);
            var unixTimeFinal = fileInfoFinal.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
            cacheService.AddFile(filePath, unixTimeFinal, currentMd5, null);

            Interlocked.Increment(ref _filesProcessed);

            progress?.Report(new BuildProgress
            {
                CurrentIndex = stage,
                CurrentStage = BuildStage.Processing,
                CurrentFile = Path.GetFileName(filePath),
                CurrentStep = $"Processing file: {Path.GetFileName(filePath)}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed)
            });

            logger.LogDebug("Processed file: {FilePath} for stage {Stage} (status: {Status})", filePath, stage, fileStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process file: {FilePath}", filePath);
            Interlocked.Increment(ref _filesFailed);
        }
    }

    /// <summary>
    /// Get the list of files to process for the given build stage.
    /// </summary>
    private List<string> GetFilesForStage(BuildIndex stage)
    {
        var files = new List<string>();

        if (_cachedBuildStructure?.StageFiles.TryGetValue(stage, out var stageFiles) == true)
        {
            files.AddRange(stageFiles);
        }

        logger.LogDebug("Found {Count} files for stage {Stage}", files.Count, stage);
        return files;
    }

    /// <summary>
    /// Determines the target path for a file based on the build stage.
    /// </summary>
    private static string GetTargetPathForFile(string sourcePath, BuildIndex stage, BuildSetup setup)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var fileName = Path.GetFileName(sourcePath);

        if (stage == BuildIndex.RawBundleItem)
        {
            return GetRawBundleItemTargetPath(sourcePath, buildDir, fileName, setup.Bundles?.Items);
        }

        return stage switch
        {
            BuildIndex.BigBundleItem => Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, fileName),
            BuildIndex.RawBundlePack => Path.Combine(buildDir, ModBuilderConstants.BundlePacksSubdir, fileName),
            BuildIndex.ReleaseBundlePack => Path.Combine(setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir, fileName),
            BuildIndex.InstallBundlePack => Path.Combine(setup.Folders?.AbsGameDir ?? string.Empty, fileName),
            _ => string.Empty,
        };
    }

    private static string GetRawBundleItemTargetPath(string sourcePath, string buildDir, string fileName, IEnumerable<BundleItem>? items)
    {
        if (items != null)
        {
            foreach (var item in items)
            {
                var matchingFile = item.Files.FirstOrDefault(f => string.Equals(f.AbsSourceFile, sourcePath, StringComparison.OrdinalIgnoreCase));
                if (matchingFile != null)
                {
                    var relPath = !string.IsNullOrEmpty(matchingFile.RelTargetFile)
                        ? matchingFile.RelTargetFile
                        : matchingFile.GetRelSourceFile();

                    if (!string.IsNullOrEmpty(relPath))
                    {
                        return Path.Combine(buildDir, ModBuilderConstants.RawBundleItemsSubdir, relPath.TrimStart('/', '\\'));
                    }
                }
            }
        }

        return Path.Combine(buildDir, ModBuilderConstants.RawBundleItemsSubdir, fileName);
    }

    /// <summary>
    /// Executes the PostBuild stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> PostBuildAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("PostBuild stage started");
        progress?.Report(new BuildProgress { CurrentStep = "PostBuild: Finalizing" });

        // fire OnPostBuild events
        FireBundleEvent(BundleEventType.OnPostBuild, setup.Folders?.AbsBuildDir);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Executes the Release stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> ReleaseAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Release stage started");
        progress?.Report(new BuildProgress
        {
            CurrentIndex = BuildIndex.ReleaseBundlePack,
            CurrentStep = "Creating release archives",
        });

        // fire OnRelease event
        FireBundleEvent(BundleEventType.OnRelease, null);

        return await BuildStageAsync(BuildIndex.ReleaseBundlePack, setup, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the Install stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> InstallAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Install stage started");
        progress?.Report(new BuildProgress
        {
            CurrentIndex = BuildIndex.InstallBundlePack,
            CurrentStep = "Installing to game directory",
        });

        // fire OnInstall event
        FireBundleEvent(BundleEventType.OnInstall, null);

        var installFiles = GetFilesForStage(BuildIndex.InstallBundlePack);

        if (installFiles.Count == 0)
        {
            logger.LogInformation("No files to install");
            return true;
        }

        var gameDir = setup.Folders?.AbsGameDir;
        if (string.IsNullOrEmpty(gameDir))
        {
            gameDir = _cachedBuildStructure?.Configuration?.Folders?.AbsGameDir;
        }

        if (string.IsNullOrEmpty(gameDir))
        {
            logger.LogError("Game directory not configured. Please specify a game directory in project settings or select an installation in Game Asset & File Manager.");
            return false;
        }

        _installedFiles.Clear();

        foreach (var sourcePath in installFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BackupAndInstallFile(sourcePath, gameDir, progress))
            {
                return false;
            }
        }

        await SaveInstallManifestAsync(gameDir, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Installed {Count} files", _installedFiles.Count);
        return true;
    }

    private bool BackupAndInstallFile(string sourcePath, string gameDir, IProgress<BuildProgress>? progress)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("Source file not found: {File}", sourcePath);
                return true;
            }

            var fileName = Path.GetFileName(sourcePath);
            var targetPath = Path.Combine(gameDir, fileName);

            if (File.Exists(targetPath))
            {
                var backupPath = targetPath + ModBuilderConstants.BackupFileExtension;
                if (!File.Exists(backupPath))
                {
                    File.Copy(targetPath, backupPath, overwrite: false);
                }

                _installedFiles[targetPath] = backupPath;
                logger.LogDebug("Backed up: {File}", targetPath);
            }
            else
            {
                _installedFiles[targetPath] = null;
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            logger.LogInformation("Installed: {File}", fileName);

            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.InstallBundlePack,
                CurrentStep = $"Installed: {fileName}",
                CurrentFile = fileName
            });
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install file: {File}", sourcePath);
            return false;
        }
    }

    /// <summary>
    /// Executes the run game stage.
    /// </summary>
    /// <param name="setup">Build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> RunGameAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Run stage started");
        progress?.Report(new BuildProgress { CurrentStep = "Launching game" });

        // fire OnRun event
        FireBundleEvent(BundleEventType.OnRun, null);

        var runnerConfig = _cachedBuildStructure?.Configuration?.Runner;
        if (runnerConfig == null)
        {
            logger.LogWarning("Runner configuration not available, skipping run");
            return true;
        }

        var gameExePath = ResolveGameExecutablePath(setup, runnerConfig);
        if (string.IsNullOrEmpty(gameExePath))
        {
            logger.LogWarning("Game executable not configured, skipping run");
            return true;
        }

        if (!File.Exists(gameExePath))
        {
            logger.LogError("Game executable not found: {Path}", gameExePath);
            throw new FileNotFoundException($"Game executable not found: {gameExePath}");
        }

        var startInfo = BuildGameStartInfo(runnerConfig, setup, gameExePath);
        logger.LogDebug("Process start: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);

        var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.LogError("Failed to start game process");
            return false;
        }

        logger.LogInformation("Game launched successfully (PID: {Pid})", process.Id);
        return true;
    }

    private string? ResolveGameExecutablePath(BuildSetup setup, RunnerConfiguration runnerConfig)
    {
        var gameExePath = runnerConfig.AbsExe;
        if (string.IsNullOrEmpty(gameExePath))
        {
            var resolvedGameDir = setup.Folders?.AbsGameDir ?? _cachedBuildStructure?.Configuration?.Folders?.AbsGameDir;
            if (!string.IsNullOrEmpty(resolvedGameDir) && Directory.Exists(resolvedGameDir))
            {
                var candidateExes = new[] { "generals.exe", "game.dat", "EAC_LaunchGeneralsOnline.exe", "worldbuilder.exe" };
                foreach (var exe in candidateExes)
                {
                    var fullCandidate = Path.Combine(resolvedGameDir, exe);
                    if (File.Exists(fullCandidate))
                    {
                        gameExePath = fullCandidate;
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(gameExePath))
        {
            return null;
        }

        if (!Path.IsPathRooted(gameExePath))
        {
            var gameDir = setup.Folders?.AbsGameDir;
            if (string.IsNullOrEmpty(gameDir))
            {
                logger.LogError("Game directory not configured");
                throw new InvalidOperationException("Game directory not configured");
            }

            gameExePath = Path.Combine(gameDir, gameExePath);
        }

        return gameExePath;
    }

    private static ProcessStartInfo BuildGameStartInfo(RunnerConfiguration runnerConfig, BuildSetup setup, string gameExePath)
    {
        var workingDirectory = runnerConfig.WorkingDir;
        if (string.IsNullOrEmpty(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(gameExePath);
        }
        else if (!Path.IsPathRooted(workingDirectory))
        {
            var gameDir = setup.Folders?.AbsGameDir;
            if (!string.IsNullOrEmpty(gameDir))
            {
                workingDirectory = Path.Combine(gameDir, workingDirectory);
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gameExePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        var args = runnerConfig.Args ?? string.Empty;

        // Native game mod folder support (-mod <FolderPath>)
        if (!args.Contains("-mod", StringComparison.OrdinalIgnoreCase))
        {
            var modFolder = !string.IsNullOrEmpty(runnerConfig.ModFolder)
                ? runnerConfig.ModFolder
                : setup.Folders?.AbsReleaseDir;

            if (!string.IsNullOrEmpty(modFolder))
            {
                args = string.IsNullOrEmpty(args)
                    ? $"-mod \"{modFolder}\""
                    : $"{args} -mod \"{modFolder}\"";
            }
        }

        if (!string.IsNullOrEmpty(args))
        {
            startInfo.Arguments = args;
        }

        return startInfo;
    }

    /// <summary>
    /// Executes the Uninstall stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> UninstallAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Uninstall stage started");
        progress?.Report(new BuildProgress { CurrentStep = "Uninstalling bundle pack" });

        // fire OnUninstall event
        FireBundleEvent(BundleEventType.OnUninstall, null);

        var gameDir = setup.Folders?.AbsGameDir;
        if (string.IsNullOrEmpty(gameDir))
        {
            gameDir = _cachedBuildStructure?.Configuration?.Folders?.AbsGameDir;
        }

        if (string.IsNullOrEmpty(gameDir))
        {
            logger.LogError("Game directory not configured. Please specify a game directory in project settings or select an installation in Game Asset & File Manager.");
            return false;
        }

        await LoadInstallManifestAsync(gameDir, cancellationToken).ConfigureAwait(false);

        if (_installedFiles.Count == 0)
        {
            logger.LogInformation("No files to uninstall");
            return true;
        }

        var successfullyRemoved = new List<string>();
        var hasErrors = false;

        foreach (var (targetPath, backupPath) in _installedFiles)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    logger.LogDebug("Removed: {File}", targetPath);
                }

                if (backupPath != null && File.Exists(backupPath))
                {
                    File.Move(backupPath, targetPath, overwrite: true);
                    logger.LogInformation("Restored: {File}", targetPath);
                }

                successfullyRemoved.Add(targetPath);

                var fileName = Path.GetFileName(targetPath);
                progress?.Report(new BuildProgress
                {
                    CurrentStep = $"Uninstalled: {fileName}",
                    CurrentFile = targetPath
                });
            }
            catch (Exception ex)
            {
                hasErrors = true;
                logger.LogWarning(ex, "Failed to uninstall {File}: {Message}", targetPath, ex.Message);
            }
        }

        foreach (var path in successfullyRemoved)
        {
            _installedFiles.Remove(path);
        }

        var manifestPath = Path.Combine(gameDir, ModBuilderConstants.InstallManifestFileName);

        if (hasErrors)
        {
            await SaveInstallManifestAsync(gameDir, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Uninstall finished with errors; preserving manifest for remaining {Count} files", _installedFiles.Count);
            return false;
        }

        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
            logger.LogDebug("Deleted install manifest: {Path}", manifestPath);
        }

        logger.LogInformation("Uninstalled {Count} files", successfullyRemoved.Count);
        _installedFiles.Clear();
        return true;
    }

    /// <summary>
    /// Fires a bundle event.
    /// </summary>
    private void FireBundleEvent(BundleEventType eventType, string? bundleName)
    {
        logger.LogDebug("Firing bundle event: {EventType}", eventType);
        BundleEventTriggered?.Invoke(this, new BundleEventArgs
        {
            EventType = eventType,
            BundleItemName = bundleName,
        });
    }

    /// <summary>
    /// Gets the start build event for a given stage.
    /// </summary>
    private static BundleEventType GetStartBuildEvent(BuildIndex stage)
    {
        return stage switch
        {
            BuildIndex.RawBundleItem => BundleEventType.OnStartBuildRawBundleItem,
            BuildIndex.BigBundleItem => BundleEventType.OnStartBuildBigBundleItem,
            BuildIndex.RawBundlePack => BundleEventType.OnStartBuildRawBundlePack,
            BuildIndex.ReleaseBundlePack => BundleEventType.OnStartBuildReleaseBundlePack,
            BuildIndex.InstallBundlePack => BundleEventType.OnStartBuildInstallBundlePack,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
    }

    /// <summary>
    /// Gets the finish build event for a given stage.
    /// </summary>
    private static BundleEventType GetFinishBuildEvent(BuildIndex stage)
    {
        return stage switch
        {
            BuildIndex.RawBundleItem => BundleEventType.OnFinishBuildRawBundleItem,
            BuildIndex.BigBundleItem => BundleEventType.OnFinishBuildBigBundleItem,
            BuildIndex.RawBundlePack => BundleEventType.OnFinishBuildRawBundlePack,
            BuildIndex.ReleaseBundlePack => BundleEventType.OnFinishBuildReleaseBundlePack,
            BuildIndex.InstallBundlePack => BundleEventType.OnFinishBuildInstallBundlePack,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
    }

    /// <summary>
    /// Gets the cache path for a given build stage.
    /// </summary>
    private static string GetCachePath(BuildIndex stage, BuildSetup setup)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        return Path.Combine(buildDir, $"{stage}.json");
    }

    /// <summary>
    /// Gets or creates the build structure, using cache if configuration hasn't changed.
    /// </summary>
    /// <param name="project">The ModBuilder project.</param>
    /// <param name="configuration">The build configuration.</param>
    /// <param name="buildSteps">The build steps to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The build structure.</returns>
    private async Task<BuildStructure> GetOrCreateBuildStructureAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        BuildStep buildSteps,
        CancellationToken cancellationToken)
    {
        var configHash = await ComputeConfigHashAsync(project, configuration, cancellationToken)
            .ConfigureAwait(false);

        if (_cachedBuildStructure != null && _cachedConfigHash == configHash)
        {
            logger.LogDebug("Using cached build structure");
            _cachedBuildStructure.Setup.Step = buildSteps;
            return _cachedBuildStructure;
        }

        logger.LogInformation("Building new build structure (config changed)");
        var structure = await CreateBuildStructureAsync(project, configuration, buildSteps, cancellationToken)
            .ConfigureAwait(false);

        _cachedBuildStructure = structure;
        _cachedConfigHash = configHash;

        return structure;
    }

    /// <summary>
    /// Computes a hash of the project configuration to detect changes.
    /// </summary>
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

    /// <summary>
    /// Creates a new build structure from the project and configuration.
    /// </summary>
    private async Task<BuildStructure> CreateBuildStructureAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        BuildStep buildSteps,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Resolving wildcards in configuration");
        configuration = await configurationLoaderService.ResolveWildcardsAsync(configuration, cancellationToken)
            .ConfigureAwait(false);

        var projectDir = project.ProjectDir;
        if (!string.IsNullOrEmpty(projectDir))
        {
            if (string.IsNullOrEmpty(configuration.Folders.AbsBuildDir))
            {
                configuration.Folders.AbsBuildDir = Path.Combine(projectDir, project.Directories.Build ?? ModBuilderConstants.DefaultBuildDir);
            }

            if (string.IsNullOrEmpty(configuration.Folders.AbsReleaseDir))
            {
                configuration.Folders.AbsReleaseDir = Path.Combine(projectDir, project.Directories.Release ?? ModBuilderConstants.DefaultReleaseDir);
            }
        }

        var gameDir = !string.IsNullOrEmpty(configuration.Folders.AbsGameDir)
            ? configuration.Folders.AbsGameDir
            : project.GameDir ?? string.Empty;

        var setup = new BuildSetup
        {
            Step = buildSteps,
            Folders = new Folders
            {
                AbsBuildDir = configuration.Folders.AbsBuildDir,
                AbsReleaseDir = configuration.Folders.AbsReleaseDir,
                AbsGameDir = gameDir,
            },
            Bundles = new Bundles
            {
                Items = configuration.Items,
                Packs = configuration.Packs,
            },
            Runner = new Runner(),
            RunnerConfig = configuration.Runner,
        };

        var stageFiles = BuildStageFiles(setup, configuration);

        var bundleItems = configuration.Items.ToDictionary(item => item.Name, item => item);
        var bundlePacks = configuration.Packs.ToDictionary(pack => pack.Name, pack => pack);

        await Task.CompletedTask.ConfigureAwait(false);

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

    private Dictionary<BuildIndex, List<string>> BuildStageFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var stageFiles = new Dictionary<BuildIndex, List<string>>();

        var rawBundleItemFiles = CollectRawBundleItemFiles(configuration);
        stageFiles[BuildIndex.RawBundleItem] = rawBundleItemFiles;

        var bigBundleItemFiles = CollectBigBundleItemFiles(setup, configuration);
        stageFiles[BuildIndex.BigBundleItem] = bigBundleItemFiles;

        var rawBundlePackFiles = CollectRawBundlePackFiles(setup, configuration);
        stageFiles[BuildIndex.RawBundlePack] = rawBundlePackFiles;

        var releaseBundlePackFiles = CollectReleaseBundlePackFiles(setup, configuration);
        stageFiles[BuildIndex.ReleaseBundlePack] = releaseBundlePackFiles;

        var installBundlePackFiles = CollectInstallBundlePackFiles(setup, configuration);
        stageFiles[BuildIndex.InstallBundlePack] = installBundlePackFiles;

        logger.LogInformation(
            "Stage file summary: RawItems={RawCount}, BigItems={BigCount}, RawPacks={RawPackCount}, ReleasePacks={ReleaseCount}, InstallPacks={InstallCount}",
            rawBundleItemFiles.Count, bigBundleItemFiles.Count, rawBundlePackFiles.Count, releaseBundlePackFiles.Count, installBundlePackFiles.Count);

        return stageFiles;
    }

    private List<string> CollectRawBundleItemFiles(BuildConfiguration configuration)
    {
        var files = new List<string>();
        foreach (var sourceFile in configuration.Items.SelectMany(item => item.Files).Select(f => f.AbsSourceFile))
        {
            if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
            {
                files.Add(sourceFile);
            }
            else
            {
                logger.LogWarning("Source file not found: {FilePath}", sourceFile);
            }
        }

        return files;
    }

    private static List<string> CollectBigBundleItemFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        return configuration.Items
            .Where(item => item.IsBig)
            .Select(item => Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, $"{item.GetFullName()}{item.BigSuffix}.big"))
            .ToList();
    }

    private static List<string> CollectRawBundlePackFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var files = new List<string>();
        foreach (var pack in configuration.Packs.Where(p => p.AllowBuild))
        {
            foreach (var itemName in pack.ItemNames)
            {
                var item = configuration.Items.FirstOrDefault(i => i.Name == itemName);
                if (item != null && item.IsBig)
                {
                    var bigFileName = $"{item.GetFullName()}{item.BigSuffix}.big";
                    files.Add(Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, bigFileName));
                }
            }
        }

        return files;
    }

    private static List<string> CollectReleaseBundlePackFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;
        return configuration.Packs
            .Where(pack => pack.AllowBuild)
            .Select(pack => Path.Combine(releaseDir, $"{pack.GetFullName()}.zip"))
            .ToList();
    }

    private static List<string> CollectInstallBundlePackFiles(BuildSetup setup, BuildConfiguration configuration)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var files = new List<string>();
        foreach (var pack in configuration.Packs.Where(p => p.AllowInstall))
        {
            foreach (var itemName in pack.ItemNames)
            {
                var item = configuration.Items.FirstOrDefault(i => i.Name == itemName);
                if (item != null && item.IsBig)
                {
                    var bigFileName = $"{item.GetFullName()}{item.BigSuffix}.big";
                    files.Add(Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, bigFileName));
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Saves the install manifest to disk.
    /// </summary>
    private async Task SaveInstallManifestAsync(string gameDir, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(gameDir, ModBuilderConstants.InstallManifestFileName);

        var json = JsonSerializer.Serialize(_installedFiles, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(manifestPath, json, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Saved install manifest: {Path}", manifestPath);
    }

    /// <summary>
    /// Loads the install manifest from disk.
    /// </summary>
    private async Task LoadInstallManifestAsync(string gameDir, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(gameDir, ModBuilderConstants.InstallManifestFileName);

        if (!File.Exists(manifestPath))
        {
            logger.LogDebug("No install manifest found");
            return;
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);

        _installedFiles.Clear();
        if (manifest != null)
        {
            foreach (var (key, value) in manifest)
            {
                _installedFiles[key] = value;
            }
        }

        logger.LogDebug("Loaded install manifest: {Count} files", _installedFiles.Count);
    }
}
