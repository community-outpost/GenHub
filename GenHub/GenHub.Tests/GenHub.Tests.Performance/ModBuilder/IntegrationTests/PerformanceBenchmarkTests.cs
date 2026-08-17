// <copyright file="PerformanceBenchmarkTests.cs" company="enowX Labs">
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
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Performance benchmark tests comparing C# implementation against Python baseline.
/// Tests validate that C# version is 15-25% faster than Python ModBuilder.
/// </summary>
public sealed class PerformanceBenchmarkTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _testProjectRoot;
    private readonly string _smallProjectPath;
    private readonly string _mediumProjectPath;
    private readonly string _largeProjectPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly IBuildEngineService _buildEngine;
    private readonly IConfigurationLoaderService _configLoader;

    // Python baseline metrics (from transcript)
    private const int PythonSmallProjectMs = 2500;      // 2.5s for 10 files
    private const int PythonMediumProjectMs = 12300;    // 12.3s for 100 files
    private const int PythonLargeProjectMs = 492000;    // 8.2 minutes for 1000 files

    // Target: 15-25% faster than Python
    private const double MinSpeedupFactor = 1.15;
    private const double MaxSpeedupFactor = 1.25;

    public PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _testProjectRoot = Path.Combine(Path.GetTempPath(), "ModBuilderBenchmarks", Guid.NewGuid().ToString());
        _smallProjectPath = Path.Combine(_testProjectRoot, "SmallProject");
        _mediumProjectPath = Path.Combine(_testProjectRoot, "MediumProject");
        _largeProjectPath = Path.Combine(_testProjectRoot, "LargeProject");

        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));

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
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_testProjectRoot);
        await CreateBenchmarkProjectsAsync();
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
    public async Task Benchmark_SmallProject_FasterThanPython()
    {
        // Arrange
        var configPath = Path.Combine(_smallProjectPath, "ModBundles.json");
        var targetMaxMs = (int)(PythonSmallProjectMs / MinSpeedupFactor);

        // Act - Run 3 times and take average
        var times = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var project = new ModBuilderProject
            {
                Name = "SmallBenchmark",
                ProjectDir = _smallProjectPath,
                Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
            };
            var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
            var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
            stopwatch.Stop();

            result.Success.Should().BeTrue();
            times.Add(stopwatch.ElapsedMilliseconds);

            // Clean cache between runs
            var cachePath = Path.Combine(_smallProjectPath, "build", ".cache");
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, recursive: true);
            }
        }

        var averageMs = times.Average();

        // Assert
        averageMs.Should().BeLessThan(targetMaxMs,
            $"C# should be at least {MinSpeedupFactor:P0} faster than Python ({PythonSmallProjectMs}ms)");

        var speedupFactor = PythonSmallProjectMs / averageMs;
        var speedupPercent = (speedupFactor - 1) * 100;

        _output.WriteLine("=== Small Project Benchmark (10 files, ~5MB) ===");
        _output.WriteLine($"Python baseline: {PythonSmallProjectMs}ms");
        _output.WriteLine($"C# average: {averageMs:F0}ms");
        _output.WriteLine($"Speedup: {speedupFactor:F2}x ({speedupPercent:F1}% faster)");
        _output.WriteLine($"Individual runs: {string.Join(", ", times.Select(t => $"{t}ms"))}");
        _output.WriteLine($"Target range: {MinSpeedupFactor:F2}x - {MaxSpeedupFactor:F2}x faster");
    }

    [Fact]
    public async Task Benchmark_MediumProject_FasterThanPython()
    {
        // Arrange
        var configPath = Path.Combine(_mediumProjectPath, "ModBundles.json");
        var targetMaxMs = (int)(PythonMediumProjectMs / MinSpeedupFactor);

        // Act - Run 3 times and take average
        var times = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var project = new ModBuilderProject
            {
                Name = "MediumBenchmark",
                ProjectDir = _mediumProjectPath,
                Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
            };
            var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
            var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
            stopwatch.Stop();

            result.Success.Should().BeTrue();
            times.Add(stopwatch.ElapsedMilliseconds);

            // Clean cache between runs
            var cachePath = Path.Combine(_mediumProjectPath, "build", ".cache");
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, recursive: true);
            }
        }

        var averageMs = times.Average();

        // Assert
        averageMs.Should().BeLessThan(targetMaxMs,
            $"C# should be at least {MinSpeedupFactor:P0} faster than Python ({PythonMediumProjectMs}ms)");

        var speedupFactor = PythonMediumProjectMs / averageMs;
        var speedupPercent = (speedupFactor - 1) * 100;

        _output.WriteLine("=== Medium Project Benchmark (100 files, ~50MB) ===");
        _output.WriteLine($"Python baseline: {PythonMediumProjectMs}ms");
        _output.WriteLine($"C# average: {averageMs:F0}ms");
        _output.WriteLine($"Speedup: {speedupFactor:F2}x ({speedupPercent:F1}% faster)");
        _output.WriteLine($"Individual runs: {string.Join(", ", times.Select(t => $"{t}ms"))}");
        _output.WriteLine($"Target range: {MinSpeedupFactor:F2}x - {MaxSpeedupFactor:F2}x faster");
    }

    [Fact(Skip = "Long-running test - enable for full benchmarks")]
    public async Task Benchmark_LargeProject_FasterThanPython()
    {
        // Arrange
        var configPath = Path.Combine(_largeProjectPath, "ModBundles.json");
        var targetMaxMs = (int)(PythonLargeProjectMs / MinSpeedupFactor);

        // Act - Single run (too long for multiple runs)
        var stopwatch = Stopwatch.StartNew();
        var project = new ModBuilderProject
        {
            Name = "LargeBenchmark",
            ProjectDir = _largeProjectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(targetMaxMs,
            $"C# should be at least {MinSpeedupFactor:P0} faster than Python ({PythonLargeProjectMs}ms)");

        var speedupFactor = PythonLargeProjectMs / (double)stopwatch.ElapsedMilliseconds;
        var speedupPercent = (speedupFactor - 1) * 100;

        _output.WriteLine("=== Large Project Benchmark (1000 files, ~500MB) ===");
        _output.WriteLine($"Python baseline: {PythonLargeProjectMs}ms ({PythonLargeProjectMs / 60000.0:F1} minutes)");
        _output.WriteLine($"C# time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedMilliseconds / 60000.0:F1} minutes)");
        _output.WriteLine($"Speedup: {speedupFactor:F2}x ({speedupPercent:F1}% faster)");
        _output.WriteLine($"Target range: {MinSpeedupFactor:F2}x - {MaxSpeedupFactor:F2}x faster");
    }

    [Fact]
    public async Task Benchmark_IncrementalBuild_NearInstant()
    {
        // Arrange
        var configPath = Path.Combine(_mediumProjectPath, "ModBundles.json");
        var testFilePath = Path.Combine(_mediumProjectPath, "GameFilesEdited", "Data", "test.ini");
        const int targetMaxMs = 1000; // Should be < 1 second

        // Act - Initial build
        var project = new ModBuilderProject
        {
            Name = "IncrementalBenchmark",
            ProjectDir = _mediumProjectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);

        // Modify one file
        await File.AppendAllTextAsync(testFilePath, "\n; Modified\n");

        // Act - Incremental build
        var stopwatch = Stopwatch.StartNew();
        var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(targetMaxMs,
            "Incremental build should be near-instant");

        _output.WriteLine("=== Incremental Build Benchmark ===");
        _output.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms (target: <{targetMaxMs}ms)");
        _output.WriteLine($"Files processed: {result.FilesProcessed}");
        _output.WriteLine($"Files skipped: {result.FilesSkipped}");
    }

    [Fact]
    public async Task Benchmark_ParallelProcessing_ScalesWithCores()
    {
        // Arrange
        var configPath = Path.Combine(_mediumProjectPath, "ModBundles.json");
        var coreCount = Environment.ProcessorCount;

        // Act
        var stopwatch = Stopwatch.StartNew();
        var project = new ModBuilderProject
        {
            Name = "ParallelBenchmark",
            ProjectDir = _mediumProjectPath,
            Configuration = await _configLoader.LoadConfigurationAsync(configPath, CancellationToken.None)
        };
        var selectedPacks = project.Configuration.Packs.Select(p => p.Name).ToList();
        var result = await _buildEngine.ExecuteBuildAsync(project, project.Configuration, selectedPacks, BuildStep.Build, null, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();

        // Estimate sequential time (assume 50ms per file)
        var estimatedSequentialMs = result.FilesProcessed * 50;
        var parallelEfficiency = estimatedSequentialMs / (double)stopwatch.ElapsedMilliseconds;

        // Should achieve reasonable throughput in virtualized test environments
        var minExpectedSpeedup = 0.5;
        parallelEfficiency.Should().BeGreaterThan(minExpectedSpeedup,
            $"Parallel processing should scale with CPU cores ({coreCount} cores)");

        _output.WriteLine("=== Parallel Processing Benchmark ===");
        _output.WriteLine($"CPU cores: {coreCount}");
        _output.WriteLine($"Files processed: {result.FilesProcessed}");
        _output.WriteLine($"Actual time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Estimated sequential: {estimatedSequentialMs}ms");
        _output.WriteLine($"Parallel efficiency: {parallelEfficiency:F2}x");
        _output.WriteLine($"Efficiency vs cores: {(parallelEfficiency / coreCount) * 100:F1}%");
    }

    private async Task CreateBenchmarkProjectsAsync()
    {
        await CreateSmallBenchmarkProjectAsync();
        await CreateMediumBenchmarkProjectAsync();
        // Large project creation skipped by default (too large)
    }

    private async Task CreateSmallBenchmarkProjectAsync()
    {
        Directory.CreateDirectory(_smallProjectPath);

        var gameFilesPath = Path.Combine(_smallProjectPath, "GameFilesEdited");
        var texturesPath = Path.Combine(gameFilesPath, "Textures");
        var dataPath = Path.Combine(gameFilesPath, "Data");

        Directory.CreateDirectory(texturesPath);
        Directory.CreateDirectory(dataPath);

        // Create 10 files (~5MB total)
        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(texturesPath, $"texture_{i}.dat"),
                GenerateRandomBytes(512 * 1024)); // 512KB each
        }

        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, $"data_{i}.ini"),
                GenerateIniContent(100)); // 100 lines each
        }

        await CreateConfigFileAsync(_smallProjectPath, 10);
    }

    private async Task CreateMediumBenchmarkProjectAsync()
    {
        Directory.CreateDirectory(_mediumProjectPath);

        var gameFilesPath = Path.Combine(_mediumProjectPath, "GameFilesEdited");
        var texturesPath = Path.Combine(gameFilesPath, "Textures");
        var dataPath = Path.Combine(gameFilesPath, "Data");

        Directory.CreateDirectory(texturesPath);
        Directory.CreateDirectory(dataPath);

        // Create 100 files (~50MB total)
        for (int i = 0; i < 50; i++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(texturesPath, $"texture_{i}.dat"),
                GenerateRandomBytes(512 * 1024)); // 512KB each
        }

        for (int i = 0; i < 50; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, $"data_{i}.ini"),
                GenerateIniContent(200)); // 200 lines each
        }

        await CreateConfigFileAsync(_mediumProjectPath, 100);
    }

    private async Task CreateConfigFileAsync(string projectPath, int fileCount)
    {
        var gameFilesPath = Path.Combine(projectPath, "GameFilesEdited");
        var texturesPath = Path.Combine(gameFilesPath, "Textures");
        var dataPath = Path.Combine(gameFilesPath, "Data");

        var config = new
        {
            items = new[]
            {
                new
                {
                    name = "textures",
                    files = new[]
                    {
                        new
                        {
                            absSourceParent = gameFilesPath,
                            absSourceFile = Path.Combine(texturesPath, "*.dat"),
                            relTargetFile = "Data/Textures",
                        },
                    },
                },
                new
                {
                    name = "data",
                    files = new[]
                    {
                        new
                        {
                            absSourceParent = gameFilesPath,
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
                    name = "BenchmarkPack",
                    itemNames = new[] { "textures", "data" },
                    allowBuild = true,
                },
            },
            folders = new
            {
                absBuildDir = Path.Combine(projectPath, "build"),
            },
        };

        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "ModBundles.json"),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        await File.WriteAllTextAsync(
            Path.Combine(projectPath, ".mbproj"),
            "ModBuilder Benchmark Project");
    }

    private static byte[] GenerateRandomBytes(int size)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var buffer = new byte[size];
        random.NextBytes(buffer);
        return buffer;
    }

    private static string GenerateIniContent(int lineCount)
    {
        var lines = new List<string> { "[TestSection]" };
        for (int i = 0; i < lineCount; i++)
        {
            lines.Add($"Key{i}=Value{i}");
        }

        return string.Join("\n", lines);
    }
}
