---
title: API Reference
description: Complete API reference for GenHub components, patterns, and interfaces
---

# API Reference

Welcome to the GenHub API Reference. This comprehensive guide covers all the key components, patterns, and interfaces used throughout the GenHub application.

## Overview

GenHub is built using modern C# patterns and architectures. The API reference is organized into logical sections covering constants, core components, result patterns, and domain-specific functionality.

## Core Components

### Constants System

The constants system provides centralized configuration and eliminates magic strings throughout the codebase.

#### [Constants API Reference](./constants)

- **ApiConstants**: Network and API-related constants including GitHub integration and user agents
- **AppConstants**: Application-wide settings and metadata
- **CasDefaults**: Content-Addressable Storage configuration defaults
- **ConfigurationKeys**: Configuration file keys and paths
- **ConversionConstants**: Unit conversion constants
- **DirectoryNames**: Standard directory naming conventions
- **DownloadDefaults**: Download operation defaults and limits
- **FileTypes**: File extensions and naming patterns
- **IoConstants**: Input/output operation constants
- **ProcessConstants**: System process and exit code constants
- **StorageConstants**: Storage and CAS operation constants
- **TimeIntervals**: Time spans and intervals
- **UiConstants**: UI-related constants

**Key Features:**

- Centralized constant management
- Dynamic user agent construction: `${AppConstants.AppName}/${AppConstants.AppVersion}`
- Comprehensive test coverage (499 tests)
- StyleCop compliant organization

### Result Pattern

GenHub uses a consistent Result pattern for handling operations that may succeed or fail.

#### [Result Pattern Documentation](./result-pattern)

**Core Components:**

- `ResultBase`: Base class for all result types
- `OperationResult<T>`: Generic result for operations that return data
- Specific result types for different domains

**Key Result Types:**

- `LaunchResult`: Game launch operation results
- `ValidationResult`: Validation operation results
- `UpdateCheckResult`: Update check operation results
- `DetectionResult<T>`: Generic detection operation results
- `DownloadResult`: File download operation results
- `GitHubUrlParseResult`: GitHub URL parsing results
- `CasGarbageCollectionResult`: CAS cleanup results
- `CasValidationResult`: CAS integrity validation results

**Factory Methods:**

```csharp
// Create successful result
OperationResult<T>.CreateSuccess(data, elapsed)

// Create failed result
OperationResult<T>.CreateFailure(error, elapsed)
```

## Domain Components

### Game Installation System

**Primary Responsibility**: Detection and cataloging of physical game installations across different platforms.

**Key Interfaces:**

- `IGameInstallationDetectionOrchestrator`: Master coordinator for all platform detectors
- `IGameInstallationDetector`: Platform-specific detection contracts
- `IGameInstallationValidator`: Installation validation
- `IGameInstallationService`: High-level installation management

**Data Models:**

- `GameInstallation`: Core installation data (path, type, game variants)
- `GameInstallationType`: Steam, EA App, Origin, Manual installations

### Game Version System

**Primary Responsibility**: Identification and categorization of specific game executables and modifications.

**Key Interfaces:**

- `IGameVersionDetectionOrchestrator`: Coordinates version detection
- `IGameVersionDetector`: Installation analysis for executable variants
- `IGameVersionValidator`: Executable verification

**Data Models:**

- `GameVersion`: Executable variant data (ID, name, path, launch config)
- `GameType`: Generals vs Zero Hour variants

### Content Manifest System

**Primary Responsibility**: Declarative description of installable content packages.

**Key Interfaces:**

- `IContentManifestBuilder`: Manifest construction
- `IContentManifestValidator`: Manifest validation
- `IContentManifestService`: Manifest management

**Data Models:**

- `ContentManifest`: Declarative content package description
- `ContentItem`: Individual content components
- `ContentDependency`: Content relationships

### Game Profile System

**Primary Responsibility**: User configuration and customization management.

**Key Interfaces:**

- `IGameProfileService`: Profile management
- `IGameProfileValidator`: Profile validation
- `IGameProfileFactory`: Profile creation

**Data Models:**

- `GameProfile`: User configuration container
- `ProfileSettings`: Configuration options
- `LaunchConfiguration`: Launch parameters

### Workspace System

**Primary Responsibility**: Isolated execution environment management.

**Key Interfaces:**

- `IWorkspaceService`: Workspace lifecycle management
- `IWorkspaceFactory`: Workspace creation
- `IWorkspaceValidator`: Workspace validation

**Data Models:**

- `Workspace`: Isolated execution context
- `WorkspaceStrategy`: Copy vs Symlink strategies
- `WorkspaceItem`: Individual workspace components

### Content Pipeline

**Primary Responsibility**: Orchestration of content discovery, resolution, and delivery.

**Key Interfaces:**

- `IContentOrchestrator`: Pipeline master coordinator
- `IContentProvider`: Content source abstraction
- `IContentDiscoverer`: Content discovery
- `IContentResolver`: Content resolution
- `IContentDeliverer`: Content delivery

**Pipeline Flow:**

1. **Discovery**: Find available content from various sources
2. **Resolution**: Transform discovered content into manifests
3. **Delivery**: Prepare content for workspace assembly

### Launching System

**Primary Responsibility**: Runtime orchestration and game execution.

**Key Interfaces:**

- `IGameLauncher`: Launch orchestration
- `ILaunchValidator`: Pre-launch validation
- `ILaunchMonitor`: Runtime monitoring

**Data Models:**

- `LaunchResult`: Launch operation outcome
- `LaunchConfiguration`: Launch parameters
- `ProcessInfo`: Runtime process information

## Infrastructure Components

### Dependency Injection

**Primary Responsibility**: Service registration and dependency management.

**Key Modules:**

- `AppUpdateModule`: Update system services
- `ContentModule`: Content pipeline services
- `DownloadModule`: Download services
- `GameInstallationModule`: Installation detection services
- `StorageModule`: Storage and CAS services

### Configuration System

**Primary Responsibility**: Application configuration management.

**Key Interfaces:**

- `IAppConfiguration`: Application-level settings
- `IConfigurationProviderService`: Configuration providers
- `IUserSettingsService`: User preferences

**Configuration Sources:**

- `appsettings.json`: Deployment configuration
- User settings files: Runtime preferences
- Environment variables: Environment-specific overrides

### Storage System

**Primary Responsibility**: Content-Addressable Storage (CAS) management.

**Key Interfaces:**

- `ICasStorage`: Low-level CAS operations
- `ICasService`: High-level CAS management
- `ICasValidator`: CAS integrity validation
- `ICasMaintenanceService`: CAS maintenance operations

**Features:**

- Atomic operations
- Integrity verification
- Garbage collection
- Concurrent access safety

## Usage Examples

### Basic Service Usage

```csharp
// Using dependency injection
public class GameService(IGameInstallationService installationService)
{
    public async Task<IEnumerable<GameInstallation>> GetInstallationsAsync()
    {
        var result = await installationService.DetectInstallationsAsync();
        return result.Success ? result.Data : Enumerable.Empty<GameInstallation>();
    }
}
```

### Result Pattern Implementation

```csharp
public async Task<OperationResult<GameProfile>> CreateProfileAsync(GameProfileRequest request)
{
    try
    {
        // Validate request
        var validation = await _validator.ValidateAsync(request);
        if (!validation.Success)
        {
            return OperationResult<GameProfile>.CreateFailure(validation.Errors);
        }

        // Create profile
        var profile = await _profileFactory.CreateAsync(request);
        return OperationResult<GameProfile>.CreateSuccess(profile);
    }
    catch (Exception ex)
    {
        return OperationResult<GameProfile>.CreateFailure($"Profile creation failed: {ex.Message}");
    }
}
```

### Constants Usage

```csharp
// Using centralized constants
public class DownloadService
{
    private readonly TimeSpan _timeout = TimeIntervals.DownloadTimeout;
    private readonly int _bufferSize = DownloadDefaults.BufferSizeBytes;

    public async Task<DownloadResult> DownloadFileAsync(string url, string destination)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
        client.Timeout = _timeout;

        // Download logic...
    }
}
```

## Best Practices

### Service Design

1. **Interface Segregation**: Use specific interfaces rather than generic ones
2. **Dependency Injection**: Always inject dependencies through constructors
3. **Async/Await**: Use async patterns for I/O operations
4. **Result Pattern**: Return results for operations that may fail

### Error Handling

1. **Meaningful Messages**: Provide descriptive error messages
2. **Exception Conversion**: Convert exceptions to appropriate result failures
3. **Logging**: Log errors with appropriate severity levels
4. **Recovery**: Implement retry logic where appropriate

### Testing

1. **Unit Tests**: Test individual components in isolation
2. **Integration Tests**: Test component interactions
3. **Mock Dependencies**: Use mocks for external dependencies
4. **Result Validation**: Test both success and failure paths

## Related Documentation

- [Architecture Overview](../architecture.md)
- [Developer Onboarding](../onboarding.md)
- [System Flowcharts](../FlowCharts/)
- [Configuration Guide](../configuration.md)

## Contributing

When adding new API components:

1. **Follow established patterns**: Use Result pattern for operations that may fail
2. **Add comprehensive tests**: Ensure all public methods are tested
3. **Update documentation**: Add API documentation following this format
4. **Use dependency injection**: Register services in appropriate modules
5. **Follow naming conventions**: Use clear, descriptive names
6. **Add constants**: Centralize magic strings and numbers in appropriate constant files
