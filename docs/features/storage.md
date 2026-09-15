---
title: Storage & CAS
description: Multi-pool Content-Addressable Storage system for efficient file management and deduplication
---

# Storage & CAS

GenHub uses a sophisticated **Content-Addressable Storage (CAS)** system with multi-pool architecture to efficiently manage game files, mods, maps, and user content. The system provides deduplication, integrity verification, and optimized cross-drive support.

## Overview

Content-Addressable Storage (CAS) is a storage mechanism where content is retrieved using its cryptographic hash rather than a file path. This provides several benefits:

- **Deduplication**: Identical files are stored only once, regardless of how many profiles use them
- **Integrity Verification**: Files can be verified against their hash to detect corruption
- **Atomic Operations**: Content is either fully stored or not stored at all
- **Concurrent Access**: Multiple operations can safely access the same content
- **Garbage Collection**: Unreferenced content can be automatically cleaned up

## Multi-Pool Architecture

GenHub implements a **two-pool CAS architecture** to optimize storage across different drives and support hard-link efficiency:

### Pool Types

#### Primary Pool

**Purpose**: Stores maps, mods, patches, and user-generated content.

**Location**: App data drive (typically `C:\Users\<User>\AppData\Local\GenHub\cas`)

**Content Types**:

- `Mod` - Community mods and modifications
- `Map` - Custom maps and map packs
- `Patch` - Game patches and updates
- `UserData` - User-generated content (replays, saves)
- `CommunityPatch` - Community patches like GenPatcher

**Rationale**: User content is stored on the app data drive to enable hard-link support for user data directories, which are typically on the same drive.

#### Installation Pool

**Purpose**: Stores game clients and base game installations.

**Location**: Same drive as the game installation (e.g., `D:\Games\GenHub\cas` if game is on `D:`)

**Content Types**:

- `GameInstallation` - Base game installations
- `GameClient` - Game executables and clients

**Rationale**: Game installations can be large and are often on a different drive. Storing them on the same drive as the game enables hard-link support for workspace assembly, avoiding expensive cross-drive copies.

### Pool Routing

The `CasPoolResolver` automatically routes content to the appropriate pool based on content type:

```csharp
public interface ICasPoolResolver
{
    CasPoolType ResolvePool(ContentType contentType);
    string GetPoolRootPath(CasPoolType poolType);
    bool IsInstallationPoolAvailable();
}
```

**Routing Logic**:

- `GameInstallation` and `GameClient` → **Installation Pool** (if available)
- All other content types → **Primary Pool**
- If Installation Pool is not configured, all content falls back to Primary Pool

## Core Interfaces

### ICasService

High-level interface for CAS operations with automatic pool routing.

**Key Methods**:

```csharp
// Store content with automatic pool routing
Task<OperationResult<string>> StoreContentAsync(
    string sourcePath, 
    ContentType contentType, 
    string? expectedHash = null, 
    CancellationToken cancellationToken = default);

// Retrieve content path by hash
Task<OperationResult<string>> GetContentPathAsync(
    string hash, 
    ContentType contentType, 
    CancellationToken cancellationToken = default);

// Check if content exists
Task<OperationResult<bool>> ExistsAsync(
    string hash, 
    ContentType contentType, 
    CancellationToken cancellationToken = default);

// Garbage collection
Task<CasGarbageCollectionResult> RunGarbageCollectionAsync(
    bool force = false, 
    CancellationToken cancellationToken = default);

// Integrity validation
Task<CasValidationResult> ValidateIntegrityAsync(
    CancellationToken cancellationToken = default);

// Statistics
Task<CasStats> GetStatsAsync(CancellationToken cancellationToken = default);
```

### ICasPoolManager

Manages multiple CAS storage pools and provides access to pool-specific storage instances.

```csharp
public interface ICasPoolManager
{
    ICasStorage GetStorage(CasPoolType poolType);
    ICasStorage GetStorage(ContentType contentType);
    IReadOnlyList<ICasStorage> GetAllStorages();
    ICasPoolResolver PoolResolver { get; }
}
```

**Usage Example**:

```csharp
// Get storage for a specific pool
var primaryStorage = poolManager.GetStorage(CasPoolType.Primary);

// Get storage for a content type (automatic routing)
var storage = poolManager.GetStorage(ContentType.Mod);

// Get all active pools (for garbage collection across all pools)
var allStorages = poolManager.GetAllStorages();
```

### ICasStorage

Low-level interface for individual pool operations.

**Responsibilities**:

- Hash-based content storage and retrieval
- File integrity verification
- Reference tracking for garbage collection
- Concurrent access management

## Configuration

### CasConfiguration

```csharp
public class CasConfiguration
{
    // Primary pool root path
    public string CasRootPath { get; set; }
    
    // Installation pool root path (optional)
    public string InstallationPoolRootPath { get; set; }
    
    // Hash algorithm (SHA-256 default)
    public HashAlgorithm HashAlgorithm { get; set; }
    
    // Garbage collection settings
    public bool EnableAutomaticGc { get; set; }
    public TimeSpan GcGracePeriod { get; set; }
    public TimeSpan AutoGcInterval { get; set; }
    
    // Performance settings
    public int MaxConcurrentOperations { get; set; }
    public long MaxCacheSizeBytes { get; set; }
    
    // Integrity verification
    public bool VerifyIntegrity { get; set; }
}
```

**Default Values**:

- `GcGracePeriod`: 7 days
- `AutoGcInterval`: 30 days
- `MaxConcurrentOperations`: 4
- `HashAlgorithm`: SHA-256
- `VerifyIntegrity`: `true`

## How It Works

### 1. Storing Content

When content is stored in CAS:

1. **Hash Calculation**: The file's SHA-256 hash is computed
2. **Pool Resolution**: Content type determines which pool to use
3. **Deduplication Check**: If hash already exists, return existing reference
4. **Atomic Write**: Content is written to a temporary file, then atomically moved
5. **Reference Tracking**: A reference is recorded for garbage collection

```csharp
// Store a mod file
var result = await casService.StoreContentAsync(
    sourcePath: "C:\\Downloads\\MyMod.big",
    contentType: ContentType.Mod,
    cancellationToken: cancellationToken
);

if (result.Success)
{
    string hash = result.Data; // e.g., "a3f5b2c1d4e6..."
    // Hash can now be used to retrieve content
}
```

### 2. Retrieving Content

Content is retrieved by hash and content type:

```csharp
var pathResult = await casService.GetContentPathAsync(
    hash: "a3f5b2c1d4e6...",
    contentType: ContentType.Mod,
    cancellationToken: cancellationToken
);

if (pathResult.Success)
{
    string casPath = pathResult.Data;
    // Use casPath for hard-linking or copying to workspace
}
```

### 3. Hard Link Support

CAS enables efficient workspace assembly via hard links:

- **Same Drive**: Hard links are created from CAS to workspace (zero copy)
- **Cross Drive**: Files must be copied (CAS on different drive than workspace)

The multi-pool architecture minimizes cross-drive scenarios:

- User content (maps, mods) → Primary Pool → User data directories (same drive)
- Game installations → Installation Pool → Workspace (same drive as game)

### 4. Garbage Collection

Garbage collection removes unreferenced content to free disk space.

**Process**:

1. **Reference Scan**: Identify all content referenced by profiles, manifests, and user data
2. **Grace Period**: Only delete content unreferenced for longer than `GcGracePeriod`
3. **Cleanup**: Remove unreferenced files from all pools
4. **Statistics**: Report objects removed and space reclaimed

**Manual Garbage Collection**:

```csharp
var gcResult = await casService.RunGarbageCollectionAsync(
    force: false, // Respect grace period
    cancellationToken: cancellationToken
);

Console.WriteLine($"Removed {gcResult.ObjectsRemoved} objects");
Console.WriteLine($"Reclaimed {gcResult.SpaceReclaimed} bytes");
```

**Automatic Garbage Collection**:

- Runs every `AutoGcInterval` (default: 30 days)
- Can be disabled via `EnableAutomaticGc = false`
- Respects `GcGracePeriod` to avoid deleting recently used content

### 5. Integrity Validation

Validates that stored content matches its hash:

```csharp
var validationResult = await casService.ValidateIntegrityAsync(cancellationToken);

if (validationResult.Success)
{
    Console.WriteLine($"Validated {validationResult.ObjectsValidated} objects");
    Console.WriteLine($"Found {validationResult.CorruptedObjects.Count} corrupted files");
}
```

## Integration Points

### Workspace Management

The workspace system uses CAS as the source of truth for all content:

1. **Manifest Resolution**: Manifests specify content hashes
2. **CAS Retrieval**: Workspace retrieves files from CAS by hash
3. **Hard Link Assembly**: Files are hard-linked from CAS to workspace
4. **Validation**: Workspace validates files against manifest hashes

### Content Storage Service

The `ContentStorageService` orchestrates content acquisition and CAS storage:

1. **Download**: Content is downloaded from publishers (GitHub, ModDB, etc.)
2. **Store in CAS**: Downloaded files are stored in appropriate pool
3. **Manifest Update**: Content manifest is updated with CAS hashes
4. **Cleanup**: Temporary download files are removed

### User Data Management

User data (maps, replays, saves) is managed via CAS:

1. **Hard Link Creation**: User data is hard-linked from CAS to user directories
2. **Profile Isolation**: Each profile has its own set of active user data
3. **Efficient Switching**: Profile switches only update hard links, not file copies
4. **Deduplication**: Shared user data (e.g., popular maps) is stored once

## Performance Considerations

### Hard Link Efficiency

**Benefits**:

- Zero-copy file operations
- Instant workspace assembly
- Minimal disk space usage

**Requirements**:

- Source and target must be on the same drive
- File system must support hard links (NTFS, ext4, etc.)

**Multi-Pool Optimization**:

- Primary Pool on app data drive → User data directories (same drive)
- Installation Pool on game drive → Workspace (same drive)

### Concurrent Operations

CAS supports concurrent operations with proper locking:

- **Read Operations**: Multiple concurrent reads are safe
- **Write Operations**: Atomic writes prevent partial states
- **Garbage Collection**: Locks prevent deletion of in-use content

**Configuration**:

```csharp
MaxConcurrentOperations = 4; // Limit concurrent CAS operations
```

### Disk Space Management

**Monitoring**:

```csharp
var stats = await casService.GetStatsAsync(cancellationToken);
Console.WriteLine($"Total objects: {stats.TotalObjects}");
Console.WriteLine($"Total size: {stats.TotalSizeBytes} bytes");
Console.WriteLine($"Referenced objects: {stats.ReferencedObjects}");
```

**Cleanup Strategies**:

1. **Automatic GC**: Runs periodically to remove old unreferenced content
2. **Manual GC**: User-initiated cleanup via Danger Zone
3. **Forced GC**: Ignores grace period for immediate cleanup

## Error Handling

### Common Scenarios

**Hash Mismatch**:

```csharp
// Expected hash doesn't match computed hash
var result = await casService.StoreContentAsync(
    sourcePath: "file.big",
    contentType: ContentType.Mod,
    expectedHash: "abc123..."
);

if (result.Failed)
{
    // result.FirstError: "Hash mismatch: expected abc123..., got def456..."
}
```

**Cross-Drive Hard Link Failure**:

- CAS automatically falls back to file copying
- Logs warning about performance impact
- Workspace assembly continues successfully

**Insufficient Disk Space**:

- Operation fails with clear error message
- Partial writes are rolled back
- User is notified to free disk space

**Corrupted Content**:

- Integrity validation detects hash mismatches
- Corrupted files are logged and can be re-downloaded
- Garbage collection can remove corrupted files

## Best Practices

### For Developers

1. **Always Specify Content Type**: Use pool routing for optimal performance

   ```csharp
   // Good: Automatic pool routing
   await casService.StoreContentAsync(path, ContentType.Mod);
   
   // Avoid: Manual pool selection (unless necessary)
   await casService.StoreContentAsync(path);
   ```

2. **Use Cancellation Tokens**: All operations support cancellation

   ```csharp
   await casService.StoreContentAsync(path, contentType, cancellationToken: cts.Token);
   ```

3. **Check Result Success**: Never assume operations succeed

   ```csharp
   var result = await casService.StoreContentAsync(path, contentType);
   if (result.Failed)
   {
       logger.LogError("Failed to store content: {Error}", result.FirstError);
       return;
   }
   ```

4. **Handle Partial Failures**: Some files may succeed while others fail

   ```csharp
   foreach (var file in files)
   {
       var result = await casService.StoreContentAsync(file, contentType);
       if (result.Failed)
       {
           logger.LogWarning("Failed to store {File}: {Error}", file, result.FirstError);
           // Continue with other files
       }
   }
   ```

### For Users

1. **Same Drive Recommended**: Keep game installation and GenHub data on the same drive for hard-link support
2. **Monitor Disk Space**: Use Danger Zone to view CAS usage and run cleanup
3. **Regular Cleanup**: Run garbage collection periodically to free disk space
4. **Backup Important Data**: While CAS is robust, external backups are always recommended

## Danger Zone Integration

The Settings Danger Zone provides CAS management tools:

- **View CAS Usage**: See total objects and disk space used
- **Run Garbage Collection**: Manually trigger cleanup
- **Force Cleanup**: Ignore grace period for immediate cleanup
- **Validate Integrity**: Check for corrupted files
- **Clear All Data**: Nuclear option to reset CAS (removes all content)

See [Danger Zone](./danger-zone.md) for details.

## Future Enhancements

Potential improvements under consideration:

- **Compression**: Compress inactive content to save space
- **Cloud Sync**: Synchronize CAS across machines
- **Distributed Pools**: Support for network-attached storage
- **Content Sharing**: Share CAS between multiple GenHub installations
- **Smart Caching**: Predictive pre-loading of frequently used content
