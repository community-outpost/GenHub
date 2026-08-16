## GenHub CAS Integration Review Board

This board tracks all files that require refactoring, changes, or improvements for a thorough Content-Addressable Storage (CAS) system integration. Each file is listed with a short note on the required change. As files are reviewed and updated, mark them as complete or add details.

---

### 1. Core Models & Enums
- GenHub.Core/Models/Enums/ManifestFileSourceType.cs *(delete, replaced by ContentSourceType)*
- GenHub.Core/Models/Enums/ContentSourceType.cs *(add, new CAS-aware source type)*
- GenHub.Core/Models/Manifest/ManifestFile.cs *(update: reference CAS hashes, use ContentSourceType)*
- GenHub.Core/Models/Storage/CasConfiguration.cs *(add: CAS config)*
- GenHub.Core/Models/Storage/CasOperationResult.cs *(add: CAS operation result)*
- GenHub.Core/Models/Storage/CasValidationResult.cs *(add: CAS validation result)*
- GenHub.Core/Models/Storage/CasGarbageCollectionResult.cs *(add: CAS GC result)*

### 2. CAS Service Interfaces & Implementations
- GenHub.Core/Interfaces/Storage/ICasService.cs *(add: high-level CAS ops)*
- GenHub.Core/Interfaces/Storage/ICasStorage.cs *(add: low-level CAS storage)*
- GenHub/Features/Storage/Services/CasService.cs *(add: CAS service impl)*
- GenHub/Features/Storage/Services/CasStorage.cs *(add: CAS storage impl)*
- GenHub/Features/Storage/Services/CasReferenceTracker.cs *(add: CAS reference tracking)*
- GenHub/Features/Storage/Services/CasMaintenanceService.cs *(add: CAS maintenance)*

### 3. Content Pipeline & Providers
- GenHub/Features/Content/Services/ContentProviders/HttpContentProvider.cs *(update: CAS population during acquisition)*
- GenHub/Features/Content/Services/ContentProviders/FileSystemContentProvider.cs *(update: CAS integration)*
- GenHub/Features/Content/Services/ContentDiscoveryService.cs *(update: coordinate CAS)*
- GenHub/Features/Content/Services/ContentOrchestrator.cs *(update: pipeline orchestration for CAS)*
- [x] Complete - GenHub/Features/Content/Services/ContentStorageService.cs *(update: CAS-aware storage, refactored to use ICasService for file storage and manifest updates)*
- GenHub/Features/Content/Services/ContentValidator.cs *(update: validate CAS content)*
- [x] Complete - GenHub/Features/Content/Services/MemoryDynamicContentCache.cs *(review: cache CAS objects, updated IDynamicContentCache interface)*

### 4. Workspace & File Operations
- GenHub.Core/Interfaces/Workspace/IFileOperationsService.cs *(update: add CAS methods)*
- GenHub/Features/Workspace/FileOperationsService.cs *(update: CAS integration)*
- GenHub/Features/Workspace/WorkspaceManager.cs *(update: CAS-aware workspace prep)*
- GenHub/Features/Workspace/WorkspaceValidator.cs *(update: validate CAS workspace)*

#### Workspace Strategies
- GenHub/Features/Workspace/Strategies/WorkspaceStrategyBase.cs *(update: CAS-priority logic)*
- GenHub/Features/Workspace/Strategies/FullCopyStrategy.cs *(update: copy from CAS)*
- GenHub/Features/Workspace/Strategies/SymlinkOnlyStrategy.cs *(update: symlink from CAS)*
- GenHub/Features/Workspace/Strategies/HybridCopySymlinkStrategy.cs *(update: hybrid CAS logic)*
- GenHub/Features/Workspace/Strategies/HardLinkStrategy.cs *(update: hardlink from CAS)*

### 5. Manifest System
- GenHub/Features/Manifest/ContentManifestBuilder.cs *(update: build CAS-aware manifests)*
- [x] Complete - GenHub/Features/Manifest/GameManifestPool.cs *(update: refactored to use ICasService and manage CAS-referenced manifests)*
- [x] Complete - GenHub/Features/Manifest/ManifestCache.cs *(update: added caching for CAS object existence checks)*
- GenHub/Features/Manifest/ManifestDiscoveryService.cs *(update: discover CAS content)*
- GenHub/Features/Manifest/ManifestGenerationService.cs *(update: generate CAS manifests)*
- GenHub/Features/Manifest/ManifestInitializationService.cs *(update: init CAS pool)*
- GenHub/Features/Manifest/ManifestProvider.cs *(update: provide CAS-aware manifests)*

### 6. Dependency Injection & Config
- GenHub/Infrastructure/DependencyInjection/StorageModule.cs *(add: register CAS services)*
- GenHub/Infrastructure/DependencyInjection/AppServices.cs *(update: register StorageModule)*

### 7. Tests
- GenHub.Tests.Core/Features/Storage/CasServiceTests.cs *(add: test CAS service)*
- GenHub.Tests.Core/Features/Storage/CasStorageTests.cs *(add: test CAS storage)*
- GenHub.Tests.Core/Features/Workspace/WorkspaceStrategyBaseTests.cs *(update: test CAS scenarios)*
- GenHub.Tests.Core/Models/ContentSourceTypeTests.cs *(add: test new enum)*

### 8. Other Areas to Review
- GenHub/Common/Services/DownloadService.cs *(update: support CAS-aware downloads and storage)*
- GenHub/Common/Services/AppConfigurationService.cs *(review: config for CAS)*
- GenHub/Common/Services/ConfigurationProvider.cs *(review: config for CAS)*
- GenHub/Common/Services/UserSettingsService.cs *(review: user settings for CAS)*
- GenHub/Features/Downloads/Views/DownloadsView.axaml.cs *(review: UI for CAS downloads)*
- GenHub/Features/GameInstallations/GameInstallationDetectionOrchestrator.cs *(review: game install detection for CAS)*
- GenHub/Features/GameVersions/GameVersionDetectionOrchestrator.cs *(review: version detection for CAS)*
- GenHub/Features/Launching/GameLauncher.cs *(review: launching from CAS)*
- GenHub/Features/Settings/ViewModels/SettingsViewModel.cs *(review: settings for CAS)*

---

**Instructions:**
- As you review and update each file, mark it as `[x] Complete` or add notes on progress/issues.
- Add new files to this board if discovered during integration.
- Use this board to track CAS integration progress across the codebase.
            | UpdateNotificationViewModel.cs | Code |

          - 📁 Views
            | File | Type |
            | ---- | ---- |
            | UpdateNotificationView.axaml | XAML |
            | UpdateNotificationWindow.axaml | XAML |
            | UpdateNotificationView.axaml.cs | Code |
            | UpdateNotificationWindow.axaml.cs | Code |

        - 📁 Content
          - 📁 Services
            | File | Type |
            | ---- | ---- |
            | ContentOrchestrator.cs | Code |
            | ContentStorageService.cs | Code |
            | ContentValidator.cs | Code |
            | MemoryDynamicContentCache.cs | Code |
            | ContentProviders/BaseContentProvider.cs | Code | <!-- CAS: Refactor pipeline orchestration to support CAS acquisition and retrieval -->
            | ContentProviders/CNCLabsContentProvider.cs | Code | <!-- CAS: Integrate CAS-aware acquisition and manifest update -->
            | ContentProviders/GitHubContentProvider.cs | Code | <!-- CAS: Integrate CAS-aware acquisition and manifest update -->
            | ContentProviders/LocalFileSystemContentProvider.cs | Code | <!-- CAS: Integrate CAS-aware acquisition and manifest update -->
            | ContentProviders/ModDBContentProvider.cs | Code | <!-- CAS: Integrate CAS-aware acquisition and manifest update -->

            - 📁 ContentDeliverers
              | File | Type |
              | ---- | ---- |
              | FileSystemDeliverer.cs | Code |
              | HttpContentDeliverer.cs | Code |

            - 📁 ContentDiscoverers
              | File | Type |
              | ---- | ---- |
              | CNCLabsMapDiscoverer.cs | Code |
              | FileSystemDiscoverer.cs | Code |
              | GitHubDiscoverer.cs | Code |
              | GitHubReleasesDiscoverer.cs | Code |

            - 📁 ContentProviders
              | File | Type |
              | ---- | ---- |
              | BaseContentProvider.cs | Code |
              | CNCLabsContentProvider.cs | Code |
              | GitHubContentProvider.cs | Code |
              | LocalFileSystemContentProvider.cs | Code |
              | ModDBContentProvider.cs | Code |

            - 📁 ContentResolvers
              | File | Type |
              | ---- | ---- |
              | CNCLabsMapResolver.cs | Code |
              | GitHubResolver.cs | Code |
              | LocalManifestResolver.cs | Code |

          - 📁 ViewModels
            | File | Type |
            | ---- | ---- |
            | ContentBrowserViewModel.cs | Code |
            | ContentItemViewModel.cs | Code |

        - 📁 Downloads
          - 📁 ViewModels
            | File | Type |
            | ---- | ---- |
            | DownloadsViewModel.cs | Code |

          - 📁 Views
            | File | Type |
            | ---- | ---- |
            | DownloadsView.axaml | XAML |
          | ContentManifestBuilder.cs | Code | <!-- CAS: Refactor to build CAS-aware manifests -->
          | GameManifestPool.cs | Code | <!-- CAS: Integrate CAS pool logic for manifest management -->
          | ManifestCache.cs | Code | <!-- CAS: Update to cache CAS objects -->
          | ManifestDiscoveryService.cs | Code | <!-- CAS: Discover and validate CAS objects -->
          | ManifestGenerationService.cs | Code | <!-- CAS: Generate manifests referencing CAS hashes -->
          | ManifestInitializationService.cs | Code | <!-- CAS: Initialize CAS pool during manifest setup -->
          | ManifestProvider.cs | Code | <!-- CAS: Provide CAS-aware manifest files -->
            | DownloadsView.axaml.cs | Code |

        - 📁 GameInstallations
          | File | Type |
          | ---- | ---- |
          | GameInstallationDetectionOrchestrator.cs | Code |

        - �└── 📄 DownloadsViewModel.cs
    │   │   │   └── 📁 Views
    │   │   │       ├── 📄 DownloadsView.axaml
    │   │   │       └── 📄 DownloadsView.axaml.cs
    │   │   ├── 📁 GameInstallations
    │   │   │   └── 📄 GameInstallationDetectionOrchestrator.cs
    │   │   ├── 📁 GameProfiles
    │   │   │   ├── 📁 ViewModels
          | FileSystemValidator.cs | Code | <!-- CAS: Validate CAS object integrity and existence -->
          | GameInstallationValidator.cs | Code | <!-- CAS: Validate installation using CAS references -->
          | GameVersionValidator.cs | Code | <!-- CAS: Validate version using CAS references -->
    │   │   │   │   ├── 📄 GameProfileItemViewModel.cs
    │   │   │   │   ├── 📄 GameProfileLauncherViewModel.cs
    │   │   │   │   └── 📄 GameProfileSettingsViewModel.cs
    │   │   │   └── 📁 Views
          | FileOperationsService.cs | Code | <!-- CAS: Implement CAS-aware file operations -->
          | WorkspaceManager.cs | Code | <!-- CAS: Manage workspace with CAS integration -->
          | WorkspaceValidator.cs | Code | <!-- CAS: Validate workspace using CAS objects -->
    │   │   │       ├── 📄 GameProfileCardView.axaml
    │   │   │       ├── 📄 GameProfileLauncherView.axaml
    │   │   │       ├── 📄 GameProfileSettingsWindow.axaml
            | FullCopyStrategy.cs | Code | <!-- CAS: Refactor to prioritize CAS retrieval and fallback -->
            | HardLinkStrategy.cs | Code | <!-- CAS: Refactor to support CAS objects -->
            | HybridCopySymlinkStrategy.cs | Code | <!-- CAS: Refactor to support CAS objects -->
            | SymlinkOnlyStrategy.cs | Code | <!-- CAS: Refactor to support CAS objects -->
            | WorkspaceStrategyBase.cs | Code | <!-- CAS: Add CAS logic to base strategy -->
    │   │   │       ├── 📄 GameProfileCardView.axaml.cs
    │   │   │       ├── 📄 GameProfileLauncherView.axaml.cs
    │   │   │       └── 📄 GameProfileSettingsWindow.axaml.cs
    │   │   ├── 📁 GameVersions
    │   │   │   └── 📄 GameVersionDetectionOrchestrator.cs
    │   │   ├── 📁 GitHub
    │   │   │   └── 📁 Services
    │   │   │       └── 📄 OctokitGitHubApiClient.cs
    │   │   ├── 📁 Launching
    │   │   │   └── 📄 GameLauncher.cs
    │   │   ├── 📁 Manifest
    │   │   │   ├── 📄 ContentManifestBuilder.cs
    │   │   │   ├── 📄 GameManifestPool.cs
    │   │   │   ├── 📄 ManifestCache.cs
    │   │   │   ├── 📄 ManifestDiscoveryService.cs
    │   │   │   ├── 📄 ManifestGenerationService.cs
      | GenHub.Core.csproj | Project | <!-- CAS: Add new CAS models, interfaces, and update references -->
      | GlobalSuppressions.cs | Code |
    │   │   │   ├── 📄 ManifestInitializationService.cs
    │   │   │   └── 📄 ManifestProvider.cs
    │   │   ├── 📁 Settings
    │   │   │   ├── 📁 ViewModels
    │   │   │   │   └── 📄 SettingsViewModel.cs
    │   │   │   └── 📁 Views
    │   │   │       ├── 📄 SettingsView.axaml
    │   │   │       └── 📄 SettingsView.axaml.cs
    │   │   ├── 📁 Validation
    │   │   │   ├── 📄 FileSystemValidator.cs
    │   │   │   ├── 📄 GameInstallationValidator.cs
    │   │   │   ├── 📄 GameVersionValidator.cs
    │   │   │   └── 📄 Validator.cs
    │   │   └── 📁 Workspace
    │   │       ├── 📄 FileOperationsService.cs
    │   │       ├── 📄 WorkspaceManager.cs
          | ContentSourceType.cs | Code | <!-- CAS: New enum replacing ManifestFileSourceType -->
          | ManifestFileSourceType.cs | Code | <!-- CAS: Remove/replace with ContentSourceType -->
    │   │       ├── 📄 WorkspaceValidator.cs
          | ManifestFile.cs | Code | <!-- CAS: Update to use ContentSourceType and CAS hash references -->
    │   │       └── 📁 Strategies
          | CasConfiguration.cs | Code | <!-- CAS: New model for CAS configuration -->
          | CasOperationResult.cs | Code | <!-- CAS: New model for CAS operation results -->
          | CasValidationResult.cs | Code | <!-- CAS: New model for CAS validation results -->
          | CasGarbageCollectionResult.cs | Code | <!-- CAS: New model for CAS garbage collection -->
    │   │           ├── 📄 FullCopyStrategy.cs
    │   │           ├── 📄 HardLinkStrategy.cs
    │   │           ├── 📄 HybridCopySymlinkStrategy.cs
    │   │           ├── 📄 SymlinkOnlyStrategy.cs
    │   │           └── 📄 WorkspaceStrategyBase.cs
    │   └── 📁 Infrastructure
    │       ├── 📁 Converters
    │       │   ├── 📄 BoolToColorConverter.cs
    │       │   ├── 📄 BoolToStatusColorConverter.cs
    │       │   ├── 📄 BoolToValueConverter.cs
    │       │   ├── 📄 BoolToVisibilityConverter.cs
    │       │   ├── 📄 ColorBrightnessConverter.cs
    │       │   ├── 📄 ContrastTextColorConverter.cs
    │       │   ├── 📄 InvertedBoolToVisibilityConverter.cs
    │       │   ├── 📄 NavigationTabConverter.cs
    │       │   ├── 📄 NotNullConverter.cs
    │       │   ├── 📄 NotNullOrEmptyConverter.cs
    │       │   ├── 📄 NullableDoubleConverter.cs
    │       │   ├── 📄 NullableIntConverter.cs
    │       │   ├── 📄 NullSafePropertyConverter.cs
    │       │   ├── 📄 NullToVisibilityConverter.cs
    │       │   ├── 📄 ProfileColorToOpacityConverter.cs
    │       │   ├── 📄 ProfileCoverConverter.cs
    │       │   ├── 📄 StringToImageConverter.cs
    │       │   ├── 📄 StringToIntConverter.cs
    │       │   └── 📄 TabIndexToVisibilityConverter.cs
    │       ├── 📁 DependencyInjection
            | WorkspaceStrategyBaseTests.cs | Code | <!-- CAS: Add/modify tests for CAS-aware strategies -->
    │       │   ├── 📄 AppServices.cs
    │       │   ├── 📄 AppUpdateModule.cs
    │       │   ├── 📄 ConfigurationModule.cs
    │       │   ├── 📄 ContentDeliveryModule.cs
    │       │   ├── 📄 DownloadModule.cs
    │       │   ├── 📄 GameDetectionModule.cs
    │       │   ├── 📄 LoggingModule.cs
    │       │   ├── 📄 ManifestModule.cs
    │       │   ├── 📄 SharedViewModelModule.cs
    │       │   ├── 📄 ValidationModule.cs
    │       │   └── 📄 WorkspaceModule.cs
    │       ├── 📁 Exceptions
    │       │   └── 📄 ManifestExceptions.cs
    │       └── 📁 Extensions
    │           ├── 📄 LoggerExtensions.cs
    │           └── 📄 NavigationTabExtensions.cs
    ├── 📁 GenHub.Core
    │   ├── 📄 GlobalSuppressions.cs
    │   ├── 📁 Extensions
    │   │   └── 📁 GameInstallations
    │   │       └── 📄 InstallationExtensions.cs
    │   ├── 📁 Interfaces
    │   │   ├── 📁 AppUpdate
    │   │   │   ├── 📄 IAppUpdateService.cs
    │   │   │   ├── 📄 IAppVersionService.cs
    │   │   │   ├── 📄 IPlatformUpdateInstaller.cs
    │   │   │   ├── 📄 IUpdateInstaller.cs
    │   │   │   └── 📄 IVersionComparator.cs
    │   │   ├── 📁 Common
    │   │   │   ├── 📄 IAppConfigurationService.cs
    │   │   │   ├── 📄 IConfigurationProvider.cs
    │   │   │   ├── 📄 IDownloadService.cs
    │   │   │   └── 📄 IUserSettingsService.cs
    │   │   ├── 📁 Content
    │   │   │   ├── 📄 IContentDeliverer.cs
    │   │   │   ├── 📄 IContentDiscoverer.cs
    │   │   │   ├── 📄 IContentOrchestrator.cs
    │   │   │   ├── 📄 IContentProvider.cs
    │   │   │   ├── 📄 IContentResolver.cs
    │   │   │   ├── 📄 IContentSource.cs
    │   │   │   ├── 📄 IContentStorageService.cs
    │   │   │   ├── 📄 IContentValidator.cs
    │   │   │   └── 📄 IDynamicContentCache.cs
    │   │   ├── 📁 GameInstallations
    │   │   │   ├── 📄 IGameInstallation.cs
    │   │   │   ├── 📄 IGameInstallationDetectionOrchestrator.cs
    │   │   │   └── 📄 IGameInstallationDetector.cs
    │   │   ├── 📁 GameProfiles
    │   │   │   └── 📄 IGameProfile.cs
    │   │   ├── 📁 GameVersions
    │   │   │   ├── 📄 IGameVersionDetectionOrchestrator.cs
    │   │   │   └── 📄 IGameVersionDetector.cs
    │   │   ├── 📁 Github
    │   │   │   └── 📄 IGitHubApiClient.cs
    │   │   ├── 📁 Launching
    │   │   │   └── 📄 IGameLauncher.cs
    │   │   ├── 📁 Manifest
    │   │   │   ├── 📄 IContentManifestBuilder.cs
    │   │   │   ├── 📄 IGameManifestPool.cs
    │   │   │   ├── [x] Complete - IManifestCache.cs | Code | <!-- CAS: Added methods for caching CAS object existence -->
    │   │   │   ├── 📄 IManifestGenerationService.cs
    │   │   │   └── 📄 IManifestProvider.cs
    │   │   ├── 📁 Validation
    │   │   │   ├── 📄 IGameInstallationValidator.cs
    │   │   │   ├── 📄 IGameVersionValidator.cs
    │   │   │   └── 📄 IValidator.cs
    │   │   └── 📁 Workspace
    │   │       ├── 📄 IFileOperationsService.cs
    │   │       ├── 📄 IWorkspaceManager.cs
    │   │       ├── 📄 IWorkspaceStrategy.cs
    │   │       └── 📄 IWorkspaceValidator.cs
    │   └── 📁 Models
    │       ├── 📁 AppUpdate
    │       │   └── 📄 UpdateProgress.cs
    │       ├── 📁 Common
    │       │   ├── 📄 AppSettings.cs
    │       │   ├── 📄 DownloadConfiguration.cs
    │       │   └── 📄 DownloadProgress.cs
    │       ├── 📁 Content
    │       │   ├── 📄 ContentAcquisitionPhase.cs
    │       │   ├── 📄 ContentAcquisitionProgress.cs
    │       │   └── 📄 ContentSearchQuery.cs
    │       ├── 📁 Enums
    │       │   ├── 📄 ContentProviderType.cs
    │       │   ├── 📄 ContentSortField.cs
    │       │   ├── 📄 ContentSourceCapabilities.cs
    │       │   ├── 📄 ContentType.cs
    │       │   ├── 📄 DependencyInstallBehavior.cs
    │       │   ├── 📄 GameInstallationType.cs
    │       │   ├── 📄 GameType.cs
    │       │   ├── 📄 ManifestFileSourceType.cs
    │       │   ├── 📄 NavigationTab.cs
    │       │   ├── 📄 PackageType.cs
    │       │   └── 📄 WorkspaceStrategy.cs
    │       ├── 📁 GameInstallations
    │       │   └── 📄 GameInstallation.cs
    │       ├── 📁 GameProfile
    │       │   ├── 📄 GameProfile.cs
    │       │   └── 📄 ProfileInfoItem.cs
    │       ├── 📁 GameVersions
    │       │   └── 📄 GameVersion.cs
    │       ├── 📁 GitHub
    │       │   ├── 📄 GitHubRelease.cs
    │       │   └── 📄 GitHubReleaseAsset.cs
    │       ├── 📁 Launching
    │       │   ├── 📄 GameLaunchConfiguration.cs
    │       │   └── 📄 GameProcessInfo.cs
    │       ├── 📁 Manifest
    │       │   ├── 📄 BundleItem.cs
    │       │   ├── 📄 ContentBundle.cs
    │       │   ├── 📄 ContentDependency.cs
    │       │   ├── 📄 ContentMetadata.cs
    │       │   ├── 📄 ContentReference.cs
    │       │   ├── 📄 ExtractionConfiguration.cs
    │       │   ├── 📄 FilePermissions.cs
    │       │   ├── 📄 GameManifest.cs
    │       │   ├── 📄 InstallationInstructions.cs
    │       │   ├── 📄 InstallationStep.cs
    │       │   ├── 📄 ManifestFile.cs
    │       │   └── 📄 PublisherInfo.cs
    │       ├── 📁 Results
    │       │   ├── 📄 ContentOperationResult.cs
    │       │   ├── 📄 ContentSearchResult.cs
    │       │   ├── 📄 DetectionResult.cs
    │       │   ├── 📄 DownloadResult.cs
    │       │   ├── 📄 LaunchResult.cs
    │       │   ├── 📄 ResultBase.cs
    │       │   ├── 📄 UpdateCheckResult.cs
    │       │   └── 📄 ValidationResult.cs
    │       ├── 📁 Validation
    │       │   ├── 📄 ValidationIssue.cs
    │       │   ├── 📄 ValidationIssueType.cs
    │       │   ├── 📄 ValidationProgress.cs
    │       │   └── 📄 ValidationSeverity.cs
    │       └── 📁 Workspace
    │           ├── 📄 WorkspaceConfiguration.cs
    │           ├── 📄 WorkspaceInfo.cs
    │           └── 📄 WorkspacePreparationProgress.cs
    ├── 📁 GenHub.Linux
    │   ├── 📄 GlobalSuppressions.cs
    │   ├── 📄 Program.cs
    │   ├── 📁 Features
    │   │   └── 📁 AppUpdate
    │   │       └── 📄 LinuxUpdateInstaller.cs
    │   └── 📁 GameInstallations
    │       ├── 📄 LinuxInstallationDetector.cs
    │       ├── 📄 SteamInstallation.cs
    │       └── 📄 WineInstallation.cs
    ├── 📁 GenHub.Tests
    │   ├── 📁 GenHub.Tests.Core
    │   │   ├── 📄 GlobalSuppressions.cs
    │   │   ├── 📁 App
    │   │   │   └── 📄 AppLifecycleTests.cs
    │   │   ├── 📁 Common
    │   │   │   └── 📁 Services
    │   │   │       ├── 📄 AppConfigurationServiceTests.cs
    │   │   │       ├── 📄 ConfigurationProviderTests.cs
    │   │   │       ├── 📄 DownloadServiceTests.cs
    │   │   │       └── 📄 UserSettingsServiceTests.cs
    │   │   ├── 📁 Features
    │   │   │   ├── 📁 AppUpdate
    │   │   │   │   ├── 📁 Factories
    │   │   │   │   │   └── 📄 UpdateInstallerFactoryTests.cs
    │   │   │   │   ├── 📁 Services
    │   │   │   │   │   ├── 📄 AppUpdateServiceIntegrationTests.cs
    │   │   │   │   │   ├── 📄 AppUpdateServiceTests.cs
    │   │   │   │   │   ├── 📄 AppVersionServiceTests.cs
    │   │   │   │   │   ├── 📄 OctokitGitHubApiClientTests.cs
    │   │   │   │   │   ├── 📄 OctokitTestStubs.cs
    │   │   │   │   │   ├── 📄 ReleasesClientStub.cs
    │   │   │   │   │   ├── 📄 RepositoriesClientStub.cs
    │   │   │   │   │   ├── 📄 SemVerComparatorTests.cs
    │   │   │   │   │   └── 📄 UpdateInstallerTests.cs
    │   │   │   │   └── 📁 ViewModels
    │   │   │   │       └── 📄 UpdateNotificationViewModelTests.cs
    │   │   │   ├── 📁 Content
    │   │   │   │   ├── 📄 BaseContentProviderTests.cs
    │   │   │   │   ├── 📄 ContentOrchestratorTests.cs
    │   │   │   │   ├── 📄 GitHubContentProviderTests.cs
    │   │   │   │   └── 📄 GitHubResolverTests.cs
    │   │   │   ├── 📁 GameInstallations
    │   │   │   │   └── 📄 GameInstallationDetectionOrchestratorTests.cs
    │   │   │   ├── 📁 GameVersions
    │   │   │   │   └── 📄 GameVersionDetectionOrchestratorTests.cs
    │   │   │   ├── 📁 Manifest
    │   │   │   │   ├── 📄 ContentManifestBuilderTests.cs
    │   │   │   │   ├── 📄 ManifestCacheTests.cs
    │   │   │   │   ├── 📄 ManifestDiscoveryServiceTests.cs
    │   │   │   │   └── 📄 ManifestProviderTests.cs
    │   │   │   ├── 📁 Validation
    │   │   │   │   ├── 📄 FileSystemValidatorTests.cs
    │   │   │   │   ├── 📄 GameInstallationValidatorTests.cs
    │   │   │   │   ├── 📄 GameVersionValidatorTests.cs
    │   │   │   │   ├── 📄 ValidationProgressTests.cs
    │   │   │   │   └── 📄 ValidationResultTests.cs
    │   │   │   └── 📁 Workspace
    │   │   │       ├── 📄 FileOperationsServiceTests.cs
    │   │   │       ├── 📄 HybridCopySymlinkStrategyTests.cs
    │   │   │       ├── 📄 StrategyTests.cs
    │   │   │       ├── 📄 WorkspaceIntegrationTests.cs
    │   │   │       ├── 📄 WorkspaceManagerTests.cs
    │   │   │       ├── 📄 WorkspaceStrategyBaseTests.cs
    │   │   │       └── 📄 WorkspaceValidatorTests.cs
    │   │   ├── 📁 Infrastructure
    │   │   │   ├── 📁 Converters
    │   │   │   │   ├── 📄 NavigationTabConverterTests.cs
    │   │   │   │   ├── 📄 StringToIntConverterTests.cs
    │   │   │   │   └── 📄 TabIndexToVisibilityConverterTests.cs
    │   │   │   ├── 📁 DependencyInjection
    │   │   │   │   ├── 📄 DownloadModuleTests.cs
    │   │   │   │   ├── 📄 LoggingModuleTests.cs
    │   │   │   │   └── 📄 SharedViewModelModuleTests.cs
    │   │   │   └── 📁 Extensions
    │   │   │       └── 📄 LoggerExtensionsTests.cs
    │   │   ├── 📁 Models
    │   │   │   ├── 📄 NavigationTabTests.cs
    │   │   │   ├── 📁 AppUpdate
    │   │   │   │   └── 📄 UpdateCheckResultTests.cs
    │   │   │   ├── 📁 Common
    │   │   │   │   ├── 📄 DownloadConfigurationTests.cs
    │   │   │   │   └── 📄 DownloadProgressTests.cs
    │   │   │   ├── 📁 GameInstallations
    │   │   │   │   └── 📄 GameInstallationTests.cs
    │   │   │   ├── 📁 GameVersions
    │   │   │   │   └── 📄 GameVersionTests.cs
    │   │   │   └── 📁 Results
    │   │   │       ├── 📄 DetectionResultTests.cs
    │   │   │       ├── 📄 DownloadResultTests.cs
    │   │   │       └── 📄 ResultBaseTests.cs
    │   │   └── 📁 ViewModels
    │   │       ├── 📄 DownloadsViewModelTests.cs
    │   │       ├── 📄 GameProfileItemViewModelTests.cs
    │   │       ├── 📄 GameProfileLauncherViewModelTests.cs
    │   │       ├── 📄 GameProfileSettingsViewModelTests.cs
    │   │       ├── 📄 MainViewModelTests.cs
    │   │       └── 📄 SettingsViewModelTests.cs
    │   ├── 📁 GenHub.Tests.Linux
    │   │   ├── 📄 GlobalSuppressions.cs
    │   │   ├── 📁 Features
    │   │   │   └── 📁 AppUpdate
    │   │   │       └── 📄 LinuxUpdateInstallerTests.cs
    │   │   └── 📁 Gameinstallations
    │   │       ├── 📄 LinuxInstallationDetectorTests.cs
    │   │       ├── 📄 SteamInstallationTests.cs
    │   │       └── 📄 WineInstallationTests.cs
    │   └── 📁 GenHub.Tests.Windows
    │       ├── 📄 GlobalSuppressions.cs
    │       ├── 📁 Features
    │       │   ├── 📁 AppUpdate
    │       │   │   └── 📄 WindowsUpdateInstallerTests.cs
    │       │   └── 📁 Workspace
    │       │       └── 📄 WindowsFileOperationsServiceTests.cs
    │       └── 📁 Gameinstallations
    │           └── 📄 WindowsInstallationDetectorTests.cs
    └── 📁 GenHub.Windows
        ├── 📄 GlobalSuppressions.cs
        ├── 📄 NativeMethods.cs
        ├── 📄 Program.cs
        ├── 📁 Features
        │   ├── 📁 AppUpdate
        │   │   └── 📄 WindowsUpdateInstaller.cs
        │   └── 📁 Workspace
        │       └── 📄 WindowsFileOperationsService.cs
        └── 📁 GameInstallations
            ├── 📄 EaAppInstallation.cs
            ├── 📄 SteamInstallation.cs
            └── 📄 WindowsInstallationDetector.cs
            ├── 📄 SteamInstallation.cs
            └── 📄 WindowsInstallationDetector.cs
