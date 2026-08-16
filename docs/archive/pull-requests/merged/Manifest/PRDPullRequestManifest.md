# PRD: Manifest System Foundation

## 1. Goal

To implement a robust, extensible, and publisher-ready manifest system that will serve as the backbone for all content management within GenHub. This system must be capable of discovering and caching manifests from multiple sources (embedded, file system), providing them efficiently to the rest of the application, and ensuring basic security and data integrity through validation. This PR establishes this foundation and includes comprehensive test coverage.

## 2. Architectural Solution

The system is architected into four cooperating components with a clear separation of concerns:

1. **`ManifestDiscoveryService` (The Loader):** Responsible for finding and loading manifest files from all supported sources (embedded, file system).
2. **`IManifestCache` (The State):** A singleton service acting as the central, in-memory repository for all loaded manifests.
3. **`IManifestProvider` (The Server):** A fast, lightweight facade over the `IManifestCache` that provides manifests to other services. It includes security validation to prevent path traversal attacks.
4. **`ManifestInitializationService` (The Initializer):** An `IHostedService` that ensures the manifest discovery process is triggered automatically at application startup.

## 3. Files Added / Modified

### Core Project (`GenHub.Core`)

* `Models/Enums/ContentType.cs` (new)
* `Models/Enums/ManifestFileSourceType.cs` (new)
* `Models/Enums/WorkspaceStrategy.cs` (new)
* `Models/Manifest/ContentDependency.cs` (new)
* `Models/Manifest/ContentMetadata.cs` (new)
* `Models/Manifest/FilePermissions.cs` (new)
* `Models/Manifest/GameManifest.cs` (new)
* `Models/Manifest/InstallationInstructions.cs` (new)
* `Models/Manifest/InstallationStep.cs` (new)
* `Models/Manifest/ManifestFile.cs` (new)
* `Models/Manifest/PublisherInfo.cs` (new)
* `Interfaces/Manifest/IContentManifestBuilder.cs` (new)
* `Interfaces/Manifest/IManifestCache.cs` (new)
* `Interfaces/Manifest/IManifestGenerationService.cs` (new)
* `Interfaces/Manifest/IManifestProvider.cs` (new)

### Infrastructure Project (`GenHub.Infrastructure`)

* `Exceptions/ManifestExceptions.cs` (new)

### Main Application (`GenHub`)

* `Features/Manifest/ContentManifestBuilder.cs` (new)
* `Features/Manifest/ManifestCache.cs` (new)
* `Features/Manifest/ManifestDiscoveryService.cs` (new)
* `Features/Manifest/ManifestGenerationService.cs` (new)
* `Features/Manifest/ManifestInitializationService.cs` (new)
* `Features/Manifest/ManifestProvider.cs` (new)
* `Infrastructure/DependencyInjection/AppServices.cs` (modified)
* `Infrastructure/DependencyInjection/ManifestModule.cs` (new)
* `Directory.Packages.props` (modified)
* `GenHub.csproj` (modified)

### Test Project (`GenHub.Tests`)

* `Features/Manifest/ContentManifestBuilderTests.cs` (new)
* `Features/Manifest/ManifestCacheTests.cs` (new)
* `Features/Manifest/ManifestDiscoveryServiceTests.cs` (new)
* `Features/Manifest/ManifestProviderTests.cs` (new)

## 4. Git Commit Strategy

The following git commands will structure the changes into a clean, logical history for the pull request.

```powershell
# Start from the main branch
git checkout main
git pull

# Create the feature branch
git checkout -b feat/manifest-system-foundation

# --- Commit 1: Core Models ---
# Description: Defines the data models for the manifest system.
git add GenHub.Core/Models/
git commit -m "feat(core): Add data models for manifest system"

# --- Commit 2: Core Interfaces ---
# Description: Defines the service interfaces for the manifest system.
git add GenHub.Core/Interfaces/Manifest/
git commit -m "feat(core): Add service interfaces for manifest system"

# --- Commit 3: Feature Implementation ---
# Description: Provides the concrete implementations for all manifest services.
git add Features/Manifest/
git commit -m "feat(manifest): Implement discovery, cache, provider, and generation services"

# --- Commit 4: Infrastructure and DI ---
# Description: Adds exception types and sets up DI registration for all manifest services.
git add Infrastructure/ ../Directory.Packages.props GenHub.csproj
git commit -m "refactor(infra): Add manifest exceptions and DI module"

# --- Commit 5: Unit Tests ---
# Description: Adds comprehensive unit tests for all new services and models.
git add GenHub.Tests/Features/Manifest/
git commit -m "test(manifest): Add unit tests for manifest system foundation"

# --- Push the branch to remote ---
# git push --set-upstream origin feat/manifest-system-foundation
```

## 5. Pull Request Details

**Title:** `feat(manifest): Establish core manifest system foundation`

**Description:**
This pull request introduces the foundational manifest and publisher-driven content ecosystem.

The following changes are made:

1. **Core Models & Interfaces**: Defines the data contract for the manifest system, including `GameManifest`, `ManifestFile`, and supporting enums `ManifestFileSourceType.cs`, `ContentType.cs`and `WorkspaceStrategy.cs` It also establishes the service contracts: `IManifestCache`, `IManifestProvider`, `IContentManifestBuilder`, and `IManifestGenerationService`.
2. **Service Implementations**: Provides the concrete implementations for all manifest services:
    * `ManifestDiscoveryService`: Scans for and loads manifest files from embedded resources and the file system.
    * `ManifestCache`: A singleton, in-memory cache that acts as the central repository for all discovered manifests.
    * `ManifestProvider`: A facade over the cache, providing other services with access to manifest data while enforcing security checks.
    * `ContentManifestBuilder`: A builder for creating `GameManifest` objects.
    * `ManifestGenerationService`: Orchestrates the creation of new `GameManifest` instances for base games, mods, addons, patches, and standalone versions by leveraging `IContentManifestBuilder` to scan directories, configure metadata and dependencies, add files, and serialize the resulting manifest to JSON.
    * `ManifestInitializationService`: An `IHostedService` that ensures the manifest system is initialized on application startup.
3. **Infrastructure**: Integrates services into di via `ManifestModule`. It also adds custom exception types like `ManifestNotFoundException` and `ManifestValidationException` for error handling.
4. **Unit Tests**: Introduces unit tests for the `ManifestCache`, `ManifestDiscoveryService`, and `ContentManifestBuilder`.

This commit establishes the foundational data contracts for the entire manifest system. It defines the schema for what a "manifest" is and all the supporting data structures required to describe a piece of content, its files, and its metadata.

##### **Commit 1: `feat(core): Add data models for manifest system`**

* **`GenHub.Core/Models/Manifest/GameManifest.cs`**: This is the central data model, representing a single distributable content package. The `Id` property (e.g., "Steam.ZeroHour") serves as a unique key. The `Files` property, a `List<ManifestFile>`, contains every file required for the content to run. The `RequiredDirectories` list ensures the launcher creates necessary folder structures. The `Publisher` property holds a `PublisherInfo` object for metadata, while `InstallationInstructions` provides a guide for complex setups.

* **`GenHub.Core/Models/Manifest/ManifestFile.cs`**: This model represents a single file within the `GameManifest`. The `RelativePath` property defines its location within the game's root directory. `Size` and `Hash` (SHA256) are critical for the upcoming Validation System to verify file integrity. The most important property is `SourceType`, a `ManifestFileSourceType` enum, which dictates how the launcher should acquire this file when building a workspace. For remote files, the `DownloadUrl` property specifies where to fetch the content from.

* **`GenHub.Core/Models/Enums/ContentType.cs`**: This enum categorizes the type of content a `GameManifest` represents. For example, `BaseGame` is used for a manifest describing the original, unmodified game. `Mod` is for a total conversion, and `Patch` is for a set of balance changes. This allows the launcher to understand the nature of the content and apply different logic, such as determining dependencies.

* **`GenHub.Core/Models/Enums/ManifestFileSourceType.cs`**: This is the most critical enum for the workspace creation logic. It tells the launcher how to handle each `ManifestFile`. `LinkFromBase` instructs the launcher to create a symbolic link to the file in the user's base game installation, saving significant disk space. `CopyUnique` is for files specific to the mod that must be copied. `Download` is for optional or large files hosted remotely. `Generate` is for files that need to be created on the fly, like a patched executable or a configuration file.

* **Other Models (`PublisherInfo.cs`, `ContentDependency.cs`, etc.)**: These are simple data-carrying models that provide structured metadata within the `GameManifest`. `PublisherInfo` contains fields like `Name` and `Website`. `ContentDependency` allows a manifest to declare that it requires another manifest to be present (e.g., a sub-mod requiring a main mod).

##### **Commit 2: `feat(core): Add service interfaces for manifest system`**

This commit defines the abstract contracts for the services that will operate on the data models defined in the previous commit.

* **`GenHub.Core/Interfaces/Manifest/IManifestProvider.cs`**: This interface acts as the primary facade for the rest of the application to interact with the manifest system. Its main responsibility is to retrieve `GameManifest` objects. It defines `GetManifestAsync` methods that can take a `GameVersion` or `GameInstallation` object, abstracting away the logic of how and where the manifest is found (cache, embedded resources, etc.).

* **`GenHub.Core/Interfaces/Manifest/IManifestCache.cs`**: This interface defines the contract for a singleton, in-memory cache for `GameManifest` objects. It exposes methods like `AddOrUpdateManifest(GameManifest manifest)` to populate the cache and `GetManifest(string manifestId)`.

* **`GenHub.Core/Interfaces/Manifest/IContentManifestBuilder.cs`**: This interface defines a builder pattern for constructing `GameManifest` objects. It provides a chainable API with methods like `WithBasicInfo(...)`, `WithFile(...)`, and `WithPublisher(...)`. This is essential for the `ManifestGenerationService`, which will use this builder to create new manifests by scanning existing game directories.

* **`GenHub.Core/Interfaces/Manifest/IManifestGenerationService.cs`**: This interface defines the contract for a high-level service responsible for creating new `GameManifest` files. It will orchestrate the process of scanning a game directory, using the `IContentManifestBuilder` to assemble a `GameManifest` object, and then serializing it to a JSON file.

##### **Commit 3: `feat(manifest): Implement discovery, cache, provider, and generation services`**

This commit provides the concrete implementations for the interfaces defined in the previous commit. This is where the core logic of the manifest system resides.

* **`GenHub/Features/Manifest/ManifestProvider.cs`**: This class implements `IManifestProvider`. It first attempts to retrieve a manifest from the `IManifestCache`. If not found, it falls back to loading it from embedded application resources. It also contains security logic to validate manifest file paths.

* **`GenHub/Features/Manifest/ManifestCache.cs`**: This class implements `IManifestCache` using a `ConcurrentDictionary<string, GameManifest>` as its backing store. The use of `ConcurrentDictionary` ensures that the cache is thread-safe.

* **`GenHub/Features/Manifest/ManifestDiscoveryService.cs`**: This service is responsible for finding and loading all manifests at application startup. It scans for `.json` files in predefined directories (like a `Manifests` folder) and also discovers manifests embedded within the application's assemblies. It then uses the `IManifestCache` to store them.

* **`GenHub/Features/Manifest/ManifestInitializationService.cs`**: This is an `IHostedService` that orchestrates the startup process. When the application starts, the Host calls its `StartAsync` method, which in turn calls the `ManifestDiscoveryService` to populate the cache.

##### **Commit 4: `refactor(infra): Add manifest exceptions and DI module`**

This commit handles infrastructure concerns, including custom error handling and dependency injection setup.

* **`GenHub/Infrastructure/Exceptions/ManifestExceptions.cs`**: This file defines custom exception types like `ManifestNotFoundException` and `ManifestValidationException`.

* **`GenHub/Infrastructure/DependencyInjection/ManifestModule.cs`**: This is a dependency injection module that registers all the manifest-related services with the DI container. It registers the `ManifestCache` as a singleton, the `ManifestProvider` as a singleton, and the `ManifestInitializationService` as a hosted service.

##### **Commit 5: `test(manifest): Add unit tests for manifest system foundation`**

This commit adds unit tests for all the new services and logic.

* **`GenHub.Tests/Features/Manifest/*Tests.cs`**: These files contain Xunit tests for each of the new services. For example, `ManifestProviderTests.cs` uses Moq to mock the `IManifestCache` and verifies that the provider correctly retrieves manifests from the cache. `ManifestCacheTests.cs` tests the thread-safety and correctness of the cache's add and retrieve operations. T

This work is a prerequisite for all subsequent features, including validation, workspace management, and content installation.

## 6. Security Features

The manifest system includes several security measures:

* **Path Traversal Protection**: Validates all file paths to prevent `../` attacks
* **Absolute Path Prevention**: Rejects manifests with absolute file paths
* **Input Validation**: Comprehensive validation of manifest data structure
* **Exception Handling**: Robust error handling with custom exception types

## 7. Testing Strategy

Comprehensive unit tests cover:

* All public APIs and methods
* Error conditions and edge cases
* Security validation scenarios
* Thread safety for concurrent operations
* Integration between services

## 8. Next Steps

This manifest system foundation enables:

1. **Validation System**: File integrity checking using manifest data
2. **Workspace Management**: Creating isolated game environments
3. **Content Installation**: Installing mods, patches, and addons
4. **Launch Management**: Starting games with proper configurations
**3. Mini–tree.md**

```
GenHub.Core/
├── Interfaces/Manifest/
│   ├── IContentManifestBuilder.cs
│   ├── IManifestCache.cs
│   ├── IManifestGenerationService.cs
│   └── IManifestProvider.cs
└── Models/
    ├── Enums/
    │   ├── ContentType.cs
    │   ├── ManifestFileSourceType.cs
    │   └── WorkspaceStrategy.cs
    └── Manifest/
        ├── ContentDependency.cs
        ├── ContentMetadata.cs
        ├── FilePermissions.cs
        ├── GameManifest.cs
        ├── InstallationInstructions.cs
        ├── InstallationStep.cs
        ├── ManifestFile.cs
        └── PublisherInfo.cs
GenHub/
├── Features/Manifest/
│   ├── ContentManifestBuilder.cs
│   ├── ManifestCache.cs
│   ├── ManifestDiscoveryService.cs
│   ├── ManifestGenerationService.cs
│   └── ManifestProvider.cs
└── Infrastructure/DependencyInjection/
    ├── ManifestModule.cs
    └── AppServices.cs              (← modified)
```

---
---
