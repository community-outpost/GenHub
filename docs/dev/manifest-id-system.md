# Manifest ID System

## Overview

The Manifest ID system provides **deterministic, human-readable, and type-safe identifiers** for all content in the GenHub ecosystem. This system ensures consistent content identification across platforms, prevents ID collisions, and provides robust validation with proper error handling.

## Architecture

The system follows a **layered architecture** with clear separation of concerns:

### Core Layer: ManifestIdGenerator

Low-level utility for generating deterministic, cross-platform manifest IDs with advanced normalization and filesystem-safe output.

### Service Layer: ManifestIdService

Implements the **ResultBase pattern** for type-safe operations, wrapping the generator with proper error handling and returning `ContentOperationResult<ManifestId>`.

### Validation Layer: ManifestIdValidator

Comprehensive validation ensuring ID format compliance and security through regex-based rules and format verification.

### Type Safety Layer: ManifestId

Strongly-typed value object with compile-time validation, implicit conversions, and JSON serialization support.

### Integration Layer

Seamless integration into `ContentManifestBuilder`, `ManifestGenerationService`, and other components.

## ID Formats

### Unified 5-Segment Format

**All content in GenHub uses a consistent 5-segment format:**

**Format**: `schemaVersion.userVersion.publisher.contentType.contentName`

This unified approach treats platform distributors (Steam, EA, etc.) as publishers, just like community content creators.

### Game Installations and Clients

**Examples**:

- Steam ZeroHour v1.04 Installation: `1.104.steam.gameinstallation.zerohour`
- EA App Generals v1.08 Client: `1.108.eaapp.gameclient.generals`
- Retail Generals v1.08: `1.108.retail.gameinstallation.generals`
- Custom Version 0: `1.0.steam.gameinstallation.generals`

**Publisher Attribution**: Platform name (Steam, EA, Retail, etc.) is treated as the publisher.

**Version Format**: The `userVersion` segment accepts either integers (0, 1, 2) or version strings ("1.08", "1.04"). Version strings automatically have dots removed for schema compliance:

- "1.08" → "108" (Generals executable version)
- "1.04" → "104" (Zero Hour executable version)
- 0 → "0" (default/first version)

This normalization ensures the manifest ID schema remains valid (dots separate segments) while supporting human-readable version strings.

### Community Content

**Examples**:

- GenHub Mod: `1.0.genhub.mod.custom-mod`
- GeneralsOnline Client: `1.0.generalsonline.gameclient.generalsonline_30hz`
- CNC Labs Map: `1.0.cnclabs.map.desert-storm`
- WorldBuilder Tool: `1.0.ea.moddingtool.worldbuilder`

**Publisher Attribution**: Community publisher name (e.g., "genhub", "generalsonline", "cnclabs")


## API Reference

### ManifestIdGenerator

```csharp
public static class ManifestIdGenerator
{
    // Generate publisher content ID with content type (used for all content)
    public static string GeneratePublisherContentId(
        string publisherId,
        ContentType contentType,
        string contentName,
        int userVersion = 0,
        string suffix = "");

    // Generate game installation ID (uses 5-segment publisher format)
    // Note: Installation type (Steam, EA, etc.) is treated as the publisher
    // Note: userVersion accepts both integers (0, 1, 2) and version strings ("1.08", "1.04")
    // Version strings have dots automatically removed for schema compliance: "1.08" → "108"
    public static string GenerateGameInstallationId(
        GameInstallation installation,
        GameType gameType,
        object? userVersion);
}
```

### ManifestIdService

```csharp
public class ManifestIdService : IManifestIdService
{
    // Generate publisher content ID with OperationResult pattern
    OperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        ContentType contentType,
        string contentName,
        int userVersion = 0,
        string suffix = "");

    // Generate game installation ID with OperationResult pattern (uses 5-segment format)
    // Note: Installation type (Steam, EA, etc.) is treated as the publisher
    // Note: userVersion accepts both integers (0, 1, 2) and version strings ("1.08", "1.04")
    // Version strings have dots automatically removed for schema compliance: "1.08" → "108"
    OperationResult<ManifestId> GenerateGameInstallationId(
        GameInstallation installation,
        GameType gameType,
        object? userVersion);

    // Validate and create ManifestId
    OperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString);
}
```

### ManifestId Struct

```csharp
public readonly struct ManifestId : IEquatable<ManifestId>
{
    public string Value { get; }

    // Implicit conversions
    public static implicit operator ManifestId(string id);
    public static implicit operator string(ManifestId id);

    // Validation and creation
    public static ManifestId Create(string id);

    // Equality operations
    public static bool operator ==(ManifestId left, ManifestId right);
    public static bool operator !=(ManifestId left, ManifestId right);

    // Object methods
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
}
```

## Usage Examples

### Game Installation IDs (5-Segment Format)

```csharp
// Generate ID for detected game installation with integer version
var installation = new GameInstallation("C:\\Games\\Generals", GameInstallationType.Steam);
var gameType = GameType.Generals;

// Using service with integer version (default 0)
var idResult = _manifestIdService.GenerateGameInstallationId(installation, gameType, 0);
if (idResult.Success)
{
    ManifestId id = idResult.Data; // 1.0.steam.gameinstallation.generals
}

// Using string version for Generals 1.08
var idResult = _manifestIdService.GenerateGameInstallationId(installation, gameType, "1.08");
if (idResult.Success)
{
    ManifestId id = idResult.Data; // 1.108.steam.gameinstallation.generals
}

// Using string version for Zero Hour 1.04
var zhInstallation = new GameInstallation("C:\\Games\\ZeroHour", GameInstallationType.Steam);
var idResult = _manifestIdService.GenerateGameInstallationId(zhInstallation, GameType.ZeroHour, "1.04");
if (idResult.Success)
{
    ManifestId id = idResult.Data; // 1.104.steam.gameinstallation.zerohour
}

// Using generator directly with version constant
string idString = ManifestIdGenerator.GenerateGameInstallationId(
    installation,
    gameType,
    ManifestConstants.GeneralsManifestVersion); // "1.08" → generates "1.108.steam.gameinstallation.generals"
```

### Publisher Content IDs

```csharp
// Generate ID for content with publisher and content type
var idResult = _manifestIdService.GeneratePublisherContentId("genhub", ContentType.Mod, "custom-mod", 0);
if (idResult.Success)
{
    ManifestId id = idResult.Data; // 1.0.genhub.mod.custom-mod
}

// Generate ID for GeneralsOnline client
var clientResult = _manifestIdService.GeneratePublisherContentId("generalsonline", ContentType.GameClient, "generalsonline_30hz", 0);
if (clientResult.Success)
{
    ManifestId id = clientResult.Data; // 1.0.generalsonline.gameclient.generalsonline_30hz
}
```

### Validation

```csharp
// Validate existing ID (5-segment format required)
var validation = _manifestIdService.ValidateAndCreateManifestId("1.0.steam.gameinstallation.generals");
if (validation.Success)
{
    ManifestId id = validation.Data;
}
```

## Version Normalization

The manifest ID system automatically normalizes version values to ensure schema compliance. The manifest ID format uses dots (`.`) to separate segments, so version strings containing dots are normalized by removing them.

### How It Works

The `NormalizeVersionString()` method processes version values as follows:

1. **Accepts flexible input**: Both integers and version strings
2. **Removes dots**: Strips all dot characters from version strings
3. **Validates numeric format**: Ensures the result is a valid number
4. **Returns normalized string**: Used in the manifest ID's `userVersion` segment

### Examples

| Input Version | Normalized Output | Resulting Manifest ID |
|--------------|-------------------|----------------------|
| `0` | `"0"` | `1.0.steam.gameinstallation.generals` |
| `1` | `"1"` | `1.1.steam.gameinstallation.generals` |
| `"1.08"` | `"108"` | `1.108.steam.gameinstallation.generals` |
| `"1.04"` | `"104"` | `1.104.steam.gameinstallation.zerohour` |
| `"2.0"` | `"20"` | `1.20.steam.gameinstallation.generals` |

### Why Version Normalization?

**Schema Compliance**: The manifest ID format uses dots (`.`) to separate segments. Each segment must not contain additional dots. If the `userVersion` segment contained dots (e.g., "1.08"), it would break the 5-segment schema:

- ❌ Invalid: `1.1.08.steam.gameinstallation.generals` (6 segments instead of 5)
- ✅ Valid: `1.108.steam.gameinstallation.generals` (5 segments as expected)

**Human-Readable Versions**: Developers can use familiar version strings like "1.08" or "1.04" that match the actual game executable versions, while the system automatically handles the normalization for schema compliance.

**Backward Compatibility**: Existing code using integer versions (0, 1, 2) continues to work without changes, as integers are converted to strings without modification.

### Usage

```csharp
// Using manifest version constants (automatically normalized)
using static GenHub.Core.Constants.ManifestConstants;

var generalsId = ManifestIdGenerator.GenerateGameInstallationId(
    installation,
    GameType.Generals,
    GeneralsManifestVersion); // "1.08" → "108"
// Result: "1.108.steam.gameinstallation.generals"

var zhId = ManifestIdGenerator.GenerateGameInstallationId(
    zhInstallation,
    GameType.ZeroHour,
    ZeroHourManifestVersion); // "1.04" → "104"
// Result: "1.104.steam.gameinstallation.zerohour"

// Using custom version strings
var customId = ManifestIdGenerator.GenerateGameInstallationId(
    installation,
    GameType.Generals,
    "2.0"); // "2.0" → "20"
// Result: "1.20.steam.gameinstallation.generals"

// Using integer versions (no normalization needed)
var defaultId = ManifestIdGenerator.GenerateGameInstallationId(
    installation,
    GameType.Generals,
    0); // 0 → "0"
// Result: "1.0.steam.gameinstallation.generals"
```

### Version Normalization Validation

The normalization method validates that the result is numeric after dot removal:

```csharp
// Valid inputs
NormalizeVersionString("1.08");  // ✅ Returns "108"
NormalizeVersionString("1.04");  // ✅ Returns "104"
NormalizeVersionString(5);       // ✅ Returns "5"
NormalizeVersionString("2.0");   // ✅ Returns "20"

// Invalid inputs (throws ArgumentException)
NormalizeVersionString("1.0a");  // ❌ Contains letters
NormalizeVersionString("v1.08"); // ❌ Contains letters
NormalizeVersionString("1..08"); // ❌ Results in "108" but has invalid format
```

## Validation Rules

### All Content (5-Segment Format)

- Format: `schemaVersion.userVersion.publisher.contentType.contentName`
- **UserVersion**: Accepts integers (0, 1, 2) or version strings ("1.08", "1.04"). Version strings have dots removed during normalization ("1.08" → "108")
- **Publisher**: Can be platform (steam, eaapp, retail) or community publisher (genhub, generalsonline, cnclabs, moddb)
- **ContentType**: Must be valid content type (gameinstallation, gameclient, mod, patch, addon, mappack, languagepack, moddingtool, etc.)
- **ContentName**: Alphanumeric with dashes (e.g., "generals", "custom-mod")
- **Total Segments**: Exactly 5 segments required
## Error Handling

Uses **ResultBase pattern** for robust error handling:

```csharp
var result = _manifestIdService.GeneratePublisherContentId("invalid", ContentType.Mod, "content", 0);
if (!result.Success)
{
    _logger.LogError($"ID generation failed: {result.ErrorMessage}");
}
```

## Integration Points

### ContentManifestBuilder

- Automatically generates and validates IDs
- Uses ManifestIdService for consistency

### ManifestGenerationService

- Uses ManifestIdService for all manifest creation
- Ensures deterministic ID generation

### ManifestProvider

- Validates manifest IDs during loading
- Uses IManifestIdService for operations

## Testing

Comprehensive test coverage in ManifestIdGeneratorTests.cs and related test files.

### Running Tests

```bash
dotnet test --filter "ManifestId"
```

## Cross-Platform Determinism

Ensures identical ID generation across all platforms through normalization and consistent processing.
