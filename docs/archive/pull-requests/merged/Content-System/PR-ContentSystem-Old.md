# PRD: GenHub Content System – Four-Layer Discovery, Delivery, and Assembly Architecture

## Executive Summary
GenHub's content system is designed to unify the fragmented modding ecosystem for C&C Generals/Zero Hour. It supports real-world sources (ModDB, CNCLabs, GitHub, local) and handles both package-based and file-based content. The architecture is a four-layer pipeline: Discovery, Resolution, Acquisition, and Assembly, fully integrated with GameProfiles and workspace management.

---

## 1. The Four-Layer Content Architecture

### 1.1 Architectural Principles
- **Package vs File Distinction**: Some sources provide packages (ZIPs), others provide individual files (GitHub assets)
- **Transformation Pipeline**: Manifests evolve through the pipeline – starting with package references, ending with specific file operations
- **Separation of Concerns**: Discovery finds content, Resolution understands it, Acquisition gets it, Assembly installs it
- **Provider Specialization**: Each provider type handles acquisition differently based on the content source

### 1.2 Layer Responsibilities
```
Layer 1: Discovery (IContentDiscoverer)
         ↓ (DiscoveredContent)
Layer 2: Resolution (IContentResolver)
         ↓ (GameManifest with Package downloads)
Layer 3: Acquisition (IContentProvider)
         ↓ (GameManifest with real file operations)
Layer 4: Assembly (IWorkspaceStrategy)
         ↓ (Ready workspace)
```

---

## 2. Layer Definitions & Responsibilities

### 2.1 Layer 1: Content Discovery
- **Purpose**: Find available content without knowing installation details
- **Input**: Search queries from user
- **Output**: `DiscoveredContent` objects with basic metadata
- **Responsibility**: Scan content sources, extract basic information (name, author, page URL)

### 2.2 Layer 2: Content Resolution
- **Purpose**: Convert discovered content into installation blueprints
- **Input**: `DiscoveredContent` from discovery layer
- **Output**: `GameManifest` with package-level download instructions
- **Responsibility**: Understand content structure, create initial manifest

### 2.3 Layer 3: Content Acquisition
- **Purpose**: Transform package-level manifests into file-level manifests by acquiring content
- **Input**: `GameManifest` with Package downloads
- **Output**: `GameManifest` with specific file operations (Copy, Symlink, Remote, Patch)
- **Responsibility**: Download packages, extract contents, scan file structure, update manifest

### 2.4 Layer 4: Workspace Assembly
- **Purpose**: Execute file operations to create ready workspace
- **Input**: `GameManifest` with specific file operations
- **Output**: `WorkspaceInfo` with ready-to-launch game setup
- **Responsibility**: Copy, symlink, download, and patch files according to manifest

---

## 3. Provider Specializations

### 3.1 HttpContentProvider (ModDB, CNCLabs)
- **Content Type**: Downloadable packages (ZIP, RAR, installers)
- **Acquisition Process**: Download → Extract → Scan → Transform manifest
- **File Operations Created**: Mostly `Copy` (from temp extraction), some `Patch`

### 3.2 GitHubProvider
- **Content Type**: Individual file assets on releases
- **Acquisition Process**: No-op (returns manifest unchanged)
- **File Operations Created**: `Remote` (direct downloads during workspace assembly)

### 3.3 FileSystemProvider
- **Content Type**: Local directories with manifests
- **Acquisition Process**: No-op (content already available locally)
- **File Operations Created**: `Copy` (from local directories), `Symlink`

---

## 4. Models & Interfaces

### 4.1 IContentProvider Interface
```csharp
public interface IContentProvider : IContentSource
{
    Task<ContentOperationResult<GameManifest>> AcquireContentAsync(
        GameManifest packageManifest,
        CancellationToken cancellationToken = default);
    Task<ContentOperationResult<GameManifest>> GetManifestAsync(
        string contentId,
        CancellationToken cancellationToken = default);
}
```

### 4.2 ManifestFileSourceType
```csharp
public enum ManifestFileSourceType
{
    Copy, CopyUnique, Symlink, Hardlink, Remote, Patch, Package
}
```

### 4.3 ContentAcquisitionProgress
```csharp
public class ContentAcquisitionProgress
{
    public string Phase { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
}
```

---

## 5. Installation Flow Examples

### 5.1 ModDB Mod Installation
```
1. User searches "Zero Hour mods"
2. ModDbDiscoverer → DiscoveredContent("Zero Hour Reborn", "https://moddb.com/mods/zhr")
3. User clicks Install
4. ModDbResolver.ResolveManifestAsync(): Scrapes mod page, creates manifest with Package entry
5. ContentDiscoveryService.InstallContentAsync(): Gets HttpContentProvider, downloads and extracts package, transforms manifest
6. WorkspaceStrategy.ProcessManifestFilesAsync(): Copies mod files from temp extraction to workspace
```

### 5.2 GitHub Release Installation
```
1. User searches for GitHub content
2. GitHubDiscoverer → DiscoveredContent with release info
3. User clicks Install
4. GitHubResolver.ResolveManifestAsync(): Gets release details, creates manifest with Remote entries
5. ContentDiscoveryService.InstallContentAsync(): Gets GitHubProvider, passes manifest to WorkspaceManager
6. WorkspaceStrategy.ProcessManifestFilesAsync(): Downloads each asset directly to workspace
```

---

## 6. Benefits of Architecture
- **Separation of Concerns**: Each layer has a clear responsibility
- **Source-Specific Optimization**: ModDB downloads/extracts, GitHub direct downloads, Local immediate availability
- **Progress Reporting**: Accurate for downloads, extraction, and file operations
- **Error Handling**: Can retry downloads, re-extract, rebuild workspace
- **Caching & Performance**: Packages and extractions can be cached and reused

---

## 7. Files Added / Modified

### Core Project (`GenHub.Core`)
* Interfaces/Content/IContentDiscoverer.cs (new)
* Interfaces/Content/IContentDiscoveryService.cs (expanded)
* Interfaces/Content/IContentProvider.cs (new)
* Interfaces/Content/IContentResolver.cs (new)
* Interfaces/Content/IContentSource.cs (new)
* Models/Content/DiscoveredContent.cs (new)
* Models/Content/ContentSearchQuery.cs (expanded)
* Models/Content/ContentSearchResult.cs (expanded)
* Models/Content/ContentOperationResult.cs (expanded)
* Models/Content/ContentAcquisitionProgress.cs (new)
* Models/Content/ContentInstallationProgress.cs (expanded)
* Models/Enums/ContentProviderType.cs (new)
* Models/Enums/ContentSortOrder.cs (expanded)
* Models/Enums/ContentType.cs (expanded)

### Main Application (`GenHub`)
* Features/Content/Services/ContentDiscoveryService.cs (new)
* Features/Content/Services/FileSystemContentProvider.cs (new)
* Features/Content/Services/HttpContentProvider.cs (new)
* Features/Content/ViewModels/ContentBrowserViewModel.cs (new)
* Infrastructure/DependencyInjection/ContentDeliveryModule.cs (new)

### Test Project (`GenHub.Tests`)
* GenHub.Tests.Core/Features/Content/ContentDiscoveryServiceTests.cs (new)
* GenHub.Tests.Core/Features/Content/FileSystemContentProviderTests.cs (new)
* GenHub.Tests.Core/Features/Content/HttpContentProviderTests.cs (new)
* GenHub.Tests.Core/Features/Content/ContentBrowserViewModelTests.cs (new)

---

## 8. Git Commit Strategy
```powershell
# Start from the main branch
git checkout main
git pull
# Create the feature branch
git checkout -b feat/content-system
# --- Commit 1: Core Content Contracts and Models ---
git add GenHub.Core/Interfaces/Content/
git add GenHub.Core/Models/Content/
git add GenHub.Core/Models/Enums/ContentProviderType.cs
# Add/expand ContentType, ContentSortOrder as needed
git commit -m "feat(core): Add contracts and models for content discovery and delivery system"
# --- Commit 2: Service Implementations ---
git add GenHub/Features/Content/Services/
git add GenHub/Features/Content/ViewModels/ContentBrowserViewModel.cs
git commit -m "feat(content): Implement discovery, provider, and browser services"
# --- Commit 3: Dependency Injection ---
git add GenHub/Infrastructure/DependencyInjection/ContentDeliveryModule.cs
git commit -m "feat(infra): Register content system services in DI"
# --- Commit 4: Unit Tests ---
git add GenHub.Tests.Core/Features/Content/
git commit -m "test(content): Add unit tests for content system foundation"
# --- Push the branch to remote ---
git push --set-upstream origin feat/content-system
```

---

## 9. Pull Request Details
**Title:** `feat(content): Establish core content discovery, delivery, and assembly system`

**Description:**
This pull request introduces the foundational content system for GenHub, enabling users to discover, resolve, acquire, and assemble mods, patches, and add-ons from multiple sources. The system is fully integrated with GameProfiles and workspace management, supporting profile-driven installation and launch workflows.

### Key Features:
1. **Discovery Layer**: Implements `IContentDiscoverer` and concrete discoverers (FileSystem, GitHub, ModDB) to scan for available content. Returns lightweight `DiscoveredContent` objects for fast UI display.
2. **Resolution Layer**: Uses `IContentResolver` implementations to transform discovered items into detailed `GameManifest` blueprints, supporting local, remote, and package-based content.
3. **Acquisition Layer**: `IContentProvider` implementations download, extract, and prepare files as described in the manifest, transforming package-level instructions into actionable file operations.
4. **Assembly Layer**: Integrates with `IWorkspaceManager` and `IWorkspaceStrategy` to build isolated workspaces, copying, linking, patching, and validating files as required.
5. **Orchestration**: `IContentDiscoveryService` coordinates all layers, providing a unified API for search, installation, and workspace preparation, fully integrated with GameProfiles.
6. **UI Integration**: `ContentBrowserViewModel` provides the user-facing interface for searching, filtering, and installing content.
7. **Extensibility**: The system is designed for easy addition of new discoverers, resolvers, and providers (e.g., CNCLabs, custom Git providers).
8. **Testing**: Comprehensive unit tests for all core services and models ensure reliability and maintainability.

### Why:
The content system is the backbone of GenHub's mod and patch management. It enables users to discover and install new content with confidence, supporting a fragmented ecosystem and ensuring compatibility through isolated workspaces and manifest-driven installation.

### How:
- All content operations are profile-driven, ensuring user actions are isolated and reproducible.
- The system uses a four-layer pipeline (Discovery, Resolution, Acquisition, Assembly) to transform raw content listings into fully prepared game environments.
- Each layer is extensible, allowing for future growth and integration of new content sources and delivery mechanisms.

### Testing:
- Unit tests for all discoverers, providers, and orchestrators.
- ViewModel tests for UI integration.
- Integration tests to ensure end-to-end workflows from search to installation and launch.

## 10. Next Steps
- Implement additional discoverers and providers (e.g., CNCLabs, advanced Git integration).
- Add content caching and update checks.
- Integrate content update notifications into GameProfile views.
- Expand UI for advanced filtering, sorting, and content management.
