---
title: GitHub Manager
description: GitHub content discovery, download, and integration system
---

# GitHub Manager

The GitHub Manager feature provides comprehensive tools for discovering, downloading, and integrating community content hosted on GitHub. Browse releases, download game clients, mods, and patches directly from GitHub repositories with full validation and automatic integration into your GameProfile system.

## Overview

GenHub's GitHub Manager enables seamless integration with GitHub-hosted content, supporting:

- Repository and release browsing
- Automated content discovery and download
- Game client detection and installation
- ZIP archive extraction and file management
- Automatic manifest generation and validation
- Direct integration with GameProfile system

## Key Capabilities

### Repository Management

- Configure trusted GitHub repositories
- Browse available releases and tags
- View release notes and changelog information
- Track repository updates and new releases

### Content Discovery

- Automatic detection of game-related content
- Content type inference from release artifacts
- Game type detection for Generals and Zero Hour
- Version identification and tracking

### Download Management

- Direct GitHub release asset downloads
- Progress tracking and cancellation support
- Hash verification for integrity
- Automatic retry on transient failures

### Content Integration

- ZIP archive extraction for game clients
- Executable detection and validation
- Manifest generation from extracted content
- Automatic addition to content manifest pool
- Immediate availability in GameProfile system

## GitHub Content Pipeline

### Three-Tier Architecture Integration

The GitHub Manager operates within GenHub's three-tier content pipeline:

**Tier 1: Content Orchestrator**
- Coordinates GitHub content alongside other providers
- Manages system-wide caching and search aggregation

**Tier 2: GitHub Content Provider**
- Orchestrates GitHub-specific pipeline components
- Manages discovery, resolution, and delivery flow

**Tier 3: Pipeline Components**
- GitHubDiscoverer: Discovers available releases
- GitHubResolver: Resolves release metadata to manifests
- GitHubContentDeliverer: Downloads and extracts content

### Component Responsibilities

#### GitHubDiscoverer

**Purpose**: Discovers available GitHub releases from configured repositories

**Key Operations**:
- Scans configured GitHub repositories
- Identifies relevant releases and tags
- Filters by content type and game compatibility
- Returns lightweight ContentSearchResult objects

**Discovery Strategy**:
- Uses GitHub API for authenticated access
- Implements rate limiting and caching
- Supports public and private repositories
- Handles pagination for large repositories

#### GitHubResolver

**Purpose**: Transforms discovered releases into detailed ContentManifest objects

**Key Operations**:
- Fetches complete release metadata
- Analyzes release assets and file structure
- Infers content type and game compatibility
- Generates comprehensive ContentManifest

**Resolution Logic**:
- Examines release asset names and types
- Detects ZIP archives for game clients
- Identifies executables and configuration files
- Applies naming conventions for metadata

#### GitHubContentDeliverer

**Purpose**: Downloads, extracts, and prepares GitHub content for installation

**Key Operations**:
- Downloads release assets from GitHub URLs
- Extracts ZIP archives for GameClient content
- Validates downloaded file integrity
- Prepares content for storage and manifest pooling

**Delivery Process**:
- Downloads all manifest files to target directory
- Detects GameClient content with ZIP files
- Extracts archives and removes originals
- Scans extracted files (marks .exe files as IsExecutable)
- Generates updated manifests with extracted files
- Returns updated manifest to ContentOrchestrator

**Special Handling for GameClients**:
When delivering GameClient content with ZIP archives:
1. Downloads all ZIP files from release assets
2. Extracts archives to target directory
3. Recursively scans extracted directory for all files
4. Builds new manifest with extracted file references
5. Marks executable files (.exe) as IsExecutable
6. Returns updated manifest to ContentOrchestrator
7. ContentOrchestrator validates and adds to manifest pool
8. Content immediately available in GameProfile dropdowns via ProfileContentLoader

## GitHub Manager UI

### Repository Configuration

**Adding Repositories**:
1. Navigate to GitHub Manager settings
2. Click "Add Repository"
3. Enter repository owner and name
4. Configure content type preferences
5. Save repository configuration

**Managing Repositories**:
- View list of configured repositories
- Edit repository settings
- Remove repositories
- Enable or disable repositories
- View repository status and last update

### Content Browser

**Browsing Releases**:
1. Open GitHub Manager window
2. Select repository from dropdown
3. View available releases and tags
4. Expand releases to view assets
5. Read release notes and descriptions

**Content Details**:
- Release version and tag information
- Release date and author
- Asset list with file sizes
- Download counts and popularity
- Compatibility information

### Download and Installation

**Installing GitHub Content**:
1. Select desired release from browser
2. Click "Install" or "Download"
3. Monitor download progress
4. Automatic extraction and validation
5. Content appears in GameProfile

**Installation Progress**:
- Download progress with speed metrics
- Extraction progress for archives
- Validation status and results
- Installation completion notification
- Error reporting and retry options

## Authentication and API Access

### GitHub Token Configuration

**Setting Up Authentication**:
1. Navigate to Settings → GitHub
2. Click "Configure Token"
3. Generate Personal Access Token on GitHub
4. Enter token in GenHub
5. Validate and save configuration

**Token Requirements**:
- Public repository access: No token required
- Private repository access: Token with repo scope
- Increased rate limits: Token recommended
- Workflow artifact access: Token required

**Token Validation**:
- Automatic validation on save
- API connection testing
- Rate limit checking
- Scope verification

### Rate Limiting

**GitHub API Limits**:
- Unauthenticated: 60 requests per hour
- Authenticated: 5000 requests per hour
- GenHub implements intelligent caching
- Automatic retry with exponential backoff

## Content Types and Detection

### Supported Content Types

**Game Clients**:
- Standalone game executables
- Patched game versions
- Modified game clients
- Development builds

**Mods**:
- Total conversion mods
- Gameplay modifications
- Balance patches
- Content packs

**Patches**:
- Bug fix patches
- Compatibility updates
- Performance improvements
- Feature additions

**Add-ons**:
- Tools and utilities
- Map editors
- Asset packs
- UI enhancements

### Content Type Inference

**Detection Heuristics**:
- Repository name analysis
- Release title parsing
- Asset filename patterns
- README content scanning

**Game Type Detection**:
- Generals vs Zero Hour identification
- Version string parsing
- Executable signature analysis
- Manifest metadata inspection

## Manifest Generation

### Automatic Manifest Creation

**From ZIP Archives**:
1. Download GitHub release asset
2. Extract ZIP to temporary location
3. Scan for executables and key files
4. Generate ManifestFile entries
5. Build ContentManifest with metadata
6. Store in manifest pool

**Manifest Structure**:
- Deterministic ManifestId generation
- Content type classification
- Game type identification
- File list with hashes and paths
- Publisher information from repository
- Metadata from release notes

### Manifest Validation

**Validation Checks**:
- All required files present
- File hashes match expected values
- Executable permissions correct
- Dependencies satisfied
- No file conflicts

**Validation Results**:
- Success or failure status
- Detailed issue descriptions
- Resolution recommendations
- Automatic retry capabilities

## Integration with GameProfile System

### Profile Content Management

**Adding GitHub Content to Profiles**:
1. Create or edit GameProfile
2. Browse available content
3. GitHub content appears in dropdowns
4. Select desired content
5. Content enabled in profile

**Content Availability**:
- Immediately after successful download
- Listed in content selection UI
- Filtered by compatibility
- Sorted by relevance

### Workspace Integration

**Content Assembly**:
- GitHub content integrates with workspace strategies
- Files copied or linked based on strategy
- Executables properly referenced
- Dependencies resolved automatically

**Launch Integration**:
- Profile launcher uses GitHub content
- Workspace prepared with content
- Game launches with modifications
- Runtime monitoring and logging

## Advanced Features

### Content Caching

**Caching Strategy**:
- API responses cached for performance
- Release metadata cached system-wide
- Manifest objects cached after generation
- Cache invalidation on updates

**Cache Management**:
- Automatic expiration policies
- Manual cache clearing options
- Pattern-based invalidation
- Memory-efficient storage

### Dependency Resolution

**Automatic Dependencies**:
- Content dependency detection
- Required content identification
- Automatic installation of dependencies
- Conflict detection and resolution

**Dependency Types**:
- Required: Must be installed
- Optional: Recommended but not required
- Conflicting: Cannot coexist
- Alternative: Choose one of several

### Update Management

**Content Updates**:
- Detection of newer releases
- Update notifications
- One-click update installation
- Automatic manifest updates

**Version Management**:
- Multiple versions supported
- Version switching in profiles
- Rollback capabilities
- Version comparison tools

## Troubleshooting

### Common Issues

**Download Failures**:
- Check network connectivity
- Verify GitHub API availability
- Confirm authentication token validity
- Review rate limit status

**Extraction Errors**:
- Ensure sufficient disk space
- Check file permissions
- Verify ZIP archive integrity
- Review extraction logs

**Content Not Appearing**:
- Verify successful download
- Check manifest pool status
- Confirm content type compatibility
- Review validation results

### Diagnostic Tools

**Logging**:
- Detailed operation logging
- Error tracking and reporting
- Performance metrics
- Debug mode for troubleshooting

**Validation**:
- Manual manifest validation
- File integrity checking
- Dependency verification
- Compatibility testing

## Best Practices

### Repository Configuration

**Recommendations**:
- Configure official game repositories first
- Add trusted community repositories
- Review repository content before adding
- Regularly update repository lists

**Security**:
- Use authentication for private repos
- Verify repository ownership
- Review release notes carefully
- Scan downloaded content

### Content Management

**Organization**:
- Use descriptive profile names
- Group related content
- Document custom configurations
- Maintain backup profiles

**Performance**:
- Enable caching for frequently accessed content
- Clean up unused content regularly
- Monitor disk space usage
- Optimize workspace strategies

## API and Extension Points

### GitHub Service Facade

**IGitHubServiceFacade Interface**:
- High-level GitHub operations
- Repository and release management
- Workflow and artifact access
- Error handling and retry logic

**Service Methods**:
- GetLatestReleaseAsync: Fetch latest release
- GetReleasesAsync: List all releases
- GetWorkflowRunsAsync: Access CI/CD runs
- DownloadArtifactAsync: Download build artifacts

### GitHub API Client

**IGitHubApiClient Interface**:
- Low-level GitHub API access
- Authentication management
- Rate limiting handling
- API versioning support

**Client Features**:
- Octokit-based implementation
- Automatic retry with backoff
- Connection pooling
- Request caching

## Integration Examples

### Adding TheSuperHackers Content

**End-to-End Workflow**:
1. User opens GitHub Manager window
2. Selects TheSuperHackers repository
3. Browses available releases
4. Clicks "Install" on latest release
5. System downloads ZIP archive
6. Extracts content to temporary directory
7. Scans for game client executables
8. Generates manifest with extracted files
9. Validates manifest structure
10. Adds manifest to pool
11. Content appears in GameProfile dropdown
12. User enables content in profile
13. Workspace prepared with content
14. Game launches successfully

### Custom Repository Integration

**Adding New Repository**:
1. Identify GitHub repository URL
2. Configure in GitHub Manager settings
3. System discovers available releases
4. Content becomes browsable
5. Download and installation flow identical
6. Integration with existing content
