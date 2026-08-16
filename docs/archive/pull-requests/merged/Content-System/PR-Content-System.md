# Pull Request: feat/content-delivery-pipeline: Complete Content Discovery and Orchestration System

## 1. Goal
Implement a comprehensive content discovery, resolution, acquisition, and assembly pipeline that enables users to search, install, and manage C&C Generals/Zero Hour mods, patches, and content from multiple sources (local filesystem, GitHub releases, HTTP repositories) through a unified interface.

## 2. Architectural Solution
The system implements a **three-tier pipeline architecture** orchestrated by `ContentOrchestrator`:

- **Tier 1 (Orchestrator)**: `IContentOrchestrator` provides system-wide coordination and provider management
- **Tier 2 (Providers)**: `IContentProvider` implementations orchestrate source-specific pipelines
- **Tier 3 (Components)**: Specialized `IContentDiscoverer`, `IContentResolver`, and `IContentDeliverer` implementations

Key components include capability-based routing, dynamic service registration, comprehensive caching, and type-safe result handling with detailed progress reporting.

## 3. Files Added / Modified

### Core Interfaces (New)
* `GenHub.Core/Interfaces/Content/IContentOrchestrator.cs` (new)
* `GenHub.Core/Interfaces/Content/IContentDiscoverer.cs` (new)
* `GenHub.Core/Interfaces/Content/IContentProvider.cs` (new) 
* `GenHub.Core/Interfaces/Content/IContentResolver.cs` (new)
* `GenHub.Core/Interfaces/Content/IContentSource.cs` (new)
* `GenHub.Core/Interfaces/Content/IContentValidator.cs` (new)
* `GenHub.Core/Interfaces/Content/IDynamicContentCache.cs` (new)
* `GenHub.Core/Interfaces/Content/IContentDeliverer.cs` (new)
* `GenHub.Core/Interfaces/Content/IContentStorageService.cs` (new)

### Common Interfaces (New)
* `GenHub.Core/Interfaces/Common/IAppConfigurationService.cs` (new)
* `GenHub.Core/Interfaces/Common/IConfigurationProvider.cs` (new)

### Enhanced Interfaces (Modified)
* `GenHub.Core/Interfaces/Github/IGitHubApiClient.cs` (modified - added GetReleaseByTagAsync)
* `GenHub.Core/Interfaces/Manifest/IContentManifestBuilder.cs` (modified - updated method signatures)
* `GenHub.Core/Interfaces/Manifest/IGameManifestPool.cs` (modified - enhanced pooling)
* `GenHub.Core/Interfaces/Validation/IValidator.cs` (modified - generic validation)
* `GenHub.Core/Interfaces/Workspace/IFileOperationsService.cs` (modified - enhanced operations)

### Models and Enums (New/Modified)
* `GenHub.Core/Models/Content/ContentSearchQuery.cs` (new)
* `GenHub.Core/Models/Content/ContentAcquisitionProgress.cs` (new)
* `GenHub.Core/Models/Content/ContentAcquisitionPhase.cs` (new)
* `GenHub.Core/Models/Results/ContentOperationResult.cs` (new)
* `GenHub.Core/Models/Results/ContentSearchResult.cs` (new)
* `GenHub.Core/Models/Enums/ContentProviderType.cs` (new)
* `GenHub.Core/Models/Enums/ContentSortOrder.cs` (new)
* `GenHub.Core/Models/Enums/ContentSourceCapabilities.cs` (new)
* `GenHub.Core/Models/Enums/PackageType.cs` (new)
* `GenHub.Core/Models/Manifest/ExtractionConfiguration.cs` (new)
* `GenHub.Core/Models/Common/AppSettings.cs` (new)

### Updated Core Models (Modified)
* `GenHub.Core/Models/Manifest/GameManifest.cs` (modified - renamed Installation to InstallationInstructions)
* `GenHub.Core/Models/Manifest/InstallationInstructions.cs` (modified - enhanced instructions)
* `GenHub.Core/Models/Manifest/ManifestFile.cs` (modified - enhanced file handling)
* `GenHub.Core/Models/Enums/ManifestFileSourceType.cs` (modified - updated enum values)
* `GenHub.Core/Models/Enums/WorkspaceStrategy.cs` (modified - enhanced strategies)
* `GenHub.Core/Models/GitHub/GitHubRelease.cs` (modified - enhanced release model)
* `GenHub.Core/Models/Validation/ValidationIssue.cs` (modified - enhanced validation)
* `GenHub.Core/Models/Workspace/WorkspaceInfo.cs` (modified - enhanced workspace info)

### Service Implementations (New)
* `Features/Content/Services/ContentOrchestrator.cs` (new)
* `Features/Content/Services/ContentValidator.cs` (new)
* `Features/Content/Services/MemoryDynamicContentCache.cs` (new)
* `Features/Content/Services/ContentStorageService.cs` (new)

### Content Discoverers (New)
* `Features/Content/Services/ContentDiscoverers/FileSystemDiscoverer.cs` (new)
* `Features/Content/Services/ContentDiscoverers/GitHubDiscoverer.cs` (new)
* `Features/Content/Services/ContentDiscoverers/GitHubReleasesDiscoverer.cs` (new)
* `Features/Content/Services/ContentDiscoverers/CNCLabsMapDiscoverer.cs` (new)

### Content Providers (New)
* `Features/Content/Services/ContentProviders/BaseContentProvider.cs` (new)
* `Features/Content/Services/ContentProviders/GitHubContentProvider.cs` (new)
* `Features/Content/Services/ContentProviders/LocalFileSystemContentProvider.cs` (new)
* `Features/Content/Services/ContentProviders/CNCLabsContentProvider.cs` (new)
* `Features/Content/Services/ContentProviders/ModDBContentProvider.cs` (new)

### Content Resolvers (New)
* `Features/Content/Services/ContentResolvers/GitHubResolver.cs` (new)
* `Features/Content/Services/ContentResolvers/LocalManifestResolver.cs` (new)
* `Features/Content/Services/ContentResolvers/CNCLabsMapResolver.cs` (new)

### Content Deliverers (New)
* `Features/Content/Services/ContentDeliverers/FileSystemDeliverer.cs` (new)
* `Features/Content/Services/ContentDeliverers/HttpContentDeliverer.cs` (new)

### ViewModels and UI (New)
* `Features/Content/ViewModels/ContentBrowserViewModel.cs` (new)
* `Features/Content/ViewModels/ContentItemViewModel.cs` (new)

### Common Services (New)
* `Common/Services/AppConfigurationService.cs` (new)
* `Common/Services/ConfigurationProvider.cs` (new)
* `Common/Services/UserSettingsService.cs` (new)

### DI and Infrastructure (New/Modified)
* `Infrastructure/DependencyInjection/ContentDeliveryModule.cs` (new)
* `Infrastructure/DependencyInjection/ValidationModule.cs` (new)
* `Infrastructure/DependencyInjection/AppServices.cs` (modified)
* `Directory.Packages.props` (modified - added Microsoft.Extensions.Caching.Memory)
* `GenHub.csproj` (modified - package references)

### Updated Services (Modified)
* `Features/GitHub/Services/OctokitGitHubApiClient.cs` (modified - enhanced API)
* `Features/Manifest/ContentManifestBuilder.cs` (modified - updated methods)
* `Features/Manifest/GameManifestPool.cs` (modified - enhanced pooling)
* `Features/Manifest/ManifestDiscoveryService.cs` (modified - enhanced discovery)
* `Features/Manifest/ManifestGenerationService.cs` (modified - enhanced generation)
* `Features/Workspace/FileOperationsService.cs` (modified - enhanced operations)
* `Features/Workspace/WorkspaceManager.cs` (modified - enhanced management)
* `Features/Workspace/Strategies/WorkspaceStrategyBase.cs` (modified - enhanced base)
* `Features/Validation/GameInstallationValidator.cs` (modified - enhanced validation)
* `Features/Validation/GameVersionValidator.cs` (modified - enhanced validation)
* `Features/Settings/ViewModels/SettingsViewModel.cs` (modified - enhanced settings)
* `Features/Settings/Views/SettingsView.axaml` (modified - UI updates)

### Platform-Specific (Modified)
* `GenHub.Windows/Features/Workspace/WindowsFileOperationsService.cs` (modified - enhanced Windows ops)

### Updated Tests (Modified)
* `GenHub.Tests/GenHub.Tests.Core/Features/Content/BaseContentProviderTests.cs` (new)
* `GenHub.Tests/GenHub.Tests.Core/Features/Content/ContentOrchestratorTests.cs` (new)
* `GenHub.Tests/GenHub.Tests.Core/Features/Content/GitHubContentProviderTests.cs` (new)
* `GenHub.Tests/GenHub.Tests.Core/Features/Content/GitHubResolverTests.cs` (new)
* `GenHub.Tests/GenHub.Tests.Core/Features/Manifest/ContentManifestBuilderTests.cs` (modified)
* `GenHub.Tests/GenHub.Tests.Core/Features/Validation/GameInstallationValidatorTests.cs` (modified)
* `GenHub.Tests/GenHub.Tests.Core/Features/Validation/GameVersionValidatorTests.cs` (modified)
* `GenHub.Tests/GenHub.Tests.Core/Features/Workspace/FileOperationsServiceTests.cs` (modified)
* `GenHub.Tests/GenHub.Tests.Core/Features/Workspace/WorkspaceIntegrationTests.cs` (modified)
* `GenHub.Tests/GenHub.Tests.Core/Features/Workspace/WorkspaceManagerTests.cs` (modified)

## 4. Git Commit Strategy

```powershell
# 1. Start from the target integration branch
git checkout main
git pull origin main

# 2. Create your feature branch
git checkout -b feat/content-delivery-pipeline

# --- Commit 1: Core interfaces and models ---
git add GenHub.Core/Interfaces/Content/IContentOrchestrator.cs
git add GenHub.Core/Interfaces/Content/IContentDiscoverer.cs
git add GenHub.Core/Interfaces/Content/IContentProvider.cs
git add GenHub.Core/Interfaces/Content/IContentResolver.cs
git add GenHub.Core/Interfaces/Content/IContentSource.cs
git add GenHub.Core/Interfaces/Content/IContentValidator.cs
git add GenHub.Core/Interfaces/Content/IDynamicContentCache.cs
git add GenHub.Core/Interfaces/Content/IContentDeliverer.cs
git add GenHub.Core/Interfaces/Content/IContentStorageService.cs
git add GenHub.Core/Interfaces/Common/IAppConfigurationService.cs
git add GenHub.Core/Interfaces/Common/IConfigurationProvider.cs
git add GenHub.Core/Models/Content/ContentSearchQuery.cs
git add GenHub.Core/Models/Content/ContentAcquisitionProgress.cs
git add GenHub.Core/Models/Content/ContentAcquisitionPhase.cs
git add GenHub.Core/Models/Results/ContentOperationResult.cs
git add GenHub.Core/Models/Results/ContentSearchResult.cs
git add GenHub.Core/Models/Enums/ContentProviderType.cs
git add GenHub.Core/Models/Enums/ContentSortOrder.cs
git add GenHub.Core/Models/Enums/ContentSourceCapabilities.cs
git add GenHub.Core/Models/Enums/PackageType.cs
git add GenHub.Core/Models/Manifest/ExtractionConfiguration.cs
git add GenHub.Core/Models/Common/AppSettings.cs
git commit -m "feat(content): add core interfaces and models for three-tier content delivery pipeline"

# --- Commit 2: Update existing models and interfaces ---
git add GenHub.Core/Models/Manifest/GameManifest.cs
git add GenHub.Core/Models/Manifest/InstallationInstructions.cs
git add GenHub.Core/Models/Manifest/ManifestFile.cs
git add GenHub.Core/Models/Enums/ManifestFileSourceType.cs
git add GenHub.Core/Models/Enums/WorkspaceStrategy.cs
git add GenHub.Core/Models/GitHub/GitHubRelease.cs
git add GenHub.Core/Models/Validation/ValidationIssue.cs
git add GenHub.Core/Models/Workspace/WorkspaceInfo.cs
git add GenHub.Core/Interfaces/Github/IGitHubApiClient.cs
git add GenHub.Core/Interfaces/Manifest/IContentManifestBuilder.cs
git add GenHub.Core/Interfaces/Manifest/IGameManifestPool.cs
git add GenHub.Core/Interfaces/Validation/IValidator.cs
git add GenHub.Core/Interfaces/Workspace/IFileOperationsService.cs
git commit -m "feat(content): update existing models and interfaces to support content delivery features"

# --- Commit 3: Content orchestration and core services ---
git add GenHub/Features/Content/Services/ContentOrchestrator.cs
git add GenHub/Features/Content/Services/ContentValidator.cs
git add GenHub/Features/Content/Services/MemoryDynamicContentCache.cs
git add GenHub/Features/Content/Services/ContentStorageService.cs
git add GenHub/Common/Services/AppConfigurationService.cs
git add GenHub/Common/Services/ConfigurationProvider.cs
git add GenHub/Common/Services/UserSettingsService.cs
git commit -m "feat(content): implement ContentOrchestrator and core content services"

# --- Commit 4: Content discoverers ---
git add GenHub/Features/Content/Services/ContentDiscoverers/FileSystemDiscoverer.cs
git add GenHub/Features/Content/Services/ContentDiscoverers/GitHubDiscoverer.cs
git add GenHub/Features/Content/Services/ContentDiscoverers/GitHubReleasesDiscoverer.cs
git add GenHub/Features/Content/Services/ContentDiscoverers/CNCLabsMapDiscoverer.cs
git commit -m "feat(content): add content discoverers for filesystem, GitHub, and CNC Labs sources"

# --- Commit 5: Content providers with base provider pattern ---
git add GenHub/Features/Content/Services/ContentProviders/BaseContentProvider.cs
git add GenHub/Features/Content/Services/ContentProviders/GitHubContentProvider.cs
git add GenHub/Features/Content/Services/ContentProviders/LocalFileSystemContentProvider.cs
git add GenHub/Features/Content/Services/ContentProviders/CNCLabsContentProvider.cs
git add GenHub/Features/Content/Services/ContentProviders/ModDBContentProvider.cs
git commit -m "feat(content): add content providers implementing three-tier pipeline orchestration"

# --- Commit 6: Content resolvers ---
git add GenHub/Features/Content/Services/ContentResolvers/GitHubResolver.cs
git add GenHub/Features/Content/Services/ContentResolvers/LocalManifestResolver.cs
git add GenHub/Features/Content/Services/ContentResolvers/CNCLabsMapResolver.cs
git commit -m "feat(content): add content resolvers for manifest generation from discovered content"

# --- Commit 7: Content deliverers ---
git add GenHub/Features/Content/Services/ContentDeliverers/FileSystemDeliverer.cs
git add GenHub/Features/Content/Services/ContentDeliverers/HttpContentDeliverer.cs
git commit -m "feat(content): add content deliverers for file acquisition and preparation"

# --- Commit 8: UI integration ---
git add GenHub/Features/Content/ViewModels/ContentBrowserViewModel.cs
git add GenHub/Features/Content/ViewModels/ContentItemViewModel.cs
git add GenHub/Features/Settings/ViewModels/SettingsViewModel.cs
git add GenHub/Features/Settings/Views/SettingsView.axaml
git commit -m "feat(content): add content browser ViewModels and settings UI integration"

# --- Commit 9: Dependency injection and service registration ---
git add GenHub/Infrastructure/DependencyInjection/ContentDeliveryModule.cs
git add GenHub/Infrastructure/DependencyInjection/ValidationModule.cs
git add GenHub/Infrastructure/DependencyInjection/AppServices.cs
git add GenHub/Directory.Packages.props
git add GenHub/GenHub.csproj
git commit -m "feat(content): add three-tier service registration and dependency injection modules"

# --- Commit 10: Update existing services for content integration ---
git add GenHub/Features/GitHub/Services/OctokitGitHubApiClient.cs
git add GenHub/Features/Manifest/ContentManifestBuilder.cs
git add GenHub/Features/Manifest/GameManifestPool.cs
git add GenHub/Features/Manifest/ManifestDiscoveryService.cs
git add GenHub/Features/Manifest/ManifestGenerationService.cs
git add GenHub/Features/Workspace/FileOperationsService.cs
git add GenHub/Features/Workspace/WorkspaceManager.cs
git add GenHub/Features/Workspace/Strategies/WorkspaceStrategyBase.cs
git add GenHub/Features/Validation/GameInstallationValidator.cs
git add GenHub/Features/Validation/GameVersionValidator.cs
git add GenHub.Windows/Features/Workspace/WindowsFileOperationsService.cs
git commit -m "feat(content): enhance existing services to support content delivery pipeline integration"

# --- Commit 11: Add comprehensive test coverage ---
git add GenHub.Tests/GenHub.Tests.Core/Features/Content/BaseContentProviderTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Content/ContentOrchestratorTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Content/GitHubContentProviderTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Content/GitHubResolverTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Manifest/ContentManifestBuilderTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Validation/GameInstallationValidatorTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Validation/GameVersionValidatorTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Workspace/FileOperationsServiceTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Workspace/WorkspaceIntegrationTests.cs
git add GenHub.Tests/GenHub.Tests.Core/Features/Workspace/WorkspaceManagerTests.cs
git commit -m "feat(content): add comprehensive test coverage for content delivery pipeline"

# 3. Push your branch to remote
git push --set-upstream origin feat/content-delivery-pipeline
```

## 5. Pull Request Details

**Title:**  
feat/content-delivery-pipeline: Complete Content Discovery and Orchestration System

**Description:**  

### What Changed
- **Three-Tier Architecture**: Orchestrator → Providers → Components with clear separation of concerns
- **Content Orchestrator**: Central service coordinating multiple content providers and system-wide operations
- **Multiple Content Sources**: Support for local filesystem, GitHub releases, CNC Labs maps, and extensible provider pattern
- **Pipeline Component Pattern**: Reusable discoverers, resolvers, and deliverers composed by providers
- **Comprehensive Progress Reporting**: Detailed progress models for acquisition phases with real-time updates
- **Dynamic Caching**: Memory-based caching with pattern-based invalidation across all tiers
- **Type-Safe Operations**: All operations return `ContentOperationResult<T>` for consistent error handling
- **Content Storage System**: Centralized content management with manifest pooling and lifecycle tracking

### Why
The existing system lacked a unified way to discover, install, and manage content from multiple sources. Users needed to manually handle different content types and sources, leading to fragmented user experience and complex maintenance. The new three-tier architecture provides clear separation between system coordination, provider-specific logic, and reusable pipeline components.

### How
- **Three-Tier Service Registration**: `ContentDeliveryModule` registers components, providers, and orchestrator with proper lifetimes
- **Flexible Provider Pattern**: Supports both simple providers and complex pipeline orchestration
- **Component Reusability**: Pipeline components can be shared across multiple providers
- **Progress Tracking**: Comprehensive progress reporting through `IProgress<T>` interfaces at all levels
- **Validation Pipeline**: Built-in validation for manifests and content integrity across all tiers
- **Workspace Integration**: Seamless integration with existing workspace preparation system

**Testing:**  
- New test suites: `BaseContentProviderTests`, `ContentOrchestratorTests`, `GitHubContentProviderTests`, `GitHubResolverTests`
- Updated existing tests: All workspace, manifest, and validation tests updated for new models
- Integration tests: Full pipeline testing from discovery through workspace preparation

**Architecture Benefits:**
- **Scalability**: Easy addition of new content sources through provider registration
- **Maintainability**: Clear separation of concerns across three architectural tiers  
- **Reusability**: Pipeline components can be shared and composed flexibly
- **Performance**: Multi-level caching strategy optimizes repeated operations
- **Reliability**: Comprehensive error handling and validation at all levels

**Next Steps:**  
- Add UI components for content browsing and installation workflows
- Implement configuration system for repository endpoints and provider settings
- Add more specialized discoverers for additional content sources
- Enhance caching strategies with persistent storage options
- Implement content update and synchronization features

---

## 6. Detailed Implementation Analysis by Commit

### Commit 1: Core Interfaces and Models for Three-Tier Content Delivery Pipeline

**IContentOrchestrator Architecture**  
`IContentOrchestrator` coordinates content discovery, manifest resolution, acquisition, and provider management. It exposes methods like `SearchAsync` (aggregates results from all enabled providers using `ContentSearchQuery`), `GetContentManifestAsync` (resolves a `ContentSearchResult` to a full `GameManifest`), `AcquireContentAsync` (downloads and prepares content), and provider registration/unregistration. All operations use type-safe `ContentOperationResult<T>` wrappers for consistent error handling and progress reporting.

**Pipeline Component Contracts**  
- `IContentDiscoverer`: Discovers content items from a source, returning `ContentSearchResult` collections based on a `ContentSearchQuery`.
- `IContentResolver`: Resolves a discovered item into a full `GameManifest`, identified by a `ResolverId`.
- `IContentDeliverer`: Validates and delivers content files to a target directory, reporting progress via `ContentAcquisitionProgress`.
- `IContentProvider`: Composes discoverer, resolver, and deliverer for a specific source, orchestrating the full pipeline (`SearchAsync`, `GetContentAsync`, `PrepareContentAsync`).
- `IContentSource`: Marker interface for all content sources, exposing `SourceName`, `Description`, `IsEnabled`, and capability flags.
- `IContentValidator`: Validates manifests and content integrity asynchronously.
- `IDynamicContentCache`: Generic async cache interface for pipeline operations.
- `IContentStorageService`: Manages persistent content storage, retrieval, deduplication, and statistics.

**Content Model Hierarchy**  
- `ContentSearchQuery`: Encapsulates search parameters (search term, content type, target game, tags, author, date range, pagination, sort order, and installed filter).
- `ContentAcquisitionProgress`: Tracks acquisition phases (`Downloading`, `Extracting`, `Copying`, etc.), progress percentage, bytes/files processed, current operation, and estimated time remaining.
- `ContentAcquisitionPhase`: Enum for acquisition pipeline stages.
- `ExtractionConfiguration`: Describes package download/extraction (URL, hash, package type, extraction path).

**Result Type System**  
- `ContentOperationResult<T>`: Generic result wrapper with success flag, data payload, error message, and factory methods for success/failure.
- `ContentSearchResult`: Represents discovered content with rich metadata (id, name, description, content type, provider, author, tags, screenshots, download size, rating, install/update status, resolver info, and optional embedded manifest).

**Configuration Architecture**  
- `AppSettings`: Centralizes configuration (auto-update, logging, settings file path, cache path, content directories, GitHub repositories, content storage path).
- `IAppConfigurationService` and `IConfigurationProvider`: Provide access to app data paths, workspace paths, cache/content directories, GitHub repo lists, and storage configuration.

**Enums and Capabilities**  
- `ContentProviderType`: Classifies provider types (FileSystem, Http, Git, Registry, Steam, ModDb).
- `ContentSourceCapabilities`: Flags for provider features (DirectSearch, RequiresDiscovery, Streaming, PackageAcquisition, ManifestGeneration, LocalFileDelivery).
- `PackageType`: Supported package formats (Zip, Tar, TarGz, SevenZip, Installer).

### Commit 2: Update Existing Models and Interfaces to Support Content Delivery Features

**GameManifest Enhancement**  
- `GameManifest` now exposes `InstallationInstructions` (renamed from `Installation`), clarifying installation metadata.
- Properties are streamlined for clarity and consistency.

**ManifestFile Evolution**  
- Added properties: `IsRequired`, `SourcePath`, `PatchSourceFile`, and `PackageInfo` for richer file metadata and patching support.
- `DownloadUrl` is retained for remote file acquisition.

**ManifestFileSourceType and WorkspaceStrategy Updates**  
- `ManifestFileSourceType` enum redefined for clearer file origin semantics.
- `WorkspaceStrategy` enum updated: `FullSymlink` and `ContentAddressable` swapped for correct strategy naming.

**Enhanced Interface Contracts**  
- `IGitHubApiClient` adds `GetReleaseByTagAsync` for fetching releases by tag, with extended `GitHubRelease` metadata (including `Author`).
- `IContentManifestBuilder` updates default parameters to use `Content` source type, adds `AddFile(ManifestFile file)` and `AddPatchFile` for patching support.
- `IGameManifestPool` interface added for manifest pooling, retrieval, searching, and lifecycle management.
- `IValidator<T>` interface introduced for generic validation, supporting progress reporting.

**Validation Framework Updates**  
- `ValidationIssue` class enhanced with multiple constructors for flexible issue reporting, supporting severity, message, path, expected/actual values, and details.

**Workspace Infrastructure**  
- `IFileOperationsService` adds `ApplyPatchAsync` for file patching operations.
- `WorkspaceInfo` model gains `Success` and `ValidationIssues` for tracking preparation results and encountered issues.

**Other Model Updates**  
- `InstallationInstructions` adds `DownloadHash` for verifying primary downloads.
- Enum and model documentation improved for clarity and maintainability.

These changes collectively enable richer manifest modeling, patching workflows, improved validation, and robust workspace preparation for the content delivery pipeline.

### Commit 3: ContentOrchestrator and Core Content Services

**ContentOrchestrator Implementation**  
The new `ContentOrchestrator` class coordinates all content operations, managing provider registration, orchestrating parallel searches, manifest retrieval, acquisition, and removal. It aggregates results from all enabled providers, applies sorting and pagination, and caches results for performance. Provider registration/unregistration is thread-safe, and all operations support cancellation tokens for responsive UI. Error handling is robust, with detailed logging and error aggregation for partial failures.

**ContentValidator Architecture**  
`ContentValidator` validates `GameManifest` objects and their content integrity. It checks manifest structure, required fields, and file hashes asynchronously, reporting progress via `IProgress<ValidationProgress>`. Validation is modular, with clear separation between manifest schema checks and file integrity verification. Results include severity levels and detailed issue descriptions.

**MemoryDynamicContentCache Strategy**  
`MemoryDynamicContentCache` uses `IMemoryCache` for fast, in-memory caching of search results and manifests. It supports both sliding and absolute expiration, with cache keys based on normalized search parameters. Pattern-based invalidation uses regex matching, allowing targeted cache clearing when content changes or providers are updated.

**ContentStorageService Management**  
`ContentStorageService` handles persistent storage of content and manifests. It ensures directory structure, atomic file operations, and SHA-256 hash verification for integrity. Content is stored in organized directories, and manifest metadata is serialized to JSON. On failure, cleanup routines remove incomplete data. Deduplication is managed via manifest IDs and file hashes, and storage statistics are available for monitoring usage.

### Commit 4: Content Discoverers for Filesystem, GitHub, and CNC Labs Sources

**FileSystemDiscoverer Implementation**  
FileSystemDiscoverer discovers content by scanning user-configured directories for manifest files using ManifestDiscoveryService. It supports recursive traversal, manifest parsing, and content filtering based on search queries (name, content type, target game). Results are mapped to ContentSearchResult objects with metadata including manifest details, author, tags, screenshots, and download size. Capability flags indicate support for direct search and manifest generation.

**GitHubDiscoverer and GitHubReleasesDiscoverer Integration**  
GitHubDiscoverer and GitHubReleasesDiscoverer utilize IGitHubApiClient to query configured repositories for the latest releases. They extract release metadata (name, tag, author, publication date) and infer content type and target game from repository and release names. Results are standardized as ContentSearchResult objects with RequiresResolution flags and resolver metadata for downstream manifest generation. Error handling logs failures per repository and aggregates partial errors.

**CNCLabsMapDiscoverer Web Integration**  
CNCLabsMapDiscoverer performs HTTP requests to the CNC Labs search endpoint, parses HTML responses, and extracts map metadata (id, name, author, detail URL). Discovered maps are mapped to ContentSearchResult objects with CNC Labs-specific resolver metadata and marked for further resolution. The implementation includes robust error handling and logging for network failures and parsing issues.

**Discovery Result Standardization**  
All discoverers produce ContentSearchResult collections with consistent property mapping: unique Ids, normalized names, content type classification, provider identification, and resolution requirements. Results support downstream manifest resolution and acquisition workflows, ensuring unified handling across all content sources.

### Commit 5: Content Providers Implementing Three-Tier Pipeline Orchestration

**BaseContentProvider Framework**  
`BaseContentProvider` defines the core orchestration logic for content providers, exposing abstract properties for `Discoverer`, `Resolver`, and `Deliverer` pipeline components. It implements the template method pattern for `SearchAsync`, coordinating discovery, resolution, and manifest validation, with consistent error handling and progress reporting. The framework ensures type-safe result handling via `ContentOperationResult<T>`, and provides extensibility for provider-specific logic through abstract methods.

**GitHubContentProvider Specialization**  
`GitHubContentProvider` composes GitHub-specific pipeline components: a discoverer for GitHub releases, a resolver for manifest generation from release assets, and an HTTP deliverer for content acquisition. It supports repository configuration, authentication, and applies asset filtering and compatibility checks. Content preparation leverages the deliverer for downloading and validating release assets, with manifest integrity checks and error reporting.

**LocalFileSystemContentProvider Implementation**  
`LocalFileSystemContentProvider` integrates a file system discoverer and local manifest resolver for direct access to local content. It validates manifest structure and file integrity, and supports synchronous operations optimized for low-latency local access. Content preparation is streamlined, as files are already present, with manifest validation ensuring correctness.

**CNCLabsContentProvider and ModDBContentProvider Architecture**  
`CNCLabsContentProvider` and `ModDBContentProvider` demonstrate specialized pipelines for web-based sources. Each composes dedicated discoverers and resolvers for their respective platforms, and uses HTTP deliverers for content acquisition. Provider orchestration manages discovery, resolution, and delivery, with robust error handling for network failures, content structure changes, and service unavailability. Manifest validation and progress reporting are integrated throughout the pipeline.

### Commit 6: Content Resolvers for Manifest Generation from Discovered Content

**GitHubResolver Implementation**  
`GitHubResolver` resolves discovered GitHub releases into full `GameManifest` objects by leveraging `IGitHubApiClient` to fetch release metadata and assets. It infers content type and target game from repository and release names, extracts tags, and constructs manifests with publisher info, changelog URLs, and asset inventories. Assets are added as downloadable files, with executable detection based on file extensions. Robust error handling and logging ensure reliability, and all operations return type-safe `ContentOperationResult<GameManifest>` results.

**LocalManifestResolver Implementation**  
`LocalManifestResolver` reads and deserializes local manifest files directly from the filesystem, validating schema and version compatibility. It handles missing or malformed files gracefully, logging errors and returning failure results when necessary. Successful deserialization yields a complete `GameManifest`, enabling direct integration of local content into the pipeline.

**CNCLabsMapResolver Implementation**  
`CNCLabsMapResolver` processes CNC Labs map detail pages via HTTP requests, extracting map metadata such as name, author, description, preview images, and download URLs. It constructs manifests with proper content type, publisher info, and required directories for map integration. The resolver supports robust error handling for network failures and parsing issues, ensuring reliable manifest generation from web-sourced content.
### Commit 7: Content Deliverers for File Acquisition and Preparation

**FileSystemDeliverer Implementation**  
`FileSystemDeliverer` delivers content from the local file system by validating and copying files specified in the manifest. It resolves file paths using configuration providers, checks for file existence, and reports progress for each file processed. Delivered files are added to a new manifest using `ContentManifestBuilder`, preserving metadata such as relative path, hash, and permissions. Error handling logs failures and returns type-safe results for missing files or delivery errors. Validation ensures all required files are present and accessible.

**HttpContentDeliverer Implementation**  
`HttpContentDeliverer` acquires content from remote HTTP/HTTPS sources by downloading files listed in the manifest. It uses an injected `IDownloadService` for file transfers, supports progress reporting, and verifies file integrity. Downloaded files are added to the manifest via `IContentManifestBuilder`, with metadata for executability and permissions. The deliverer handles directory creation, download failures, and network errors with robust logging and error propagation. Validation checks that all required URLs are valid and accessible.

**Delivery Pipeline Integration**  
Both deliverers implement the `IContentDeliverer` interface, providing `CanDeliver` and `DeliverContentAsync` methods for capability-based routing. Progress is tracked using `ContentAcquisitionProgress` objects, and error handling ensures failures are reported through `ContentOperationResult<T>`. The design supports both local and remote content acquisition, enabling flexible integration into the overall content pipeline.

### Commit 8: Content Browser ViewModels and Settings UI Integration

**ContentBrowserViewModel Implementation**  
`ContentBrowserViewModel` provides the UI logic for browsing and searching content from multiple providers. It exposes observable properties for search term, selected content type, sort order, loading state, and error messages. The `SearchResults` collection is updated asynchronously via the `SearchAsync` command, which interacts with `IContentOrchestrator` to perform searches based on user input. Error handling and progress indication are integrated for responsive UI feedback.

**ContentItemViewModel Representation**  
`ContentItemViewModel` wraps individual `ContentSearchResult` items, exposing properties such as Name, Description, AuthorName, Version, and IconUrl for UI binding. It ensures type-safe access to underlying model data and supports property change notifications for dynamic UI updates.

**SettingsViewModel and SettingsView.axaml Enhancements**  
`SettingsViewModel` adds new observable properties for cache path, content storage path, content directories, and GitHub discovery repositories. These properties are synchronized with the underlying settings model, supporting multi-line text input for directory and repository lists.  
`SettingsView.axaml` introduces new UI sections for configuring cache and content storage directories, local content directories, and GitHub repositories. Each section provides descriptive labels, watermarks, and guidance for user input, improving configuration clarity and flexibility.

### Commit 9: Three-Tier Service Registration and Dependency Injection Modules

**ContentPipelineModule Registration Strategy**  
A new `ContentPipelineModule` class provides extension methods for hierarchical service registration, following the three-tier architecture.  
- Registers the core orchestrator (`IContentOrchestrator`), memory-based cache (`IDynamicContentCache`), and GitHub API client (`IGitHubApiClient`) as singletons.
- Registers all pipeline components (`IContentProvider`, `IContentDiscoverer`, `IContentResolver`, `IContentDeliverer`) as transient services, enabling flexible composition and injection.
- Providers for GitHub, CNC Labs, ModDB, and local filesystem are registered for capability-based routing.
- Component registration ensures discoverers, resolvers, and deliverers are available for provider orchestration.

**ValidationModule Infrastructure**  
`ValidationModule` is updated to register content validation services:
- Registers `IContentValidator` as a transient service for manifest and content integrity validation.
- Registers domain-specific validators (`IGameInstallationValidator`, `IGameVersionValidator`) and generic validator interfaces for type-safe validation.
- Supports progress reporting and error aggregation for validation pipelines.

**AppServices Integration**  
`AppServices` is updated to include `AddContentPipelineServices` in the main DI registration flow, ensuring all pipeline components and orchestrator are available application-wide. Platform-specific services can be injected via delegates for extensibility.

**Package Dependencies**  
`GenHub.csproj` and `Directory.Packages.props` are updated to include `Microsoft.Extensions.Caching.Memory` for caching infrastructure, alongside existing DI and HTTP libraries. This enables efficient caching and dependency management for all pipeline operations.


### Commit 10: Enhanced Existing Services for Content Delivery Pipeline Integration

**OctokitGitHubApiClient Enhancement**  
OctokitGitHubApiClient now supports `GetReleaseByTagAsync`, enabling retrieval of specific GitHub releases by tag. Release mapping is centralized via `MapOctokitRelease`, which standardizes metadata including author, assets, and timestamps. Error handling is improved for not-found and general exceptions, with detailed logging for API failures and rate limits.

**ContentManifestBuilder Evolution**  
ContentManifestBuilder updates default file source types to `Content`, adds `AddFile(ManifestFile file)` for direct file injection, and introduces `AddPatchFile` for patch management. Manifest generation now uses `InstallationInstructions` for installation metadata, and pre/post install steps are managed under this property. Builder methods enforce manifest consistency and support patch workflows.

**GameManifestPool Implementation**  
A new GameManifestPool service provides persistent manifest storage, retrieval, searching, and lifecycle management. It integrates with the content storage service for atomic operations, supports manifest pooling from source directories, and exposes search/filter capabilities for acquired manifests. Error handling and logging are included for all storage operations.

**ManifestDiscoveryService Improvements**  
ManifestDiscoveryService now scans both standard and custom manifest directories, supporting `.manifest.json` and `.json` files. Discovery logic avoids conflicts with stored manifests and improves cache initialization by loading embedded and filesystem manifests. Logging is enhanced for directory scanning and manifest loading.

**ManifestGenerationService Updates**  
ManifestGenerationService updates file source types for manifest generation, ensuring correct classification of content and base game files. Executable marking and workspace strategy assignment are streamlined for clarity and consistency.

**Validation Pipeline Integration**  
GameInstallationValidator and GameVersionValidator now delegate core validation logic to ContentValidator, separating manifest schema checks and content integrity verification. Progress reporting is improved with multi-phase updates, and installation-specific checks are performed after core validation. Validation results aggregate issues from all phases for comprehensive reporting.

**Workspace Service Integration**  
FileOperationsService adds `ApplyPatchAsync` for patch file application, with placeholder logic for future patching implementations. WorkspaceManager now uses a configurable metadata path and integrates with the configuration provider for storage location management. Directory deletion is refactored for reliability, and workspace metadata is saved atomically.

**Platform-Specific Enhancements**  
WindowsFileOperationsService implements `ApplyPatchAsync` for Windows environments, delegating patch operations to the base service.

---

### Commit 11: Comprehensive Test Coverage for Content Delivery Pipeline

**Provider Pipeline Unit Tests**  
`BaseContentProviderTests` verifies the provider pipeline logic, including manifest validation, content preparation, and error handling. Tests use mock implementations for all pipeline dependencies (`IContentValidator`, `IContentDiscoverer`, `IContentResolver`, `IContentDeliverer`, `ILogger`) to simulate both successful and failure scenarios. Coverage includes manifest validation before preparation, error propagation when validation fails, and correct invocation of pipeline components.

**Orchestrator Coordination Tests**  
`ContentOrchestratorTests` validate system-wide coordination, result aggregation from multiple providers, and acquisition workflows. Tests use mock providers to simulate search aggregation, manifest retrieval, and content acquisition. Scenarios include successful aggregation, validation and storage of acquired content, and error handling for failed provider operations. Verification includes correct invocation of validator and manifest pool, and result consistency.

**Component Integration and Error Handling**  
`GitHubContentProviderTests` and `GitHubResolverTests` provide integration-level coverage for GitHub-based pipelines. Tests mock GitHub API responses, resolver logic, and deliverer operations to validate discovery, resolution, and content preparation. Scenarios include successful orchestration of discovery and resolution, deliverer invocation, manifest validation, and error handling for missing metadata or network failures. Tests ensure that pipeline components interact correctly and propagate errors as expected.

**Validation and Workspace Tests**  
Existing validation and workspace tests (`GameInstallationValidatorTests`, `GameVersionValidatorTests`, `FileOperationsServiceTests`, `WorkspaceIntegrationTests`, `WorkspaceManagerTests`) are updated to use the new content validation pipeline. Tests cover manifest integrity, missing file detection, workspace preparation, and error scenarios. Mocked content validator ensures consistent validation logic and progress reporting.

---

## 7. Pull Request Summary

This pull request delivers a comprehensive three-tier content delivery pipeline that transforms GenHub from a local game management tool into a unified content ecosystem platform. The architecture separates system coordination (ContentOrchestrator), provider-specific logic (ContentProviders), and reusable pipeline components (Discoverers, Resolvers, Deliverers) enabling scalable content source integration.
