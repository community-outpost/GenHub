---
title: API Reference
description: Complete API reference for GeneralsHub components, patterns, and development interfaces
---

# API Reference

Welcome to the GeneralsHub API Reference.  
This section provides comprehensive documentation for GeneralsHub's APIs, core
patterns, domain models, and development tools. It is intended for developers
extending, integrating, or contributing to the GeneralsHub ecosystem.

---

## Core Components

### Result Pattern

GeneralsHub uses a consistent [Result pattern](./result-pattern) for handling
operations that may succeed or fail. This ensures standardized error handling
and predictable outcomes across the codebase.

- `ResultBase`: Base class for all result types  
- `OperationResult<T>`: Generic result for operations that return data  
- Domain-specific results: `LaunchResult`, `ValidationResult`,
  `UpdateCheckResult`, etc.

```csharp
// Create successful result
OperationResult<T>.CreateSuccess(data, elapsed);

// Create failed result
OperationResult<T>.CreateFailure(error, elapsed);
```

---

### Models

[Data models](./models) represent domain objects, configuration classes, and
result types used throughout the system.

Examples include:

- `GameInstallation`, `GameClient`, `ContentManifest`
- `GameProfile`, `Workspace`, `LaunchConfiguration`

---

### Constants

[Application constants](./constants) provide centralized configuration and
eliminate magic strings:

- `ApiConstants`: API-related values (GitHub, user agents)  
- `AppConstants`: Application-wide metadata  
- `DownloadDefaults`, `TimeIntervals`, `DirectoryNames`  
- `StorageConstants`, `ProcessConstants`, etc.

```csharp
client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
client.Timeout = TimeIntervals.DownloadTimeout;
```

---

## Architecture

### Dependency Injection

GeneralsHub uses `Microsoft.Extensions.DependencyInjection` for service
registration and resolution:

```csharp
// Register services
builder.Services.AddSingleton<IMyService, MyService>();
builder.Services.AddScoped<IMyScopedService, MyScopedService>();

// Resolve services
var service = serviceProvider.GetRequiredService<IMyService>();
```

---

### Configuration System

Configuration is layered for flexibility and safety:

1. **Application Configuration (`IAppConfiguration`)**  
   Defaults from `appsettings.json` and environment variables
2. **User Settings (`IUserSettingsService`)**  
   User preferences persisted between sessions
3. **Unified Configuration (`IConfigurationProviderService`)**  
   Combines defaults with user overrides

```csharp
public class ConfigurationProviderService : IConfigurationProviderService
{
    public string GetWorkspacePath()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.WorkspacePath)))
            return settings.WorkspacePath;

        return _appConfig.GetDefaultWorkspacePath();
    }
}
```

---

### Logging

Structured logging is provided via `Microsoft.Extensions.Logging`:

```csharp
public class MyService(ILogger<MyService> logger)
{
    private readonly ILogger<MyService> _logger = logger;

    public async Task DoWorkAsync()
    {
        _logger.LogInformation("Starting work operation");
        try
        {
            // Do work
            _logger.LogInformation("Work completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Work failed with error");
            throw;
        }
    }
}
```

---

## Development Tools

### Testing

GeneralsHub includes comprehensive unit and integration tests:

- **xUnit**: Testing framework  
- **FluentAssertions**: Readable assertions  
- **Moq**: Mocking dependencies  

---

## Related Documentation

- [Architecture Overview](../architecture.md)  
- [Developer Onboarding](../onboarding.md)  
- [System Flowcharts](../FlowCharts/)  
