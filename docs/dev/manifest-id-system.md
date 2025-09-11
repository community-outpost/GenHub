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

### Publisher Content IDs

**Format**: `schemaVersion.userVersion.publisher.content`  
**Example**: `1.0.ea.generals.mod`  
**Use Case**: Content created by publishers (mods, patches, addons)

### Base Game IDs

**Format**: `schemaVersion.userVersion.installationType.gameType`  
**Example**: `1.0.steam.generals`, `1.0.origin.zerohour`  
**Use Case**: Base game installations detected on the system

### Simple IDs

**Format**: Alphanumeric with dashes and dots  
**Example**: `test-id`, `simple.id`  
**Use Case**: Test scenarios and simple identifiers

## API Reference

### ManifestIdGenerator

```csharp
public static class ManifestIdGenerator
{
    // Generate publisher content ID
    public static string GeneratePublisherContentId(
        string publisherId,
        string contentName,
        int userVersion = 0);

    // Generate base game ID
    public static string GenerateBaseGameId(
        GameInstallation installation,
        GameType gameType,
        int userVersion = 0);
}
```

### ManifestIdService

```csharp
public class ManifestIdService : IManifestIdService
{
    // Generate publisher content ID with ResultBase pattern
    ContentOperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        string contentName,
        int userVersion = 0);

    // Generate base game ID with ResultBase pattern
    ContentOperationResult<ManifestId> GenerateBaseGameId(
        GameInstallation installation,
        GameType gameType,
        int userVersion = 0);

    // Validate and create ManifestId
    ContentOperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString);
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

### Generating Publisher Content IDs

```csharp
// Using ManifestIdService (recommended)
var idResult = _manifestIdService.GeneratePublisherContentId("EA", "Generals Mod", 0);
if (idResult.Success)
{
    ManifestId id = idResult.Data; // 1.0.ea.generals.mod
    Console.WriteLine(id); // Implicit conversion to string
}
else
{
    Console.WriteLine($"Failed: {idResult.ErrorMessage}");
}

// Using ManifestIdGenerator directly
string idString = ManifestIdGenerator.GeneratePublisherContentId("EA", "Generals Mod", 0);
ManifestId id = ManifestId.Create(idString);
```

### Generating Base Game IDs

```csharp
var installation = new GameInstallation("C:\\Games\\Generals", GameInstallationType.Steam);
var gameType = GameType.Generals;

// Using service
var idResult = _manifestIdService.GenerateBaseGameId(installation, gameType, 0);
if (idResult.Success)
{
    ManifestId id = idResult.Data; // 1.0.steam.generals
}

// Using generator directly
string idString = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, 0);
```

### Validating IDs

```csharp
// Using service
var validation = _manifestIdService.ValidateAndCreateManifestId("1.0.steam.generals");
if (validation.Success)
{
    ManifestId id = validation.Data;
}

// Using struct directly
try
{
    ManifestId id = ManifestId.Create("1.0.steam.generals");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Invalid ID: {ex.Message}");
}
```

### Creating Manifests with Builder

```csharp
var builder = new ContentManifestBuilder(_logger, _hashProvider, _manifestIdService)
    .WithBasicInfo("EA", "Generals Mod", 0)
    .WithContentType(ContentType.Mod, GameType.Generals)
    .WithPublisher("EA Games", "https://ea.com", "support@ea.com");

ContentManifest manifest = builder.Build();
// manifest.Id will be properly generated and validated
```

## Validation Rules

### Publisher Content Validation

- Must contain at least 4 segments separated by dots
- Format: `schemaVersion.userVersion.publisher.content`
- Each segment can contain alphanumeric characters and dashes
- No dots within segments (dots are separators only)
- Case-insensitive for comparison but preserves original casing

### Base Game Validation

- Must follow `schemaVersion.userVersion.installationType.gameType` format
- Installation types: `steam`, `eaapp`, `origin`, `thefirstdecade`, `rgmechanics`, `cdiso`, `wine`, `retail`, `unknown`
- Game types: `generals`, `zerohour`
- Schema version is automatically extracted from constants
- User version defaults to 0 if not specified

### Simple ID Validation

- Alphanumeric characters with dashes and dots
- Used for tests and simple scenarios
- More permissive validation for flexibility

## Error Handling

The system uses the **ResultBase pattern** for robust error handling:

```csharp
// Success case
var result = _manifestIdService.GeneratePublisherContentId("EA", "Mod", 0);
if (result.Success)
{
    ManifestId id = result.Data;
    // Use the ID
}
else
{
    // Handle error
    _logger.LogError($"ID generation failed: {result.ErrorMessage}");
}
```

## Integration Points

### ContentManifestBuilder

- Automatically generates and validates IDs when `WithBasicInfo` is called
- Uses `ManifestIdService` for consistent ID generation
- Provides fallback mechanisms if service fails

### ManifestGenerationService

- Uses `ManifestIdService` for all manifest creation operations
- Ensures deterministic ID generation across all manifest types

### ManifestProvider

- Validates manifest IDs during loading and processing
- Uses `IManifestIdService` for ID operations

## Testing

The system includes comprehensive test coverage:

- **ManifestIdGeneratorTests**: 51 tests covering all generation scenarios
- **ManifestIdServiceTests**: 20+ tests for service layer validation
- **ManifestIdTests**: Tests for struct functionality and validation
- **Integration tests**: End-to-end testing with ContentManifestBuilder

### Running Tests

```bash
# Run all manifest ID tests
dotnet test --filter "ManifestId"

# Run specific test classes
dotnet test --filter "ManifestIdGeneratorTests"
dotnet test --filter "ManifestIdServiceTests"
dotnet test --filter "ManifestIdTests"
```

## Cross-Platform Determinism

The system ensures identical ID generation across all platforms:

- **Normalization**: Converts to lowercase, removes special characters
- **Safe Characters**: Only alphanumeric, dots, and dashes allowed
- **Consistent Ordering**: Deterministic segment processing
- **Filesystem Safety**: Generated IDs are safe for use as filenames

## Future Enhancements

- **Custom ID Formats**: Support for extended validation rules
- **Migration Tools**: Utilities for updating existing content to new ID format
- **Performance Monitoring**: Metrics for ID generation performance
- **Extended Validation**: Additional security and compliance checks
