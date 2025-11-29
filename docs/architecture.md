# GenHub: Complete System Architecture Analysis

## Executive Summary

GenHub represents a sophisticated, multi-layered architecture designed to solve the fundamental problem of C&C Generals/Zero Hour ecosystem fragmentation. The system operates through **six core architectural pillars**: **GameInstallation** (physical detection), **GameClient** (executable identification), **ContentManifest** (declarative packaging), **GameProfile** (user configuration), **Workspace** (isolated execution environment), and **GameLaunching** (runtime orchestration). These pillars interact through a carefully orchestrated **three-tier content pipeline** that transforms raw game installations into customizable, isolated gaming experiences through a **ContentOrchestrator** → **ContentProvider** → **Pipeline Components** architecture, culminating in a comprehensive game profile management and launching system.

---

## 1. Core System Foundation: The Six Architectural Pillars

### 1.1 GameInstallation: The Physical Foundation Layer

**Primary Responsibility**: Detection and cataloging of physical game installations across different platforms and distribution methods.

**Key Components**:

- **IGameInstallationDetectionOrchestrator**: Master coordinator that aggregates results from all platform detectors
- **IGameInstallationDetector**: Platform-specific detection contracts implemented by WindowsInstallationDetector, LinuxInstallationDetector
- **GameInstallation**: Core data model containing InstallationPath, InstallationType, HasGenerals, HasZeroHour properties
- **GameInstallationType**: Enumeration distinguishing Steam, EaApp, Origin, Manual installations
- **IGameInstallationValidator**: Ensures detected installations are functional and complete
- **IGameInstallationService**: High-level service for installation management and validation

**Detection Methodology**:
The system employs specialized detectors for each platform and distribution method. WindowsInstallationDetector scans registry entries for Steam libraries, EA App installations, and Origin game paths. LinuxInstallationDetector focuses on Steam Proton compatibility layers and Wine prefixes. Each detector implements the common IGameInstallationDetector contract, enabling polymorphic detection orchestration.

**Data Flow Pattern**:
Detection begins with IGameInstallationDetectionOrchestrator.DetectAllInstallationsAsync, which coordinates multiple IGameInstallationDetector implementations. Each detector returns DetectionResult containing discovered GameInstallation objects. The orchestrator aggregates these results, validates them through IGameInstallationValidator, and maintains a centralized registry of available installations through IGameInstallationService.

**Caching Strategy**:
GameInstallationService caches detection results with a lightweight in-memory cache guarded by a SemaphoreSlim, using IGameInstallationDetectionOrchestrator as the source of truth to avoid repeated detection runs per app lifetime.

### 1.2 GameClient: The Executable Identity Layer

**Primary Responsibility**: Identification and categorization of specific game executables, patches, and modifications within detected installations.

**Key Components**:

- **IGameClientDetectionOrchestrator**: Coordinates game client detection across all known installations
- **IGameVersionDetector**: Analyzes installations to identify distinct executable variants
- **GameClient**: Data model with Id, Name, Version, ExecutablePath, GameType, InstallationType, LaunchArguments, and EnvironmentVariables properties
- **GameType**: Enumeration distinguishing Generals versus ZeroHour variants
- **IGameClientValidator**: Verifies executable functionality and compatibility

**Version Identification Logic**:
The system recognizes that a single GameInstallation may contain multiple executable variants. These could represent different patch levels, community modifications, or standalone executables. GameClientDetectionOrchestrator systematically scans each detected installation, analyzing executable signatures, file versions, and directory structures to create distinct GameClient entries.

**GameClient Model**:
The GameClient model has been enhanced to support launch configuration with LaunchArguments and EnvironmentVariables properties, enabling per-version customization of startup parameters and environment settings for compatibility with different game clients.

### 1.3 ContentManifest: The Declarative Blueprint Layer

**Primary Responsibility**: Standardized, declarative description of installable content packages, serving as the universal contract between content creators and the GenHub system.

**Core Architectural Components**:

- **ContentManifest**: Central blueprint containing Id, Name, Version, ContentType, TargetGame, Files, Dependencies
- **ManifestFile**: Atomic file operation descriptor with RelativePath, SourceType, Size, Hash, DownloadUrl
- **ContentType**: Enumeration covering BaseGame, StandaloneVersion, Mod, Patch, Addon, MapPack, LanguagePack
- **ContentSourceType**: Operation type enum including `BaseGame`, `Content`, `Patch`, `OptionalAddon`, `Download`, `Generated`
- **PublisherInfo**: Content creator metadata with Name, Website, SupportUrl, ContactEmail
- **ContentMetadata**: Rich descriptive data including Description, Tags, IconUrl, ScreenshotUrls
- **ContentDependency**: Prerequisite specification with Id, Name, DependencyType, InstallBehavior, VersionConstraints
- **InstallationInstructions**: Execution guidance with PreferredStrategy, PreInstallSteps, PostInstallSteps

**Manifest Identification System**:

- **ManifestId**: Strongly-typed value object providing deterministic, human-readable content identification with compile-time validation and implicit conversions
- **ManifestIdGenerator**: Low-level utility for generating deterministic IDs with cross-platform normalization and filesystem-safe output
- **ManifestIdService**: Service layer implementing ResultBase pattern for type-safe ID operations with proper error handling
- **ManifestIdValidator**: Comprehensive validation ensuring ID format compliance and security with regex-based rules
- **ManifestIdJsonConverter**: JSON serialization support enabling seamless persistence and API integration

**Manifest Creation Infrastructure**:

- **IContentManifestBuilder**: Fluent builder interface for programmatic manifest construction
- **ContentManifestBuilder**: Implementation providing WithBasicInfo, WithContentType, WithPublisher, AddDependency, AddFileAsync methods
- **IManifestGenerationService**: High-level service for automated manifest creation from directories, installations, and content packages
- **ManifestGenerationService**: Concrete implementation with CreateContentManifestAsync, CreateGameInstallationManifestAsync, CreateGameVersionManifestAsync, CreateContentBundleAsync, CreatePublisherReferralAsync, CreateContentReferralAsync

**Manifest Lifecycle Management**:

- **IManifestProvider**: Abstraction for manifest retrieval from GameClient and GameInstallation objects
- **ManifestProvider**: Implementation that generates manifests for detected content
  - GameClient: tries pool by ManifestId (if gameVersion.Id is a valid manifest id), then embedded resources GenHub.Manifests.{id}.json, then optional fallback generation (GenerateFallbackManifests=false by default)
  - GameInstallation: uses deterministic ID via ManifestIdGenerator (install type + game type + version), same "pool → embedded → optional fallback" flow
  - Provider requires IManifestIdService and IContentManifestBuilder (injected); options (ManifestProviderOptions) control fallback generation
- **IContentManifestPool**: Manages the lifecycle of all acquired (installed) content manifests, acting as the source of truth for what content is available on the user's system
- **IContentManifestPool**: Interface for content manifest management with full CRUD operations
- **IContentStorageService**: Handles the physical storage and retrieval of content files and their associated manifests, providing a content-addressable-like storage system
  - ContentType.GameInstallation: storage is "manifest metadata only" (no file copy)
  - All other content: copied into ContentStorageRoot/Data, hashed and re-written to the manifest

### 1.4 GameProfile: The User Configuration Layer

**Primary Responsibility**: User-defined launch configurations combining base game versions with selected content modifications, launch parameters, and workspace strategies.

**Core Components**:

- **GameProfile**: Central configuration object with comprehensive profile state including:
  - Core properties: Id, Name, Description, GameClient, ExecutablePath, GameInstallationId
  - Content management: EnabledContentIds (List of manifest ID strings for content references)
  - Workspace configuration: WorkspaceStrategy, CustomExecutablePath, WorkingDirectory, ActiveWorkspaceId
  - Launch configuration: CommandLineArguments (string), LaunchOptions (Dictionary), EnvironmentVariables (Dictionary)
  - UI state: ThemeColor, IconPath, BuildInfo
  - Game settings: Video properties (ResolutionWidth/Height, Windowed, TextureQuality, Shadows, ParticleEffects, ExtraAnimations, BuildingAnimations, Gamma [0-100])
  - Audio settings: Audio volumes (SoundVolume, ThreeDSoundVolume, SpeechVolume, MusicVolume), AudioEnabled, NumSounds
  - Timestamps: CreatedAt, LastPlayedAt
- **CreateProfileRequest**: Data transfer object for profile creation with GameInstallationId, GameClientId, PreferredStrategy, EnabledContentIds, CommandLineArguments, and UI properties
- **UpdateProfileRequest**: Data transfer object for profile updates with partial modification support (all fields optional, CommandLineArguments as string parameter)
- **ProfileInfoItem**: UI-specific data transfer object for profile display
- **WorkspaceStrategy**: Enumeration defining file assembly approaches including FullCopy, SymlinkOnly, HybridCopySymlink, HardLink
- **IGameProfile**: Contract for profile-like objects ensuring Version, ExecutablePath, EnabledContentIds, PreferredStrategy accessibility

**Profile Management Architecture**:

- **IGameProfileManager**: High-level service for profile CRUD operations with CreateProfileAsync, UpdateProfileAsync, DeleteProfileAsync, GetProfileAsync, GetAllProfilesAsync
- **IGameProfileRepository**: Data persistence layer for profile storage and retrieval
- **GameProfileManager**: Implementation handling business logic, validation, and orchestration
- **GameProfileRepository**: File-based storage implementation with JSON serialization

**Profile Integration Model**:
GameProfile objects serve as the primary user-facing abstraction, encapsulating all decisions about game configuration. Each profile maintains references to a base GameClient and a collection of EnabledContentIds representing installed modifications. The WorkspaceStrategy property determines how files will be assembled during workspace preparation. Launch customization is provided through CommandLineArguments (simple command string) for game-specific parameters and LaunchOptions/EnvironmentVariables (Dictionary collections) for advanced configuration. Game video and audio settings are stored directly on the profile (VideoResolutionWidth, AudioSoundVolume, etc.), enabling per-profile Options.ini customization without requiring separate file parsing.

**ProfileEditorFacade Integration**:
ProfileEditorFacade auto-enables matching GameInstallation content (and GameClient if available) after creation by scanning the manifest pool for the profile's GameType; then resolves dependencies and prepares a workspace, persisting ActiveWorkspaceId.

**Content Integration Model**:
Profiles maintain loose coupling with content through string-based EnabledContentIds identifiers rather than direct object references. This design enables content to be added, removed, or updated without invalidating existing profiles. The system resolves these identifiers during workspace preparation through the IContentManifestPool, allowing for flexible content management. GetAvailableContentAsync currently filters by TargetGame only; the pool content must already exist (seeded by ManifestInitializationService or acquired by providers).

### 1.5 Workspace: The Isolated Execution Environment

**Primary Responsibility**: Creation and management of isolated, profile-specific game directories ensuring conflict-free execution with CAS integration.

**Core Workspace Architecture**:

- **IWorkspaceManager**: High-level coordinator with PrepareWorkspaceAsync, GetAllWorkspacesAsync, CleanupWorkspaceAsync methods
- **WorkspaceManager**: Implementation orchestrating strategy selection, execution, and CAS reference tracking
- **WorkspaceConfiguration**: Input specification with Id, Manifests, Strategy, WorkspaceRootPath, BaseInstallationPath, GameClient
- **WorkspaceInfo**: Result descriptor with Id, WorkspacePath, GameVersionId, Strategy, FileCount, TotalSizeBytes, ExecutablePath, WorkingDirectory, Success, ValidationIssues

**Strategy Pattern Implementation**:

- **IWorkspaceStrategy**: Strategy contract with Name, Description, RequiresAdminRights, RequiresSameVolume, CanHandle, EstimateDiskUsage, PrepareAsync methods
- **WorkspaceStrategyBase**: Abstract base providing common functionality for all concrete strategies with improved file processing, CAS integration, and validation
- **FullCopyStrategy**: Maximum compatibility approach with optimized file copying and CAS content support
- **SymlinkOnlyStrategy**: Minimum disk usage approach creating symbolic links to source files
- **HybridCopySymlinkStrategy**: Balanced approach with intelligent file classification - copying essential files (executables, configurations) while linking large media assets
- **HardLinkStrategy**: Platform-specific optimization using filesystem hard links

**Strategy Capabilities**:

- Supported source types in workspace assembly: ContentAddressable (CAS), GameInstallation, LocalFile
- RemoteDownload, ExtractedPackage, PatchFile are handled at acquisition/delivery time; manifests should be materialized to LocalFile or ContentAddressable before workspace prep
- Essential file classification: add CNC specifics (.big, .str, .csf, .w3d) and default "copy files under 1MB"

**File Operations Infrastructure**:

- **IFileOperationsService**: Comprehensive low-level operations contract with CopyFileAsync, CreateSymlinkAsync, CreateHardLinkAsync, VerifyFileHashAsync, DownloadFileAsync, ApplyPatchAsync, CopyFromCasAsync
- **FileOperationsService**: Platform-aware implementation with full CAS integration, cross-platform file operations, symbolic link creation, and hash verification

**Workspace Lifecycle Management**:

**Creation:**

- Deferred until first profile launch (not during profile creation)
- Workspace ID stored in profile.ActiveWorkspaceId after preparation

**Persistence:**

- Workspaces persist across launches for quick re-launch
- No cleanup on profile stop (only on deletion or strategy changes)

**Cleanup:**

- Manual: User-initiated cleanup via profile deletion
- Automatic: Content changes requiring workspace refresh
- CAS unreference: Tracked via CasReferenceTracker

**CAS Integration**:
Workspace strategies now fully integrate with the Content Addressable Storage system through  CAS operations:

- **CAS File Linking**: Strategies can link files directly from CAS storage using hash-based references
- **CAS Reference Tracking**: WorkspaceManager coordinates with CasReferenceTracker to track which CAS objects are referenced by each workspace after preparation
- **Metadata Persistence**: WorkspaceManager writes all workspace metadata into a single workspaces.json located at ContentStoragePath (not WorkspaceRootPath)
- **Automatic Cleanup**: CAS references are automatically managed during workspace creation and cleanup; CAS unreference/GC is handled by CAS services (CasMaintenanceService), not directly in WorkspaceManager directly on cleanup

**Workspace Validation Framework**:

- **IWorkspaceValidator**: Validation contract with ValidateConfigurationAsync, ValidatePrerequisitesAsync, and ValidateWorkspaceAsync methods
- **WorkspaceValidator**: Implementation ensuring workspace prerequisites, permissions, disk space availability, and post-creation integrity validation

**Workspace Reconciliation System**:

GenHub implements a sophisticated workspace reconciliation system that enables intelligent incremental updates, dramatically improving performance when content changes.

**Core Reconciliation Architecture**:

- **WorkspaceReconciler**: Analyzes workspace state and determines delta operations needed to reconcile with target configuration
- **WorkspaceDelta**: Represents a single file operation (Add, Update, Remove, Skip) with reason tracking
- **WorkspaceDeltaOperation**: Enumeration defining operation types for reconciliation
- **ReconciliationDeltas**: Property on WorkspaceConfiguration passing delta information to strategies

**Reconciliation Workflow**:

1. **Workspace Discovery**: When PrepareWorkspaceAsync is called with ForceRecreate=false, WorkspaceManager checks if workspace already exists
2. **Delta Analysis**: WorkspaceReconciler.AnalyzeWorkspaceDeltaAsync compares existing workspace against target manifests:
   - Builds dictionary of all files that SHOULD exist from manifests
   - Scans existing workspace to determine what files currently exist
   - Detects files to Add (missing from workspace)
   - Detects files to Update (hash mismatch, broken symlinks, size mismatch)
   - Detects files to Remove (in workspace but not in manifests)
   - Marks files to Skip (already current)
3. **Reuse Optimization**: If all files are marked Skip (no changes), workspace is reused immediately without strategy execution
4. **Incremental Preparation**: If changes detected, deltas are passed to strategy for incremental update

**Delta Detection Logic**:

```csharp
// WorkspaceReconciler analyzes differences between existing and target state
var deltas = await workspaceReconciler.AnalyzeWorkspaceDeltaAsync(existingWorkspace, targetConfiguration);

// Log reconciliation needs
_logger.LogInformation(
    "Workspace needs reconciliation: +{Add} files, ~{Update} files, -{Remove} files, ={Skip} unchanged",
    addCount, updateCount, removeCount, skipCount);

// Fast path: No changes needed
if (addCount == 0 && updateCount == 0 && removeCount == 0)
{
    _logger.LogInformation("Reusing existing workspace - all {FileCount} files are current", skipCount);
    return OperationResult<WorkspaceInfo>.CreateSuccess(existingWorkspace);
}
```

**File Update Detection**:

The reconciler uses multiple heuristics to determine if a file needs updating:

- **Symlink Validation**: For symlinked files, verifies target exists and is accessible
- **Hash Verification**: For regular files under 100MB, computes SHA-256 hash and compares with manifest
- **Size Verification**: Fast check comparing file size against manifest expectation
- **Existence Check**: Verifies file exists at expected path

**Multi-Priority Source Path Resolution**:

WorkspaceStrategyBase.ResolveSourcePath uses priority-based resolution:

1. **Absolute SourcePath**: File's SourcePath if already absolute
2. **Manifest Source Map**: configuration.ManifestSourcePaths[manifestId]
3. **GameInstallation Base**: configuration.BaseInstallationPath for GameInstallation content
4. **Relative SourcePath**: Combine BaseInstallationPath + file.SourcePath
5. **Fallback**: Combine BaseInstallationPath + file.RelativePath

This enables multi-source installations where content comes from different directories.

### 1.6 GameLaunching: The Runtime Orchestration Layer

**Primary Responsibility**: Transform prepared workspaces into running game processes with comprehensive process management, launch tracking, and runtime monitoring.

**Core Launch Architecture**:

- **IGameLauncher**: Primary launch coordinator with LaunchProfileAsync, TerminateGameAsync, GetActiveGamesAsync, GetGameProcessInfoAsync methods
- **GameLauncher**: Implementation handling the complete launch pipeline from profile resolution to process creation and monitoring
  - Adds profile-level concurrency locks (SemaphoreSlim per profile) to prevent duplicate launches
  - Runs a CAS preflight (verifies all CAS hashes referenced by manifests exist) before workspace prep
  - Ensures a base installation manifest is present (via IManifestProvider) even if not explicitly enabled
  - Exposes LaunchGameAsync for direct (non-profile) launches
- **GameLaunchConfiguration**: Input specification with ExecutablePath, WorkingDirectory, Arguments, EnvironmentVariables
- **GameProcessInfo**: Runtime descriptor with ProcessId, ProcessName, ExecutablePath, StartTime, IsRunning properties
- **LaunchProgress**: Progress reporting with Phase, PercentComplete, CurrentOperation properties
- **LaunchPhase**: Enumeration defining launch stages including ValidatingProfile, ResolvingContent, PreparingWorkspace, Starting, Running

**Process Management Infrastructure**:

- **IGameProcessManager**: Process lifecycle management with StartProcessAsync, TerminateProcessAsync, GetProcessInfoAsync, GetActiveProcessesAsync methods
- **GameProcessManager**: Implementation providing process creation, monitoring, cleanup, and cross-platform process management
  - Returns OperationResult&lt;T&gt; (not ProcessOperationResult&lt;T&gt;)
  - Handles Windows .bat/.cmd via cmd /c
  - Robust argument quoting and environment injection
- **ProcessOperationResult**: Specialized result type for process operations with success/failure status and detailed error information

**Launch Registry System**:

- **ILaunchRegistry**: Launch session tracking with RegisterLaunchAsync, UnregisterLaunchAsync, GetLaunchInfoAsync, GetAllActiveLaunchesAsync methods
- **LaunchRegistry**: Implementation maintaining active launch sessions, enabling termination, monitoring, and cleanup operations
  - Keeps launch records in-memory
  - GetAllActiveLaunchesAsync marks stale processes TerminatedAt but does not remove them (historical record)
  - Consumers should filter on TerminatedAt if they only want running sessions
- **GameLaunchInfo**: Launch session descriptor with LaunchId, ProfileId, WorkspaceId, ProcessInfo, LaunchedAt properties

**Launch Result Architecture**:

- **LaunchOperationResult**: Comprehensive result type for launch operations with Success, Data, LaunchId, ProfileId, and detailed error information
- **LaunchResult**: Simplified result for basic launch operations

**Launch Process Flow**:
Game launching follows a comprehensive pipeline:

1. **Profile Validation**: Verify profile exists and is properly configured
2. **Content Resolution**: Resolve all enabled content through IContentManifestPool
3. **Workspace Preparation**: Create isolated workspace using configured strategy
4. **Process Configuration**: Build launch configuration with arguments and environment
5. **Process Creation**: Start game process through IGameProcessManager
6. **Launch Registration**: Register active launch session through ILaunchRegistry
7. **Runtime Monitoring**: Track process status and provide termination capabilities

---

## 2. Three-Tier Content Pipeline Architecture

### 2.1 Architectural Overview

GenHub implements a **three-tier content pipeline architecture** that provides clear separation of concerns while enabling flexible content handling:

**Tier 1: Content Orchestrator** - System-wide coordination and provider management
**Tier 2: Content Providers** - Source-specific pipeline orchestration
**Tier 3: Pipeline Components** - Specialized operations (Discovery, Resolution, Delivery)

This architecture enables multiple providers to coexist, each orchestrating their own internal pipeline while being coordinated by the system-wide orchestrator.

**Workspace Strategy Integration Note**:
Workspace strategies only accept ContentAddressable, GameInstallation, LocalFile at assembly time; deliverers must resolve RemoteDownload, ExtractedPackage, PatchFile source types before storage/workspace preparation.

**Result Pattern Integration in Pipeline Operations**:

- **OperationResult&lt;T&gt;**: Used for all content provider operations with typed data
- **DetectionResult**: Specialized for content discovery and validation operations
- **ValidationResult**: Used for manifest and content validation with detailed issue tracking
- **DownloadResult**: Specialized for content download operations with progress tracking

**Error Handling in Pipeline Components**:

```csharp
// Example: Content provider error handling with new patterns
var searchResult = await _contentProvider.SearchAsync(query);
if (!searchResult.Success)
{
    _logger.LogError("Content search failed: {Error}", searchResult.FirstError);
    return OperationResult<List<ContentSearchResult>>.CreateFailure(
        searchResult.Errors.ToList());
}
```

**Progress Reporting in Pipeline Operations**:

- **ContentAcquisitionProgress**: Tracks the complete content acquisition pipeline
- **DownloadProgress**: Provides detailed download progress with speed metrics
- **ValidationProgress**: Reports validation operation progress

### 2.2 Tier 1: Content Orchestrator (System Coordination)

**Primary Responsibility**: System-wide coordination of multiple content providers, caching, and integration with the content storage, game profile, and workspace systems.

**Core Orchestrator Architecture**:

- **IContentOrchestrator**: Master coordination interface for all content operations
- **ContentOrchestrator**: Iimplementation managing provider registry, caching, and the end-to-end content acquisition workflow with game profile integration
- **Provider Registry**: Dynamic collection of registered content providers
- **System-wide Caching**: Performance optimization across all providers using `IDynamicContentCache`
- **Storage Integration**: Coordination with `IContentManifestPool`, `IContentManifestPool`, and `IContentStorageService` to persist acquired content and manifests

**Orchestrator Responsibilities**:

1. **Provider Management**: Registration, discovery, and lifecycle management of content providers
2. **Search Coordination**: Aggregates search results from multiple providers concurrently
3. **Content Acquisition**: Orchestrates the full pipeline: Provider Search → Provider Preparation → Content Storage → Manifest Pooling → Profile Integration
4. **Caching Strategy**: System-wide caching of search results and manifests for performance
5. **Error Aggregation**: Collects and reports errors from multiple providers uniformly
6. **Profile Integration**: Coordinates with GameProfile system for content enablement

**User-Centric Content Discovery Workflow**:
The orchestrator presents a simplified interface that abstracts provider complexity:

1. **Entry Point**: User navigates to "Discover" area or Profile content management
2. **Category Selection**: User selects content categories (e.g., "Find Mods," "Find Maps")
3. **Orchestrated Search**: `ContentOrchestrator.SearchAsync` broadcasts to all enabled providers
4. **Unified Results**: Providers return fully resolved `ContentSearchResult` objects
5. **Acquisition**: `ContentOrchestrator.AcquireContentAsync` manages the entire process of downloading, validating, and storing the content via the appropriate provider
6. **Profile Integration**: Acquired content becomes available for enabling in GameProfile configurations

### 2.3 Tier 2: Content Providers (Source-Specific Orchestration)

**Primary Responsibility**: Source-specific orchestration that composes pipeline components and handles the complete content lifecycle for particular content sources.

**Provider Architectural Patterns**:

**Pattern 1: Simple Providers** - Handle everything internally without complex pipeline.

```csharp
public class LocalFileSystemContentProvider : BaseContentProvider
{
    // Uses FileSystemDiscoverer, LocalManifestResolver, and FileSystemDeliverer
    // to handle content already on the user's machine.
}
```

**Pattern 2: Pipeline Providers** - Orchestrate a discoverer→resolver→deliverer pipeline.

```csharp
public abstract class BaseContentProvider : IContentProvider
{
    protected abstract IContentDiscoverer Discoverer { get; }
    protected abstract IContentResolver Resolver { get; }
    protected abstract IContentDeliverer Deliverer { get; }
    
    // Implements common pipeline orchestration logic for SearchAsync and PrepareContentAsync
}
```

**Pattern 3: Multi-Component Providers** - Use multiple discoverers/resolvers for flexibility.

```csharp
public class GitHubContentProvider : BaseContentProvider
{
    // This provider is configured with a specific set of discoverers,
    // resolvers, and deliverers relevant to GitHub, allowing it to handle
    // various types of content from that source (e.g., releases, artifacts).
}
```

**Provider Specialization Examples**:

- **GitHubContentProvider**: Orchestrates `GitHubDiscoverer` → `GitHubResolver` → `HttpContentDeliverer`
- **ModDBContentProvider**: Orchestrates `ModDBDiscoverer` → `ModDBResolver` → `HttpContentDeliverer`
- **CNCLabsContentProvider**: Orchestrates `CNCLabsMapDiscoverer` → `CNCLabsMapResolver` → `HttpContentDeliverer`
- **LocalFileSystemContentProvider**: Simple provider handling all operations for local files
- **GeneralsOnlineProvider**: Orchestrates `GeneralsOnlineDiscoverer` → `GeneralsOnlineResolver` → `GeneralsOnlineDeliverer` (specialized for dual 30Hz/60Hz variant creation)
- **SuperHackersProvider**: Uses GitHub pipeline (`GitHubDiscoverer` → `GitHubResolver` → `GitHubContentDeliverer`) for weekly game client releases
- **CommunityOutpostProvider**: Orchestrates `CommunityOutpostDiscoverer` → `CommunityOutpostResolver` → `CommunityOutpostDeliverer` (specialized for community patch extraction)

**Provider Internal Orchestration Flow**:

1. **SearchAsync**: Provider orchestrates Discovery → Resolution → Validation pipeline to produce searchable results
2. **GetContentAsync**: Provider retrieves a complete `ContentManifest` through its internal pipeline
3. **PrepareContentAsync**: Provider orchestrates Acquisition → Delivery → Validation pipeline, preparing content in a temporary location for storage

### 2.4 Tier 3: Pipeline Components (Specialized Operations)

**Primary Responsibility**: Focused, reusable components that handle specific aspects of content processing, composed by providers based on their needs. All components inherit from `IContentSource`.

#### 2.4.1 Content Discovery Components

**IContentDiscoverer Interface**:

- **Primary Purpose**: Lightweight scanning of content sources to identify available packages
- **Key Method**: `DiscoverAsync(ContentSearchQuery)` returning `ContentSearchResult` objects
- **Usage Pattern**: Used by providers requiring a separate discovery phase (web scraping, API polling)

**Specialized Discovery Implementations**:

- **GitHubReleasesDiscoverer**: Monitors configured GitHub repositories for new releases
- **CNCLabsMapDiscoverer**: Scrapes CNC Labs website for maps
- **FileSystemDiscoverer**: Scans local directories for manifest files or recognizable content
- **GeneralsOnlineDiscoverer**: Fetches releases from Generals Online API
- **CommunityOutpostDiscoverer**: Scrapes legi.cc for weekly community patches

**Discovery Coordination**:

- **ContentSearchQuery**: Input specification with SearchTerm, ContentType, TargetGame, Tags, SortOrder
- **ContentSortOrder**: Enumeration supporting Relevance, Name, DateCreated, DownloadCount, Rating sorting

#### 2.4.2 Content Resolution Components

**IContentResolver Interface**:

- **Primary Purpose**: Transform lightweight discovery results into detailed `ContentManifest` blueprints
- **Key Method**: `ResolveAsync(ContentSearchResult)` returning a complete `ContentManifest`
- **Usage Pattern**: Used by providers for discovered content requiring detailed manifest generation

**Specialized Resolution Implementations**:

- **GitHubResolver**: Fetches GitHub release details, analyzes assets, and constructs a `ContentManifest`
- **CNCLabsMapResolver**: Scrapes individual map pages for download URLs and metadata to build a manifest
- **LocalManifestResolver**: Reads `ContentManifest` files directly from the filesystem
- **GeneralsOnlineResolver**: Creates ContentManifest from GeneralsOnline release data
- **CommunityOutpostResolver**: Builds ContentManifest with remote ZIP download URL for community patches

**Resolution Flow**:

1. Discoverer creates a `ContentSearchResult` with `RequiresResolution = true` and a `ResolverId`
2. Provider identifies the appropriate resolver via `ResolverId`
3. Resolver transforms the `ContentSearchResult` into a complete `ContentManifest`
4. Provider validates the manifest and embeds it in the final `ContentSearchResult`

#### 2.4.3 Content Delivery Components

**IContentDeliverer Interface**:

- **Primary Purpose**: Transform `ContentManifest` entries into file-level operations in a target directory
- **Key Methods**:
  - `CanDeliver(ContentManifest)`: Determines if the deliverer can handle a specific manifest
  - `DeliverContentAsync(ContentManifest, targetDirectory)`: Executes content acquisition (e.g., downloading, extracting)

**Specialized Delivery Implementations**:

- **HttpContentDeliverer**: Downloads content from HTTP/HTTPS URLs, handles package extraction
- **FileSystemDeliverer**: Handles content already available on the filesystem, preparing it for storage
- **GitHubContentDeliverer**: Downloads GitHub release assets, extracts ZIPs, creates manifests via factory
- **GeneralsOnlineDeliverer**: Downloads Generals Online ZIP, extracts, creates dual 30Hz/60Hz manifests via factory, registers to CAS
- **CommunityOutpostDeliverer**: Downloads community patch ZIP, extracts, creates manifests via CommunityOutpostManifestFactory, registers to CAS

**Delivery Transformation Process**:
Deliverers receive a `ContentManifest`. The delivery process might involve downloading packages, extracting contents, and verifying files. It produces a final, validated manifest and a directory of content ready for the `IContentStorageService`.

### 2.5 Multi-Component Provider Architecture

**Flexibility for Complex Sources**:
Some content providers need multiple discoverers, resolvers, or deliverers to handle different content types from the same source:

**GitHub Example**:

- **GitHubReleasesDiscoverer**: Finds GitHub releases
- **GitHubArtifactsDiscoverer**: Finds GitHub workflow artifacts  
- **GitHubWorkflowDiscoverer**: Finds GitHub workflow definitions
- **GitHubResolver**: Resolves GitHub release manifests
- **GitHubArtifactResolver**: Resolves GitHub artifact manifests

**ModDB Example**:

- **ModDBModDiscoverer**: Finds ModDB modifications
- **ModDBMapDiscoverer**: Finds ModDB maps
- **ModDBAddonDiscoverer**: Finds ModDB addons
- **ModDBResolver**: Universal ModDB content resolver

This architecture allows providers to select the most appropriate component based on query context or content type, providing maximum flexibility while maintaining clean separation of concerns.

### 2.6 ProfileContentLoader: Content Resolution for Game Profiles

**Primary Responsibility**: Bridge game profile content requirements with the content pipeline, providing seamless content resolution and validation for profile launches.

**Core ProfileContentLoader Architecture**:

- **IProfileContentLoader**: Interface for resolving and validating profile content requirements
- **ProfileContentLoader**: Implementation that orchestrates content resolution for game profiles
- **ContentResolutionRequest**: Input specification with ProfileId, EnabledContentIds, and resolution options
- **ContentResolutionResult**: Comprehensive result with resolved manifests, validation issues, and dependency information

**Content Resolution Flow**:

1. **Profile Content Analysis**: Extract EnabledContentIds from game profile
2. **Manifest Resolution**: Resolve each content ID through IContentManifestPool
3. **Dependency Validation**: Check content compatibility and dependency requirements
4. **Conflict Detection**: Identify conflicting content that cannot be enabled together
5. **Resolution Optimization**: Optimize content loading order and workspace preparation

**Profile-Content Integration Features**:

- **Automatic Content Validation**: Ensures all enabled content is available and compatible
- **Dependency Resolution**: Automatically resolves content dependencies during profile launch
- **Content Conflict Prevention**: Prevents enabling mutually exclusive content
- **Workspace Optimization**: Optimizes content loading for efficient workspace preparation
- **Launch Readiness Verification**: Confirms all content is ready before initiating launch pipeline

## 3. Content Caching Strategy

**Primary Responsibility**: Improve performance and reduce redundant operations through strategic caching at multiple levels.

**Multi-Level Caching Architecture**:

**Result Pattern Integration in Caching Operations**:

**Cache Operation Results**:

- **OperationResult&lt;T&gt;**: Used for cache retrieval and storage operations
- **ValidationResult**: Used for cache validation and integrity checking
- **ProfileOperationResult&lt;T&gt;**: Used for cached profile operations

**Cache Invalidation with Result Patterns**:

```csharp
// Example: Cache invalidation with new result patterns
var invalidateResult = await _cache.InvalidatePatternAsync("content:*");
if (!invalidateResult.Success)
{
    _logger.LogWarning("Cache invalidation failed: {Error}", invalidateResult.FirstError);
    // Continue with stale data rather than failing the operation
}
```

**Error Handling in Cached Operations**:

```csharp
// Example: Cached content search with error handling
var cacheKey = $"search::{query.SearchTerm}::{query.ContentType}";
var cachedResult = await _cache.GetAsync<OperationResult<List<ContentSearchResult>>>(cacheKey);

if (cachedResult != null)
{
    // Return cached result, but check if it contains errors
    if (!cachedResult.Success && cachedResult.FirstError != null)
    {
        _logger.LogWarning("Cached result contains error: {Error}", cachedResult.FirstError);
        // Remove invalid cached result
        await _cache.RemoveAsync(cacheKey);
    }
    else
    {
        return cachedResult;
    }
}
```

**Cache Performance Monitoring**:

- **OperationResult&lt;T&gt;**: Tracks cache hit/miss ratios with performance metrics
- **DownloadResult**: Monitors cached download operations
- **ValidationResult**: Validates cached content integrity

**Level 1: Orchestrator Caching** - System-wide performance optimization

- **Search Result Caching**: `ContentOrchestrator` caches `SearchAsync` results for repeated queries
- **Manifest Caching**: Caches `ContentManifest` objects after successful provider retrieval
- **Cache Invalidation**: Pattern-based invalidation when content is installed/updated

**Level 2: Provider Caching** - Provider-specific optimization  

- **Discovery Result Caching**: Providers can cache discovery results for expensive operations (e.g., API calls)
- **Resolution Caching**: Cache resolved manifests to avoid repeated processing

**Level 3: Component Caching** - Component-specific optimization

- **API Response Caching**: GitHub/ModDB API responses can be cached at the HTTP client level
- **File System Scan Caching**: Local directory scans can be cached by `FileSystemDiscoverer`

**Core Caching Contracts**:

- **IContentManifestPool**: Acts as a long-term cache/database for acquired content manifests
- **IContentManifestPool**: Manifest pool with full CRUD operations and validation
- **IDynamicContentCache**: General-purpose cache with expiration and pattern-based invalidation for transient data like search results
- **MemoryDynamicContentCache**: In-memory implementation using `IMemoryCache`
- **IManifestCache**: Simple in-memory cache for manifests and known CAS hashes used by manifest flows

**Caching Integration Example**:

```csharp
// Orchestrator-level caching
var cacheKey = $"search::{query.SearchTerm}::{query.ContentType}";
var cachedResults = await _cache.GetAsync<List<ContentSearchResult>>(cacheKey);
if (cachedResults != null) return OperationResult<T>.CreateSuccess(cachedResults);

// Execute provider search and cache results
var result = await ExecuteProviderSearchAsync(query);
if(result.Success)
{
    await _cache.SetAsync(cacheKey, result.Data, TimeSpan.FromMinutes(5));
}
return result;
```

**ManifestProvider Fallbacks**:

The content pipeline includes robust fallback mechanisms for manifest resolution when primary providers are unavailable:

- **Provider Chain Resolution**: IManifestProvider implementations are tried in priority order until one succeeds
- **Fallback to Local Manifests**: If remote manifest providers fail, system falls back to locally cached manifests
- **Generated Manifest Creation**: For content without explicit manifests, system can generate basic manifests from file analysis
- **Cross-Provider Manifest Sharing**: Manifests from one provider can be used as fallbacks for content from other providers
- **Offline Mode Support**: System maintains functionality with cached manifests when network providers are unavailable

## 4. Game Profile Management and Runtime Orchestration

### 4.1 Profile Management Infrastructure

**Result Pattern Integration in Profile Management**:

**Profile Operation Results**:

- **ProfileOperationResult&lt;T&gt;**: Used for all profile CRUD operations with validation
- **ValidationResult**: Used for profile validation with detailed issue tracking
- **OperationResult&lt;T&gt;**: Used for content-related profile operations

**Launch Operation Results**:

- **LaunchOperationResult&lt;T&gt;**: Used for launch operations with session tracking
- **OperationResult&lt;T&gt;**: Used for process management during launches
- **LaunchResult**: Used for simple launch status reporting

**Profile Management Error Handling**:

```csharp
// Example: Profile creation with new result patterns
var createResult = await _profileManager.CreateProfileAsync(request);
if (!createResult.Success)
{
    _logger.LogError("Profile creation failed: {Error}", createResult.FirstError);

    // Handle validation errors specifically
    if (createResult.ValidationErrors.Any())
    {
        foreach (var validationError in createResult.ValidationErrors)
        {
            _logger.LogWarning("Validation error: {Error}", validationError);
        }
    }

    return ProfileOperationResult<GameProfile>.CreateFailure(
        createResult.ValidationErrors,
        createResult.FirstError);
}
```

**Launch Orchestration with Result Patterns**:

```csharp
// Example: Profile launch with comprehensive result handling
var launchResult = await _gameLauncher.LaunchProfileAsync(profileId);
if (!launchResult.Success)
{
    _logger.LogError("Profile launch failed: {Error}", launchResult.FirstError);

    // Handle different launch phases
    if (launchResult.Data?.Phase == LaunchPhase.ValidatingProfile)
    {
        // Profile validation failed
        return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
            launchResult.FirstError,
            profileId: profileId);
    }
}
```

**Progress Reporting in Profile Operations**:

- **LaunchProgress**: Tracks launch pipeline progress with phase information
- **WorkspacePreparationProgress**: Reports workspace creation progress
- **ContentAcquisitionProgress**: Tracks content resolution progress

**Primary Responsibility**: Provide comprehensive game profile management with CRUD operations, validation, and integration with content and workspace systems.

**Core Profile Management Architecture**:

- **IGameProfileManager**: High-level service interface with comprehensive profile operations
- **GameProfileManager**: Implementation handling business logic, validation, and orchestration with other systems
- **IGameProfileRepository**: Data persistence abstraction for profile storage
- **GameProfileRepository**: File-based implementation with JSON serialization and atomic operations

**Profile Management Operations**:

1. **CreateProfileAsync**: Creates new profiles with validation and uniqueness checks
2. **UpdateProfileAsync**: Updates existing profiles with partial modification support
3. **DeleteProfileAsync**: Removes profiles with cleanup and validation
4. **GetProfileAsync**: Retrieves individual profiles with caching
5. **GetAllProfilesAsync**: Lists all profiles with filtering and sorting options
6. **ValidateProfileAsync**: Comprehensive profile validation including content and workspace compatibility

**Profile Validation Framework**:
The profile management system includes comprehensive validation:

- **Profile Uniqueness**: Ensures profile names and IDs are unique
- **GameClient Compatibility**: Validates that referenced GameClient exists and is compatible
- **Content Validation**: Verifies that EnabledContentIds reference valid, available content
- **Workspace Strategy Validation**: Ensures selected workspace strategy is supported and compatible
- **Launch Configuration Validation**: Validates launch arguments and environment variables

**UI Workflow Integration**:
MainViewModel exposes ScanAndCreateProfilesAsync: scans installations, creates a default profile per installation (via IProfileEditorFacade), auto-enables base content (GameInstallation, optionally GameClient), resolves dependencies, prepares a workspace, and persists ActiveWorkspaceId.

### 4.2 Launch Orchestration Infrastructure

**Primary Responsibility**: Transform prepared workspaces into running game processes with comprehensive monitoring and management.

**Core Launch Architecture**:

- **IGameLauncher**: Primary launch interface with comprehensive launch operations
- **GameLauncher**: Implementation handling the complete launch pipeline from profile to running process
- **GameLaunchConfiguration**: Launch specification with executable, arguments, environment, and working directory
- **GameLaunchInfo**: Launch session descriptor with process information, workspace details, and timing
- **LaunchProgress**: Detailed progress reporting throughout the launch pipeline
- **LaunchPhase**: Phase enumeration covering the entire launch process

**Launch Pipeline Flow**:
The launch process follows a comprehensive, monitored pipeline:

1. **ValidatingProfile** (0-10%): Verify profile exists and is properly configured
2. **ResolvingContent** (10-40%): Resolve all enabled content through manifest pools
3. **PreparingWorkspace** (40-70%): Create isolated workspace using configured strategy
4. **Starting** (70-90%): Configure and start game process
5. **Running** (90-100%): Register launch session and begin monitoring

**Launch Preflight and Base Installation Handling**:

- **CAS Preflight**: Validates all CAS hashes referenced by manifests exist before workspace preparation (fail early)
- **Base Installation Guarantee**: If a base installation manifest is available via IManifestProvider and not explicitly enabled, the launcher injects it into the workspace configuration

**Process Management**:

- **IGameProcessManager**: Process lifecycle management
- **GameProcessManager**: Cross-platform implementation with process monitoring, cleanup, and resource management
- **ProcessOperationResult**: Specialized result types for process operations with detailed error reporting

**Launch Session Management**:

- **ILaunchRegistry**: Launch session tracking and management
- **LaunchRegistry**: Implementation maintaining active sessions with persistence and recovery
- **GameLaunchInfo**: Comprehensive launch session descriptor with process info, workspace details, and timing information

### 4.3 Profile-to-Launch Integration Pipeline

**Comprehensive Integration Flow**:
The system provides seamless integration between profile management and launch orchestration:

1. **Profile Selection**: User selects GameProfile for launch
2. **Profile Validation**: Validate profile configuration and dependencies
3. **Content Resolution**: Resolve EnabledContentIds through IContentManifestPool
4. **Workspace Preparation**: Create workspace using profile's PreferredStrategy
5. **Launch Configuration**: Build GameLaunchConfiguration from profile settings
6. **Process Creation**: Start game process through IGameProcessManager
7. **Session Registration**: Register active launch through ILaunchRegistry
8. **Runtime Monitoring**: Provide termination and monitoring capabilities

**Profile-Workspace Integration**:
GameProfile objects seamlessly integrate with the workspace system:

- **Strategy Selection**: Profile's PreferredStrategy determines workspace creation approach
- **Content Integration**: EnabledContentIds are resolved to manifests for workspace preparation
- **Launch Customization**: Profile's LaunchArguments and EnvironmentVariables customize process creation
- **Isolation**: Each profile launch uses an isolated workspace preventing conflicts

### 4.4 Additional Services and Interfaces

**Profile Content Integration Services**:

- **IProfileContentLoader**: Orchestrates content resolution for game profiles, ensuring all enabled content is available and compatible
- **IProfileEditorFacade**: Provides high-level profile editing operations for UI integration, including ScanAndCreateProfilesAsync

**Manifest and Content Services**:

- **IManifestProvider**: Provides base installation manifests with fallback chain resolution
- **IContentManifestPool**: Long-term cache and database for acquired content manifests with full CRUD operations
- **IContentStorageService**: Manages content storage operations and CAS integration

**Launch and Process Services**:

- **ILaunchRegistry**: Tracks active launch sessions with persistence and recovery capabilities
- **IGameProcessManager**: Cross-platform process lifecycle management with monitoring and cleanup
- **IProcessMonitor**: Real-time process monitoring and health checking

**Validation and Compatibility Services**:

- **IWorkspaceValidator**: Ensures workspace integrity and validates file operations
- **IGameInstallationValidator**: Validates game installation compatibility and requirements
- **IContentValidator**: Validates content integrity and security before installation

## 5. Data Models and Type System

### 5.1 Enumeration Type System

**Game Classification Types**:

- **GameType**: Distinguishes Generals versus ZeroHour variants
- **GameInstallationType**: Categorizes Steam, EaApp, Origin, Manual installation sources
- **ContentType**: Comprehensive content classification including BaseGame, StandaloneVersion, Mod, Patch, Addon, MapPack, LanguagePack, ContentBundle, PublisherReferral, ContentReferral

**Operational Control Types**:

- **WorkspaceStrategy**: Defines file assembly approaches including FullCopy, SymlinkOnly, HybridCopySymlink, HardLink
- **ContentSourceType**: Specifies file operations including ContentAddressable, GameInstallation, LocalFile, RemoteDownload, ExtractedPackage, PatchFile
  - Workspace strategies currently handle ContentAddressable, GameInstallation, LocalFile; the others require pre-resolution
- **DependencyInstallBehavior**: Controls dependency installation with Required, Optional, Recommended, Conflicting values
- **ContentSortOrder**: Enables search result organization by Relevance, Name, DateCreated, DateUpdated, DownloadCount, Rating, Size
- **ContentSourceCapabilities**: Defines provider capabilities including DirectSearch, RequiresDiscovery, SupportsManifestGeneration, SupportsPackageAcquisition, LocalFileDelivery
- **ContentAcquisitionPhase**: Tracks the stages of content installation, such as `Downloading`, `Extracting`, `Validating`, `Delivering`

**Launch and Process Types**:

- **LaunchPhase**: Defines launch pipeline stages including ValidatingProfile, ResolvingContent, PreparingWorkspace, Starting, Running
- **ProcessState**: Tracks process lifecycle states

**UI and Navigation Types**:

- **NavigationTab**: Application section enumeration including Home, Library, Discover, Downloads, Settings
- **ContentProviderType**: Provider categorization with FileSystem, Http, Git, Registry, Steam, ModDb values

### 5.2 Progress and Result Type System

**Operation Result Hierarchy**:

- **ResultBase**: Abstract foundation with Success, Errors, FirstError (computed), Elapsed, CompletedAt properties
- **ContentOperationResult\<T\>**: Generic content operation wrapper with Success, Data, FirstError, Errors, Elapsed
- **DetectionResult**: Specialized for installation and version detection operations
- **ValidationResult**: Focused on validation operations with `ValidationIssue` collections
- **LaunchResult**: Basic game launching operation results
- **LaunchOperationResult\<T\>**: Launch operation results with LaunchId and ProfileId tracking
- **ProcessOperationResult\<T\>**: Process operation results with detailed process information
- **ProfileOperationResult\<T\>**: Profile operation results with validation and error details
- **DownloadResult**: File download operation results with BytesDownloaded, HashVerified, AverageSpeedBytesPerSecond

**Progress Reporting Hierarchy**:

- **ContentAcquisitionProgress**: Content acquisition pipeline progress with Phase, PercentComplete, CurrentOperation, FilesProcessed, TotalFiles
- **DownloadProgress**: File download progress with BytesReceived, TotalBytes, Percentage, BytesPerSecond, FormattedProgress, FormattedSpeed
- **WorkspacePreparationProgress**: Workspace assembly progress with FilesProcessed, TotalFiles, CurrentOperation, PercentComplete
- **LaunchProgress**: Launch pipeline progress with Phase, PercentComplete, CurrentOperation
- **ValidationProgress**: Validation operation progress with CurrentItem, TotalItems, CurrentOperation

### 5.3 Configuration and Metadata Models

**Game Profile Models**:

- **GameProfile**: Comprehensive profile model containing:
  - Identity: Id, Name, Description
  - Base game: GameClient (the game version this profile uses), GameInstallationId, ExecutablePath
  - Content: EnabledContentIds (list of manifest IDs for enabled content)
  - Workspace: WorkspaceStrategy (HybridCopySymlink default), CustomExecutablePath, WorkingDirectory, ActiveWorkspaceId
  - Launch: CommandLineArguments (string), LaunchOptions (Dictionary), EnvironmentVariables (Dictionary)
  - Video settings: ResolutionWidth, ResolutionHeight, Windowed, TextureQuality, Shadows, ParticleEffects, ExtraAnimations, BuildingAnimations, Gamma (0-100 range, maps to in-game gamma setting)
  - Audio settings: SoundVolume, ThreeDSoundVolume, SpeechVolume, MusicVolume, AudioEnabled, NumSounds
  - UI: ThemeColor, IconPath, BuildInfo
  - Timestamps: CreatedAt, LastPlayedAt
- **CreateProfileRequest**: Profile creation DTO with GameInstallationId, GameClientId, PreferredStrategy, EnabledContentIds, CommandLineArguments, ThemeColor, IconPath
- **UpdateProfileRequest**: Profile update DTO with partial modification support (Name, Description, EnabledContentIds, PreferredStrategy, LaunchArguments, EnvironmentVariables, CustomExecutablePath)
- **ProfileInfoItem**: UI-optimized profile display model

**Launch Configuration Models**:

- **GameLaunchConfiguration**: Comprehensive launch specification with executable, arguments, environment, and working directory
- **GameLaunchInfo**: Launch session descriptor with LaunchId, ProfileId, WorkspaceId, ProcessInfo, LaunchedAt, TerminatedAt
- **GameProcessInfo**: Process runtime information with ProcessId, ProcessName, ExecutablePath, StartTime, IsRunning

**Workspace Configuration Models**:

- **WorkspaceConfiguration**: Input specification with Id, Manifests, Strategy, WorkspaceRootPath, BaseInstallationPath, GameClient, ReconciliationDeltas
- **WorkspaceInfo**: Result description with Id, WorkspacePath, GameVersionId, Strategy, FileCount, TotalSizeBytes, ExecutablePath, WorkingDirectory, Success, ValidationIssues
- **FilePermissions**: Cross-platform file permission specification

**Content Metadata Models**:

- **ContentMetadata**: Rich content description with Description, Tags, IconUrl, ScreenshotUrls, ReleaseDate
- **PublisherInfo**: Content creator information with Name, Website, SupportUrl, ContactEmail
- **ContentDependency**: Prerequisite specification with comprehensive version constraints
- **ContentReference**: Cross-publisher content linking with ContentId, PublisherId, ContentType
- **ContentBundle**: Content collection with BundleId, BundleName, BundleVersion, Items
- **BundleItem**: Individual bundle component with ContentId, Name, ContentType, IsRequired

**Download and Network Models**:

- **DownloadConfiguration**: Comprehensive download specification with Url, DestinationPath, ExpectedHash, MaxRetryAttempts
- **GitHubRelease**: GitHub release metadata with TagName, Name, Body, HtmlUrl, Assets
- **GitHubReleaseAsset**: Individual release asset with Name, Size, BrowserDownloadUrl, ContentType

---

## 6. Dependency Injection and Service Registration

### 6.1 Modular Service Architecture

**Core Service Modules**:

- **AppServices**: Application-wide service registration and configuration
- **ContentPipelineModule**: **Primary module** for three-tier pipeline registration
- **GameDetectionModule**: Installation and version detection service registration
- **GameProfileModule**: **New module** for profile management and launching services
- **WorkspaceModule**: workspace management and strategy registration with CAS integration
- **ManifestModule**: manifest creation, caching, and management services
- **ValidationModule**: Validation service infrastructure
- **LoggingModule**: Logging infrastructure and configuration
- **CasModule**: Content Addressable Storage service registration

**GameProfileModule Registration**:
The new `GameProfileModule` provides comprehensive registration for the game profile and launching systems:

```csharp
public static class GameProfileModule
{
    public static IServiceCollection AddGameProfileServices(this IServiceCollection services, IConfigurationProviderService configProvider)
    {
        // Profile Management Services
        services.AddSingleton<IGameProfileRepository>(provider =>
            new GameProfileRepository(profilesPath, provider.GetRequiredService<ILogger<GameProfileRepository>>()));
        services.AddScoped<IGameProfileManager, GameProfileManager>();
        
        // Process Management Services
        services.AddSingleton<IGameProcessManager, GameProcessManager>();
        
        return services;
    }
    
    public static IServiceCollection AddLaunchingServices(this IServiceCollection services, IConfigurationProviderService configProvider)
    {
        // Launch Registry and Management
        services.AddSingleton<ILaunchRegistry, LaunchRegistry>();
        services.AddScoped<IGameLauncher, GameLauncher>();
        
        return services;
    }
}
```

**ContentPipelineModule Registration**:
The `ContentPipelineModule` now follows a three-tier registration pattern with caching and storage integration:

```csharp
public static class ContentPipelineModule
{
    public static IServiceCollection AddContentPipelineServices(this IServiceCollection services)
    {
        var hashProvider = new Sha256HashProvider();
        services.AddSingleton<IFileHashProvider>(hashProvider);
        services.AddSingleton<IStreamHashProvider>(hashProvider);

        services.AddMemoryCache();

        services.AddSingleton<IContentStorageService, ContentStorageService>();
        services.AddScoped<IContentManifestPool, ContentManifestPool>();

        services.AddSingleton<IContentOrchestrator, ContentOrchestrator>();
        services.AddSingleton<IDynamicContentCache, MemoryDynamicContentCache>();

        services.AddSingleton<IGitHubApiClient, OctokitGitHubApiClient>();

        services.AddTransient<IContentProvider, GitHubContentProvider>();
        services.AddTransient<IContentProvider, CNCLabsContentProvider>();
        services.AddTransient<IContentProvider, ModDBContentProvider>();
        services.AddTransient<IContentProvider, LocalFileSystemContentProvider>();

        services.AddTransient<IContentDiscoverer, GitHubDiscoverer>();
        services.AddTransient<IContentDiscoverer, GitHubReleasesDiscoverer>();
        services.AddTransient<IContentDiscoverer, CNCLabsMapDiscoverer>();
        services.AddTransient<IContentDiscoverer, FileSystemDiscoverer>();

        services.AddTransient<IContentResolver, GitHubResolver>();
        services.AddTransient<IContentResolver, CNCLabsMapResolver>();
        services.AddTransient<IContentResolver, LocalManifestResolver>();

        services.AddTransient<IContentDeliverer, HttpContentDeliverer>();
        services.AddTransient<IContentDeliverer, FileSystemDeliverer>();

        return services;
    }
}
```

**WorkspaceModule Registration**:
The `WorkspaceModule` includes CAS integration and validation:

```csharp
public static class WorkspaceModule
{
    public static IServiceCollection AddWorkspaceServices(this IServiceCollection services)
    {
        // Register workspace strategies
        services.AddTransient<IWorkspaceStrategy, FullCopyStrategy>();
        services.AddTransient<IWorkspaceStrategy, SymlinkOnlyStrategy>();
        services.AddTransient<IWorkspaceStrategy, HybridCopySymlinkStrategy>();
        services.AddTransient<IWorkspaceStrategy, HardLinkStrategy>();

        // Register workspace manager with CAS integration
        services.AddScoped<IWorkspaceManager, WorkspaceManager>();
        
        // Register workspace validator
        services.AddScoped<IWorkspaceValidator, WorkspaceValidator>();

        return services;
    }
}
```

### 6.2 Hierarchical Service Resolution

**Six-Tier Resolution Pattern**:

1. **GameLauncher** receives `IGameProfileManager`, `IWorkspaceManager`, `IContentManifestPool`, `IGameProcessManager`, `ILaunchRegistry`
2. **GameProfileManager** receives `IGameProfileRepository`, `IGameInstallationService`, `IContentManifestPool`
3. **WorkspaceManager** receives `IWorkspaceStrategy` collection, `ICasService`, `CasReferenceTracker`
4. **ContentOrchestrator** receives all registered `IContentProvider` instances
5. **ContentProviders** receive collections of `IContentDiscoverer`, `IContentResolver`, `IContentDeliverer`
6. **Pipeline Components** are resolved independently with their specific dependencies

**Service Lifecycle Management**:

- **Core Services** (ContentOrchestrator, CAS services, Repositories): Singleton for system-wide coordination and data consistency
- **Business Logic Services** (GameProfileManager, GameLauncher, WorkspaceManager): Scoped for request isolation
- **ContentProviders**: Transient for flexible pipeline composition
- **Pipeline Components**: Transient for stateless operation handling
- **IContentManifestPool**: Scoped (not Singleton)
- **IGameLauncher**: Scoped (factory)
- **ILaunchRegistry**: Singleton
- **IManifestProvider**: Scoped
- **IGameProcessManager**: Singleton
- **Hosted services**: CasMaintenanceService and ManifestInitializationService

---

## 7. Cross-Platform Compatibility Architecture

### 7.1 Platform Abstraction Strategy

**Platform-Specific Implementations**:

- **WindowsInstallationDetector**: Windows-specific registry scanning and EA App/Steam library detection
- **LinuxInstallationDetector**: Linux-specific Steam Proton and Wine prefix detection  
- **WindowsUpdateInstaller**: Windows-specific application update installation
- **LinuxUpdateInstaller**: Linux-specific update installation procedures
- **WindowsFileOperationsService**: Windows-specific file operations with NTFS features
- **GameProcessManager**: Cross-platform process management with platform-specific optimizations

**Shared Interface Contracts**:
All platform-specific implementations conform to common interfaces (IGameInstallationDetector, IPlatformUpdateInstaller, IFileOperationsService, IGameProcessManager), enabling polymorphic behavior across platforms. The dependency injection system automatically resolves platform-appropriate implementations based on runtime environment detection.

### 7.2 File System Abstraction

**Cross-Platform File Operations**:
The IFileOperationsService interface abstracts platform-specific file operations including symbolic link creation, hard link management, file permission handling, and CAS integration. The FileOperationsService implementation includes platform-specific logic for Windows CreateHardLinkW API calls versus Unix-style operations.

**Process Management Abstraction**:
The IGameProcessManager interface provides cross-platform process lifecycle management, abstracting Windows-specific process creation APIs and Unix-style process management while providing consistent process monitoring and termination capabilities. Handles Windows .bat/.cmd via cmd /c, robust argument quoting, and environment injection.

**Path and Directory Handling**:
The system uses Path.Combine and similar .NET abstractions for cross-platform path construction while handling platform-specific nuances like case sensitivity, path separators, symbolic link support, and file permission models. Symlink strategies require elevation on Windows; hard links require same-volume constraint (the strategies surface these via RequiresAdminRights and RequiresSameVolume, and WorkspaceValidator checks both).

---

## 8. Real-World Implementation Examples

### 8.1 Complete End-to-End Game Profile Launch Workflow

**User-Initiated Profile Launch**:
User selects "My Modded Zero Hour" profile from the game profile launcher interface.

**Launch Pipeline Execution**:

1. **Profile Validation** (ValidatingProfile Phase): `GameLauncher.LaunchProfileAsync` validates the profile through `IGameProfileManager`
2. **Content Resolution** (ResolvingContent Phase): Resolves all `EnabledContentIds` through `IContentManifestPool`
   - Base game content from Steam installation
   - GenTool mod from GitHub provider
   - Custom maps from CNC Labs provider
   - Community patches from ModDB provider
3. **Workspace Preparation** (PreparingWorkspace Phase): `IWorkspaceManager.PrepareWorkspaceAsync` creates isolated workspace
   - Uses profile's `PreferredStrategy` (e.g., HybridCopySymlink)
   - Copies essential executables and configurations
   - Creates symbolic links for large media files
   - Links CAS content using hash-based references
   - Tracks CAS references through `CasReferenceTracker`
4. **Process Configuration** (Starting Phase): Builds `GameLaunchConfiguration`
   - Uses profile's `LaunchArguments` for game-specific parameters
   - Applies profile's `EnvironmentVariables` for compatibility
   - Sets workspace as working directory
5. **Process Creation**: `IGameProcessManager.StartProcessAsync` launches the game
6. **Session Registration**: `ILaunchRegistry.RegisterLaunchAsync` tracks the active session
7. **Runtime Monitoring** (Running Phase): Provides termination and monitoring capabilities

**Launch Enhancements**:

- **Profile-level concurrency locks**: Prevents duplicate launch of the same profile
- **CAS preflight**: Validates all CAS hashes referenced by manifests exist before workspace preparation (fail early)
- **Base installation guarantee**: If a base installation manifest is available via IManifestProvider and not explicitly enabled, the launcher injects it into the workspace configuration
- **Direct launch path**: LaunchGameAsync(GameLaunchConfiguration) for non-profile launching

**Multi-Provider Content Integration**:
The launched profile seamlessly integrates content from multiple sources:

- **Base Game**: From Steam installation detected by WindowsInstallationDetector
- **Primary Mod**: Acquired through GitHubContentProvider pipeline
- **Maps**: Acquired through CNCLabsContentProvider pipeline
- **Patches**: Acquired through ModDBContentProvider pipeline
- **CAS Content**: Linked from content-addressable storage for shared assets### 8.2 Complex Profile Management with Content Dependencies

**Profile Creation Workflow**:
User creates "Tournament Setup" profile with complex content dependencies.

**Profile Management Flow**:

1. **Profile Creation Request**: User submits `CreateProfileRequest` through UI
2. **Validation Pipeline**: `GameProfileManager.CreateProfileAsync` validates:
   - Profile name uniqueness
   - GameClient compatibility and existence
   - Content dependency resolution through `IContentManifestPool`
   - Workspace strategy compatibility
3. **Content Dependency Resolution**: System validates that all required content is available:
   - Checks each `EnabledContentId` against manifest pools
   - Validates content compatibility and dependencies
   - Ensures no conflicting content is enabled
4. **Profile Persistence**: `IGameProfileRepository.SaveProfileAsync` persists the validated profile
5. **Cache Integration**: Profile becomes available through caching layer for fast access

**Content Management Integration**:
The profile system seamlessly integrates with content management:

- **Content Discovery**: Users can browse and install content through the three-tier pipeline
- **Profile Integration**: Installed content automatically becomes available for profile configuration
- **Dependency Management**: System automatically resolves and validates content dependencies
- **Conflict Resolution**: System prevents enabling conflicting content within profiles

**Scan and Auto-Create Profiles Example**:
User clicks "Scan and Create Profiles" → detects installations → creates profiles → auto-enables base content (GameInstallation, optionally GameClient) → resolves dependencies → prepares workspace (HybridCopySymlink by default) → persists ActiveWorkspaceId.

### 8.3 Advanced Workspace and CAS Integration

**CAS-Integrated Workspace Creation**:
Profile launch with mixed local and CAS content demonstrates advanced integration.

**Workspace Preparation Flow**:

1. **Strategy Selection**: Profile's `PreferredStrategy` determines workspace approach
2. **Content Classification**: `HybridCopySymlinkStrategy` intelligently classifies files:
   - **Copy**: Executables, configurations, small essential files
   - **Symlink**: Large media files, textures, audio
   - **CAS Link**: Content-addressable files using hash-based references
3. **CAS Reference Resolution**: `WorkspaceManager` coordinates with `ICasService`:
   - Resolves CAS hashes to physical file paths
   - Creates appropriate links (copy/symlink) from CAS storage
   - Tracks workspace CAS references through `CasReferenceTracker`
4. **File Operations**: `IFileOperationsService` executes cross-platform operations:
   - Handles Windows-specific hardlinks and NTFS features
   - Creates Unix-style symbolic links on Linux platforms
   - Manages file permissions and executable flags
5. **Validation**: `IWorkspaceValidator` ensures workspace integrity:
   - Validates all links and copies are successful
   - Checks file integrity against expected hashes
   - Verifies executable permissions and accessibility

**CAS Lifecycle Management**:
The system provides comprehensive CAS lifecycle management:

- **Reference Tracking**: Each workspace tracks its CAS dependencies
- **Cleanup Coordination**: Workspace deletion automatically unreferences CAS objects
- **Garbage Collection**: Unused CAS objects can be safely removed
- **Deduplication**: Multiple workspaces can share CAS content efficiently

### 8.4 GitHub Manager Content Acquisition Workflow

**GitHub Content Discovery and Installation**:
User discovers and installs community content through the GitHub Manager feature.

**End-to-End Workflow**:

1. **Repository Configuration**: User opens GitHub Manager and configures TheSuperHackers repository
2. **Content Discovery**: GitHubDiscoverer scans repository releases through GitHub API
   - Fetches available releases and tags
   - Applies content type inference heuristics
   - Detects game type compatibility
   - Returns ContentSearchResult objects to UI
3. **User Selection**: User browses releases in GitHub Manager window and selects desired release
4. **Installation Request**: User clicks "Install" triggering content acquisition pipeline
5. **Pipeline Orchestration**: ContentOrchestrator routes request to GitHubContentProvider
   - Provider coordinates GitHubDiscoverer → GitHubResolver → GitHubContentDeliverer
6. **Content Resolution**: GitHubResolver transforms release into ContentManifest
   - Fetches complete release metadata from GitHub
   - Analyzes release assets for file structure
   - Generates ContentManifest with download URLs
7. **Content Delivery**: GitHubContentDeliverer executes download and extraction
   - Downloads ZIP archives from GitHub release assets
   - Detects GameClient content type
   - Extracts archives to target directory
   - Recursively scans extracted files
   - Marks executable files (.exe) as IsExecutable
   - Builds updated ContentManifest with all extracted files
   - Returns updated manifest to ContentOrchestrator
8. **Content Storage and Validation**: ContentOrchestrator finalizes acquisition
   - Validates delivered content against manifest
   - Reports validation progress to UI
   - Adds validated manifest to IContentManifestPool
   - Cleans up staging directories
9. **Profile Integration**: Manifests available through ProfileContentLoader
   - Manifests validated for completeness
   - Added to pool with deterministic ManifestId
   - Indexed by GameType for profile queries
10. **Profile Integration**: Content immediately available
    - Appears in GameProfile content dropdowns
    - User enables content in profile
    - Workspace preparation includes GitHub content
    - Game launches successfully with modifications

**GitHub Pipeline Components**:

**GitHubDiscoverer**:

- Queries GitHub API for repository releases
- Implements rate limiting and authentication
- Caches API responses for performance
- Filters and classifies release types

**GitHubResolver**:

- Transforms lightweight search results into full manifests
- Analyzes release assets and metadata
- Generates file lists with download URLs
- Preserves publisher and version information

**GitHubContentDeliverer**:

- Downloads assets from GitHub URLs
- Extracts ZIP archives for GameClient content
- Recursively scans extracted files and marks executables
- Builds updated manifests with extracted file references
- Returns manifests to ContentOrchestrator for validation and storage

**Integration Benefits**:
The GitHub Manager provides seamless content integration:

- **Zero-Configuration Discovery**: Automatic detection of game content in releases
- **Type Inference**: Intelligent content and game type classification
- **Executable Detection**: Automatic scanning for game clients
- **Manifest Generation**: Automated ContentManifest creation from extracted content
- **Immediate Availability**: Content ready for use immediately after installation
- **Profile Integration**: Direct enablement in GameProfiles without manual configuration
- **Workspace Compatibility**: GitHub content works with all workspace strategies
- **Error Recovery**: Robust handling of download failures and extraction errors

**Real-World Example - TheSuperHackers Content**:

1. User configures TheSuperHackers/ZeroHour repository
2. System discovers latest release with game client ZIP
3. User clicks install on release
4. System downloads and extracts 200MB ZIP archive
5. Deliverer scans extracted files and marks executables
6. Manifest generated with all extracted files and IsExecutable flags
7. ContentOrchestrator validates and adds manifest to pool
8. Content appears in profile UI within seconds via ProfileContentLoader
9. User enables in "My Modded Setup" profile
10. Workspace prepared with TheSuperHackers content
11. Game launches with community modifications active

This comprehensive architecture analysis demonstrates GenHub's sophisticated approach to solving C&C Generals/Zero Hour ecosystem complexity through a **six-pillar architectural foundation** with **three-tier content pipeline**, enhanced by comprehensive **game profile management**, **runtime orchestration**, and **GitHub integration** systems. The architecture enables seamless integration of content from diverse sources including GitHub repositories, provides isolated execution environments, and offers comprehensive game profile management while maintaining user experience simplicity and system reliability through well-defined contracts and flexible component composition.
