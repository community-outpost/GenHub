# Flowchart: Game Detection & Validation

This flowchart illustrates the pipeline for detecting, identifying, and validating game installations and their specific versions (executables).

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
    subgraph Start ["🚀 Initiation"]
        A["System Start or<br/>User Scan Request"]
    end

    subgraph InstallationDetection ["🔍 Phase 1: Installation Detection"]
        B["IGameInstallation<br/>DetectionOrchestrator"]
        C1["Windows<br/>InstallationDetector"]
        C2["Linux<br/>InstallationDetector"]
        D["Registry, Steam,<br/>EA App, Manual Paths"]
        E["Unvalidated<br/>GameInstallation Objects"]
    end

    subgraph InstallationValidation ["✅ Phase 2: Installation Validation"]
        F["IGameInstallation<br/>Validator"]
        G["Validated<br/>GameInstallation Objects"]
    end

    subgraph VersionDetection ["🔎 Phase 3: Version Detection"]
        H["IGameVersion<br/>DetectionOrchestrator"]
        I["IGameVersionDetector"]
        J["Scans Installation<br/>for Executables<br/>(game.dat, generals.exe)"]
        K["Unvalidated<br/>GameVersion Objects"]
    end

    subgraph VersionValidation ["✔️ Phase 4: Version Validation"]
        L["IGameVersion<br/>Validator"]
        M["Validated<br/>GameVersion Objects"]
    end
    
    subgraph FinalOutput ["🏁 Final Output"]
        N["Registry of all<br/>available GameVersions"]
    end

    A --> B
    B --> C1
    B --> C2
    C1 --> D
    C2 --> D
    D --> E
    E --> F
    F --> G
    
    G --> H
    H --> I
    I --> J
    J --> K
    K --> L
    L --> M
    M --> N

    classDef orchestrator fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    classDef detector fill:#3182ce,stroke:#2c5282,stroke-width:2px,color:#ffffff
    classDef validator fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    classDef data fill:#ed8936,stroke:#dd6b20,stroke-width:2px,color:#ffffff

    class B,H orchestrator
    class C1,C2,I detector
    class F,L validator
    class A,D,E,G,J,K,M,N data
```

**Detection & Validation Logic:**

| Phase | Component | Responsibility | Input | Output |
|---|---|---|---|---|
| **1. Installation Detection** | `IGameInstallationDetectionOrchestrator` | Coordinates platform-specific detectors to find game folders. | User request | `GameInstallation` objects |
| **2. Installation Validation** | `IGameInstallationValidator` | Ensures detected folders are valid, complete game installations. | `GameInstallation` | Validated `GameInstallation` |
| **3. Version Detection** | `IGameVersionDetectionOrchestrator` | Scans validated installations to find all executable versions. | Validated `GameInstallation` | `GameVersion` objects |
| **4. Version Validation** | `IGameVersionValidator` | Verifies that each executable is functional and identifiable. | `GameVersion` | Validated `GameVersion` |
