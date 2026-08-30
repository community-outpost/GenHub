---
title: CSV Generation Utility (GenHub.Tools)
description: Command-line tool for scanning vanilla game installations and generating authoritative CSV catalogs and index metadata
---

# CSV Generation Utility (`GenHub.Tools`)

The **CSV Generation Utility** (`GenHub.Tools`) is a standalone cross-platform CLI tool used by developers and maintainers to scan clean *Command & Conquer: Generals* and *Zero Hour* installations, calculate MD5 and SHA256 hashes, categorize assets, detect language-specific components, and generate RFC 4180 compliant CSV catalogs and `index.json` registry metadata.

---

## Overview & Capabilities

- **Deep Directory Scanning**: Recursively indexes all files in the vanilla game installation root.
- **Dual Cryptographic Checksums**: Streams files efficiently to calculate both legacy **MD5** and **SHA256** checksums.
- **Language Detection & Classification**:
  - Automatically identifies vanilla shared files and assigns `language = "All"`.
  - Maps localized audio archives, language strings (`game.str`), and localized INIs (`English.ini`, `German.ini`, etc.) to canonical language codes (`EN`, `DE`, `FR`, `ES`, `IT`, `KO`, `PL`, `PT-BR`, `ZH-CN`, `ZH-TW`).
- **Required File Tagging**: Marks essential game engine executables (`generals.exe`, `ZeroHour.exe`, `game.dat`), base archives, and core INIs as `isRequired = true`.
- **JSON Metadata Categorization**: Embeds structured category tags (`config`, `language`, `maps`, `audio`, `graphics`, `other`).
- **Automated Index Maintenance**: Updates `docs/GameInstallationFilesRegistry/index.json` with file counts, byte sizes, checksums, and timestamps via the `--updateIndex` switch.

---

## Command-Line Syntax

```bash
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir <path> \
  --gameType <Generals|ZeroHour> \
  --version <version> \
  --output <csv-path> \
  [--language <code>] \
  [--updateIndex] \
  [--index <json-path>] \
  [--downloadUrl <url>]
```

### Argument Reference

| Argument | Required | Default | Description |
| :--- | :--- | :--- | :--- |
| `--installDir` | **Yes** | — | Absolute or relative path to the vanilla game installation root directory. |
| `--gameType` | **Yes** | — | Target game identifier: `Generals` or `ZeroHour` (also accepts `ZH`). |
| `--version` | **Yes** | — | Official release or patch version string (e.g. `1.08`, `1.04`). |
| `--output` | **Yes** | — | Path where the output CSV file will be written. |
| `--language` | No | `EN` | Canonical uppercase language code for localized files (`EN`, `DE`, `FR`, `ES`, `IT`, `KO`, `PL`, `PT-BR`, `ZH-CN`, `ZH-TW`). |
| `--updateIndex`| No | `false` | When present, automatically updates `index.json` with entry metadata and file checksums. |
| `--index` | No | *(auto)* | Path to target `index.json` (defaults to `index.json` in output directory). |
| `--downloadUrl`| No | *(auto)* | Custom download URL or template for the `downloadUrl` column. |
| `--help`, `-h` | No | — | Displays CLI help and argument usage information. |

---

## Usage Examples

### 1. Generating Generals 1.08 English Catalog

```bash
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "C:\Games\Command & Conquer Generals" \
  --gameType Generals \
  --version 1.08 \
  --output "docs/GameInstallationFilesRegistry/Generals-1.08.csv" \
  --language EN \
  --updateIndex
```

### 2. Generating Zero Hour 1.04 German Edition Catalog

```bash
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "C:\Games\Command & Conquer Generals Zero Hour" \
  --gameType ZeroHour \
  --version 1.04 \
  --output "docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv" \
  --language DE \
  --updateIndex
```

### 3. Standalone CSV Generation (Without Modifying `index.json`)

```bash
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "/home/user/.wine/drive_c/Games/Generals" \
  --gameType Generals \
  --version 1.08 \
  --output "scratch/Generals-custom.csv" \
  --language EN
```

---

## Programmatic API Usage

`GenHub.Tools` provides a reusable library API that can be consumed directly by test suites and orchestration services:

```csharp
using GenHub.Tools;
using Microsoft.Extensions.Logging.Abstractions;

var generator = new CsvGenerator(NullLogger.Instance);

var options = new CsvGeneratorOptions(
    InstallDir: @"C:\Games\Generals",
    OutputPath: @"docs\GameInstallationFilesRegistry\Generals-1.08.csv",
    GameType: "Generals",
    Version: "1.08",
    Language: "EN",
    UpdateIndex: true);

var result = await generator.GenerateCsvFileAsync(options, cancellationToken);

if (result.Success)
{
    Console.WriteLine($"Wrote {result.Data.TotalEntriesWritten} files. SHA256: {result.Data.CsvSha256}");
}
```

---

## Exit Codes

| Exit Code | Meaning |
| :--- | :--- |
| `0` | Success / Help displayed. |
| `1` | Validation error, invalid arguments, or generation failure. |
