# ModBuilder Performance Benchmarks

Comprehensive BenchmarkDotNet test suite for validating ModBuilder performance optimizations.

## Overview

This benchmark suite validates the 10-20% performance improvement of the C# ModBuilder implementation over the Python baseline. It measures key optimization areas identified during Week 1 and Week 2 development.

## Running Benchmarks

### Quick Start

```bash
cd GenHub.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmarks

```bash
# Run only MD5 hashing benchmarks
dotnet run -c Release --filter "*Md5Hashing*"

# Run only image conversion benchmarks
dotnet run -c Release --filter "*ImageConversion*"

# Run only cache serialization benchmarks
dotnet run -c Release --filter "*CacheSerialization*"
```

### Advanced Options

```bash
# Run with memory profiler
dotnet run -c Release --memory

# Export results to CSV
dotnet run -c Release --exporters csv

# Run with detailed diagnostics
dotnet run -c Release --info
```

## Benchmark Categories

### 1. MD5 Hashing Benchmarks

**Optimization**: 64KB buffer size + parallel processing

- `Md5Hashing_OptimizedBuffer_10Files` - Tests optimized buffer size (64KB)
- `Md5Hashing_Parallel_100Files` - Tests 8x speedup from parallel processing

**Expected Results**: 8x faster with parallel processing on 8-core CPU

### 2. Image Conversion Benchmarks

**Optimization**: RGBA channel-split with DangerousTryGetSinglePixelMemory

- `ImageConversion_RGBA_ChannelSplit_2048x2048` - Tests 50x faster channel splitting
- `ImageConversion_AlphaDetection` - Tests alpha channel detection performance
- `ImageConversion_ToDDS_WithMipMaps` - Tests DDS encoding with BCnEncoder

**Expected Results**: 50x faster RGBA resizing with memory-optimized channel splitting

### 3. Cache Serialization Benchmarks

**Optimization**: MessagePack instead of JSON

- `CacheSerialization_MessagePack_Write` - Tests 10x faster serialization
- `CacheSerialization_MessagePack_Read` - Tests 10x faster deserialization
- `BuildCache_ChangeDetection_100Files` - Tests complete cache workflow with MD5 reuse

**Expected Results**: 10x faster cache I/O with MessagePack format

### 4. Archive Creation Benchmarks

**Optimization**: Parallel file reading with ArrayPool

- `ArchiveCreation_ZIP_100Files` - Tests parallel ZIP creation
- `ArchiveCreation_TAR_100Files` - Tests parallel TAR creation
- `ArchiveCreation_TARGZ_100Files` - Tests parallel TAR.GZ creation

**Expected Results**: Significant speedup from parallel I/O operations

### 5. End-to-End Build Benchmarks

**Optimization**: Complete build pipeline with all optimizations

- `SmallProject_Build_10Files_5MB` - Tests small project build cycle
- `MediumProject_Build_100Files_50MB` - Tests medium project with parallel processing

**Expected Results**: 10-20% overall improvement over Python baseline

## Test Data

Benchmarks use realistic test data:

- **Small Project**: 10 files, ~5MB total (500KB each)
- **Medium Project**: 100 files, ~50MB total (500KB each)
- **Test Image**: 2048x2048 RGBA PNG with random data
- **Test Cache**: 1000 entries with realistic metadata

All test data is generated in `GlobalSetup` and cleaned up in `GlobalCleanup`.

## Performance Targets

Based on Week 1 and Week 2 optimizations:

| Optimization | Target Improvement |
|--------------|-------------------|
| MD5 Hashing (Parallel) | 8x faster |
| RGBA Channel Split | 50x faster |
| MessagePack Cache I/O | 10x faster |
| Overall Build Pipeline | 10-20% faster |

## Interpreting Results

BenchmarkDotNet provides detailed metrics:

- **Mean**: Average execution time
- **Error**: Half of 99.9% confidence interval
- **StdDev**: Standard deviation of all measurements
- **Gen0/Gen1/Gen2**: Garbage collection counts per 1000 operations
- **Allocated**: Total memory allocated per operation

### Example Output

```
| Method                                    | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------------ |----------:|---------:|---------:|-------:|----------:|
| Md5Hashing_OptimizedBuffer_10Files        | 12.34 ms  | 0.23 ms  | 0.19 ms  | -      | 1.2 KB    |
| ImageConversion_RGBA_ChannelSplit_2048x2048| 45.67 ms | 0.89 ms  | 0.74 ms  | 1000.0 | 32.5 MB   |
| CacheSerialization_MessagePack_Write      | 3.21 ms   | 0.06 ms  | 0.05 ms  | 125.0  | 512 KB    |
```

## Troubleshooting

### Build Errors

If you encounter build errors, ensure:

1. .NET 8.0 SDK is installed
2. All NuGet packages are restored: `dotnet restore`
3. Project references are correct

### Benchmark Failures

If benchmarks fail:

1. Check available disk space (benchmarks create temporary files)
2. Ensure sufficient memory (image benchmarks use ~100MB)
3. Close other applications to reduce CPU contention

### Slow Execution

Benchmarks take 5-10 minutes to complete:

- 3 warmup iterations per benchmark
- 5 measurement iterations per benchmark
- Multiple benchmarks in the suite

Use `--filter` to run specific benchmarks during development.

## CI/CD Integration

To integrate benchmarks into CI/CD:

```bash
# Run benchmarks and fail if performance regresses
dotnet run -c Release --filter "*" --exporters json
# Parse JSON results and compare against baseline
```

## Contributing

When adding new benchmarks:

1. Use `[Benchmark]` attribute
2. Add XML documentation explaining what is being tested
3. Use realistic test data
4. Clean up resources in the benchmark method
5. Update this README with the new benchmark

## References

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [ModBuilder Optimization Guide](../docs/ModBuilder_Optimizations.md)
- [Performance Comparison: C# vs Python](../docs/Performance_Comparison.md)
