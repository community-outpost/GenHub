---
title: GitHub Manager Workflow
description: Complete end-to-end flow for GitHub content acquisition
---

# Flowchart: GitHub Manager Workflow

This flowchart illustrates the complete workflow of the GitHub Manager feature, from repository configuration through content installation and profile integration.

```mermaid
%%{init: {
  'theme': 'base',
  'themeVariables': {
    'primaryColor': '#e2e8f0',
    'primaryTextColor': '#1a202c',
    'primaryBorderColor': '#4a5568',
    'lineColor': '#2d3748',
    'secondaryColor': '#f7fafc',
    'tertiaryColor': '#edf2f7',
    'background': '#ffffff',
    'mainBkg': '#f7fafc',
    'secondBkg': '#edf2f7',
    'nodeBorder': '#4a5568',
    'clusterBkg': '#f7fafc',
    'clusterBorder': '#a0aec0',
    'edgeLabelBackground': '#ffffff'
  }
}}%%

graph TD
    subgraph UserAction ["👤 User Interaction"]
        A["User opens GitHub Manager"]
        B["Selects repository"]
        C["Browses releases"]
        D["Clicks Install on release"]
    end

    subgraph GitHubManagerUI ["🎨 GitHub Manager UI"]
        E["GitHubManagerViewModel"]
        F["Display releases and assets"]
        G["Show installation progress"]
    end

    subgraph GitHubService ["🔌 GitHub Service Layer"]
        H["IGitHubServiceFacade"]
        I["OctokitGitHubApiClient"]
        J["Fetch release metadata"]
        K["Download release assets"]
    end

    subgraph ContentPipeline ["⚙️ Content Pipeline"]
        L["ContentOrchestrator"]
        M["GitHubContentProvider"]
        N["GitHubDiscoverer"]
        O["GitHubResolver"]
        P["GitHubContentDeliverer"]
    end

    subgraph DeliveryProcess ["📦 Content Delivery"]
        Q["Download ZIP files"]
        R{Is GameClient<br/>content?}
        S["Extract ZIP archives"]
        T["Scan files & mark<br/>executables (.exe)"]
    end

    subgraph ManifestManagement ["📋 Manifest Management"]
        V["Build ContentManifest"]
        W["Validate manifest"]
        X["IContentManifestPool"]
        Y["Store in manifest pool"]
    end

    subgraph ProfileIntegration ["🎮 Profile Integration"]
        Z["Content available in<br/>GameProfile UI"]
        AA["User enables content<br/>in profile"]
        AB["Workspace preparation"]
        AC["Game launch with content"]
    end

    A --> E
    B --> E
    E --> F
    C --> F
    D --> G
    
    E --> H
    H --> I
    I --> J
    J --> F
    
    D --> L
    L --> M
    M --> N
    N -->|Discover releases| M
    M --> O
    O -->|Resolve manifests| M
    M --> P
    
    P --> Q
    Q --> R
    R -->|Yes| S
    R -->|No| V
    S --> T
    T --> V
    
    V --> W
    W --> X
    X --> Y
    Y --> Z
    
    Z --> AA
    AA --> AB
    AB --> AC

    classDef user fill:#3182ce,stroke:#2c5282,stroke-width:2px,color:#ffffff
    classDef ui fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    classDef service fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    classDef pipeline fill:#e53e3e,stroke:#c53030,stroke-width:2px,color:#ffffff
    classDef delivery fill:#d97706,stroke:#b45309,stroke-width:2px,color:#ffffff
    classDef manifest fill:#0891b2,stroke:#0e7490,stroke-width:2px,color:#ffffff
    classDef profile fill:#7c3aed,stroke:#6d28d9,stroke-width:2px,color:#ffffff

    class A,B,C,D user
    class E,F,G ui
    class H,I,J,K service
    class L,M,N,O,P pipeline
    class Q,R,S,T delivery
    class V,W,X,Y manifest
    class Z,AA,AB,AC profile
```

**GitHub Manager Workflow Overview:**

1. **User Interaction**: User navigates GitHub Manager UI to discover and select content
2. **Repository Browsing**: UI displays available releases through GitHub Service Layer
3. **Content Selection**: User selects release for installation
4. **Pipeline Activation**: Content Orchestrator routes to GitHub Content Provider
5. **Download**: GitHubContentDeliverer downloads release assets
6. **GameClient Extraction**: For ZIP archives, system extracts and marks executable files
7. **Manifest Generation**: System builds ContentManifest with extracted file references and IsExecutable flags
8. **Validation**: Manifest validated and stored in manifest pool
9. **Profile Integration**: Content immediately available in GameProfile UI
10. **Launch Integration**: User enables content in profile for game launching

## Key Decision Points

### Content Type Determination

The workflow includes a critical decision point for GameClient content:

**Is GameClient Content?**
- **Yes**: Extract ZIP, scan for executables, generate detailed manifest
- **No**: Use manifest as-is, store without extraction

### ZIP Archive Handling

For GameClient content with ZIP archives:

1. Downloads complete successfully
2. System detects ZIP file extensions
3. Extracts to target directory (in-place)
4. Recursively scans extracted directory for all files
5. Builds new manifest with extracted file paths
6. Marks .exe files with IsExecutable flag
7. Removes original ZIP files
8. Returns updated manifest to ContentOrchestrator
9. ContentOrchestrator validates and stores in manifest pool

### Manifest Pool Storage

After validation, manifests are stored in the pool:

1. ManifestId generated deterministically
2. Manifest added to IContentManifestPool
3. Content becomes queryable by GameProfile system
4. Appears immediately in profile content dropdowns

## Component Interactions

### GitHub Manager → GitHub Service Facade

- UI requests release information
- Facade coordinates API calls
- Returns structured GitHubRelease objects
- Handles authentication and rate limiting

### Content Provider → Deliverer

- Provider orchestrates pipeline
- Deliverer downloads assets
- Returns OperationResult with success/failure
- Provides progress reporting

### Content Deliverer → ContentOrchestrator

- Deliverer downloads and extracts content
- Scans extracted files and marks executables
- Builds updated ContentManifest
- Returns manifest to ContentOrchestrator
- Orchestrator validates and adds to manifest pool

### Manifest Pool → GameProfile

- Profile queries pool for available content
- Pool returns manifests matching GameType
- Profile displays content in UI
- User selects content for enabling

## Error Handling Flow

### Download Failures

```mermaid
%%{init: {
  'theme': 'base',
  'themeVariables': {
    'primaryColor': '#e2e8f0',
    'primaryTextColor': '#1a202c',
    'primaryBorderColor': '#4a5568'
  }
}}%%

graph LR
    A[Download Attempt] --> B{Success?}
    B -->|Yes| C[Continue]
    B -->|No| D{Retry Count<br/>< Max?}
    D -->|Yes| E[Wait with<br/>backoff]
    E --> A
    D -->|No| F[Report Failure]
    F --> G[Log Error]
    G --> H[Notify User]
```

### Extraction Failures

```mermaid
%%{init: {
  'theme': 'base',
  'themeVariables': {
    'primaryColor': '#e2e8f0',
    'primaryTextColor': '#1a202c',
    'primaryBorderColor': '#4a5568'
  }
}}%%

graph LR
    A[Extract ZIP] --> B{Success?}
    B -->|Yes| C[Scan Content]
    B -->|No| D[Cleanup Partial]
    D --> E[Log Error]
    E --> F[Notify User]
    F --> G[Suggest Actions]
```

## Performance Optimizations

### Caching Strategy

The workflow includes multiple caching layers:

1. **API Response Cache**: GitHub release metadata cached
2. **Manifest Cache**: Resolved manifests cached
3. **Search Results Cache**: Discovery results cached
4. **Pool Query Cache**: Manifest pool queries cached

### Parallel Operations

Where possible, operations execute in parallel:

- Multiple asset downloads from same release
- Concurrent file extraction from archives
- Parallel manifest validation checks

### Resource Management

The system manages resources efficiently:

- Streaming downloads for large files
- Incremental extraction with progress
- Memory-efficient file scanning
- Automatic cleanup of temporary files
