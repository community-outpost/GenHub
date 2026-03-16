# Publisher Studio Workflow

This flowchart illustrates the complete workflow for content creators using Publisher Studio to create, configure, and publish content catalogs.

## Overview

Publisher Studio is a desktop tool that enables content creators to become publishers without manually writing JSON files. It guides users through project creation, content management, release configuration, artifact upload, and catalog publishing.

## Flow Diagram

```mermaid
flowchart TD
    Start([Creator opens Publisher Studio]) --> CheckProject{Existing project?}

    CheckProject -->|No| CreateProject[Create new publisher project]
    CreateProject --> EnterPublisher[Enter publisher information:<br/>- Publisher ID<br/>- Name<br/>- Description<br/>- Website URL]
    EnterPublisher --> UploadAvatar[Upload avatar image]
    UploadAvatar --> SelectHosting[Select hosting provider]

    SelectHosting --> HostingChoice{Which provider?}

    HostingChoice -->|Google Drive| AuthGoogle[Authenticate with Google OAuth]
    AuthGoogle --> AuthSuccess{Auth successful?}
    AuthSuccess -->|No| ErrorAuth[Error: Authentication failed]
    ErrorAuth --> SelectHosting
    AuthSuccess -->|Yes| SelectFolder[Select Google Drive folder]
    SelectFolder --> SaveProject

    HostingChoice -->|GitHub| AuthGitHub[Authenticate with GitHub]
    AuthGitHub --> SelectRepo[Select repository]
    SelectRepo --> SaveProject

    HostingChoice -->|Dropbox| AuthDropbox[Authenticate with Dropbox]
    AuthDropbox --> SelectDropboxFolder[Select Dropbox folder]
    SelectDropboxFolder --> SaveProject

    HostingChoice -->|Manual| EnterURLs[Enter manual URLs]
    EnterURLs --> SaveProject

    SaveProject[Save project file]
    SaveProject --> ProjectReady

    CheckProject -->|Yes| LoadProject[Load existing project]
    LoadProject --> ProjectReady[Project ready]

    ProjectReady --> MainMenu{User action?}

    MainMenu -->|Add Content| AddContent[Open Content Library]
    AddContent --> CreateContent[Create new content item]
    CreateContent --> EnterContentInfo[Enter content information:<br/>- Content ID<br/>- Name<br/>- Description<br/>- Content Type<br/>- Target Game]

    EnterContentInfo --> UploadBanner[Upload banner image]
    UploadBanner --> UploadScreenshots[Upload screenshots]
    UploadScreenshots --> AddTags[Add tags]
    AddTags --> SetMetadata[Set metadata]

    SetMetadata --> IsAddon{Is addon/extension?}
    IsAddon -->|Yes| SelectBase[Select base content (extendsContentId)]
    SelectBase --> SaveContent
    IsAddon -->|No| SaveContent[Save content item]

    SaveContent --> AddRelease{Add release?}
    AddRelease -->|No| MainMenu

    AddRelease -->|Yes| CreateRelease[Create new release]
    CreateRelease --> EnterVersion[Enter version number]
    EnterVersion --> ValidateVersion{Valid SemVer?}

    ValidateVersion -->|No| ErrorVersion[Error: Invalid version format]
    ErrorVersion --> EnterVersion

    ValidateVersion -->|Yes| EnterChangelog[Enter changelog]
    EnterChangelog --> AddArtifacts[Add artifacts]

    AddArtifacts --> ArtifactLoop{More artifacts?}
    ArtifactLoop -->|Yes| SelectFile[Select artifact file]
    SelectFile --> CalcHash[Calculate SHA256 hash]
    CalcHash --> GetSize[Get file size]
    GetSize --> AddArtifact[Add artifact to release]
    AddArtifact --> ArtifactLoop

    ArtifactLoop -->|No| AddDependencies{Add dependencies?}
    AddDependencies -->|Yes| DepLoop[Add dependency]
    DepLoop --> EnterDepInfo[Enter dependency:<br/>- Publisher ID<br/>- Content ID<br/>- Version constraint]
    EnterDepInfo --> ValidateDep{Valid dependency?}

    ValidateDep -->|No| ErrorDep[Error: Invalid dependency]
    ErrorDep --> DepLoop

    ValidateDep -->|Yes| AddDepToRelease[Add to release dependencies]
    AddDepToRelease --> MoreDeps{More dependencies?}
    MoreDeps -->|Yes| DepLoop
    MoreDeps -->|No| SaveRelease

    AddDependencies -->|No| SaveRelease[Save release]
    SaveRelease --> MainMenu

    MainMenu -->|Validate| RunValidation[Run validation checks]
    RunValidation --> CheckCircular[Check circular dependencies]
    CheckCircular --> CheckConflicts[Check conflicts]
    CheckConflicts --> CheckSchema[Validate JSON schema]
    CheckSchema --> ValidationResult{Validation passed?}

    ValidationResult -->|No| ShowErrors[Show validation errors]
    ShowErrors --> MainMenu

    ValidationResult -->|Yes| ShowSuccess2[Show success message]
    ShowSuccess2 --> MainMenu

    MainMenu -->|Publish| PublishWorkflow[Start publish workflow]
    PublishWorkflow --> CheckValid{Project validated?}

    CheckValid -->|No| ForceValidate[Run validation]
    ForceValidate --> ValidationResult2{Validation passed?}
    ValidationResult2 -->|No| ShowErrors2[Show errors]
    ShowErrors2 --> MainMenu

    ValidationResult2 -->|Yes| UploadArtifacts

    CheckValid -->|Yes| UploadArtifacts[Upload artifacts to hosting]

    UploadArtifacts --> ArtifactUploadLoop{More artifacts?}
    ArtifactUploadLoop -->|Yes| UploadNext[Upload next artifact]
    UploadNext --> UploadSuccess{Upload successful?}

    UploadSuccess -->|No| RetryUpload{Retry?}
    RetryUpload -->|Yes| UploadNext
    RetryUpload -->|No| ErrorUpload[Error: Artifact upload failed]
    ErrorUpload --> End12([End])

    UploadSuccess -->|Yes| GetDownloadURL[Get download URL from hosting]
    GetDownloadURL --> UpdateCatalog[Update catalog with download URL]
    UpdateCatalog --> ArtifactUploadLoop

    ArtifactUploadLoop -->|No| GenerateCatalog[Generate catalog JSON]
    GenerateCatalog --> MultipleCatalogs{Multiple catalogs?}

    MultipleCatalogs -->|Yes| CatalogLoop[Generate each catalog]
    CatalogLoop --> FilterContent[Filter content by catalog type]
    FilterContent --> BuildCatalogJSON[Build catalog JSON]
    BuildCatalogJSON --> MoreCatalogs{More catalogs?}
    MoreCatalogs -->|Yes| CatalogLoop
    MoreCatalogs -->|No| UploadCatalogs

    MultipleCatalogs -->|No| BuildSingleCatalog[Build single catalog JSON]
    BuildSingleCatalog --> UploadCatalogs[Upload catalogs to hosting]

    UploadCatalogs --> CatalogUploadLoop{More catalogs?}
    CatalogUploadLoop -->|Yes| UploadCatalogFile[Upload catalog file]
    UploadCatalogFile --> CatalogUploadSuccess{Upload successful?}

    CatalogUploadSuccess -->|No| ErrorCatalogUpload[Error: Catalog upload failed]
    ErrorCatalogUpload --> End13([End])

    CatalogUploadSuccess -->|Yes| GetCatalogURL[Get catalog URL]
    GetCatalogURL --> StoreCatalogURL[Store catalog URL]
    StoreCatalogURL --> CatalogUploadLoop

    CatalogUploadLoop -->|No| GenerateDefinition[Generate publisher definition JSON]
    GenerateDefinition --> AddCatalogURLs[Add catalog URLs to definition]
    AddCatalogURLs --> AddReferrals{Add referrals?}

    AddReferrals -->|Yes| SelectReferrals[Select referral publishers]
    SelectReferrals --> AddReferralURLs[Add referral definition URLs]
    AddReferralURLs --> UploadDefinition

    AddReferrals -->|No| UploadDefinition[Upload definition to hosting]

    UploadDefinition --> DefUploadSuccess{Upload successful?}
    DefUploadSuccess -->|No| ErrorDefUpload[Error: Definition upload failed]
    ErrorDefUpload --> End14([End])

    DefUploadSuccess -->|Yes| GetDefinitionURL[Get definition URL]
    GetDefinitionURL --> GenerateLink[Generate genhub:// subscription link]
    GenerateLink --> ShowShareDialog[Show share dialog]

    ShowShareDialog --> DisplayLink[Display subscription link:<br/>genhub://subscribe?url=...]
    DisplayLink --> ShareOptions{User action?}

    ShareOptions -->|Copy Link| CopyToClipboard[Copy link to clipboard]
    CopyToClipboard --> ShowCopied[Show: Link copied]
    ShowCopied --> MainMenu

    ShareOptions -->|Generate QR| GenerateQR[Generate QR code]
    GenerateQR --> ShowQR[Display QR code]
    ShowQR --> MainMenu

    ShareOptions -->|Share Social| OpenShare[Open social share dialog]
    OpenShare --> MainMenu

    ShareOptions -->|Done| MainMenu

    MainMenu -->|Close| SaveState[Save project state]
    SaveState --> End15([End])
```

## Key Components

### Publisher Studio ViewModel

- **File**: `PublisherStudioViewModel.cs`
- **Responsibilities**:
  - Project lifecycle management
  - Navigation between views
  - State persistence
  - Validation orchestration

### Content Library

- **ViewModel**: `ContentLibraryViewModel.cs`
- **Features**:
  - Add/edit/delete content items
  - Manage releases and versions
  - Configure dependencies
  - Upload metadata (images, descriptions)

### Publish & Share

- **ViewModel**: `PublishShareViewModel.cs`
- **Features**:
  - Artifact upload progress tracking
  - Catalog generation and upload
  - Definition generation and upload
  - Subscription link generation
  - QR code generation

### Hosting Provider Abstraction

- **Interface**: `IHostingProvider.cs`
- **Implementations**:
  - `GoogleDriveHostingProvider.cs`
  - `GitHubHostingProvider.cs`
  - `DropboxHostingProvider.cs`
  - `ManualHostingProvider.cs`

### Hosting Provider Factory

- **File**: `HostingProviderFactory.cs`
- **Purpose**: Create appropriate hosting provider based on user selection
- **Features**:
  - OAuth flow management
  - State persistence
  - URL generation

## Validation Checks

### Project Validation

- Publisher ID uniqueness
- Required fields present
- Valid URLs
- Avatar image format

### Content Validation

- Content ID uniqueness within project
- Valid content type
- Target game specified
- At least one release

### Release Validation

- Valid SemVer version
- At least one artifact
- Artifact files exist
- Valid dependency references

### Dependency Validation

- No circular dependencies
- Valid publisher IDs
- Valid content IDs
- Valid version constraints

### Catalog Validation

- Schema version compatibility
- Size limit (5 MB recommended)
- Valid JSON syntax
- All URLs accessible

## Publishing Workflow

### Pre-Publish Checklist

1. All artifacts have files selected
2. All releases have versions
3. All dependencies are valid
4. No circular dependencies
5. No conflicts detected
6. Hosting provider configured

### Upload Process

1. **Artifacts**: Upload to hosting (Tier 3)
2. **Catalogs**: Generate and upload (Tier 2)
3. **Definition**: Generate and upload (Tier 1)

### Post-Publish

1. Generate subscription link
2. Test subscription link
3. Share with community
4. Monitor subscriptions (future feature)

## Error Handling

### Authentication Errors

- OAuth token expired
- Invalid credentials
- Network timeout

### Upload Errors

- File too large
- Network interruption
- Quota exceeded
- Permission denied

### Validation Errors

- Schema violations
- Circular dependencies
- Missing required fields
- Invalid references

### User Experience

- Progress indicators for uploads
- Detailed error messages
- Retry mechanisms
- Rollback on failure

## Project File Structure

### Project File

- **Location**: User-selected directory
- **Filename**: `{project-name}.genhub-project`
- **Format**: JSON
- **Contents**:
  - Publisher information
  - Hosting configuration
  - Content library
  - Releases and artifacts
  - OAuth tokens (encrypted)

### Project Directory

```
MyPublisher/
├── MyPublisher.genhub-project
├── artifacts/
│   ├── mod-v1.0.0.zip
│   ├── mod-v1.1.0.zip
│   └── map-pack-v1.0.0.zip
├── images/
│   ├── avatar.png
│   ├── banner-mod.jpg
│   └── screenshot-1.jpg
└── generated/
    ├── catalog.json
    ├── catalog-maps.json
    └── publisher_definition.json
```

## Related Files

- `GenHub/Features/Tools/ViewModels/PublisherStudioViewModel.cs`
- `GenHub/Features/Tools/ViewModels/ContentLibraryViewModel.cs`
- `GenHub/Features/Tools/ViewModels/PublishShareViewModel.cs`
- `GenHub/Features/Tools/Services/PublisherStudioService.cs`
- `GenHub/Features/Tools/Services/Hosting/HostingProviderFactory.cs`
- `GenHub/Features/Tools/Services/Hosting/GoogleDriveHostingProvider.cs`
- `GenHub.Core/Models/Providers/PublisherDefinition.cs`
- `GenHub.Core/Models/Providers/PublisherCatalog.cs`
