---
title: CSV Registry Maintenance & Troubleshooting Guide
description: Maintainer guide for generating, updating, and verifying authoritative CSV catalogs and index.json metadata
---

# CSV Registry Maintenance & Troubleshooting Guide

This guide covers operational maintenance procedures, generation workflows, and diagnostic resolutions for the authoritative game installation CSV registries located in `docs/GameInstallationFilesRegistry/`.

---

## 1. Responsibilities & Lifecycle

### Who Generates CSV Catalogs?
Registry generation is performed by **developers and maintainers** with access to clean, unmodded retail/digital game installations (e.g., EA App, Steam, The First Decade, CD-ROM releases).

### When to Regenerate or Update?
1. **New Game Patch / Release**: When official or community patch releases alter base game files (e.g. Generals 1.09 or Zero Hour 1.05).
2. **New Language Variant**: When adding or revising supported language assets (e.g. localized audio archives or translation string tables).
3. **Registry Correction**: When fixing incorrect metadata categories or required-file flags.
4. **Periodic Integrity Audits**: To ensure catalog checksums in `index.json` match repository content.

---

## 2. Step-by-Step Generation Procedure

### Step 1: Prepare Clean Installation
Ensure the source game directory is completely clean and contains no third-party mods, custom maps, replay files, or cache artifacts.

### Step 2: Run `GenHub.Tools`
Execute the CSV Generator with the `--updateIndex` switch:

```bash
# For Generals 1.08
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "C:\Games\Command & Conquer Generals" \
  --gameType Generals \
  --version 1.08 \
  --output "docs/GameInstallationFilesRegistry/Generals-1.08.csv" \
  --language EN \
  --updateIndex

# For Zero Hour 1.04
dotnet run --project GenHub/GenHub.Tools/GenHub.Tools.csproj -- \
  --installDir "C:\Games\Command & Conquer Generals Zero Hour" \
  --gameType ZeroHour \
  --version 1.04 \
  --output "docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv" \
  --language EN \
  --updateIndex
```

### Step 3: Verify Integrity
Run the automated test suite to verify that `CsvDiscoverer`, `CsvResolver`, and `GameInstallationValidator` correctly consume the updated registry:

```bash
dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj -c Release
```

### Step 4: Commit and Push
Commit both the updated CSV catalog file and `index.json` to Git:

```bash
git add docs/GameInstallationFilesRegistry/
git commit -m "feat(registry): update Generals 1.08 and Zero Hour 1.04 catalogs"
```

---

## 3. Metadata & Dynamic Index System (`index.json`)

The `index.json` file serves as the single source of truth for remote discovery:

- **Primary Discovery**: `CsvDiscoverer` fetches `index.json` via HTTPS from GitHub Raw URLs.
- **Failover / Offline**: If `index.json` is unreachable or unparseable, `CsvDiscoverer` falls back to configured fallback endpoints in `CsvCatalogConfiguration` or cached local entries.
- **Integrity Gating**: `CsvResolver` downloads the CSV and compares its SHA256 against `checksum.sha256` in `index.json`. If the hash does not match, resolution fails safely without risking silent corruption.

---

## 4. Troubleshooting Common Issues

### Issue 1: `fileCount` Mismatch in `index.json`
- **Symptom**: `index.json` reports a different number of files than the lines in the CSV.
- **Cause**: Manual edits to the CSV file or counting the CSV header row.
- **Fix**: Re-run the generator with `--updateIndex` to automatically synchronize entry counts and checksums.

### Issue 2: Checksum Validation Fails (`SHA256 Mismatch`)
- **Symptom**: `CsvResolver` reports `Checksum mismatch for CSV catalog`.
- **Cause**: The CSV file was edited after `index.json` was generated, or line endings were converted (`CRLF` vs `LF`).
- **Fix**: Recompute the hash using `GenHub.Tools --updateIndex` or verify Git line-ending normalization (`.gitattributes`).

### Issue 3: GitHub Raw URL Returns 404
- **Symptom**: `CsvDiscoverer` or `CsvResolver` reports HTTP 404 for catalog URLs.
- **Cause**: Branch not yet merged to `main`, repository renamed, or file path casing mismatch on Linux hosts.
- **Fix**: Ensure URLs use the canonical `community-outpost/GenHub` repository path and `main` branch.

### Issue 4: Absolute Paths in CSV Output
- **Symptom**: `GameInstallationValidator` fails to find files on target machines.
- **Cause**: Tool did not strip installation root or used backslashes instead of forward slashes.
- **Fix**: Ensure all entries use forward-slash normalized relative paths (e.g., `Data/INI/GameData.ini`).

### Issue 5: Localized Files Tagged as `"All"`
- **Symptom**: Installing a non-English game edition fails validation due to unexpected English BIG archives.
- **Cause**: Missing language directory or archive regex pattern in `IsLanguageSpecific()`.
- **Fix**: Verify language patterns in `LanguageDirectoryNames` and `LanguageFilePatterns`.

---

## 5. Cross-Cutting Component Architecture

- **#150**: Language parameter in `ContentSearchQuery` and normalization logic.
- **#151**: `CsvCatalogEntry` model with RFC 4180 CSV attributes.
- **#152**: `ILanguageDetector` / `LanguageDetector` for automated directory inspection.
- **#154**: `GenHub.Tools` CLI for automated CSV generation.
- **#155**: `docs/GameInstallationFilesRegistry/` multi-game registry layout.
- **#156**: `index.json` schema and metadata indexing.
- **#157**: Complete documentation and maintenance guidelines.
