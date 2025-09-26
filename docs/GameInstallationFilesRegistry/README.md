# Game Installation Files Registry

This directory contains the authoritative CSV registries for Command & Conquer Generals and Zero Hour installation validation.

## Structure

- `index.json` - Metadata index for all available CSV registries
- `Generals-1.08.csv` - File registry for Command & Conquer Generals 1.08
- `ZeroHour-1.04.csv` - File registry for Command & Conquer Generals: Zero Hour 1.04

## CSV Schema

Each CSV file contains the following columns:

- `relativePath`: Relative path from installation root (e.g., `Data/INI/GameData.ini`)
- `size`: File size in bytes
- `md5`: MD5 hash of the file
- `sha256`: SHA256 hash of the file
- `gameType`: "Generals" or "ZeroHour"
- `language`: Language code ("All", "EN", "DE", "FR", "ES", "IT", "KO", "PL", "PT-BR", "ZH-CN", "ZH-TW")
- `isRequired`: Boolean indicating if file is required for validation
- `metadata`: JSON metadata (optional)

## Versioning

- New game versions get new CSV files (e.g., `Generals-1.09.csv`)
- Old CSV files remain for historical reference
- `index.json` always references the latest active versions

## GitHub Access

Files are accessible via GitHub raw URLs:

- [index.json](https://raw.githubusercontent.com/Community-Outpost/GenHub/main/docs/GameInstallationFilesRegistry/index.json)
- [Generals-1.08.csv](https://raw.githubusercontent.com/Community-Outpost/GenHub/main/docs/GameInstallationFilesRegistry/Generals-1.08.csv)
- [ZeroHour-1.04.csv](https://raw.githubusercontent.com/Community-Outpost/GenHub/main/docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv)

## Maintenance

CSVs are generated using the CsvGenerator tool and committed to this directory. The index.json is updated with metadata and checksums after each generation.
