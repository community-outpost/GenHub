using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Tools.ModBuilder.Services;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GenHub.Benchmarks.ModBuilder;

/// <summary>
/// Direct execution runner for authentic GenHub ModBuilder services.
/// Measures high-precision timing, CPU times, and throughput for single-thread vs multi-core benchmarking.
/// </summary>
public sealed class ModBuilderDirectRunner
{
    private readonly IMd5HashProvider md5HashProvider = new Md5HashProvider();
    private readonly IImageConversionService imageConversionService = new ImageConversionService(NullLogger<ImageConversionService>.Instance);
    private readonly IBuildCacheService buildCacheService = new BuildCacheService(new Md5HashProvider(), NullLogger<BuildCacheService>.Instance);
    private readonly IArchiveService archiveService = new ArchiveService(NullLogger<ArchiveService>.Instance);

    /// <summary>
    /// Runs benchmarks based on command-line arguments.
    /// </summary>
    public async Task<int> RunAsync(string[] args)
    {
        var bench = "all";
        var dataDir = Path.Combine(Path.GetTempPath(), "modbuilder_test_dataset");
        var outDir = Path.Combine(Path.GetTempPath(), "modbuilder_cs_bench_out");
        var threads = Environment.ProcessorCount;
        var iterations = 10;
        string? jsonOut = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--bench=") || arg == "--bench")
            {
                bench = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : (i + 1 < args.Length ? args[++i] : bench);
            }
            else if (arg.StartsWith("--data-dir=") || arg == "--data-dir")
            {
                dataDir = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : (i + 1 < args.Length ? args[++i] : dataDir);
            }
            else if (arg.StartsWith("--out-dir=") || arg == "--out-dir")
            {
                outDir = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : (i + 1 < args.Length ? args[++i] : outDir);
            }
            else if (arg.StartsWith("--threads=") || arg == "--threads")
            {
                var val = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : (i + 1 < args.Length ? args[++i] : "1");
                _ = int.TryParse(val, out threads);
            }
            else if (arg.StartsWith("-n=") || arg == "-n" || arg.StartsWith("--iterations="))
            {
                var val = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : (i + 1 < args.Length ? args[++i] : "10");
                _ = int.TryParse(val, out iterations);
            }
            else if (arg.StartsWith("--json-out=") || arg == "--json-out")
            {
                jsonOut = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : (i + 1 < args.Length ? args[++i] : null);
            }
        }

        Directory.CreateDirectory(outDir);

        var files = Directory.Exists(dataDir)
            ? Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();

        var totalBytes = files.Sum(f => new FileInfo(f).Length);

        Console.WriteLine($"=== C# GenHub ModBuilder Benchmark Suite (Threads={threads}) ===");
        Console.WriteLine($"Dataset: {dataDir} ({files.Length} files, {totalBytes / (1024.0 * 1024.0):F2} MB)");
        Console.WriteLine($"Iterations: {iterations}\n");

        var results = new Dictionary<string, object>();

        // 1. MD5 Hashing
        if (bench is "all" or "md5" && files.Length > 0)
        {
            var times = new List<double>();
            for (var iter = 0; iter < iterations; iter++)
            {
                var sw = Stopwatch.StartNew();
                if (threads <= 1)
                {
                    foreach (var f in files)
                    {
                        await md5HashProvider.ComputeFileHashAsync(f, CancellationToken.None);
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(
                        files,
                        new ParallelOptions { MaxDegreeOfParallelism = threads },
                        async (f, ct) => await md5HashProvider.ComputeFileHashAsync(f, ct));
                }

                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgMs = times.Average();
            var mb = totalBytes / (1024.0 * 1024.0);
            var thMbS = mb / (avgMs / 1000.0);
            var thFiles = files.Length / (avgMs / 1000.0);
            Console.WriteLine($"[C# Micro] MD5 Hashing (64KB Buffer, {threads} Threads): Mean = {avgMs:F2} ms | Throughput = {thMbS:F2} MB/s ({thFiles:F1} files/s)");

            results["md5"] = new
            {
                mean_ms = avgMs,
                throughput_mb_s = thMbS,
                throughput_files_s = thFiles,
                times_ms = times,
            };
        }

        // 2. BIG Archive Creation (BigFilePacker / ArchiveService)
        if (bench is "all" or "big" && files.Length > 0)
        {
            var outBig = Path.Combine(outDir, "CSharpBenchmarkOutput.big");
            var times = new List<double>();
            for (var iter = 0; iter < iterations; iter++)
            {
                if (File.Exists(outBig))
                {
                    File.Delete(outBig);
                }

                var sw = Stopwatch.StartNew();
                await BigFilePacker.PackAsync(dataDir, outBig, CancellationToken.None);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgMs = times.Average();
            var outSizeMb = File.Exists(outBig) ? new FileInfo(outBig).Length / (1024.0 * 1024.0) : 0.0;
            var thMbS = outSizeMb / (avgMs / 1000.0);
            Console.WriteLine($"[C# Micro] BIG Packager: Mean = {avgMs:F2} ms | Output = {outSizeMb:F2} MB | Packing Throughput = {thMbS:F2} MB/s");

            results["big"] = new
            {
                mean_ms = avgMs,
                throughput_mb_s = thMbS,
                output_size_mb = outSizeMb,
                times_ms = times,
            };
        }

        // 3. Cache Serialization (MessagePack)
        if (bench is "all" or "cache")
        {
            const int count = 2000;
            var cacheData = new Dictionary<string, BuildFilePathInfo>(count);
            for (var i = 0; i < count; i++)
            {
                var key = $"Art/Textures/Texture_{i:D4}.dds";
                cacheData[key] = new BuildFilePathInfo
                {
                    Path = key,
                    ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Md5 = "d41d8cd98f00b204e9800998ecf8427e",
                    Params = new Dictionary<string, object>
                    {
                        { "format", "dds" },
                        { "compression", "dxt5" },
                        { "mipmaps", true },
                    },
                };
            }

            var cachePath = Path.Combine(outDir, "cache.msgpack");
            var writeTimes = new List<double>();
            var readTimes = new List<double>();

            for (var iter = 0; iter < iterations; iter++)
            {
                // Write
                var swW = Stopwatch.StartNew();
                await using (var fs = File.Create(cachePath))
                {
                    await MessagePackSerializer.SerializeAsync(fs, cacheData, cancellationToken: CancellationToken.None);
                }

                swW.Stop();
                writeTimes.Add(swW.Elapsed.TotalMilliseconds);

                // Read
                var swR = Stopwatch.StartNew();
                await using (var fs = File.OpenRead(cachePath))
                {
                    _ = await MessagePackSerializer.DeserializeAsync<Dictionary<string, BuildFilePathInfo>>(fs, cancellationToken: CancellationToken.None);
                }

                swR.Stop();
                readTimes.Add(swR.Elapsed.TotalMilliseconds);
            }

            var avgW = writeTimes.Average();
            var avgR = readTimes.Average();
            var thW = count / (avgW / 1000.0);
            var thR = count / (avgR / 1000.0);
            Console.WriteLine($"[C# Micro] Cache Serialization (MessagePack, 2,000 entries): Write = {avgW:F2} ms ({thW:F0} entries/s) | Read = {avgR:F2} ms ({thR:F0} entries/s)");

            results["cache"] = new
            {
                write_mean_ms = avgW,
                read_mean_ms = avgR,
                write_throughput_items_s = thW,
                read_throughput_items_s = thR,
                write_times_ms = writeTimes,
                read_times_ms = readTimes,
            };
        }

        // 4. Image RGBA Channel-Split Resizing
        if (bench is "all" or "image")
        {
            var testImgPath = Path.Combine(outDir, "bench_test_img.png");
            if (!File.Exists(testImgPath))
            {
                using var img = new Image<Rgba32>(2048, 2048);
                img.SaveAsPng(testImgPath);
            }

            var outImgPath = Path.Combine(outDir, "bench_test_img_out.png");
            var times = new List<double>();
            var parameters = new Dictionary<string, object>
            {
                { "resize", new[] { 1024, 1024 } },
                { "resampling", "bilinear" },
            };

            for (var iter = 0; iter < iterations; iter++)
            {
                if (File.Exists(outImgPath))
                {
                    File.Delete(outImgPath);
                }

                var sw = Stopwatch.StartNew();
                await imageConversionService.ConvertImageAsync(testImgPath, outImgPath, parameters, CancellationToken.None);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgMs = times.Average();
            Console.WriteLine($"[C# Micro] Image RGBA Channel-Split Resize (ImageSharp Fast Span): Mean = {avgMs:F2} ms/image");

            results["image"] = new
            {
                mean_ms = avgMs,
                times_ms = times,
            };
        }

        // 5. Build Cache Change Detection Workflow (Cold vs Warm)
        if (bench is "all" or "cache_workflow" && files.Length > 0)
        {
            var cachePath = Path.Combine(outDir, "build_cache_wf.msgpack");
            var coldTimes = new List<double>();
            var warmTimes = new List<double>();

            for (var iter = 0; iter < iterations; iter++)
            {
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }

                // Cold Build: all files are newly hashed and registered
                buildCacheService.Clear();
                var swCold = Stopwatch.StartNew();
                if (threads <= 1)
                {
                    foreach (var f in files)
                    {
                        var md5 = await buildCacheService.ComputeOrReuseMd5Async(f, CancellationToken.None);
                        _ = buildCacheService.DetermineFileStatus(f, md5);
                        buildCacheService.AddFile(f, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), md5);
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(
                        files,
                        new ParallelOptions { MaxDegreeOfParallelism = threads },
                        async (f, ct) =>
                        {
                            var md5 = await buildCacheService.ComputeOrReuseMd5Async(f, ct);
                            _ = buildCacheService.DetermineFileStatus(f, md5);
                            buildCacheService.AddFile(f, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), md5);
                        });
                }

                await buildCacheService.SaveCacheAsync(cachePath, CancellationToken.None);
                swCold.Stop();
                coldTimes.Add(swCold.Elapsed.TotalMilliseconds);

                // Warm Build: cache loaded, 0 files modified (MD5 reuse)
                buildCacheService.Clear();
                var swWarm = Stopwatch.StartNew();
                await buildCacheService.LoadCacheAsync(cachePath, CancellationToken.None);
                if (threads <= 1)
                {
                    foreach (var f in files)
                    {
                        var md5 = await buildCacheService.ComputeOrReuseMd5Async(f, CancellationToken.None);
                        _ = buildCacheService.DetermineFileStatus(f, md5);
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(
                        files,
                        new ParallelOptions { MaxDegreeOfParallelism = threads },
                        async (f, ct) =>
                        {
                            var md5 = await buildCacheService.ComputeOrReuseMd5Async(f, ct);
                            _ = buildCacheService.DetermineFileStatus(f, md5);
                        });
                }

                swWarm.Stop();
                warmTimes.Add(swWarm.Elapsed.TotalMilliseconds);
            }

            var avgCold = coldTimes.Average();
            var avgWarm = warmTimes.Average();
            Console.WriteLine($"[C# Macro] Cache Workflow Cold Build: Mean = {avgCold:F2} ms");
            Console.WriteLine($"[C# Macro] Cache Workflow Warm Build: Mean = {avgWarm:F2} ms (Speedup: {avgCold / Math.Max(0.001, avgWarm):F1}x)");

            results["cache_workflow"] = new
            {
                cold_mean_ms = avgCold,
                warm_mean_ms = avgWarm,
                cold_times_ms = coldTimes,
                warm_times_ms = warmTimes,
            };
        }

        if (!string.IsNullOrEmpty(jsonOut))
        {
            var jsonString = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonOut, jsonString);
            Console.WriteLine($"\nTelemetry JSON written to: {jsonOut}");
        }

        Console.WriteLine("\nC# GenHub Benchmark Suite Run Completed Successfully.\n");
        return 0;
    }
}
