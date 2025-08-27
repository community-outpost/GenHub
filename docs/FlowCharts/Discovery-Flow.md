# Flowchart: Content Discovery

This flowchart details the process of discovering content from multiple sources, coordinated by the `ContentOrchestrator`.

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
    subgraph UserAction ["👤 User Action"]
        A["User initiates search<br/>in Content Browser"]
    end

    subgraph Tier1 ["Tier 1: Content Orchestrator"]
        B["IContentOrchestrator.SearchAsync()"]
        C["Broadcasts search query<br/>to all registered providers"]
        D["Aggregates results<br/>from all providers"]
        E["Returns unified list<br/>of ContentSearchResult"]
    end

    subgraph Tier2 ["Tier 2: Content Providers"]
        P1["GitHubContentProvider"]
        P2["ModDBContentProvider"]
        P3["LocalFileSystemProvider"]
    end

    subgraph Tier3 ["Tier 3: Pipeline Components (Discoverers)"]
        D1["GitHubReleasesDiscoverer"]
        D2["ModDBDiscoverer"]
        D3["FileSystemDiscoverer"]
    end

    A --> B
    B --> C
    C --> P1
    C --> P2
    C --> P3

    P1 -->|Uses| D1
    P2 -->|Uses| D2
    P3 -->|Uses| D3

    D1 -->|Returns ContentSearchResult| P1
    D2 -->|Returns ContentSearchResult| P2
    D3 -->|Returns ContentSearchResult| P3

    P1 -->|Returns results| D
    P2 -->|Returns results| D
    P3 -->|Returns results| D
    
    D --> E
    E -->|Updates UI| A

    classDef orchestrator fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    classDef provider fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    classDef component fill:#e53e3e,stroke:#c53030,stroke-width:2px,color:#ffffff
    classDef user fill:#3182ce,stroke:#2c5282,stroke-width:2px,color:#ffffff

    class A user
    class B,C,D,E orchestrator
    class P1,P2,P3 provider
    class D1,D2,D3 component
```

**Discovery Workflow:**

1.  **Initiation**: The user starts a search from the UI.
2.  **Orchestration**: The `IContentOrchestrator` receives the request and forwards it to every registered `IContentProvider`.
3.  **Provider Action**: Each `ContentProvider` invokes its specific `IContentDiscoverer` component.
4.  **Discovery**: The `IContentDiscoverer` performs the source-specific action (API call, web scrape, file scan) and returns lightweight `ContentSearchResult` objects.
5
