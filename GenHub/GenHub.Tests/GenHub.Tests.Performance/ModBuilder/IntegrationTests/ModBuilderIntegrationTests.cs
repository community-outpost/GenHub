// <copyright file="ModBuilderIntegrationTests.cs" company="enowX Labs">
// Copyright (c) enowX Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Performance.ModBuilder.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// XUnit logger provider for capturing logs in test output.
/// </summary>
internal sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XunitLogger(output, categoryName);
    public void Dispose() { }
}

/// <summary>
/// XUnit logger for capturing logs in test output.
/// </summary>
internal sealed class XunitLogger(ITestOutputHelper output, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            output.WriteLine($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
            if (exception != null)
            {
                output.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // Ignore errors writing to test output
        }
    }
}

/// <summary>
/// End-to-end integration tests for ModBuilder build pipeline.
/// Tests the complete workflow from configuration loading to build execution.
/// </summary>
public sealed class ModBuilderIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _testProjectRoot;
    private readonly string _smallProjectPath;
    private readonly string _mediumProjectPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly IBuildEngineService _buildEngine;
    private readonly IConfigurationLoaderService _configLoader;
    private readonly IBuildCacheService _cacheService;

    public ModBuilderIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _testProjectRoot = Path.Combine(Path.GetTempPath(), "ModBuilderIntegrationTests", Guid.NewGuid().ToString());
        _smallProjectPath = Path.Combine(_testProjectRoot, "SmallProject");
        _mediumProjectPath = Path.Combine(_testProjectRoot, "MediumProject");

        // Setup DI container
        var services = new ServiceCollection();

        // Add xUnit logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register ModBuilder services (match ModBuilderModule.cs)
        services.AddSingleton<IBuildEngineService, GenHub.Features.Tools.ModBuilder.Services.BuildEngineService>();
        services.AddSingleton<IProjectConfigService, GenHub.Features.Tools.ModBuilder.Services.ProjectConfigService>();
        services.AddSingleton<IConfigurationLoaderService, GenHub.Features.Tools.ModBuilder.Services.ConfigurationLoaderService>();
        services.AddSingleton<IFileConversionService, GenHub.Features.Tools.ModBuilder.Services.FileConversionService>();
        services.AddSingleton<IImageConversionService, GenHub.Features.Tools.ModBuilder.Services.ImageConversionService>();
        services.AddSingleton<IStringTableConversionService, GenHub.Features.Tools.ModBuilder.Services.StringTableConversionService>();
        services.AddSingleton<ITextProcessingService, GenHub.Features.Tools.ModBuilder.Services.TextProcessingService>();
        services.AddSingleton<IArchiveService, GenHub.Features.Tools.ModBuilder.Services.ArchiveService>();
        services.AddSingleton<IBuildCacheService, GenHub.Features.Tools.ModBuilder.Services.BuildCacheService>();
        services.AddSingleton<IExternalToolService, GenHub.Features.Tools.ModBuilder.Services.ExternalToolService>();
        services.AddSingleton<IFileHashRegistryService, GenHub.Features.Tools.ModBuilder.Services.FileHashRegistryService>();
        services.AddSingleton<IMd5HashProvider, GenHub.Features.Tools.ModBuilder.Services.Md5HashProvider>();

        _serviceProvider = services.BuildServiceProvider();
        _buildEngine = _serviceProvider.GetRequiredService<IBuildEngineService>();
        _configLoader = _serviceProvider.GetRequiredService<IConfigurationLoaderService>();
        _cacheService = _serviceProvider.GetRequiredService<IBuildCacheService>();
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_testProjectRoot);
        await CreateSmallTestProjectAsync();
        await CreateMediumTestProjectAsync();
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();

        if (Directory.Exists(_testProjectRoot))
        {
            await Task.Run(() =>
            {
                try
                {
                    Directory.Delete(_testProjectRoot, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Failed to cleanup test directory: {ex.Message}");
                }
            });
        }
    }

    [Fact]
    public async Task FullBuildPipeline_WithSmallProject_Succeeds()
    {
        // Arrange
        var projectPath = _smallProjectPath;
        var configPath = Path.Combine(projectPath, "ModBundles.json");
        var buildOutputPath = Path.Combine(projectPath, "build");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var project = new ModBuilderProject
        {
            Name = "SmallTest",
            ProjectDir = projectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue($"Build should succeed. Errors: {string.Join(", ", result.Errors)}");
        result.Errors.Should().BeEmpty();

        // Verify build artifacts exist
        Directory.Exists(buildOutputPath).Should().BeTrue("Build output directory should exist");

        _output.WriteLine($"Small project build completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Files processed: {result.FilesProcessed}");
        _output.WriteLine($"Files unchanged: {result.FilesSkipped}");
    }

    [Fact]
    public async Task IncrementalBuild_OnlyProcessesChangedFiles()
    {
        // Arrange
        var projectPath = _smallProjectPath;
        var configPath = Path.Combine(projectPath, "ModBundles.json");
        var dataPath = Path.Combine(projectPath, "GameFilesEdited", "Data");

        // Create multiple test files
        for (int i = 0; i < 5; i++)
        {
            var filePath = Path.Combine(dataPath, $"test_{i}.ini");
            await File.WriteAllTextAsync(filePath, $"[TestSection]\nTestKey{i}=TestValue{i}\n");
        }

        var testFilePath = Path.Combine(dataPath, "test_0.ini");

        // Act - First build
        var project = new ModBuilderProject
        {
            Name = "IncrementalTest",
            ProjectDir = projectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var firstBuild = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        _output.WriteLine($"First build: {firstBuild.FilesProcessed} files processed, {firstBuild.FilesSkipped} skipped");

        // Modify one file
        await File.AppendAllTextAsync(testFilePath, "\n; Modified for incremental test\n");

        // Act - Second build (need to invalidate cache)
        _buildEngine.InvalidateBuildStructureCache();
        var secondBuild = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        _output.WriteLine($"Second build: {secondBuild.FilesProcessed} files processed, {secondBuild.FilesSkipped} skipped");

        // Assert
        firstBuild.Success.Should().BeTrue();
        secondBuild.Success.Should().BeTrue();
        firstBuild.FilesProcessed.Should().BeGreaterThan(1, "First build should process multiple files");

        // Second build should process fewer files (only the changed one)
        secondBuild.FilesProcessed.Should().BeLessThan(firstBuild.FilesProcessed,
            "Incremental build should only process changed files");
        secondBuild.FilesProcessed.Should().Be(1, "Only the modified file should be processed");
    }

    [Fact]
    public async Task MD5ChangeDetection_SkipsUnchangedFiles()
    {
        // Arrange
        var projectPath = _smallProjectPath;
        var configPath = Path.Combine(projectPath, "ModBundles.json");

        // Act - First build
        var project = new ModBuilderProject
        {
            Name = "MD5Test",
            ProjectDir = projectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var firstBuild = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        // Act - Second build without changes
        var secondBuild = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        // Assert
        firstBuild.Success.Should().BeTrue();
        secondBuild.Success.Should().BeTrue();

        // All files should be unchanged in second build
        secondBuild.FilesSkipped.Should().Be(firstBuild.FilesProcessed,
            "All files should be unchanged when nothing changed");

        _output.WriteLine($"First build processed: {firstBuild.FilesProcessed} files");
        _output.WriteLine($"Second build unchanged: {secondBuild.FilesSkipped} files");
    }

    [Fact]
    public async Task ConfigurationLoading_LoadsAllBundleComponents()
    {
        // Arrange
        var configPath = Path.Combine(_smallProjectPath, "ModBundles.json");

        // Act
        var config = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None);

        // Assert
        config.Should().NotBeNull();
        config.Packs.Should().NotBeEmpty("Configuration should contain bundle packs");
        config.Items.Should().NotBeEmpty("Configuration should contain bundle items");

        var totalFiles = config.Items
            .SelectMany(i => i.Files)
            .Count();

        totalFiles.Should().BeGreaterThan(0, "Configuration should contain files");

        _output.WriteLine($"Loaded {config.Packs.Count} bundle packs");
        _output.WriteLine($"Total bundle items: {config.Items.Count}");
        _output.WriteLine($"Total files: {totalFiles}");
    }

    [Fact]
    public async Task WildcardResolution_ResolvesAllPatterns()
    {
        // Arrange
        var projectPath = _mediumProjectPath;
        var dataPath = Path.Combine(projectPath, "GameFilesEdited", "Data");

        // Create multiple files matching wildcard patterns (use .ini files instead of .tga to avoid conversion issues)
        Directory.CreateDirectory(dataPath);
        for (int i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, $"test_{i}.ini"),
                $"[TestSection]\nTestKey{i}=TestValue{i}\n");
        }

        // Create config with wildcard
        var config = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "data_files",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = dataPath,
                            AbsSourceFile = Path.Combine(dataPath, "*.ini"),
                            RelTargetFile = "Data/INI",
                        },
                    },
                },
            },
            Packs = new List<BundlePack>
            {
                new()
                {
                    Name = "TestPack",
                    ItemNames = new List<string> { "data_files" },
                    AllowBuild = true,
                },
            },
            Folders = new FolderConfiguration
            {
                AbsBuildDir = Path.Combine(projectPath, "build"),
            },
        };

        var project = new ModBuilderProject
        {
            Name = "WildcardTest",
            ProjectDir = projectPath,
            Configuration = config
        };

        // Act
        var selectedPacks = new List<string> { "TestPack" };
        var result = await _buildEngine.ExecuteBuildAsync(project, config, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue($"Build should succeed. Errors: {string.Join(", ", result.Errors)}");
        result.FilesProcessed.Should().Be(10, "All 10 INI files should be resolved and processed");
        result.FilesFailed.Should().Be(0, "No files should fail");

        _output.WriteLine($"Wildcard resolved {result.FilesProcessed} files");
    }

    [Fact]
    public async Task MultiThreading_ProcessesFilesInParallel()
    {
        // Arrange
        var projectPath = _mediumProjectPath;

        // Create 50 INI files to process (BEFORE loading config)
        var dataPath = Path.Combine(projectPath, "GameFilesEdited", "Data");
        Directory.CreateDirectory(dataPath);

        for (int i = 0; i < 50; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, $"test_{i}.ini"),
                $"[TestSection]\nTestKey{i}=TestValue{i}\n");
        }

        // Now load config (which has wildcard pattern)
        var configPath = Path.Combine(projectPath, "ModBundles.json");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var project = new ModBuilderProject
        {
            Name = "MultiThreadTest",
            ProjectDir = projectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();
        result.FilesProcessed.Should().BeGreaterOrEqualTo(50, "At least 50 INI files should be processed");

        // With parallel processing, should be significantly faster than sequential
        var estimatedSequentialTime = result.FilesProcessed * 50; // Assume 50ms per file
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(estimatedSequentialTime,
            "Parallel processing should be faster than sequential");

        _output.WriteLine($"Processed {result.FilesProcessed} files in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average time per file: {stopwatch.ElapsedMilliseconds / (double)result.FilesProcessed:F2}ms");
    }

    [Fact]
    public async Task BuildCache_PersistsAndLoadsCorrectly()
    {
        // Arrange
        var projectPath = _smallProjectPath;
        var buildDir = Path.Combine(projectPath, "build");
        var configPath = Path.Combine(projectPath, "ModBundles.json");

        // Act - First build creates cache
        var project = new ModBuilderProject
        {
            Name = "CacheTest",
            ProjectDir = projectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var firstBuild = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        // Verify build directory exists
        Directory.Exists(buildDir).Should().BeTrue("Build directory should be created");

        // Verify cache files exist
        var cacheFiles = Directory.Exists(buildDir)
            ? Directory.GetFiles(buildDir, "*.msgpack", SearchOption.AllDirectories)
            : Array.Empty<string>();
        cacheFiles.Should().NotBeEmpty("Cache files should be created");

        _output.WriteLine($"Found {cacheFiles.Length} cache files");
        foreach (var file in cacheFiles)
        {
            _output.WriteLine($"  - {Path.GetFileName(file)}");
        }

        // Act - Load cache (use one of the cache files)
        if (cacheFiles.Length > 0)
        {
            var cacheLoaded = await _cacheService.LoadCacheAsync(cacheFiles[0], CancellationToken.None);

            // Assert
            cacheLoaded.Should().BeTrue("Cache should load successfully");
            _output.WriteLine($"Cache loaded successfully from {Path.GetFileName(cacheFiles[0])}");
        }
    }

    [Fact]
    public async Task PerformanceBenchmark_SmallProject_MeetsTarget()
    {
        // Arrange
        var projectPath = _smallProjectPath;
        var configPath = Path.Combine(projectPath, "ModBundles.json");
        const int targetMs = 2500; // Target: < 2.5s for small project

        // Act
        var stopwatch = Stopwatch.StartNew();
        var project = new ModBuilderProject
        {
            Name = "PerfTest",
            ProjectDir = projectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(targetMs,
            $"Small project build should complete in less than {targetMs}ms");

        _output.WriteLine($"Small project build: {stopwatch.ElapsedMilliseconds}ms (target: <{targetMs}ms)");
        _output.WriteLine($"Performance margin: {targetMs - stopwatch.ElapsedMilliseconds}ms");
    }

    private async Task CreateSmallTestProjectAsync()
    {
        Directory.CreateDirectory(_smallProjectPath);

        // Create directory structure
        var gameFilesPath = Path.Combine(_smallProjectPath, "GameFilesEdited");
        var dataPath = Path.Combine(gameFilesPath, "Data");

        Directory.CreateDirectory(dataPath);

        // Create initial test file
        await File.WriteAllTextAsync(
            Path.Combine(dataPath, "test.ini"),
            "[TestSection]\nTestKey=TestValue\n");

        // Create ModBundles.json with wildcard pattern
        var config = new
        {
            items = new[]
            {
                new
                {
                    name = "test_data",
                    files = new[]
                    {
                        new
                        {
                            absSourceParent = dataPath,  // Changed from gameFilesPath to dataPath
                            absSourceFile = Path.Combine(dataPath, "*.ini"),
                            relTargetFile = "Data/INI",
                        },
                    },
                },
            },
            packs = new[]
            {
                new
                {
                    name = "TestPack",
                    itemNames = new[] { "test_data" },
                    allowBuild = true,
                },
            },
            folders = new
            {
                absBuildDir = Path.Combine(_smallProjectPath, "build"),
            },
        };

        await File.WriteAllTextAsync(
            Path.Combine(_smallProjectPath, "ModBundles.json"),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        // Create .mbproj marker
        await File.WriteAllTextAsync(
            Path.Combine(_smallProjectPath, ".mbproj"),
            "ModBuilder Project");
    }

    private async Task CreateMediumTestProjectAsync()
    {
        Directory.CreateDirectory(_mediumProjectPath);

        // Create directory structure
        var gameFilesPath = Path.Combine(_mediumProjectPath, "GameFilesEdited");
        var dataPath = Path.Combine(gameFilesPath, "Data");

        Directory.CreateDirectory(dataPath);

        // Create ModBundles.json (use INI files instead of TGA to avoid conversion issues)
        var config = new
        {
            items = new[]
            {
                new
                {
                    name = "data_files",
                    files = new[]
                    {
                        new
                        {
                            absSourceParent = dataPath,  // Changed from gameFilesPath to dataPath
                            absSourceFile = Path.Combine(dataPath, "*.ini"),
                            relTargetFile = "Data/INI",
                        },
                    },
                },
            },
            packs = new[]
            {
                new
                {
                    name = "MediumPack",
                    itemNames = new[] { "data_files" },
                    allowBuild = true,
                },
            },
            folders = new
            {
                absBuildDir = Path.Combine(_mediumProjectPath, "build"),
            },
        };

        await File.WriteAllTextAsync(
            Path.Combine(_mediumProjectPath, "ModBundles.json"),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        await File.WriteAllTextAsync(
            Path.Combine(_mediumProjectPath, ".mbproj"),
            "ModBuilder Project");
    }

    private static byte[] GenerateRandomBytes(int size)
    {
        var random = new Random();
        var buffer = new byte[size];
        random.NextBytes(buffer);
        return buffer;
    }
}
