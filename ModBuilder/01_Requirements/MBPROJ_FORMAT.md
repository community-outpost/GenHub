# ModBuilder Project File Format (.mbproj)

## Overview
The `.mbproj` file is a JSON-based configuration file that defines a ModBuilder project. It contains project metadata, directory structure, bundle configurations, and build settings.

## File Format Version
Current version: `1.0`

## File Structure

```json
{
  "version": "1.0",
  "name": "MyMod",
  "description": "A sample mod for Command & Conquer Generals",
  "gameInstallationId": "installation-guid-here",
  "createdAt": "2026-03-17T10:30:00Z",
  "lastModified": "2026-03-17T15:45:00Z",
  "lastBuild": "2026-03-17T15:30:00Z",
  "projectVersion": "1.0.0",
  "author": "ModAuthor",
  "directories": {
    "configs": "Configs",
    "gameFilesEdited": "GameFilesEdited",
    "build": ".Build",
    "release": ".Release"
  },
  "bundleConfigs": [
    "bundles.json",
    "advanced_bundles.json"
  ],
  "metadata": {
    "customKey1": "customValue1",
    "customKey2": "customValue2"
  }
}
```

## Field Descriptions

### Root Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `version` | string | Yes | Project file format version (currently "1.0") |
| `name` | string | Yes | Project name |
| `description` | string | No | Project description |
| `gameInstallationId` | string | No | GUID of the associated game installation |
| `createdAt` | datetime | Yes | UTC timestamp when project was created |
| `lastModified` | datetime | Yes | UTC timestamp when project was last modified |
| `lastBuild` | datetime | No | UTC timestamp of the last successful build |
| `projectVersion` | string | Yes | Semantic version of the mod (e.g., "1.0.0") |
| `author` | string | No | Mod author name |
| `directories` | object | Yes | Directory structure configuration |
| `bundleConfigs` | array | Yes | List of bundle configuration files |
| `metadata` | object | No | Custom key-value metadata |

### Directories Object

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `configs` | string | Yes | "Configs" | Relative path to bundle config JSONs |
| `gameFilesEdited` | string | Yes | "GameFilesEdited" | Relative path to modified game files |
| `build` | string | Yes | ".Build" | Relative path to build cache |
| `release` | string | Yes | ".Release" | Relative path to release archives |

## Project Directory Structure

When a project is created, the following directory structure is generated:

```
MyMod/
├── MyMod.mbproj              # Project configuration file
├── Configs/                  # Bundle configuration JSONs
│   ├── bundles.json
│   └── advanced_bundles.json
├── GameFilesEdited/          # Modified game files (mirrors game Data structure)
│   ├── Data/
│   │   ├── INI/
│   │   ├── Art/
│   │   └── ...
├── .Build/                   # Build cache (MD5 hashes, intermediate files)
│   └── cache.json
└── .Release/                 # Output archives (.zip, .big files)
    ├── MyMod_v1.0.0.zip
    └── checksums.txt
```

## Bundle Configuration Files

Bundle configuration files (referenced in `bundleConfigs`) define the actual mod content:

```json
{
  "bundles": [
    {
      "name": "MyMod",
      "description": "Main mod bundle",
      "items": [
        {
          "name": "CoreFiles",
          "files": [
            "Data/INI/Object/*.ini",
            "Data/INI/Weapon/*.ini"
          ]
        }
      ]
    }
  ]
}
```

## Usage Examples

### Creating a New Project

```csharp
var service = new ProjectConfigService(logger);

var result = await service.CreateProjectAsync(
    projectPath: @"C:\Mods\MyMod\MyMod.mbproj",
    projectName: "MyMod",
    gameInstallationId: "game-installation-guid",
    template: ProjectTemplates.BasicMod,
    cancellationToken: cancellationToken
);

if (result.Success)
{
    var project = result.Data;
    Console.WriteLine($"Created project: {project.Name}");
}
```

### Loading an Existing Project

```csharp
var result = await service.LoadProjectAsync(
    projectPath: @"C:\Mods\MyMod\MyMod.mbproj",
    validateIntegrity: true,
    cancellationToken: cancellationToken
);

if (result.Success)
{
    var project = result.Data;
    Console.WriteLine($"Loaded project: {project.Name}");
    Console.WriteLine($"Last build: {project.LastBuild}");
}
else if (result.HasValidationErrors)
{
    Console.WriteLine("Validation errors:");
    foreach (var error in result.ValidationErrors)
    {
        Console.WriteLine($"  - {error}");
    }
}
```

### Saving Project Changes

```csharp
project.Description = "Updated description";
project.ProjectVersion = "1.1.0";

var result = await service.SaveProjectAsync(
    projectPath: @"C:\Mods\MyMod\MyMod.mbproj",
    project: project,
    cancellationToken: cancellationToken
);
```

### Managing Recent Projects

```csharp
// Get recent projects
var recentResult = await service.GetRecentProjectsAsync(maxCount: 10);
foreach (var projectPath in recentResult.Data)
{
    Console.WriteLine(projectPath);
}

// Add to recent projects
await service.AddToRecentProjectsAsync(@"C:\Mods\MyMod\MyMod.mbproj");

// Remove from recent projects
await service.RemoveFromRecentProjectsAsync(@"C:\Mods\OldMod\OldMod.mbproj");
```

## Validation Rules

When validating a project, the following checks are performed:

1. **Directory Structure**: All required directories must exist
   - `Configs/`
   - `GameFilesEdited/`
   - `.Build/`
   - `.Release/`

2. **Bundle Configs**: All files listed in `bundleConfigs` must exist in the `Configs/` directory

3. **Required Fields**: The following fields must be present and non-empty:
   - `version`
   - `name`
   - `directories`

## Migration and Versioning

Future versions of the `.mbproj` format will include migration logic:

- Version 1.0 → 1.1: Add new fields with defaults
- Version 1.1 → 2.0: Breaking changes with migration path

The `version` field allows the system to detect and migrate older project files automatically.

## Best Practices

1. **Version Control**: Commit `.mbproj` files to version control
2. **Ignore Build Artifacts**: Add `.Build/` and `.Release/` to `.gitignore`
3. **Relative Paths**: Use relative paths in bundle configs for portability
4. **Semantic Versioning**: Follow semver for `projectVersion`
5. **Metadata**: Use the `metadata` object for custom tooling integration

## Error Handling

All operations return `ProjectOperationResult<T>` with:
- `Success`: Boolean indicating operation success
- `Data`: The result data (if successful)
- `Errors`: List of error messages
- `ValidationErrors`: List of validation-specific errors
- `Elapsed`: Time taken for the operation

Example error handling:

```csharp
var result = await service.LoadProjectAsync(projectPath);

if (!result.Success)
{
    Console.WriteLine($"Failed to load project: {result.FirstError}");

    if (result.HasValidationErrors)
    {
        Console.WriteLine("Validation errors:");
        foreach (var error in result.ValidationErrors)
        {
            Console.WriteLine($"  - {error}");
        }
    }
}
```

## Recent Projects Storage

Recent projects are stored in:
```
%APPDATA%\GeneralsHub\ModBuilder\recent_projects.json
```

Format:
```json
[
  "C:\\Mods\\MyMod\\MyMod.mbproj",
  "C:\\Mods\\AnotherMod\\AnotherMod.mbproj"
]
```

The list is automatically cleaned of non-existent projects when accessed.
