using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ContentManifest = GenHub.Core.Models.Manifest.ContentManifest;

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
    private readonly IServiceScopeFactory _serviceScopeFactory;
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
    /// Initializes a new instance of the <see cref="BuildEngineService"/> class.
    /// </summary>
    /// <param name="cacheService">The build cache service.</param>
    /// <param name="fileConversionService">The file conversion service.</param>
    /// <param name="hashProvider">The MD5 hash provider.</param>
    /// <param name="configurationLoaderService">The configuration loader service.</param>
    /// <param name="archiveService">The archive service.</param>
    /// <param name="serviceScopeFactory">The service scope factory for resolving scoped dependencies.</param>
    /// <param name="logger">The logger instance.</param>
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

        if (IsPathInsideAppDirectory(project.ProjectDir))
        {
            var msg = $"Cannot execute build within the application installation directory: '{project.ProjectDir}'. The project must be located in a user directory.";
            _logger.LogError(msg);
            return BuildOperationResult.CreateFailure(msg);
        }

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
        catch (OperationCanceledException)
        {
            _lastErrorMessage = "Build was cancelled by user";
            throw;
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
        _logger.LogInformation("PreBuild stage started (using cached build structure)");
        progress?.Report(new BuildProgress { CurrentStep = "PreBuild: Initializing build structure" });

        FireBundleEvent(BundleEventType.OnPreBuild, null);

        if (buildStructure.Configuration == null)
        {
            _logger.LogError("Project configuration is null");
            _lastErrorMessage = "Configuration is null";
            return false;
        }

        _logger.LogDebug(
            "Build structure contains {ItemCount} items and {PackCount} packs",
            buildStructure.BundleItems.Count,
            buildStructure.BundlePacks.Count);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
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
                _logger.LogWarning(ex, "Could not create cache directory {CacheDir}, build caching disabled for this stage", cacheDir);
            }
        }

        if (cachingEnabled)
        {
            await _cacheService.LoadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        }

        var initialFailed = Volatile.Read(ref _filesFailed);
        var filesToProcess = GetFilesForStage(stage);

        _logger.LogInformation("Processing {Count} files for stage {Stage}", filesToProcess.Count, stage);

        await ExecuteStageFilesAsync(stage, setup, progress, filesToProcess, cancellationToken).ConfigureAwait(false);

        if (cachingEnabled)
        {
            await _cacheService.SaveCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
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
            _logger.LogInformation("No bundle packs explicitly defined; created default release pack '{PackName}' for {ItemCount} items", defaultPackName, setup.Bundles.Items.Count);
        }

        if (candidatePacks.Count == 0)
        {
            _logger.LogWarning("No bundle packs or items found to release");
            Interlocked.Increment(ref _filesFailed);
            _lastErrorMessage = "No bundle packs or items found to release. Please configure bundle items or packs in ModBuilder.";
            return;
        }

        if (!Directory.Exists(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
        }

        var packs = candidatePacks.Where(p => p.AllowBuild).ToList();
        if (setup.SelectedPacks != null && setup.SelectedPacks.Count > 0)
        {
            var selectedFiltered = packs.Where(p =>
                setup.SelectedPacks.Contains(p.Name, StringComparer.OrdinalIgnoreCase) ||
                p.ItemNames.Any(item => setup.SelectedPacks.Contains(item, StringComparer.OrdinalIgnoreCase))).ToList();
            packs = selectedFiltered;
        }

        if (packs.Count == 0)
        {
            _logger.LogWarning("No bundle packs enabled or selected for release");
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
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        var bundlesDir = Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir);
        var items = setup.Bundles?.Items;
        var compressionLevel = System.IO.Compression.CompressionLevel.Optimal;

        var packFileName = GetPackFileName(pack);
        var packFilePath = Path.Combine(releaseDir, packFileName);

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
                await StagePackFilesAsync(pack, items, bundlesDir, packStagingDir, buildDir, cancellationToken).ConfigureAwait(false);
            }

            var stagedFiles = Directory.GetFiles(packStagingDir, "*", SearchOption.AllDirectories);
            if (stagedFiles.Length == 0)
            {
                Interlocked.Increment(ref _filesFailed);
                _logger.LogError("No files were staged for pack {PackName}; release archive cannot be created.", pack.Name);
                _lastErrorMessage = $"No files were staged for pack '{pack.Name}'. Check that bundle items exist and contain files.";
                return;
            }

            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.ReleaseBundlePack,
                CurrentStep = $"Packaging release {pack.Name}",
                ProcessedFiles = Volatile.Read(ref _filesProcessed),
            });

            var archiveResult = pack.IsBigPack
                ? await _archiveService.CreateBigArchiveAsync(packStagingDir, packFilePath, new Progress<double>(p =>
                {
                    progress?.Report(new BuildProgress
                    {
                        CurrentIndex = BuildIndex.ReleaseBundlePack,
                        CurrentStep = $"Packing {packFileName} ({p:P0})",
                        ProcessedFiles = Volatile.Read(ref _filesProcessed),
                    });
                }), cancellationToken).ConfigureAwait(false)
                : await _archiveService.CreateZipArchiveAsync(packStagingDir, packFilePath, compressionLevel, null, cancellationToken).ConfigureAwait(false);

            if (!archiveResult.Success)
            {
                Interlocked.Increment(ref _filesFailed);
                _logger.LogError("Failed to create archive for pack {PackName}: {Error}", pack.Name, archiveResult.FirstError);
                _lastErrorMessage = $"Failed to create archive for pack {pack.Name}: {archiveResult.FirstError}";
            }
            else
            {
                Interlocked.Increment(ref _filesProcessed);
                _logger.LogInformation("Successfully created archive: {Path}", packFilePath);
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

    private async Task StagePackFilesAsync(BundlePack pack, IReadOnlyList<BundleItem> items, string bundlesDir, string packStagingDir, string buildDir, CancellationToken cancellationToken)
    {
        if (pack.IsBigPack)
        {
            StageBigPackFiles(pack, items, packStagingDir, buildDir);
        }
        else
        {
            await StageStandardPackFilesAsync(pack, items, bundlesDir, packStagingDir, buildDir, cancellationToken).ConfigureAwait(false);
        }
    }

    private void StageBigPackFiles(BundlePack pack, IReadOnlyList<BundleItem> items, string packStagingDir, string buildDir)
    {
        foreach (var itemName in pack.ItemNames)
        {
            var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                continue;
            }

            foreach (var file in item.Files)
            {
                StageBigPackFile(file, packStagingDir, pack.Name, item.Name, buildDir);
            }
        }
    }

    private (string Path, string TargetRelPath)? ProbeConvertedOutput(
        string rawDir,
        string targetRelPath,
        string sourcePath,
        string targetExtension)
    {
        var convertedRel = Path.ChangeExtension(targetRelPath, targetExtension);
        var subPath = Path.Combine(rawDir, convertedRel);
        if (IsSubpathOf(rawDir, subPath) && File.Exists(subPath))
        {
            return (subPath, convertedRel);
        }

        var flatSource = Path.Combine(rawDir, Path.ChangeExtension(Path.GetFileName(sourcePath), targetExtension));
        if (File.Exists(flatSource))
        {
            return (flatSource, convertedRel);
        }

        var flatTarget = Path.Combine(rawDir, Path.GetFileName(convertedRel));
        if (File.Exists(flatTarget))
        {
            return (flatTarget, convertedRel);
        }

        _logger.LogWarning("Expected converted {Extension} output not found for {SourcePath}; falling back to raw source", targetExtension.ToUpperInvariant(), sourcePath);
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

        var rawDir = Path.Combine(buildDir, ModBuilderConstants.RawBundleItemsSubdir);
        if (!Directory.Exists(rawDir))
        {
            return (sourcePath, targetRelPath);
        }

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Check if converted output exists (DDS for image, CSF for string table)
        if (ext is ".tga" or ".png")
        {
            var probed = ProbeConvertedOutput(rawDir, targetRelPath, sourcePath, ".dds");
            if (probed != null)
            {
                return probed.Value;
            }
        }
        else if (ext == ".str")
        {
            var probed = ProbeConvertedOutput(rawDir, targetRelPath, sourcePath, ".csf");
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
            _logger.LogWarning("Source file {SourceFile} not found for bundle item {ItemName}", sourcePath, itemName);
            return;
        }

        var targetRelPath = GetTargetRelativePath(file);
        var (actualSource, finalTargetRelPath) = ResolveStagedSource(file, targetRelPath, buildDir);

        var destPath = Path.Combine(packStagingDir, finalTargetRelPath);
        EnsureDestinationDirectory(destPath);
        File.Copy(actualSource, destPath, true);
        _logger.LogDebug("Staged file {RelPath} for BIG pack {PackName}", finalTargetRelPath, packName);
    }

    private async Task StageStandardPackFilesAsync(BundlePack pack, IReadOnlyList<BundleItem> items, string bundlesDir, string packStagingDir, string buildDir, CancellationToken cancellationToken)
    {
        foreach (var itemName in pack.ItemNames)
        {
            var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                continue;
            }

            if (item.IsBig)
            {
                await StageBigBundleArchiveAsync(item, bundlesDir, packStagingDir, pack.Name, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                StageRawBundleFiles(item, packStagingDir, pack.Name, buildDir);
            }
        }
    }

    private async Task StageBigBundleArchiveAsync(BundleItem item, string bundlesDir, string packStagingDir, string packName, CancellationToken cancellationToken)
    {
        var bigFileName = GetBigFileName(item);
        var srcBig = Path.Combine(bundlesDir, bigFileName);
        if (!File.Exists(srcBig))
        {
            _logger.LogInformation("BIG bundle {BigFileName} missing in bundles directory; building it on demand...", bigFileName);
            if (item.Files.Count > 0)
            {
                if (!Directory.Exists(bundlesDir))
                {
                    Directory.CreateDirectory(bundlesDir);
                }

                await BuildSingleBigBundleItemAsync(item, bundlesDir, null, cancellationToken, 1, 1).ConfigureAwait(false);
            }
        }

        if (File.Exists(srcBig))
        {
            var destBig = Path.Combine(packStagingDir, bigFileName);
            File.Copy(srcBig, destBig, true);
            _logger.LogDebug("Staged .BIG archive {BigFileName} for pack {PackName}", bigFileName, packName);
        }
        else
        {
            _logger.LogError("BIG bundle {BigFileName} missing for pack {PackName} and could not be built.", bigFileName, packName);
            Interlocked.Increment(ref _filesFailed);
            _lastErrorMessage = $"BIG bundle '{bigFileName}' missing for pack '{packName}'.";
        }
    }

    private void StageRawBundleFiles(BundleItem item, string packStagingDir, string packName, string? buildDir = null)
    {
        foreach (var file in item.Files)
        {
            var sourcePath = file.AbsSourceFile;
            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning("Source file {SourceFile} not found for raw bundle item {ItemName}", sourcePath, item.Name);
                continue;
            }

            var relPath = !string.IsNullOrEmpty(file.RelTargetFile) ? file.RelTargetFile : file.GetRelSourceFile();
            if (string.IsNullOrEmpty(relPath))
            {
                relPath = Path.GetFileName(sourcePath);
            }

            var (actualSource, finalRelPath) = ResolveStagedSource(file, relPath, buildDir);

            var destPath = Path.Combine(packStagingDir, finalRelPath);
            EnsureDestinationDirectory(destPath);
            File.Copy(actualSource, destPath, true);
            _logger.LogDebug("Staged loose file {RelPath} for pack {PackName}", finalRelPath, packName);
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
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found for stage {Stage}: {FilePath}", stage, filePath);
            Interlocked.Increment(ref _filesFailed);
            _lastErrorMessage = $"File not found: {filePath}";
            return;
        }

        var currentMd5 = await _cacheService.ComputeOrReuseMd5Async(filePath, cancellationToken).ConfigureAwait(false);
        var status = _cacheService.DetermineFileStatus(filePath, currentMd5, null);

        if (status is BuildFileStatus.Unchanged or BuildFileStatus.Irrelevant)
        {
            _logger.LogDebug("Skipping unchanged/irrelevant file: {FilePath}", filePath);
            var fileInfo = new FileInfo(filePath);
            var mtime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
            _cacheService.AddFile(filePath, mtime, currentMd5);
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
            _cacheService.AddFile(filePath, mtime, currentMd5);

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

    private string GetTargetPathForFile(string sourcePath, BuildIndex stage, BuildSetup setup)
    {
        var buildDir = setup.Folders?.AbsBuildDir ?? ModBuilderConstants.DefaultBuildDir;
        string? relPath = null;

        if (_cachedBuildStructure?.BundleItems != null)
        {
            var bundleFile = _cachedBuildStructure.BundleItems.Values
                .SelectMany(i => i.Files)
                .FirstOrDefault(f => string.Equals(f.AbsSourceFile, sourcePath, StringComparison.OrdinalIgnoreCase));
            if (bundleFile != null)
            {
                relPath = GetTargetRelativePath(bundleFile);
            }
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

        Directory.CreateDirectory(bundlesDir);

        if (!await EnsureBundlesPreparedAsync(setup, bundlesDir, progress, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogError("No bundle files found in {BundlesDir} to create manifest.", bundlesDir);
            _lastErrorMessage = $"No bundle files found in {bundlesDir}. Ensure bundle items have source files.";
            return false;
        }

        var stagingDir = Path.Combine(buildDir, ".staging_manifest");
        var manifestContentDir = PrepareManifestContentDirectory(setup, bundlesDir, stagingDir);

        try
        {
            return await ExecuteCreateLocalManifestAsync(
                buildStructure,
                manifestContentDir,
                bundlesDir,
                buildDir,
                setup.Folders?.AbsReleaseDir,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupStagingDirectory(stagingDir);
        }
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

        _logger.LogInformation("No files found in bundles directory; preparing bundle files before creating manifest...");
        foreach (var item in setup.Bundles.Items)
        {
            if (item.Files.Count == 0)
            {
                continue;
            }

            if (item.IsBig)
            {
                await BuildSingleBigBundleItemAsync(item, bundlesDir, progress, cancellationToken, 1, 1).ConfigureAwait(false);
            }
            else
            {
                StageRawBundleFiles(item, bundlesDir, item.Name, setup.Folders?.AbsBuildDir);
            }
        }

        return Directory.GetFiles(bundlesDir, "*", SearchOption.AllDirectories).Length > 0;
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

    private async Task<bool> ExecuteCreateLocalManifestAsync(
        BuildStructure buildStructure,
        string manifestContentDir,
        string bundlesDir,
        string buildDir,
        string? releaseDir,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var localContentService = scope.ServiceProvider.GetRequiredService<ILocalContentService>();

        try
        {
            var projectName = buildStructure.Project.Name;
            var targetGame = buildStructure.Project.TargetGame;
            var contentType = ResolveProjectContentType(buildStructure.Project.ContentType);

            var manifestResult = await localContentService.CreateLocalContentManifestAsync(
                manifestContentDir,
                projectName,
                contentType,
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

            if (manifest != null)
            {
                await PersistManifestOutputsAsync(manifest, buildDir, releaseDir, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new BuildProgress
            {
                CurrentIndex = BuildIndex.CreateManifest,
                CurrentStep = $"Local manifest {manifest?.Id} created and saved to manifest.json",
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
    }

    private static ContentType ResolveProjectContentType(ContentType contentType) =>
        contentType != ContentType.UnknownContentType ? contentType : ContentType.Mod;

    private async Task PersistManifestOutputsAsync(
        ContentManifest manifest,
        string buildDir,
        string? releaseDir,
        CancellationToken cancellationToken)
    {
        PublishContentAcquiredSafely(manifest);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var manifestJson = JsonSerializer.Serialize(manifest, options);
        var buildManifestPath = Path.Combine(buildDir, "manifest.json");
        await File.WriteAllTextAsync(buildManifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Saved manifest file to {Path}", buildManifestPath);

        if (!string.IsNullOrEmpty(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
            var releaseManifestPath = Path.Combine(releaseDir, "manifest.json");
            await File.WriteAllTextAsync(releaseManifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Saved manifest file to {Path}", releaseManifestPath);
        }
    }

    private void PublishContentAcquiredSafely(ContentManifest manifest)
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new ContentAcquiredMessage(manifest));
            _logger.LogInformation("Published ContentAcquiredMessage for manifest {ManifestId}", manifest.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish ContentAcquiredMessage for manifest {ManifestId}", manifest.Id);
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
            _logger.LogDebug(ex, "Failed to clean up manifest staging directory: {StagingDir}", stagingDir);
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
                : Path.Combine(Path.GetTempPath(), "GenHub_ModBuilder", ModBuilderConstants.DefaultBuildDir);
        }

        return Path.Combine(buildDir, $"{stage}.json");
    }

    private static bool IsPathInsideAppDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fullPath, baseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        {
            return true;
        }
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

        AddProjectAndConfigFileHashParts(project, configuration, hashParts);
        AddBundleAndPackHashParts(project, configuration, hashParts);
        AddSourceFileHashParts(project, hashParts);

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
                project.Directories?.Configs ?? "config",
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        _logger.LogDebug("Resolving wildcards in configuration");
        configuration = await _configurationLoaderService.ResolveWildcardsAsync(configuration, cancellationToken)
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
        return configuration.Items
            .SelectMany(item => item.Files)
            .Select(file => file.AbsSourceFile)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            if (pack.IsBigPack && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(fileName, ".big");
            }

            if (!pack.IsBigPack && fileName.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(fileName, ".zip");
            }

            return fileName;
        }

        var extension = pack.IsBigPack ? ".big" : ".zip";
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
