# Flowchart: Complete User Installation Flow

This flowchart illustrates the end-to-end process when a user subscribes to a publisher and installs content, showing how all architectural layers work together with the subscription system.

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

flowchart TD
 subgraph P0["🔗 Phase 0: Subscription"]
        A0["👤 User clicks<br>genhub://subscribe<br>link from website
<br>"]
        A01["📥 PublisherDefinition<br>Service fetches<br>definition JSON
<br>"]
        A02["✅ User confirms<br>subscription in<br>dialog
<br>"]
        A03["💾 Publisher saved<br>to subscriptions.json<br>appears in sidebar
<br>"]
  end
 subgraph P1["🔍 Phase 1: Discovery"]
        A1["👤 User selects<br>publisher from<br>Downloads sidebar
<br>"]
        A2["🌐 GenericCatalog<br>Discoverer fetches<br>catalog JSON
<br>"]
        A3["📦 ContentSearchResult<br>objects returned<br>with content metadata
<br>"]
  end
 subgraph P2["🎯 Phase 2: Resolution"]
        B1["👆 User clicks<br>Install button<br>on content entry
<br>"]
        B2["🌐 GenericCatalog<br>Resolver fetches<br>release details
<br>"]
        B3["📋 ContentManifest<br>created with<br>artifact references
<br>"]
  end
 subgraph P3["⬇️ Phase 3: Acquisition"]
        C1["📦 Download artifacts<br>to temp location<br>with progress tracking
<br>"]
        C2["📂 Extract archives<br>and verify<br>SHA256 hashes
<br>"]
        C3["🗄️ Store files in<br>Content-Addressable<br>Storage (CAS)
<br>"]
        C4["📋 Manifest updated<br>with CAS file<br>references
<br>"]
  end
 subgraph P4["🏗️ Phase 4: Assembly"]
        D1["⚖️ Workspace Strategy<br>selected from<br>profile settings
<br>"]
        D2["🔗 Symlink/Copy files<br>from CAS to<br>workspace
<br>"]
        D3["📝 Write Options.ini<br>with game<br>settings
<br>"]
        D4["✅ Workspace<br>prepared and<br>validated
<br>"]
  end
 subgraph P5["🚀 Phase 5: Launch"]
        E1["👆 User clicks<br>Launch button<br>on profile
<br>"]
        E2["🎮 GameLauncher starts<br>isolated process<br>from workspace
<br>"]
        E3["🎯 Game runs with<br>installed content<br>enabled
<br>"]
  end
    A0 -- Protocol Handler --> A01
    A01 -- Fetch Definition --> A02
    A02 -- Confirm --> A03
    P0 -- Publisher Added --> P1
    A1 -- Select Publisher --> A2
    A2 -- Fetch Catalog --> A3
    P1 -- User Selection --> P2
    B1 -- Install Request --> B2
    B2 -- Fetch Release --> B3
    P2 -- Manifest Ready --> P3
    C1 -- Download Complete --> C2
    C2 -- Verified --> C3
    C3 -- Stored --> C4
    P3 -.-> P4
    D1 -- Strategy Applied --> D2
    D2 -- Files Mapped --> D3
    D3 -- Config Written --> D4
    P4 -.-> P5
    E1 -- Launch Command --> E2
    E2 -- Process Started --> E3
    style P0 fill:#9f7aea,stroke:#805ad5,stroke-width:2px,color:#ffffff
    style P1 fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    style P2 fill:#e53e3e,stroke:#c53030,stroke-width:2px,color:#ffffff
    style P3 fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    style P4 fill:#ed8936,stroke:#dd6b20,stroke-width:2px,color:#ffffff
    style P5 fill:#3182ce,stroke:#2c5282,stroke-width:3px,color:#ffffff

```
**End-to-End Data Flow Analysis:**

| Phase | Input Data | Processing Method | Output Data | Key Transformation |
|-------|------------|-------------------|-------------|-------------------|
| **Subscription** | genhub:// URL | Protocol handler + definition fetch | Subscribed publisher | URL → Publisher registration |
| **Discovery** | Publisher selection | Catalog fetch + parsing | `ContentSearchResult` collection | Catalog JSON → Structured results |
| **Resolution** | Content selection | Release fetch + parsing | `ContentManifest` | Lightweight data → Installation plan |
| **Acquisition** | Artifact URLs | Download + hash verification + CAS storage | Files in CAS | Remote artifacts → Local deduplicated storage |
| **Assembly** | File references + strategy | CAS file mapping + workspace creation | Ready workspace | CAS references → Functional environment |
| **Launch** | Workspace path + config | Process creation + monitoring | Running game process | Static files → Active game session |

**Real-World Implementation Example:**

1. **Subscription**: User clicks genhub://subscribe link → Definition fetch → Publisher added to sidebar
2. **Discovery**: User selects publisher → Catalog fetch → Content list displayed
3. **Resolution**: User clicks Install → Release details fetched → Manifest with artifact URLs created
4. **Acquisition**: Artifacts downloaded (150MB) → SHA256 verified → Files stored in CAS by hash
5. **Assembly**: Strategy selection → Files symlinked/copied from CAS → Workspace validated
6. **Launch**: Process execution → Isolated environment → Content-enabled gameplay experience
