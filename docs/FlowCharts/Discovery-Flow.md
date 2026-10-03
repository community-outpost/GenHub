# Flowchart: Content Discovery

This flowchart details the process of discovering content from publishers and other sources, coordinated by the `ContentOrchestrator`.

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
        A["User selects publisher<br/>or searches in Content Browser"]
    end

    subgraph Tier1 ["Tier 1: Content Orchestrator"]
        B["IContentOrchestrator.SearchAsync()"]
        C["Broadcasts search query<br/>to all registered discoverers"]
        D["Aggregates results<br/>from all discoverers"]
        E["Returns unified list<br/>of ContentSearchResult"]
    end

    subgraph Tier2 ["Tier 2: Content Discoverers"]
        P1["GenericCatalogDiscoverer"]
        P2["ModDBDiscoverer"]
        P3["CNCLabsDiscoverer"]
        P4["AODMapsDiscoverer"]
        P5["GitHubDiscoverer"]
    end

    subgraph Tier3 ["Tier 3: Data Sources"]
        D1["Publisher Catalogs<br/>(catalog.json)"]
        D2["ModDB Web Pages<br/>(scraping)"]
        D3["CNCLabs API<br/>(JSON)"]
        D4["AOD Maps API<br/>(JSON)"]
        D5["GitHub Releases<br/>(API)"]
    end

    A --> B
    B --> C
    C --> P1
    C --> P2
    C --> P3
    C --> P4
    C --> P5

    P1 -->|Fetches| D1
    P2 -->|Scrapes| D2
    P3 -->|Queries| D3
    P4 -->|Queries| D4
    P5 -->|Queries| D5

    D1 -->|Returns ContentSearchResult| P1
    D2 -->|Returns ContentSearchResult| P2
    D3 -->|Returns ContentSearchResult| P3
    D4 -->|Returns ContentSearchResult| P4
    D5 -->|Returns ContentSearchResult| P5

    P1 -->|Returns results| D
    P2 -->|Returns results| D
    P3 -->|Returns results| D
    P4 -->|Returns results| D
    P5 -->|Returns results| D

    D --> E
    E -->|Updates UI| A

    classDef orchestrator fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    classDef discoverer fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    classDef source fill:#e53e3e,stroke:#c53030,stroke-width:2px,color:#ffffff
    classDef user fill:#3182ce,stroke:#2c5282,stroke-width:2px,color:#ffffff

    class A user
    class B,C,D,E orchestrator
    class P1,P2,P3,P4,P5 discoverer
    class D1,D2,D3,D4,D5 source
```

**Discovery Workflow:**

1.  **Initiation**: The user selects a publisher from the Downloads sidebar or initiates a search from the UI.
2.  **Orchestration**: The `IContentOrchestrator` receives the request and forwards it to every registered `IContentDiscoverer`.
3.  **Discoverer Action**: Each `ContentDiscoverer` performs its source-specific action (catalog fetch, API call, web scrape, file scan) and returns lightweight `ContentSearchResult` objects.
4.  **Aggregation**: The orchestrator collects all results from discoverers and returns a unified list.
5.  **Display**: Results are displayed in the Content Browser UI for user selection.

**Publisher/Catalog Model:**

The GenericCatalogDiscoverer implements the 3-tier hosting model:
- **Tier 1**: PublisherDefinition (publisher identity + catalog URLs)
- **Tier 2**: PublisherCatalog (content items + releases + dependencies)
- **Tier 3**: Artifacts (downloadable files referenced by catalog)

Users subscribe to publishers via `genhub://` protocol links, which point to Tier 1 definitions. The definition contains stable URLs to Tier 2 catalogs, allowing publishers to migrate hosting without breaking subscriptions.
