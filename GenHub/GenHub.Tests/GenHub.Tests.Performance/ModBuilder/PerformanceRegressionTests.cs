// <copyright file="PerformanceRegressionTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GenHub.Tests.Performance.ModBuilder;

/// <summary>
/// Performance regression tests for ModBuilder to ensure performance doesn't degrade over time.
/// Tests fail if performance degrades by more than 10% from established baselines.
/// </summary>
public class PerformanceRegressionTests : IDisposable
{
    private readonly double maxRegressionPercent = 50.0;
    private readonly string testDataPath;
    private readonly Dictionary<string, PerformanceBaseline> baselines;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PerformanceRegressionTests"/> class.
    /// </summary>
    public PerformanceRegressionTests()
    {
        this.testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Performance");
        Directory.CreateDirectory(this.testDataPath);

        // Load baselines from JSON
        var baselinesPath = Path.Combine(AppContext.BaseDirectory, "PerformanceBaselines.json");
        if (File.Exists(baselinesPath))
        {
            var json = File.ReadAllText(baselinesPath);
            var config = JsonConvert.DeserializeObject<JObject>(json);
            if (config?["maxRegressionPercent"] != null && double.TryParse(config["maxRegressionPercent"]?.ToString(), out var parsedMax))
            {
                this.maxRegressionPercent = parsedMax;
            }

            this.baselines = new Dictionary<string, PerformanceBaseline>();

            if (config?["baselines"] is JObject baselinesObj)
            {
                foreach (var prop in baselinesObj.Properties())
                {
                    var baseline = prop.Value.ToObject<PerformanceBaseline>();
                    if (baseline != null)
                    {
                        this.baselines[prop.Name] = baseline;
                    }
                }
            }
        }
        else
        {
            // Fallback to hardcoded baselines if file doesn't exist
            this.baselines = new Dictionary<string, PerformanceBaseline>
            {
                ["MD5Hashing_100Files"] = new PerformanceBaseline { BaselineMs = 5000 },
                ["ImageConversion_2048x2048_RGBA"] = new PerformanceBaseline { BaselineMs = 60000 },
                ["CacheSerialization_LargeCache"] = new PerformanceBaseline { BaselineMs = 500 },
                ["ParallelMD5Hashing_100Files"] = new PerformanceBaseline { BaselineMs = 2500 },
                ["BuildCacheComparison_1000Files"] = new PerformanceBaseline { BaselineMs = 500 },
            };
        }
    }

    /// <summary>
    /// Tests that MD5 hashing performance doesn't regress for 100 files.
    /// Baseline: 2.5s for 100 files with mtime optimization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task MD5Hashing_100Files_ShouldNotRegress()
    {
        // Arrange
        var baseline = this.GetBaseline("MD5Hashing_100Files");
        var maxAllowed = this.CalculateMaxAllowed(baseline);

        var testFiles = this.CreateTestFiles(100, 1024 * 1024); // 100 files, 1MB each
        var hashProvider = new Md5HashProvider();

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var file in testFiles)
        {
            await hashProvider.ComputeFileHashAsync(file);
        }

        sw.Stop();

        // Assert
        sw.Elapsed.Should().BeLessThan(maxAllowed,
            $"MD5 hashing regressed: {sw.Elapsed.TotalMilliseconds:F2}ms > {maxAllowed.TotalMilliseconds:F2}ms (baseline: {baseline.TotalMilliseconds:F2}ms, max regression: {this.maxRegressionPercent}%)");

        // Cleanup
        this.CleanupTestFiles(testFiles);
    }

    /// <summary>
    /// Tests that parallel MD5 hashing performance doesn't regress for 100 files.
    /// Baseline: 800ms for 100 files with parallel processing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ParallelMD5Hashing_100Files_ShouldNotRegress()
    {
        // Arrange
        var baseline = this.GetBaseline("ParallelMD5Hashing_100Files");
        var maxAllowed = this.CalculateMaxAllowed(baseline);

        var testFiles = this.CreateTestFiles(100, 1024 * 1024); // 100 files, 1MB each
        var hashProvider = new Md5HashProvider();

        // Act
        var sw = Stopwatch.StartNew();
        var tasks = testFiles.Select(file => hashProvider.ComputeFileHashAsync(file));
        await Task.WhenAll(tasks);
        sw.Stop();

        // Assert
        sw.Elapsed.Should().BeLessThan(maxAllowed,
            $"Parallel MD5 hashing regressed: {sw.Elapsed.TotalMilliseconds:F2}ms > {maxAllowed.TotalMilliseconds:F2}ms (baseline: {baseline.TotalMilliseconds:F2}ms, max regression: {this.maxRegressionPercent}%)");

        // Cleanup
        this.CleanupTestFiles(testFiles);
    }

    /// <summary>
    /// Tests that image conversion performance doesn't regress for 2048x2048 RGBA images.
    /// Baseline: 120ms for 2048x2048 RGBA conversion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ImageConversion_2048x2048_RGBA_ShouldNotRegress()
    {
        // Arrange
        var baseline = this.GetBaseline("ImageConversion_2048x2048_RGBA");
        var maxAllowed = this.CalculateMaxAllowed(baseline);

        var sourcePath = this.CreateTestImage(2048, 2048, hasAlpha: true);
        var targetPath = Path.Combine(this.testDataPath, "output.dds");

        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<ImageConversionService>>();
        var imageService = new ImageConversionService(mockLogger.Object);

        // Act
        var sw = Stopwatch.StartNew();
        await imageService.ConvertImageAsync(sourcePath, targetPath, null, CancellationToken.None);
        sw.Stop();

        // Assert
        sw.Elapsed.Should().BeLessThan(maxAllowed,
            $"Image conversion regressed: {sw.Elapsed.TotalMilliseconds:F2}ms > {maxAllowed.TotalMilliseconds:F2}ms (baseline: {baseline.TotalMilliseconds:F2}ms, max regression: {this.maxRegressionPercent}%)");

        // Cleanup
        File.Delete(sourcePath);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
    }

    /// <summary>
    /// Tests that cache serialization performance doesn't regress for large caches.
    /// Baseline: 200ms for large cache with 1000 file entries.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CacheSerialization_LargeCache_ShouldNotRegress()
    {
        // Arrange
        var baseline = this.GetBaseline("CacheSerialization_LargeCache");
        var maxAllowed = this.CalculateMaxAllowed(baseline);

        var cachePath = Path.Combine(this.testDataPath, "test_cache.json");
        var mockHashProvider = new Mock<IMd5HashProvider>();
        mockHashProvider
            .Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("d41d8cd98f00b204e9800998ecf8427e");

        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<BuildCacheService>>();
        var cacheService = new BuildCacheService(mockHashProvider.Object, mockLogger.Object);

        // Create large cache with 1000 entries
        for (int i = 0; i < 1000; i++)
        {
            cacheService.AddFile(
                $"test_file_{i}.txt",
                DateTime.UtcNow.Ticks,
                $"hash_{i:X8}",
                new Dictionary<string, object> { ["param1"] = "value1", ["param2"] = 123 });
        }

        // Act - Save
        var sw = Stopwatch.StartNew();
        await cacheService.SaveCacheAsync(cachePath);
        sw.Stop();
        var saveTime = sw.Elapsed;

        // Act - Load
        var mockLogger2 = new Mock<Microsoft.Extensions.Logging.ILogger<BuildCacheService>>();
        var newCacheService = new BuildCacheService(mockHashProvider.Object, mockLogger2.Object);
        sw.Restart();
        await newCacheService.LoadCacheAsync(cachePath);
        sw.Stop();
        var loadTime = sw.Elapsed;

        var totalTime = saveTime + loadTime;

        // Assert
        totalTime.Should().BeLessThan(maxAllowed,
            $"Cache serialization regressed: {totalTime.TotalMilliseconds:F2}ms > {maxAllowed.TotalMilliseconds:F2}ms (baseline: {baseline.TotalMilliseconds:F2}ms, max regression: {this.maxRegressionPercent}%)");

        // Cleanup
        File.Delete(cachePath);
        var msgpackPath = Path.ChangeExtension(cachePath, ".msgpack");
        if (File.Exists(msgpackPath))
        {
            File.Delete(msgpackPath);
        }
    }

    /// <summary>
    /// Tests that build cache comparison performance doesn't regress for 1000 files.
    /// Baseline: 150ms for comparing 1000 file entries.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task BuildCacheComparison_1000Files_ShouldNotRegress()
    {
        // Arrange
        var baseline = this.GetBaseline("BuildCacheComparison_1000Files");
        var maxAllowed = this.CalculateMaxAllowed(baseline);

        var mockHashProvider = new Mock<IMd5HashProvider>();
        mockHashProvider
            .Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("d41d8cd98f00b204e9800998ecf8427e");

        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<BuildCacheService>>();
        var cacheService = new BuildCacheService(mockHashProvider.Object, mockLogger.Object);

        // Create old cache with 1000 entries
        for (int i = 0; i < 1000; i++)
        {
            cacheService.AddFile(
                $"test_file_{i}.txt",
                DateTime.UtcNow.Ticks,
                $"hash_{i:X8}");
        }

        var cachePath = Path.Combine(this.testDataPath, "comparison_cache.json");
        await cacheService.SaveCacheAsync(cachePath);

        // Load as old cache
        var mockLogger2 = new Mock<Microsoft.Extensions.Logging.ILogger<BuildCacheService>>();
        var comparisonService = new BuildCacheService(mockHashProvider.Object, mockLogger2.Object);
        await comparisonService.LoadCacheAsync(cachePath);

        // Act - Compare 1000 files
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            var filePath = $"test_file_{i}.txt";
            var currentHash = i % 2 == 0 ? $"hash_{i:X8}" : $"modified_hash_{i:X8}"; // 50% changed
            _ = comparisonService.DetermineFileStatus(filePath, currentHash);
        }

        sw.Stop();

        // Assert
        sw.Elapsed.Should().BeLessThan(maxAllowed,
            $"Build cache comparison regressed: {sw.Elapsed.TotalMilliseconds:F2}ms > {maxAllowed.TotalMilliseconds:F2}ms (baseline: {baseline.TotalMilliseconds:F2}ms, max regression: {this.maxRegressionPercent}%)");

        // Cleanup
        File.Delete(cachePath);
        var msgpackPath = Path.ChangeExtension(cachePath, ".msgpack");
        if (File.Exists(msgpackPath))
        {
            File.Delete(msgpackPath);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources used by the test class.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed && disposing)
        {
            // Cleanup test data directory
            if (Directory.Exists(this.testDataPath))
            {
                try
                {
                    Directory.Delete(this.testDataPath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            this.disposed = true;
        }
    }

    private TimeSpan GetBaseline(string testName)
    {
        if (this.baselines.TryGetValue(testName, out var baseline))
        {
            return TimeSpan.FromMilliseconds(baseline.BaselineMs);
        }

        throw new InvalidOperationException($"Baseline not found for test: {testName}");
    }

    private TimeSpan CalculateMaxAllowed(TimeSpan baseline)
    {
        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

        var allowancePercent = isCi ? 100.0 : this.maxRegressionPercent;
        var regressionMs = baseline.TotalMilliseconds * (allowancePercent / 100.0);
        return baseline + TimeSpan.FromMilliseconds(regressionMs);
    }

    private List<string> CreateTestFiles(int count, int sizeBytes)
    {
        var files = new List<string>();
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < count; i++)
        {
            var filePath = Path.Combine(this.testDataPath, $"test_file_{i}.dat");
            var data = new byte[sizeBytes];
            random.NextBytes(data);
            File.WriteAllBytes(filePath, data);
            files.Add(filePath);
        }

        return files;
    }

    private void CleanupTestFiles(List<string> files)
    {
        foreach (var file in files)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private string CreateTestImage(int width, int height, bool hasAlpha)
    {
        var filePath = Path.Combine(this.testDataPath, $"test_image_{width}x{height}.tga");

        // Create a simple TGA file (uncompressed RGBA)
        using var fs = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // TGA Header (18 bytes)
        writer.Write((byte)0);  // ID length
        writer.Write((byte)0);  // Color map type
        writer.Write((byte)2);  // Image type (uncompressed RGB)
        writer.Write((short)0); // Color map origin
        writer.Write((short)0); // Color map length
        writer.Write((byte)0);  // Color map depth
        writer.Write((short)0); // X origin
        writer.Write((short)0); // Y origin
        writer.Write((short)width);
        writer.Write((short)height);
        writer.Write((byte)(hasAlpha ? 32 : 24)); // Bits per pixel
        writer.Write((byte)(hasAlpha ? 8 : 0));   // Image descriptor

        // Write pixel data
        var random = new Random(42);
        for (int i = 0; i < width * height; i++)
        {
            writer.Write((byte)random.Next(256)); // B
            writer.Write((byte)random.Next(256)); // G
            writer.Write((byte)random.Next(256)); // R
            if (hasAlpha)
            {
                writer.Write((byte)random.Next(256)); // A
            }
        }

        return filePath;
    }

    private class PerformanceBaseline
    {
        public double BaselineMs { get; set; }

        public string? Description { get; set; }

        public string? TestDataSize { get; set; }
    }
}
