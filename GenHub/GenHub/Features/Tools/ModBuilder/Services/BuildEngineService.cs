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
                : BuildOperationResult.CreateFailure("Build failed", _filesProcessed, _filesSkipped, _filesFailed, sw.Elapsed);
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

            var setup = buildStructure.Setup;
            var steps = setup.Step;

            // validate setup
            if (steps == BuildStep.Zero)
            {
                logger.LogWarning("BuildStep is Zero, nothing to do");
                return true;
            }

            // auto-enable dependent steps
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

            var success = true;

            // execute build pipeline stages
            if (success && (steps & BuildStep.PreBuild) != 0)
            {
                success &= await PreBuildAsync(buildStructure, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.Clean) != 0)
            {
                success &= await CleanAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.Build) != 0)
            {
                success &= await BuildAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.PostBuild) != 0)
            {
                success &= await PostBuildAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.Release) != 0)
            {
                success &= await ReleaseAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.Uninstall) != 0)
            {
                success &= await UninstallAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.Install) != 0)
            {
                success &= await InstallAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            if (success && (steps & BuildStep.Run) != 0)
            {
                success &= await RunGameAsync(setup, progress, _abortTokenSource.Token).ConfigureAwait(false);
            }

            logger.LogInformation("Build pipeline completed with success={Success}", success);
            return success;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Build was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build pipeline failed with exception");
            return false;
        }
        finally
        {
            _isRunning = false;
            _abortTokenSource?.Dispose();
            _abortTokenSource = null;
        }
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
        var filesToProcess = GetFilesForStage(stage, setup);

        logger.LogInformation("Processing {Count} files for stage {Stage}", filesToProcess.Count, stage);

        if (stage == BuildIndex.BigBundleItem)
        {
            var rawDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.RawBundleItemsSubdir);
            var bundlesDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.BundlesSubdir);

            if (Directory.Exists(rawDir))
            {
                if (!Directory.Exists(bundlesDir))
                {
                    Directory.CreateDirectory(bundlesDir);
                }

                if (setup.Bundles?.Items != null)
                {
                    foreach (var item in setup.Bundles.Items.Where(i => i.IsBig))
                    {
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

                        foreach (var file in item.Files)
                        {
                            var targetRel = !string.IsNullOrEmpty(file.RelTargetFile)
                                ? file.RelTargetFile
                                : (!string.IsNullOrEmpty(file.GetRelSourceFile()) ? file.GetRelSourceFile() : Path.GetFileName(file.AbsSourceFile));
                            if (!string.IsNullOrEmpty(targetRel))
                            {
                                var srcInRaw = Path.Combine(rawDir, targetRel.TrimStart('/', '\\'));
                                if (File.Exists(srcInRaw))
                                {
                                    var destInStaging = Path.Combine(itemStagingDir, targetRel.TrimStart('/', '\\'));
                                    var destDir = Path.GetDirectoryName(destInStaging);
                                    if (!string.IsNullOrEmpty(destDir))
                                    {
                                        Directory.CreateDirectory(destDir);
                                    }

                                    File.Copy(srcInRaw, destInStaging, overwrite: true);
                                }
                            }
                        }

                        var sourceToPack = Directory.Exists(itemStagingDir) && Directory.EnumerateFileSystemEntries(itemStagingDir).Any()
                            ? itemStagingDir
                            : rawDir;

                        var archiveResult = await archiveService.CreateBigArchiveAsync(sourceToPack, bigFilePath, null, cancellationToken).ConfigureAwait(false);
                        if (!archiveResult.Success)
                        {
                            logger.LogError("Failed to create BIG archive {Archive}: {Error}", bigFilePath, archiveResult.FirstError);
                            Interlocked.Increment(ref _filesFailed);
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
            }
        }
        else if (stage == BuildIndex.ReleaseBundlePack)
        {
            var bundlesDir = Path.Combine(setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.BundlesSubdir);
            var releaseDir = setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir;

            if (Directory.Exists(bundlesDir) && !string.IsNullOrEmpty(releaseDir))
            {
                if (!Directory.Exists(releaseDir))
                {
                    Directory.CreateDirectory(releaseDir);
                }

                if (setup.Bundles?.Packs != null)
                {
                    var packsToRelease = setup.SelectedPacks != null && setup.SelectedPacks.Count > 0
                        ? setup.Bundles.Packs.Where(p => p.AllowBuild && setup.SelectedPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                        : setup.Bundles.Packs.Where(p => p.AllowBuild);

                    foreach (var pack in packsToRelease)
                    {
                        var zipFileName = $"{pack.GetFullName()}.zip";
                        var zipFilePath = Path.Combine(releaseDir, zipFileName);
                        var archiveResult = await archiveService.CreateZipArchiveAsync(bundlesDir, zipFilePath, System.IO.Compression.CompressionLevel.Optimal, null, cancellationToken).ConfigureAwait(false);
                        if (archiveResult.Success)
                        {
                            try
                            {
                                if (File.Exists(zipFilePath))
                                {
                                    using var fileStream = File.OpenRead(zipFilePath);
                                    var md5Hash = await System.Security.Cryptography.MD5.HashDataAsync(fileStream, cancellationToken).ConfigureAwait(false);
                                    fileStream.Position = 0;
                                    var sha256Hash = await System.Security.Cryptography.SHA256.HashDataAsync(fileStream, cancellationToken).ConfigureAwait(false);
                                    var md5Hex = Convert.ToHexString(md5Hash).ToLowerInvariant();
                                    var sha256Hex = Convert.ToHexString(sha256Hash).ToLowerInvariant();
                                    var sizeBytes = fileStream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);

                                    await File.WriteAllTextAsync($"{zipFilePath}.md5", md5Hex, cancellationToken).ConfigureAwait(false);
                                    await File.WriteAllTextAsync($"{zipFilePath}.sha256", sha256Hex, cancellationToken).ConfigureAwait(false);
                                    await File.WriteAllTextAsync($"{zipFilePath}.size", sizeBytes, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to generate release checksum files for {ZipFile}", zipFilePath);
                            }
                        }
                        else
                        {
                            logger.LogError("Failed to create release ZIP archive {Archive}: {Error}", zipFilePath, archiveResult.FirstError);
                            Interlocked.Increment(ref _filesFailed);
                        }
                    }
                }
            }
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
                async (file, ct) =>
                {
                    await ProcessFileAsync(file, stage, setup, ct).ConfigureAwait(false);
                })
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

    /// <summary>
    /// Process a single file for the given build stage.
    /// </summary>
    private async Task ProcessFileAsync(
        string filePath,
        BuildIndex stage,
        BuildSetup setup,
        CancellationToken cancellationToken)
    {
        try
        {
            // check if file exists
            if (!File.Exists(filePath))
            {
                logger.LogWarning("Source file not found: {FilePath}", filePath);
                return;
            }

            // compute md5 hash with optimization
            var currentMd5 = await cacheService.ComputeOrReuseMd5Async(filePath, cancellationToken)
                .ConfigureAwait(false);

            // determine file status using cache
            var fileStatus = cacheService.DetermineFileStatus(filePath, currentMd5, null);

            // skip unchanged files for performance
            if (fileStatus == BuildFileStatus.Unchanged || fileStatus == BuildFileStatus.Irrelevant)
            {
                logger.LogDebug("Skipping unchanged file: {FilePath}", filePath);

                // still add to new cache
                var fileInfo = new FileInfo(filePath);
                var unixTime = fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
                cacheService.AddFile(filePath, unixTime, currentMd5, null);

                Interlocked.Increment(ref _filesSkipped);
                return;
            }

            // determine target path based on stage
            var targetPath = GetTargetPathForFile(filePath, stage, setup);
            if (string.IsNullOrEmpty(targetPath))
            {
                logger.LogWarning("Could not determine target path for: {FilePath}", filePath);
                return;
            }

            // ensure target directory exists
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
                cancellationToken)
                .ConfigureAwait(false);

            if (!conversionResult.Success)
            {
                logger.LogError("File conversion failed: {Error}", conversionResult.FirstError);
                Interlocked.Increment(ref _filesFailed);
                return;
            }

            // update cache entry
            var fileInfoFinal = new FileInfo(filePath);
            var unixTimeFinal = fileInfoFinal.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
            cacheService.AddFile(filePath, unixTimeFinal, currentMd5, null);

            Interlocked.Increment(ref _filesProcessed);

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
    private List<string> GetFilesForStage(BuildIndex stage, BuildSetup setup)
    {
        var files = new List<string>();

        // get files from cached build structure
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
            if (setup.Bundles?.Items != null)
            {
                foreach (var item in setup.Bundles.Items)
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

        return stage switch
        {
            BuildIndex.BigBundleItem => Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir, fileName),
            BuildIndex.RawBundlePack => Path.Combine(buildDir, ModBuilderConstants.BundlePacksSubdir, fileName),
            BuildIndex.ReleaseBundlePack => Path.Combine(setup.Folders?.AbsReleaseDir ?? ModBuilderConstants.DefaultReleaseDir, fileName),
            BuildIndex.InstallBundlePack => Path.Combine(setup.Folders?.AbsGameDir ?? string.Empty, fileName),
            _ => string.Empty,
        };
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
        logger.LogInformation("PostBuild stage started");
        progress?.Report(new BuildProgress { CurrentStep = "PostBuild: Finalizing" });

        // fire OnPostBuild events
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

        await BuildStageAsync(BuildIndex.ReleaseBundlePack, setup, progress, cancellationToken).ConfigureAwait(false);

        return true;
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
        logger.LogInformation("Install stage started");
        progress?.Report(new BuildProgress
        {
            CurrentIndex = BuildIndex.InstallBundlePack,
            CurrentStep = "Installing to game directory",
        });

        // fire OnInstall event
        FireBundleEvent(BundleEventType.OnInstall, null);

        var installFiles = GetFilesForStage(BuildIndex.InstallBundlePack, setup);

        if (installFiles.Count == 0)
        {
            logger.LogInformation("No files to install");
            return true;
        }

        var gameDir = setup.Folders?.AbsGameDir;
        if (string.IsNullOrEmpty(gameDir))
        {
            logger.LogError("Game directory not configured");
            return false;
        }

        _installedFiles.Clear();

        foreach (var sourcePath in installFiles)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    logger.LogWarning("Source file not found: {File}", sourcePath);
                    continue;
                }

                var fileName = Path.GetFileName(sourcePath);
                var targetPath = Path.Combine(gameDir, fileName);

                // backup existing file if it exists and hasn't already been backed up
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
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to install file: {File}", sourcePath);
                return false;
            }
        }

        await SaveInstallManifestAsync(gameDir, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Installed {Count} files", _installedFiles.Count);
        return true;
    }

    /// <summary>
    /// Executes the Run stage.
    /// </summary>
    /// <param name="setup">The build setup.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    private async Task<bool> RunGameAsync(BuildSetup setup, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
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

        var gameExePath = runnerConfig.AbsExe;
        if (string.IsNullOrEmpty(gameExePath))
        {
            logger.LogWarning("Game executable not configured, skipping run");
            return true;
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

        if (!File.Exists(gameExePath))
        {
            logger.LogError("Game executable not found: {Path}", gameExePath);
            throw new FileNotFoundException($"Game executable not found: {gameExePath}");
        }

        logger.LogInformation("Launching game: {Path}", gameExePath);

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
            CreateNoWindow = false
        };

        var args = runnerConfig.Args ?? string.Empty;

        // Native game mod folder support (-mod <FolderPath>)
        if (!args.Contains("-mod", StringComparison.OrdinalIgnoreCase))
        {
            var modFolder = !string.IsNullOrEmpty(runnerConfig.ModFolder)
                ? runnerConfig.ModFolder
                : setup.Folders?.AbsReleaseDir;

            if (!string.IsNullOrEmpty(modFolder) && Directory.Exists(modFolder))
            {
                args = string.IsNullOrEmpty(args)
                    ? $"-mod \"{modFolder}\""
                    : $"{args} -mod \"{modFolder}\"";
                logger.LogInformation("Using native game mod folder argument: -mod {ModFolder}", modFolder);
            }
        }

        if (!string.IsNullOrEmpty(args))
        {
            startInfo.Arguments = args;
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
            logger.LogInformation("Game launched successfully (PID: {ProcessId})", process.Id);
            progress?.Report(new BuildProgress { CurrentStep = "Game launched successfully" });

            return await Task.FromResult(true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch game: {Message}", ex.Message);
            throw;
        }
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
        progress?.Report(new BuildProgress { CurrentStep = "Uninstalling from game directory" });

        // fire OnUninstall event
        FireBundleEvent(BundleEventType.OnUninstall, null);

        var gameDir = setup.Folders?.AbsGameDir;
        if (string.IsNullOrEmpty(gameDir))
        {
            logger.LogError("Game directory not configured");
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

        foreach (var configFile in configuration.LoadedConfigFiles)
        {
            if (File.Exists(configFile))
            {
                var fileInfo = new FileInfo(configFile);
                hashParts.Add($"{configFile}:{fileInfo.LastWriteTimeUtc.Ticks}");
            }
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
        var tempFile = Path.GetTempFileName();
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

        var setup = new BuildSetup
        {
            Step = buildSteps,
            Folders = new Folders
            {
                AbsBuildDir = configuration.Folders.AbsBuildDir,
                AbsReleaseDir = configuration.Folders.AbsReleaseDir,
                AbsGameDir = configuration.Folders.AbsGameDir,
            },
            Bundles = new Bundles
            {
                Items = configuration.Items,
                Packs = configuration.Packs,
            },
            Runner = new Runner(),
            RunnerConfig = configuration.Runner,
        };

        var stageFiles = new Dictionary<BuildIndex, List<string>>();

        var rawBundleItemFiles = new List<string>();
        foreach (var item in configuration.Items)
        {
            foreach (var file in item.Files)
            {
                if (!string.IsNullOrEmpty(file.AbsSourceFile) && File.Exists(file.AbsSourceFile))
                {
                    rawBundleItemFiles.Add(file.AbsSourceFile);
                }
                else
                {
                    logger.LogWarning("Source file not found: {FilePath}", file.AbsSourceFile);
                }
            }
        }

        stageFiles[BuildIndex.RawBundleItem] = rawBundleItemFiles;
        logger.LogInformation("Stage RawBundleItem: {Count} files", rawBundleItemFiles.Count);

        var bigBundleItemFiles = new List<string>();
        foreach (var item in configuration.Items)
        {
            if (item.IsBig)
            {
                var bigFileName = $"{item.GetFullName()}{item.BigSuffix}.big";
                var bigFilePath = Path.Combine(setup.Folders.AbsBuildDir, ModBuilderConstants.BundlesSubdir, bigFileName);
                bigBundleItemFiles.Add(bigFilePath);
            }
        }

        stageFiles[BuildIndex.BigBundleItem] = bigBundleItemFiles;
        logger.LogInformation("Stage BigBundleItem: {Count} archives", bigBundleItemFiles.Count);

        var rawBundlePackFiles = new List<string>();
        foreach (var pack in configuration.Packs)
        {
            if (pack.AllowBuild)
            {
                foreach (var itemName in pack.ItemNames)
                {
                    var item = configuration.Items.FirstOrDefault(i => i.Name == itemName);
                    if (item != null && item.IsBig)
                    {
                        var bigFileName = $"{item.GetFullName()}{item.BigSuffix}.big";
                        var bigFilePath = Path.Combine(setup.Folders.AbsBuildDir, ModBuilderConstants.BundlesSubdir, bigFileName);
                        rawBundlePackFiles.Add(bigFilePath);
                    }
                }
            }
        }

        stageFiles[BuildIndex.RawBundlePack] = rawBundlePackFiles;
        logger.LogInformation("Stage RawBundlePack: {Count} files", rawBundlePackFiles.Count);

        var releaseBundlePackFiles = new List<string>();
        foreach (var pack in configuration.Packs)
        {
            if (pack.AllowBuild)
            {
                var zipFileName = $"{pack.GetFullName()}.zip";
                var zipFilePath = Path.Combine(setup.Folders.AbsReleaseDir, zipFileName);
                releaseBundlePackFiles.Add(zipFilePath);
            }
        }

        stageFiles[BuildIndex.ReleaseBundlePack] = releaseBundlePackFiles;
        logger.LogInformation("Stage ReleaseBundlePack: {Count} archives", releaseBundlePackFiles.Count);

        var installBundlePackFiles = new List<string>();
        foreach (var pack in configuration.Packs)
        {
            if (pack.AllowInstall)
            {
                foreach (var itemName in pack.ItemNames)
                {
                    var item = configuration.Items.FirstOrDefault(i => i.Name == itemName);
                    if (item != null && item.IsBig)
                    {
                        var bigFileName = $"{item.GetFullName()}{item.BigSuffix}.big";
                        var bigFilePath = Path.Combine(setup.Folders.AbsBuildDir, ModBuilderConstants.BundlesSubdir, bigFileName);
                        installBundlePackFiles.Add(bigFilePath);
                    }
                }
            }
        }

        stageFiles[BuildIndex.InstallBundlePack] = installBundlePackFiles;
        logger.LogInformation("Stage InstallBundlePack: {Count} files", installBundlePackFiles.Count);

        var bundleItems = configuration.Items.ToDictionary(
            item => item.Name,
            item => item);

        var bundlePacks = configuration.Packs.ToDictionary(
            pack => pack.Name,
            pack => pack);

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
