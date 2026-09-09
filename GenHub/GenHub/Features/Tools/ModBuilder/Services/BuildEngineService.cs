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
    ILocalContentService localContentService,
    ILogger<BuildEngineService> logger) : IBuildEngineService
{
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
        logger.LogDebug(
            "Build structure contains {ItemCount} items and {PackCount} packs",
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
                    CancellationToken = cancellationToken,
                },
                async (filePath, ct) =>
                {
                    await ProcessSingleFileAsync(filePath, stage, setup, progress, ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        else
        {
            foreach (var filePath in filesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessSingleFileAsync(filePath, stage, setup, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        // save cache for this stage
        await cacheService.SaveCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);

        // fire finish event
        var finishEvent = GetFinishBuildEvent(stage);
        FireBundleEvent(finishEvent, null);

        var finalFailed = Volatile.Read(ref _filesFailed);
        return finalFailed == initialFailed;
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

    private async Task BuildSingleBigBundleItemAsync(
        BundleItem item,
        string bundlesDir,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken,
        int currentItem,
        int totalBigItems)
    {
        var bigFileName = $"{item.GetFullName()}{item.BigSuffix}.big";
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
                    logger.LogWarning("File not found for BIG bundle: {FilePath}", sourceFile);
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
                    ProcessedFiles = Volatile.Read(ref _filesProcessed),
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

            var archiveResult = await archiveService.CreateBigArchiveAsync(stagingDir, bigFilePath, archiveProgress, cancellationToken)
                .ConfigureAwait(false);

            if (!archiveResult.Success)
            {
                Interlocked.Increment(ref _filesFailed);
                logger.LogError("Failed to create BIG archive for item {ItemName}: {Error}", item.Name, archiveResult.FirstError);
                _lastErrorMessage = $"Failed to create BIG archive for item {item.Name}: {archiveResult.FirstError}";
            }
            else
            {
                Interlocked.Increment(ref _filesProcessed);
                logger.LogInformation("Successfully created BIG archive: {Path}", bigFilePath);
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

    private static string GetTargetRelativePath(BundleFile file)
    {
        if (!string.IsNullOrEmpty(file.RelTargetFile))
        {
            return file.RelTargetFile;
        }

        if (!string.IsNullOrEmpty(file.AbsSourceParent) &&
            file.AbsSourceFile.StartsWith(file.AbsSourceParent, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(file.AbsSourceParent, file.AbsSourceFile);
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
            Interlocked.Increment(ref _filesFailed);
            logger.LogError("Failed to create ZIP archive for pack {PackName}: {Error}", pack.Name, archiveResult.FirstError);
            _lastErrorMessage = $"Failed to create ZIP archive for pack {pack.Name}: {archiveResult.FirstError}";
        }
        else
        {
            Interlocked.Increment(ref _filesProcessed);
            logger.LogInformation("Successfully created ZIP archive: {Path}", zipFilePath);
        }

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

    private void StagePackBigFiles(BundlePack pack, IReadOnlyList<BundleItem> items, string bundlesDir, string packStagingDir)
    {
        foreach (var itemName in pack.ItemNames)
        {
            var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (item != null && item.IsBig)
            {
                var suffix = item.BigSuffix ?? string.Empty;
                var bigFileName = suffix.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                    ? $"{item.GetFullName()}{suffix}"
                    : $"{item.GetFullName()}{suffix}.big";

                var srcBig = Path.Combine(bundlesDir, bigFileName);
                if (File.Exists(srcBig))
                {
                    var destBig = Path.Combine(packStagingDir, bigFileName);
                    File.Copy(srcBig, destBig, true);
                }
                else
                {
                    logger.LogWarning("BIG bundle {BigFileName} missing when packaging pack {PackName}", bigFileName, pack.Name);
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
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);

        // check file status using cache
        var status = cacheService.DetermineFileStatus(filePath, relativePath, null);

        if (status == BuildFileStatus.Unchanged)
        {
            logger.LogDebug("Skipping unchanged file: {FilePath}", filePath);
            Interlocked.Increment(ref _filesSkipped);
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
            // compute hash and update cache
            var hash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
            var fileInfo = new FileInfo(filePath);
            var mtime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
            cacheService.AddFile(relativePath, mtime, hash);

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
            logger.LogError("Failed to process file: {FilePath}", filePath);
        }
    }

    private async Task<bool> ProcessRawBundleItemFileAsync(string filePath, BuildSetup setup, CancellationToken cancellationToken)
    {
        // determine if file needs conversion
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var targetPath = GetTargetPathForFile(filePath, BuildIndex.RawBundleItem, setup);

        // perform format conversions based on file type
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
        // change extension to .dds for converted textures
        var ddsTargetPath = Path.ChangeExtension(targetPath, ".dds");
        var result = await fileConversionService.ConvertFileAsync(sourcePath, ddsTargetPath, "DDS", null, cancellationToken)
            .ConfigureAwait(false);

        return result.Success;
    }

    private async Task<bool> ConvertStringTableFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        // change extension to .csf for compiled string tables
        var csfTargetPath = Path.ChangeExtension(targetPath, ".csf");
        var result = await fileConversionService.ConvertFileAsync(sourcePath, csfTargetPath, "CSF", null, cancellationToken)
            .ConfigureAwait(false);

        return result.Success;
    }

    private async Task<bool> ProcessIniFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        // copy INI files directly (text processing handled by separate service)
        return await CopyFileDirectlyAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> CopyFileDirectlyAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            await using var sourceStream = File.OpenRead(sourcePath);
            await using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

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
            BuildIndex.CreateManifest => Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, fileName),
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

    private List<string> GetFilesForStage(BuildIndex stage)
    {
        if (_cachedBuildStructure?.StageFiles.TryGetValue(stage, out var files) == true)
        {
            return files;
        }

        return [];
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
        progress?.Report(new BuildProgress { CurrentStep = "PostBuild: Finalizing build" });

        // fire OnPostBuild event
        FireBundleEvent(BundleEventType.OnPostBuild, null);

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
    /// Executes the CreateManifest stage: stores built bundles into CAS and generates a local ContentManifest in the GenHub library.
    /// </summary>
    /// <param name="buildStructure">The build structure containing project and setup info.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> CreateManifestAsync(
        BuildStructure buildStructure,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("CreateManifest stage started");
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
            logger.LogError("Bundles directory does not exist: {BundlesDir}. Please run the Build step first.", bundlesDir);
            _lastErrorMessage = $"Bundles directory does not exist: {bundlesDir}";
            return false;
        }

        var bigFiles = Directory.GetFiles(bundlesDir, "*.big");
        if (bigFiles.Length == 0)
        {
            logger.LogError("No .big bundle files found in {BundlesDir} to create manifest.", bundlesDir);
            _lastErrorMessage = $"No .big bundle files found in {bundlesDir}";
            return false;
        }

        // Determine which .big files to include
        var filesToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (setup.SelectedPacks != null && setup.SelectedPacks.Count > 0 && setup.Bundles?.Packs != null && setup.Bundles?.Items != null)
        {
            foreach (var packName in setup.SelectedPacks)
            {
                var pack = setup.Bundles.Packs.FirstOrDefault(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));
                if (pack != null)
                {
                    foreach (var itemName in pack.ItemNames)
                    {
                        var item = setup.Bundles.Items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
                        if (item != null && item.IsBig)
                        {
                            var suffix = item.BigSuffix ?? string.Empty;
                            var bigFileName = suffix.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                                ? $"{item.GetFullName()}{suffix}"
                                : $"{item.GetFullName()}{suffix}.big";
                            filesToInclude.Add(bigFileName);
                        }
                    }
                }
            }
        }

        var stagingDir = Path.Combine(buildDir, ".staging_manifest");
        var manifestContentDir = bundlesDir;

        if (filesToInclude.Count > 0)
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

            manifestContentDir = stagingDir;
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
                logger.LogError("Failed to create local content manifest: {Error}", manifestResult.FirstError);
                _lastErrorMessage = $"Failed to create manifest: {manifestResult.FirstError}";
                return false;
            }

            var manifest = manifestResult.Data;
            logger.LogInformation(
                "Successfully created local ContentManifest '{ManifestId}' for project '{ProjectName}' in CAS",
                manifest?.Id,
                projectName);

            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.CreateManifest,
                CurrentStep = $"Created local ContentManifest: {manifest?.Id}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
            });

            return true;
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
                    logger.LogDebug(ex, "Failed to clean up manifest staging directory: {StagingDir}", stagingDir);
                }
            }
        }
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
            BuildIndex.CreateManifest => BundleEventType.OnStartCreateManifest,
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
            BuildIndex.CreateManifest => BundleEventType.OnFinishCreateManifest,
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

        var manifestFiles = CollectBigBundleItemFiles(setup, configuration);
        stageFiles[BuildIndex.CreateManifest] = manifestFiles;

        logger.LogInformation(
            "Stage file summary: RawItems={RawCount}, BigItems={BigCount}, RawPacks={RawPackCount}, ReleasePacks={ReleaseCount}, ManifestFiles={ManifestCount}",
            rawBundleItemFiles.Count, bigBundleItemFiles.Count, rawBundlePackFiles.Count, releaseBundlePackFiles.Count, manifestFiles.Count);

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
}
