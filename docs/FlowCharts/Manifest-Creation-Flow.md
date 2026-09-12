# Flowchart: ContentManifest Creation

This flowchart outlines the process of creating a `ContentManifest` file, either programmatically via a builder or automatically through a generation service.

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
    subgraph InputSource ["📥 Input Source"]
        A1["Publisher Studio<br/>(catalog creation)"]
        A2["Local Directory<br/>(e.g., a mod folder)"]
        A3["Game Installation<br/>(for base game manifest)"]
        A4["Programmatic Need<br/>(e.g., resolver logic)"]
    end

    subgraph PublisherStudio ["🎨 Publisher Studio Workflow"]
        PS1["Create Project<br/>Configure Profile"]
        PS2["Add Content Items<br/>Add Releases"]
        PS3["Upload Artifacts<br/>to Hosting"]
        PS4["Generate Catalog<br/>with Metadata"]
        PS5["Publish Catalog<br/>Share genhub:// Link"]
    end

    subgraph GenerationService ["🛠️ Generation Service"]
        B["IManifestGenerationService"]
        C["CreateFromDirectoryAsync()"]
        D["CreateFromInstallationAsync()"]
    end

    subgraph Builder ["🏗️ Fluent Builder: IContentManifestBuilder"]
        E["WithBasicInfo(...)"]
        F["WithContentType(...)"]
        G["WithPublisher(...)"]
        H["WithMetadata(...)"]
        I["AddFileAsync(...)<br/>AddFilesFromDirectoryAsync(...)"]
        J["AddDependency(...)"]
        K["WithInstallationInstructions(...)"]
        L["Build()"]
    end

    subgraph Output ["📤 Output"]
        M["📋 Complete<br/>ContentManifest Object"]
        N1["💾 Serialized to<br/>manifest.json"]
        N2["📦 Included in<br/>PublisherCatalog.json"]
    end

    A1 --> PS1
    PS1 --> PS2 --> PS3 --> PS4 --> PS5
    PS5 --> N2

    A2 --> C
    A3 --> D
    A4 --> E

    C --> E
    D --> E

    E --> F --> G --> H --> I --> J --> K --> L

    I -.->|Loop for each file| I
    J -.->|Loop for each dependency| J

    L --> M
    M --> N1
    M --> N2

    classDef studio fill:#9f7aea,stroke:#805ad5,stroke-width:2px,color:#ffffff
    classDef service fill:#38a169,stroke:#2f855a,stroke-width:2px,color:#ffffff
    classDef builder fill:#805ad5,stroke:#6b46c1,stroke-width:2px,color:#ffffff
    classDef input fill:#3182ce,stroke:#2c5282,stroke-width:2px,color:#ffffff
    classDef output fill:#ed8936,stroke:#dd6b20,stroke-width:2px,color:#ffffff

    class PS1,PS2,PS3,PS4,PS5 studio
    class B,C,D service
    class E,F,G,H,I,J,K,L builder
    class A1,A2,A3,A4 input
    class M,N1,N2 output
```

**Manifest Creation Workflow:**

1. **Input Source Selection**: Determine the source of content (Publisher Studio, local directory, game installation, or programmatic)
2. **Publisher Studio Path** (for content creators):
   - Create project and configure publisher profile
   - Add content items with metadata (name, description, tags, screenshots)
   - Add releases with version numbers and changelogs
   - Upload artifacts to hosting provider (Google Drive, GitHub, Dropbox)
   - Generate catalog JSON with all content and release metadata
   - Publish catalog and share genhub:// subscription link
3. **Generation Service Path** (for local content):
   - Scan directory or installation for files
   - Calculate file hashes and sizes
   - Generate manifest with file metadata
4. **Builder Path** (for programmatic creation):
   - Use fluent builder API to construct manifest
   - Add basic info, content type, publisher, metadata
   - Add files and dependencies
   - Build final manifest object
5. **Output**: Resulting ContentManifest is either serialized to manifest.json or included in PublisherCatalog.json

**Publisher Studio Integration:**

The Publisher Studio provides a complete workflow for content creators to become publishers without writing JSON:

- **Multi-Catalog Support**: Create separate catalogs for mods, maps, tools
- **Addon Chain Management**: Define mod → addon → sub-addon relationships
- **Cross-Publisher Dependencies**: Reference content from other publishers
- **Hosting Provider Integration**: OAuth with Google Drive, GitHub, Dropbox
- **Validation**: Circular dependency detection, version constraint validation
- **One-Click Publishing**: Generate and upload catalog with single action

**Optimization Note**:
During game installation detection, the system first checks the `IContentManifestPool` for existing manifests matching the installation. If a valid manifest is found, the generation process is skipped entirely to prevent unnecessary directory scanning, ensuring that Steam-integrated and other stable installations do not trigger redundant CAS operations.
