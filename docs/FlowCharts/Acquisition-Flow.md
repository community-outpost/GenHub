# Flowchart: Content Acquisition Layer

This flowchart details the critical transformation step where a `ContentManifest` with artifact references is processed, downloaded, and stored in the Content-Addressable Storage (CAS) system.

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

graph TB
    subgraph SI ["📥 Service Input"]
        A["📋 Resolved<br/>ContentManifest<br/>From Resolution
<br>"]
        B["🎯 Acquisition<br/>Service<br/>Process Start
<br>"]
        C["⚡ AcquireContent<br/>Async Method<br/>Execution
<br>"]
    end

    subgraph DL ["⬇️ Download Phase"]
        D1["📦 Download<br/>Artifacts<br/>Progress Tracking
<br>"]
        D2["🔐 Verify<br/>SHA256 Hashes<br/>Integrity Check
<br>"]
        D3["📂 Extract<br/>Archives<br/>Temp Directory
<br>"]
    end

    subgraph CAS ["🗄️ CAS Storage Phase"]
        E1["🔍 Scan Extracted<br/>Files<br/>Hash Calculation
<br>"]
        E2["💾 Store Files<br/>in CAS<br/>By Hash
<br>"]
        E3["🔄 Deduplication<br/>Check<br/>Reuse Existing
<br>"]
        E4["📋 Update Manifest<br/>File References<br/>CAS Paths
<br>"]
    end

    subgraph DEP ["🔗 Dependency Phase"]
        F1["🔍 Check<br/>Dependencies<br/>Recursive Scan
<br>"]
        F2["📥 Resolve Missing<br/>Dependencies<br/>Cross-Publisher
<br>"]
        F3["⬇️ Acquire<br/>Dependencies<br/>Recursive Call
<br>"]
    end

    subgraph SO ["📤 Service Output"]
        G["📋 Updated<br/>ContentManifest<br/>CAS References
<br>"]
        H["🎯 Ready for<br/>Assembly Stage<br/>Workspace Creation
<br>"]
    end

    A -->|Input| B
    B -->|Start| C

    C -->|Download| D1
    D1 -->|Verify| D2
    D2 -->|Extract| D3

    D3 -->|Scan| E1
    E1 -->|Store| E2
    E2 -->|Check| E3
    E3 -->|Update| E4

    E4 -->|Check| F1
    F1 -->|Missing?| F2
    F2 -->|Resolve| F3
    F3 -.->|Recursive| C

    F1 -->|All Present| G
    G -->|Final Output| H

    classDef service fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    classDef download fill:#e53e3e,stroke:#c53030,stroke-width:2px,color:#ffffff
    classDef cas fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    classDef dependency fill:#ed8936,stroke:#dd6b20,stroke-width:2px,color:#ffffff
    classDef output fill:#3182ce,stroke:#2c5282,stroke-width:2px,color:#ffffff

    class A,B,C service
    class D1,D2,D3 download
    class E1,E2,E3,E4 cas
    class F1,F2,F3 dependency
    class G,H output
```

**Acquisition Workflow:**

| Phase | Process | Details | Key Benefits |
|-------|---------|---------|--------------|
| **Download** | Artifact retrieval | Downloads files from publisher-hosted URLs with progress tracking | Supports any hosting provider |
| **Verification** | Hash validation | Verifies SHA256 hashes match catalog metadata | Ensures file integrity |
| **Extraction** | Archive processing | Extracts ZIP/RAR archives to temporary directory | Handles compressed content |
| **CAS Storage** | Content-addressable storage | Stores files by SHA256 hash, deduplicates automatically | Saves disk space, enables sharing |
| **Dependency Resolution** | Recursive acquisition | Resolves and acquires dependencies (same-catalog and cross-publisher) | Ensures complete installation |

**Content-Addressable Storage (CAS) Benefits:**

- **Deduplication**: Files shared across multiple mods are stored only once
- **Integrity**: Files are verified by hash and immutable once stored
- **Efficiency**: Workspace strategies (symlink/hardlink) reference CAS files without duplication
- **Reliability**: Corrupted files are automatically detected and re-downloaded

**Cross-Publisher Dependencies:**

The acquisition phase handles dependencies that reference content from other publishers:
1. Check if dependency is already installed in ManifestPool
2. If missing, check if user is subscribed to the dependency's publisher
3. If not subscribed, prompt user to subscribe via genhub:// link
4. Recursively acquire dependency content before continuing with main content
