# Game Installation Files Registry

The **Game Installation Files Registry** provides authoritative catalog definitions and cryptographic file checksums for vanilla *Command & Conquer: Generals* (v1.08) and *Command & Conquer: Generals Zero Hour* (v1.04) across all 10 supported language variants and shared assets.

---

## Directory Overview

```text
docs/GameInstallationFilesRegistry/
├── index.json              # Primary metadata index for dynamic catalog discovery
├── Generals-1.08.csv       # Authoritative file catalog for C&C Generals 1.08
├── ZeroHour-1.04.csv       # Authoritative file catalog for C&C Zero Hour 1.04
└── README.md               # This documentation file
```

---

## Remote Access URLs

Files in this registry are accessible via GitHub Raw Content URLs:

- **Index Metadata**:  
  `https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/index.json`
- **Generals 1.08 Catalog**:  
  `https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/Generals-1.08.csv`
- **Zero Hour 1.04 Catalog**:  
  `https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv`

---

## Metadata Schema (`index.json`)

The `index.json` manifest acts as the root index queried by `CsvDiscoverer` during game installation discovery and validation.

```json
{
  "version": "1.0.0",
  "lastUpdated": "2026-08-30T17:40:00Z",
  "description": "Index of CSV registries for Command & Conquer Generals and Zero Hour validation",
  "registries": [
    {
      "id": "generals-1.08",
      "gameType": "Generals",
      "version": "1.08",
      "url": "https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/Generals-1.08.csv",
      "fileCount": 164,
      "totalSizeBytes": 48166,
      "languages": ["All", "EN", "DE", "FR", "ES", "IT", "KO", "PL", "PT-BR", "ZH-CN", "ZH-TW"],
      "checksum": {
        "md5": "41e3f06a608156eaea960d432d6be682",
        "sha256": "97c72beab9b92918ccf2629cf104034007337873783f2f7f03855e9857fa6267"
      },
      "generatedAt": "2025-09-17T09:15:00Z",
      "generatorVersion": "1.0.0",
      "isActive": true
    }
  ]
}
```

### Field Definitions

| Field | Type | Description |
| :--- | :--- | :--- |
| `version` | string | Index schema version (e.g., `"1.0.0"`). |
| `lastUpdated` | string (ISO-8601) | Timestamp of last registry update. |
| `description` | string | Description of the registry collection. |
| `registries` | array | List of individual catalog entries. |
| `registries[].id` | string | Unique catalog identifier (`"{gameType}-{version}"` lowercase). |
| `registries[].gameType` | string | Target game type (`"Generals"` or `"ZeroHour"`). |
| `registries[].version` | string | Game patch version (`"1.08"`, `"1.04"`). |
| `registries[].url` | string | Direct GitHub raw URL to download the CSV. |
| `registries[].fileCount` | integer | Total data entries in the CSV (excluding header row). |
| `registries[].totalSizeBytes` | integer | File size of the CSV file itself in bytes. |
| `registries[].languages` | string[] | List of language codes supported by this catalog. |
| `registries[].checksum.md5` | string | MD5 hash of the CSV file for fast integrity check. |
| `registries[].checksum.sha256`| string | SHA256 hash of the CSV file for verification. |
| `registries[].generatedAt` | string (ISO-8601) | Generation timestamp. |
| `registries[].generatorVersion` | string | Version of `GenHub.Tools` used. |
| `registries[].isActive` | boolean | Indicates whether the catalog is active. |

---

## CSV Catalog Schema

Each CSV file is RFC 4180 compliant with headers on the first line.

```csv
relativePath,size,md5,sha256,gameType,language,isRequired,metadata,downloadUrl
Data/INI/GameData.ini,12345,aebed2f8fa6f42b8c76929dfc8f90a00,ef61474057b21db70ae4356c3f22c088e583b3fd00c0b21da0c298949e8c3d62,Generals,All,True,"{""category"":""config""}",
Data/Lang/English/game.str,67890,fc015ddbe16ac6b4d39a85f5612d7233,39d67dba96111178fcceefae2bedb2dc65b55b968741162c714b333c0b0f5f2e,Generals,EN,True,"{""category"":""language""}",
```

### Column Specifications

1. **`relativePath`** *(string)*: Relative file path from game installation root using forward slashes (`/`), case-normalized.
2. **`size`** *(integer)*: File size in bytes.
3. **`md5`** *(string)*: 32-character lowercase MD5 checksum.
4. **`sha256`** *(string)*: 64-character lowercase SHA256 cryptographic checksum.
5. **`gameType`** *(string)*: `"Generals"` or `"ZeroHour"`.
6. **`language`** *(string)*: `"All"` for shared game files, or an uppercase canonical code (`"EN"`, `"DE"`, `"FR"`, `"ES"`, `"IT"`, `"KO"`, `"PL"`, `"PT-BR"`, `"ZH-CN"`, `"ZH-TW"`) for localized assets.
7. **`isRequired`** *(boolean)*: `True` if missing file represents a corrupted/incomplete game installation.
8. **`metadata`** *(string)*: JSON-encoded dictionary specifying category (`config`, `language`, `maps`, `audio`, `graphics`, `other`).
9. **`downloadUrl`** *(string)*: Remote content download URL.

---

## Language Support Matrix

| Code | Language | Example Asset |
| :--- | :--- | :--- |
| `All` | Shared Vanilla Files | `game.dat`, `Generals.exe`, `BINKW32.DLL` |
| `EN` | English | `English.big`, `AudioEnglish.big`, `Data/English/` |
| `DE` | German (Deutsch) | `German.big`, `AudioGerman.big`, `Data/German/` |
| `FR` | French (Français) | `French.big`, `AudioFrench.big`, `Data/French/` |
| `ES` | Spanish (Español) | `Spanish.big`, `AudioSpanish.big`, `Data/Spanish/` |
| `IT` | Italian (Italiano) | `Italian.big`, `AudioItalian.big`, `Data/Italian/` |
| `KO` | Korean | `Korean.big`, `AudioKorean.big`, `Data/Korean/` |
| `PL` | Polish (Polski) | `Polish.big`, `AudioPolish.big`, `Data/Polish/` |
| `PT-BR` | Portuguese (Brazil) | `PortugueseBrazil.big`, `AudioPortugueseBrazil.big` |
| `ZH-CN` | Chinese (Simplified) | `Chinese.big`, `AudioChinese.big`, `Data/Chinese/` |
| `ZH-TW` | Chinese (Traditional) | `ChineseTraditional.big`, `Data/ChineseTraditional/` |

---

## Generating and Updating Catalogs

Maintainers generate and update catalogs using the `GenHub.Tools` utility:

```bash
# Generate Generals 1.08 CSV and update index.json
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "C:\Games\Command & Conquer Generals" \
  --gameType Generals \
  --version 1.08 \
  --output "docs/GameInstallationFilesRegistry/Generals-1.08.csv" \
  --language EN \
  --updateIndex

# Generate Zero Hour 1.04 CSV and update index.json
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "C:\Games\Command & Conquer Generals Zero Hour" \
  --gameType ZeroHour \
  --version 1.04 \
  --output "docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv" \
  --language EN \
  --updateIndex
```

---

## Versioning & Immutability Rules

1. **Immutability**: Published CSV files are immutable. When updating official release manifests, maintain backwards-compatible file naming and increment version identifiers if file contents change.
2. **Dynamic Discovery**: `CsvDiscoverer` first queries `index.json` to obtain current active catalog URLs and checksums. If `index.json` is unreachable, it seamlessly falls back to cached catalogs or configured endpoints.
3. **Deterministic Filtering**: `CsvResolver` always combines rows matching `language = "All"` with rows matching the user's selected language (e.g. `language = "DE"`), guaranteeing complete manifests without missing engine files.
