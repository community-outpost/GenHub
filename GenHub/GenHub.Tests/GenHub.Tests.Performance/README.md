# ModBuilder Performance Regression Tests

This project contains automated performance regression tests for the ModBuilder tool to ensure performance doesn't degrade over time.

## Overview

Performance regression tests automatically fail if performance degrades by more than **10%** from established baselines. This helps catch performance issues early in the development cycle.

## Test Coverage

### 1. MD5 Hashing Performance
- **Test**: `MD5Hashing_100Files_ShouldNotRegress`
- **Baseline**: 2.5 seconds for 100 files (1MB each)
- **What it tests**: MD5 hash computation with modification time optimization

### 2. Parallel MD5 Hashing Performance
- **Test**: `ParallelMD5Hashing_100Files_ShouldNotRegress`
- **Baseline**: 800ms for 100 files (1MB each)
- **What it tests**: Parallel MD5 hash computation efficiency

### 3. Image Conversion Performance
- **Test**: `ImageConversion_2048x2048_RGBA_ShouldNotRegress`
- **Baseline**: 120ms for 2048x2048 RGBA image
- **What it tests**: Image conversion from TGA to DDS format

### 4. Cache Serialization Performance
- **Test**: `CacheSerialization_LargeCache_ShouldNotRegress`
- **Baseline**: 200ms for 1000 file entries
- **What it tests**: Build cache save/load with MessagePack serialization

### 5. Build Cache Comparison Performance
- **Test**: `BuildCacheComparison_1000Files_ShouldNotRegress`
- **Baseline**: 150ms for 1000 file comparisons
- **What it tests**: Change detection algorithm efficiency

## Running Tests

```bash
# Run all performance tests
dotnet test GenHub.Tests.Performance.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~MD5Hashing_100Files"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Baseline Management

### Baseline Configuration
Baselines are stored in `PerformanceBaselines.json`:

```json
{
  "version": "1.0.0",
  "baselines": {
    "MD5Hashing_100Files": {
      "baselineMs": 2500,
      "description": "MD5 hashing for 100 files with mtime optimization",
      "testDataSize": "100 files, ~1MB each"
    }
  },
  "maxRegressionPercent": 10.0
}
```

### Updating Baselines
When you make **intentional performance improvements**:

1. Run the tests to verify the improvement
2. Update the baseline values in `PerformanceBaselines.json`
3. Document the change in git commit message
4. Include before/after metrics

Example:
```json
"MD5Hashing_100Files": {
  "baselineMs": 2000,  // Improved from 2500ms
  "description": "MD5 hashing with new streaming optimization"
}
```

## CI/CD Integration

### GitHub Actions
Add to your workflow:

```yaml
- name: Run Performance Tests
  run: dotnet test GenHub.Tests.Performance.csproj --no-build

- name: Fail on Regression
  if: failure()
  run: echo "Performance regression detected!"
```

### Local Pre-commit Hook
```bash
#!/bin/bash
dotnet test GenHub.Tests/GenHub.Tests.Performance/GenHub.Tests.Performance.csproj
if [ $? -ne 0 ]; then
    echo "Performance regression detected. Commit blocked."
    exit 1
fi
```

## Test Data

Tests automatically create and clean up test data in the `TestData/Performance` directory:
- Random binary files for MD5 hashing tests
- TGA images for conversion tests
- Cache files for serialization tests

All test data is cleaned up after each test run.

## Interpreting Results

### Test Passes
```
✓ MD5Hashing_100Files_ShouldNotRegress (2.3s)
  Actual: 2300ms < Max Allowed: 2750ms (baseline: 2500ms)
```

### Test Fails (Regression Detected)
```
✗ MD5Hashing_100Files_ShouldNotRegress (3.1s)
  MD5 hashing regressed: 3100ms > 2750ms (baseline: 2500ms, max regression: 10%)
```

## Performance Optimization History

Track major optimizations here:

### Week 3 Optimizations (2026-03-18)
- **MD5 Hashing**: 2.5s baseline established
  - Streaming with 64KB buffer
  - Modification time caching

- **Cache Serialization**: 200ms baseline established
  - MessagePack format (10x faster than JSON)
  - Pre-allocated dictionary capacity

- **Parallel Processing**: 800ms baseline established
  - Task.WhenAll for concurrent MD5 hashing
  - 3x speedup over sequential processing

## Troubleshooting

### Tests Failing Locally
1. Check if you have pending changes that affect performance
2. Verify test data directory has write permissions
3. Run tests individually to isolate the issue

### Baselines Too Strict
If tests fail on slower hardware:
1. Consider environment-specific baselines
2. Use percentile-based metrics instead of absolute times
3. Run tests on CI/CD environment for consistency

### False Positives
If tests occasionally fail due to system load:
1. Run tests multiple times and use average
2. Increase `MaxRegressionPercent` temporarily
3. Use dedicated test environment

## Contributing

When adding new performance tests:
1. Establish baseline from 3+ test runs
2. Use realistic test data sizes
3. Document what the test measures
4. Add cleanup logic in `Dispose()`
5. Update this README with the new test

## References

- [Week 3 Optimization Report](../../docs/ModBuilder_Week3_Optimizations.md)
- [Benchmark Results](../../docs/ModBuilder_Benchmarks.md)
- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions](https://fluentassertions.com/)
