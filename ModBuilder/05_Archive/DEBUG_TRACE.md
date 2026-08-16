# ModBuilder File Resolution Debug Trace

## Executive Summary

**Root Cause Identified**: The JSON config files use a simplified format that doesn't match the C# model structure. The ConfigurationLoaderService attempts to deserialize directly without proper conversion.

## Issue Breakdown

### Issue 1: Config Format Mismatch

**Sample Config** (`ModBundleItems.json`):
```json
{
  "BundleItems": [
    {
      "Name": "SampleTextures",
      "SourceFiles": [
        "GameFilesEdited/Art/Textures/**/*.tga"
      ],
      "OutputFormat": "DDS",
      "Compression": "DXT5"
    }
  ]
}
```

**Expected C# Structure** (`BuildConfiguration`):
```csharp
{
  "items": [
    {
      "name": "SampleTextures",
      "files": [
        {
          "absSourceParent": "Z:\\path\\to\\project",
          "absSourceFile": "GameFilesEdited/Art/Textures/**/*.tga",
          "relTargetFile": "Art/Textures/**/*.tga",
          "params": { "format": "DDS", "compression": "DXT5" }
        }
      ]
    }
  ]
}
```

**Problem**: The JSON property names don't match:
- `BundleItems` vs `items`
- `SourceFiles` (string array) vs `files` (BundleFile array)
- Missing `absSourceParent`, `absSourceFile`, `relTargetFile` structure
- `OutputFormat`/`Compression` need to be converted to `params`

### Issue 2: Wildcard Resolution Fails

**Current Flow**:
1. ConfigurationLoaderService.LoadConfigurationAsync() deserializes JSON
2. Deserialization fails because property names don't match
3. Returns empty/invalid configuration
4. ResolveWildcardsAsync() has no items to process
5. Result: "Resolved 0 files from wildcard patterns"

**Evidence from Code**:
- Line 65 in ConfigurationLoaderService.cs: Direct deserialization without conversion
- Line 148-209: ResolveWildcardsAsync() expects items with files already populated
- No conversion logic for simplified JSON format

### Issue 3: Project Loading Error

**BasicMod.mbproj**:
```json
{
  "name": "BasicMod",
  "version": "1.0.0",
  "author": "Sample Project",
  "description": "A basic sample project demonstrating ModBuilder functionality",
  "directories": {
    "config": "config",
    "output": ".Release"
  }
}
```

**Problems**:
1. Missing `configFiles` or `bundleConfigs` array to specify which config files to load
2. Project loader doesn't auto-discover `config/ModBundleItems.json` and `config/ModBundlePacks.json`
3. No default config file discovery logic

### Issue 4: File Count Shows 5

**Actual Files in Project**:
```
Z:\GeneralsHub\SampleProjects\ModBuilder\BasicMod/
├── BasicMod.mbproj (1 file)
├── README.md (1 file)
├── config/
│   ├── ModBundleItems.json (1 file)
│   └── ModBundlePacks.json (1 file)
└── GameFilesEdited/
    └── Art/
        └── Textures/
            └── sample.tga (1 file) ← ACTUAL GAME FILE
```

**Total**: 5 files (4 project files + 1 game file)

**Problem**: The build system is counting all files in the project directory instead of just the files specified in the bundle configuration.

## Required Fixes

### Fix 1: Add Simplified Config Format Parser

Create a new parser that converts the simplified JSON format to the full BuildConfiguration structure:

**Location**: `ConfigurationLoaderService.cs`

**New Method**:
```csharp
private BuildConfiguration ConvertSimplifiedConfig(SimplifiedConfig simplified, string projectDir)
{
    var config = new BuildConfiguration();

    // Convert BundleItems
    if (simplified.BundleItems != null)
    {
        foreach (var item in simplified.BundleItems)
        {
            var bundleItem = new BundleItem { Name = item.Name };

            // Convert SourceFiles to BundleFile entries
            foreach (var sourceFile in item.SourceFiles ?? new List<string>())
            {
                bundleItem.Files.Add(new BundleFile
                {
                    AbsSourceParent = projectDir,
                    AbsSourceFile = sourceFile,
                    RelTargetFile = sourceFile,
                    Params = new Dictionary<string, object>
                    {
                        ["format"] = item.OutputFormat ?? "DDS",
                        ["compression"] = item.Compression ?? "DXT5"
                    }
                });
            }

            config.Items.Add(bundleItem);
        }
    }

    // Convert BundlePacks
    if (simplified.BundlePacks != null)
    {
        foreach (var pack in simplified.BundlePacks)
        {
            config.Packs.Add(new BundlePack
            {
                Name = pack.Name,
                ItemNames = pack.Items ?? new List<string>()
            });
        }
    }

    return config;
}
```

**New Models**:
```csharp
private class SimplifiedConfig
{
    [JsonPropertyName("BundleItems")]
    public List<SimplifiedBundleItem>? BundleItems { get; set; }

    [JsonPropertyName("BundlePacks")]
    public List<SimplifiedBundlePack>? BundlePacks { get; set; }
}

private class SimplifiedBundleItem
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("SourceFiles")]
    public List<string>? SourceFiles { get; set; }

    [JsonPropertyName("OutputFormat")]
    public string? OutputFormat { get; set; }

    [JsonPropertyName("Compression")]
    public string? Compression { get; set; }
}

private class SimplifiedBundlePack
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("Items")]
    public List<string>? Items { get; set; }

    [JsonPropertyName("OutputFile")]
    public string? OutputFile { get; set; }
}
```

### Fix 2: Update LoadConfigurationAsync

**Location**: `ConfigurationLoaderService.cs`, line 38-91

**Change**:
```csharp
public async Task<BuildConfiguration> LoadConfigurationAsync(string configPath, CancellationToken cancellationToken = default)
{
    try
    {
        _logger.LogInformation("Loading configuration from: {ConfigPath}", configPath);

        if (!File.Exists(configPath))
        {
            _logger.LogError("Configuration file not found: {ConfigPath}", configPath);
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }

        var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var projectDir = Path.GetDirectoryName(configPath) ?? string.Empty;

        // Go up one level if config is in subdirectory
        if (Path.GetFileName(projectDir).Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            projectDir = Path.GetDirectoryName(projectDir) ?? projectDir;
        }

        BuildConfiguration config;

        // Try Python format first
        var pythonConfig = JsonSerializer.Deserialize<PythonConfigRoot>(json, _jsonOptions);
        if (pythonConfig?.Bundles != null)
        {
            _logger.LogInformation("Detected Python ModBuilder config format");
            config = ConvertPythonConfig(pythonConfig.Bundles, projectDir);
        }
        // Try simplified format (ModBundleItems.json / ModBundlePacks.json)
        else
        {
            var simplifiedConfig = JsonSerializer.Deserialize<SimplifiedConfig>(json, _jsonOptions);
            if (simplifiedConfig?.BundleItems != null || simplifiedConfig?.BundlePacks != null)
            {
                _logger.LogInformation("Detected simplified config format");
                config = ConvertSimplifiedConfig(simplifiedConfig, projectDir);
            }
            // Try direct C# format
            else
            {
                config = JsonSerializer.Deserialize<BuildConfiguration>(json, _jsonOptions);
                if (config == null)
                {
                    _logger.LogError("Failed to deserialize configuration from: {ConfigPath}", configPath);
                    throw new InvalidOperationException($"Failed to deserialize configuration from: {configPath}");
                }
            }
        }

        config.LoadedConfigFiles.Add(configPath);

        _logger.LogInformation("Successfully loaded configuration with {ItemCount} items and {PackCount} packs",
            config.Items.Count, config.Packs.Count);

        return config;
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "JSON parsing error in configuration file: {ConfigPath}", configPath);
        throw new InvalidOperationException($"Invalid JSON in configuration file: {configPath}", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to load configuration: {ConfigPath}", configPath);
        throw;
    }
}
```

### Fix 3: Add Auto-Discovery for Config Files

**Location**: Create new method in project loading service

**New Method**:
```csharp
private List<string> DiscoverConfigFiles(string projectDir)
{
    var configFiles = new List<string>();
    var configDir = Path.Combine(projectDir, "config");

    if (Directory.Exists(configDir))
    {
        // Look for standard config files
        var itemsFile = Path.Combine(configDir, "ModBundleItems.json");
        var packsFile = Path.Combine(configDir, "ModBundlePacks.json");

        if (File.Exists(itemsFile))
        {
            configFiles.Add(itemsFile);
            _logger.LogInformation("Auto-discovered config file: {File}", itemsFile);
        }

        if (File.Exists(packsFile))
        {
            configFiles.Add(packsFile);
            _logger.LogInformation("Auto-discovered config file: {File}", packsFile);
        }
    }

    return configFiles;
}
```

### Fix 4: Enhanced Logging for Debugging

Add comprehensive logging at each step:

1. **Config Loading**:
   ```csharp
   _logger.LogDebug("Raw JSON content: {Json}", json.Substring(0, Math.Min(500, json.Length)));
   _logger.LogDebug("Attempting to deserialize as format: {Format}", "Simplified");
   ```

2. **Wildcard Resolution**:
   ```csharp
   _logger.LogDebug("Processing item '{Name}' with {FileCount} file patterns", item.Name, item.Files.Count);
   foreach (var file in item.Files)
   {
       _logger.LogDebug("  Pattern: {Pattern}, Parent: {Parent}", file.AbsSourceFile, file.AbsSourceParent);
   }
   ```

3. **File Discovery**:
   ```csharp
   _logger.LogDebug("Searching for pattern '{Pattern}' in '{Dir}'", normalizedPattern, basePath);
   _logger.LogDebug("Found {Count} matching files", matchedFiles.Count);
   foreach (var match in matchedFiles)
   {
       _logger.LogDebug("  Matched: {File}", match);
   }
   ```

## Testing Plan

### Test 1: Config Format Detection
1. Load `ModBundleItems.json`
2. Verify it's detected as simplified format
3. Verify conversion to BuildConfiguration
4. Check that items and files are populated

### Test 2: Wildcard Resolution
1. Load converted configuration
2. Call ResolveWildcardsAsync()
3. Verify pattern `GameFilesEdited/Art/Textures/**/*.tga` matches `sample.tga`
4. Check that resolved file has correct paths

### Test 3: Project Loading
1. Load `BasicMod.mbproj`
2. Verify auto-discovery finds config files
3. Verify configs are loaded and merged
4. Check final configuration has all items and packs

### Test 4: Build Pipeline
1. Execute full build
2. Verify file count is 1 (only sample.tga)
3. Verify file is processed through conversion pipeline
4. Check output in .Release directory

## Expected Results After Fixes

1. **Config Loading**: Successfully loads and converts simplified format
2. **Wildcard Resolution**: Finds 1 file matching `**/*.tga` pattern
3. **Build Pipeline**: Processes 1 file through DDS conversion
4. **Output**: Creates `.Release/BasicMod.big` with converted texture

## Implementation Priority

1. **HIGH**: Fix 1 - Add simplified config parser (blocks everything)
2. **HIGH**: Fix 2 - Update LoadConfigurationAsync (required for Fix 1)
3. **MEDIUM**: Fix 3 - Add auto-discovery (improves UX)
4. **LOW**: Fix 4 - Enhanced logging (helps debugging)

## Next Steps

1. Implement Fix 1 and Fix 2 together (they're interdependent)
2. Test with BasicMod sample project
3. Verify wildcard resolution works
4. Implement Fix 3 for better project loading
5. Add Fix 4 for ongoing debugging support
