# Content-Addressable Storage (CAS) Subsystem for GenHub

## Executive Summary

The Content-Addressable Storage (CAS) subsystem introduces a shared, hash-based storage pool that eliminates redundant downloads and extractions across all GenHub content operations. This system integrates seamlessly into the existing four-layer content pipeline, primarily affecting the **Acquisition** and **Assembly** phases by introducing content deduplication and efficient workspace preparation.

---

## 1. CAS Architecture Integration

### 1.1 Position in Existing Architecture

The CAS system operates as a **cross-cutting storage layer** that enhances the existing content pipeline:

```
Layer 1: Discovery → Layer 2: Resolution → Layer 3: Acquisition + CAS → Layer 4: Assembly + CAS
```

**CAS Integration Points**:
- **Acquisition Phase**: Content providers populate CAS during download/extraction
- **Assembly Phase**: Workspace strategies prioritize CAS retrieval over direct operations
- **File Operations**: IFileOperationsService gains CAS-aware methods
- **Manifest Processing**: ManifestFile entries reference CAS through hash-based lookup

### 1.2 Core CAS Components

**New Service Interfaces**:
```csharp
namespace GenHub.Core.Interfaces.Storage;

public interface ICasService
{
    Task<CasOperationResult<string>> StoreContentAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default);
    Task<CasOperationResult<string>> GetContentPathAsync(string hash, CancellationToken cancellationToken = default);
    Task<CasOperationResult<bool>> ExistsAsync(string hash, CancellationToken cancellationToken = default);
    Task<CasOperationResult<Stream>> OpenContentStreamAsync(string hash, CancellationToken cancellationToken = default);
    Task<CasGarbageCollectionResult> RunGarbageCollectionAsync(CancellationToken cancellationToken = default);
    Task<CasValidationResult> ValidateIntegrityAsync(CancellationToken cancellationToken = default);
}

public interface ICasStorage
{
    string GetObjectPath(string hash);
    Task<bool> ObjectExistsAsync(string hash, CancellationToken cancellationToken = default);
    Task<string> StoreObjectAsync(Stream content, string hash, CancellationToken cancellationToken = default);
    Task<Stream> OpenObjectStreamAsync(string hash, CancellationToken cancellationToken = default);
    Task DeleteObjectAsync(string hash, CancellationToken cancellationToken = default);
}
```

---

## 2. On-Disk Layout and Directory Structure

### 2.1 CAS Pool Organization

```
GenHub/
└── cas-pool/
    ├── objects/
    │   ├── ab/
    │   │   └── ab123456789abcdef1234567890abcdef12345678  # SHA-256 hash as filename
    │   ├── cd/
    │   │   └── cd987654321fedcba0987654321fedcba09876543
    │   └── [00-ff]/  # 256 subdirectories for hash distribution
    ├── temp/
    │   ├── download-{guid}-filename.tmp
    │   └── extract-{guid}-archive.tmp
    ├── refs/
    │   ├── manifests/
    │   │   └── {manifestId}.refs  # JSON file tracking object references
    │   └── workspaces/
    │       └── {workspaceId}.refs  # Track workspace object usage
    ├── locks/
    │   └── {hash}.lock  # Coordination files for concurrent access
    └── config/
        └── cas.json  # CAS configuration and metadata
```

### 2.2 Hash-Based Storage Strategy

**Hash Algorithm**: SHA-256 for cryptographic integrity and collision avoidance
**Path Resolution**: `objects/{first-2-hex-chars}/{full-hash}`
**Example**: Hash `ab123...def` → `objects/ab/ab123456789abcdef1234567890abcdef12345678`

---

## 3. Enhanced ManifestFile Model

### 3.1 Updated ManifestFileSourceType

```csharp
namespace GenHub.Core.Models.Enums;

public enum ManifestFileSourceType
{
    Copy,
    CopyUnique, 
    Symlink,
    Hardlink,
    Remote,
    Patch,
    Package,
    Content  // NEW: Content-addressable storage reference
}
```

### 3.2 CAS-Aware ManifestFile Usage

```csharp
// During acquisition, files are processed into CAS and manifest updated
var manifestFile = new ManifestFile
{
    RelativePath = "Data/INI/GameData.ini",
    Hash = "ab123456789abcdef1234567890abcdef12345678",
    Size = 51234,
    SourceType = ManifestFileSourceType.Content  // References CAS
};
```

---

## 4. Integration with IFileOperationsService

### 4.1 Enhanced Interface

```csharp
namespace GenHub.Core.Interfaces.Workspace;

public interface IFileOperationsService
{
    // Existing methods...
    Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    
    // NEW: CAS-aware operations
    Task<string> StoreInCasAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default);
    Task<bool> CopyFromCasAsync(string hash, string destinationPath, CancellationToken cancellationToken = default);
    Task<bool> LinkFromCasAsync(string hash, string destinationPath, bool useHardLink = false, CancellationToken cancellationToken = default);
    Task<Stream> OpenCasContentAsync(string hash, CancellationToken cancellationToken = default);
}
```

### 4.2 Implementation Enhancement

```csharp
public class FileOperationsService : IFileOperationsService
{
    private readonly ICasService _casService;
    
    public async Task<bool> CopyFromCasAsync(string hash, string destinationPath, CancellationToken cancellationToken = default)
    {
        var casResult = await _casService.GetContentPathAsync(hash, cancellationToken);
        if (!casResult.IsSuccess)
        {
            _logger.LogWarning("Content not found in CAS: {Hash}", hash);
            return false;
        }
        
        await CopyFileAsync(casResult.Data!, destinationPath, cancellationToken);
        return true;
    }
}
```

---

## 5. Workspace Strategy Integration

### 5.1 CAS-Aware Strategy Base Class

```csharp
public abstract class WorkspaceStrategyBase<T> : IWorkspaceStrategy
{
    protected readonly ICasService _casService;
    
    protected async Task<bool> ProcessManifestFileWithCasAsync(
        ManifestFile file, 
        string workspacePath, 
        string baseInstallationPath,
        CancellationToken cancellationToken)
    {
        var destinationPath = Path.Combine(workspacePath, file.RelativePath);
        
        // Priority 1: Try CAS if hash available and SourceType is Content
        if (!string.IsNullOrEmpty(file.Hash) && 
            (file.SourceType == ManifestFileSourceType.Content || await _casService.ExistsAsync(file.Hash, cancellationToken).ConfigureAwait(false)).IsSuccess)
        {
            return await ProcessFromCasAsync(file, destinationPath, cancellationToken);
        }
        
        // Priority 2: Fall back to original source type processing
        return await ProcessFromSourceAsync(file, destinationPath, baseInstallationPath, cancellationToken);
    }
    
    private async Task<bool> ProcessFromCasAsync(ManifestFile file, string destinationPath, CancellationToken cancellationToken)
    {
        switch (GetCasStrategy(file))
        {
            case CasLinkStrategy.Copy:
                return await _fileOperations.CopyFromCasAsync(file.Hash, destinationPath, cancellationToken);
            case CasLinkStrategy.Symlink:
                return await _fileOperations.LinkFromCasAsync(file.Hash, destinationPath, useHardLink: false, cancellationToken);
            case CasLinkStrategy.HardLink:
                return await _fileOperations.LinkFromCasAsync(file.Hash, destinationPath, useHardLink: true, cancellationToken);
            default:
                return false;
        }
    }
}
```

### 5.2 Strategy-Specific CAS Behavior

**HybridCopySymlinkStrategy**:
- Essential files (< 1MB, .exe, .dll, .ini): Copy from CAS
- Large media files: Symlink from CAS
- Maintains existing essential file detection logic

**FullCopyStrategy**: 
- Always copy from CAS to workspace

**SymlinkOnlyStrategy**:
- Always symlink from CAS to workspace

---

## 6. Content Acquisition Enhancement

### 6.1 CAS Population During Acquisition

```csharp
public class HttpContentProvider : IContentProvider
{
    private readonly ICasService _casService;
    
    public async Task<ContentOperationResult<GameManifest>> AcquireContentAsync(
        GameManifest packageManifest, 
        string tempDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var transformedManifest = new GameManifest { /* copy properties */ };
        
        foreach (var packageFile in packageManifest.Files.Where(f => f.SourceType == ManifestFileSourceType.Package))
        {
            // Download and extract package
            var extractedFiles = await DownloadAndExtractPackageAsync(packageFile, tempDirectory, cancellationToken);
            
            // Store each extracted file in CAS and create Content-type manifest entries
            foreach (var (relativePath, extractedPath) in extractedFiles)
            {
                var storeResult = await _casService.StoreContentAsync(extractedPath, cancellationToken: cancellationToken);
                if (storeResult.IsSuccess)
                {
                    transformedManifest.Files.Add(new ManifestFile
                    {
                        RelativePath = relativePath,
                        Hash = storeResult.Data!,
                        Size = new FileInfo(extractedPath).Length,
                        SourceType = ManifestFileSourceType.Content
                    });
                }
            }
        }
        
        return ContentOperationResult<GameManifest>.Success(transformedManifest);
    }
}
```

---

## 7. Concurrency and Safety Mechanisms

### 7.1 Atomic Storage Operations

```csharp
public class CasStorage : ICasStorage
{
    public async Task<string> StoreObjectAsync(Stream content, string hash, CancellationToken cancellationToken)
    {
        var objectPath = GetObjectPath(hash);
        var tempPath = Path.Combine(_tempDirectory, $"store-{Guid.NewGuid():N}");
        var lockPath = Path.Combine(_lockDirectory, $"{hash}.lock");
        
        // Coordinate concurrent access
        using var lockFile = await AcquireLockAsync(lockPath, cancellationToken);
        
        // Check if object already exists (race condition protection)
        if (await ObjectExistsAsync(hash, cancellationToken))
        {
            return objectPath;
        }
        
        try
        {
            // Atomic write: temp file → verify hash → move to final location
            await using var tempStream = File.Create(tempPath);
            await content.CopyToAsync(tempStream, cancellationToken);
            await tempStream.FlushAsync(cancellationToken);
            
            // Verify integrity before moving
            var actualHash = await ComputeFileHashAsync(tempPath, cancellationToken);
            if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Hash mismatch: expected {hash}, got {actualHash}");
            }
            
            // Ensure target directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            
            // Atomic move to final location
            File.Move(tempPath, objectPath);
            
            return objectPath;
        }
        finally
        {
            // Cleanup temp file if it still exists
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
```

### 7.2 Lock Management

```csharp
private class CasLock : IAsyncDisposable
{
    private readonly FileStream _lockStream;
    
    public async ValueTask DisposeAsync()
    {
        _lockStream?.Dispose();
        // Delete lock file
    }
}

private async Task<CasLock> AcquireLockAsync(string lockPath, CancellationToken cancellationToken)
{
    var lockStream = new FileStream(lockPath, FileMode.Create, FileAccess.Write, FileShare.None);
    await lockStream.WriteAsync(Encoding.UTF8.GetBytes(Environment.ProcessId.ToString()), cancellationToken);
    await lockStream.FlushAsync(cancellationToken);
    return new CasLock { _lockStream = lockStream };
}
```

---

## 8. Garbage Collection and Maintenance

### 8.1 Reference Tracking

```csharp
public class CasReferenceTracker
{
    public async Task TrackManifestReferencesAsync(string manifestId, GameManifest manifest)
    {
        var refsPath = Path.Combine(_refsDirectory, "manifests", $"{manifestId}.refs");
        var references = manifest.Files
            .Where(f => f.SourceType == ManifestFileSourceType.Content && !string.IsNullOrEmpty(f.Hash))
            .Select(f => f.Hash)
            .ToHashSet();
            
        await File.WriteAllTextAsync(refsPath, JsonSerializer.Serialize(new
        {
            ManifestId = manifestId,
            References = references,
            TrackedAt = DateTime.UtcNow
        }));
    }
}
```

### 8.2 Garbage Collection Strategy

```csharp
public async Task<CasGarbageCollectionResult> RunGarbageCollectionAsync(CancellationToken cancellationToken)
{
    // 1. Collect all object hashes in CAS
    var allObjects = await ScanAllObjectsAsync(cancellationToken);
    
    // 2. Collect all referenced hashes from manifests and active workspaces
    var referencedHashes = await CollectReferencedHashesAsync(cancellationToken);
    
    // 3. Identify unreferenced objects (candidates for deletion)
    var unreferencedObjects = allObjects.Except(referencedHashes).ToList();
    
    // 4. Apply grace period (don't delete recently created objects)
    var gracePeriod = TimeSpan.FromDays(7);
    var safeToDelete = unreferencedObjects.Where(hash => 
        File.GetCreationTime(GetObjectPath(hash)) < DateTime.UtcNow - gracePeriod).ToList();
    
    // 5. Delete unreferenced objects
    long bytesFreed = 0;
    foreach (var hash in safeToDelete)
    {
        var objectPath = GetObjectPath(hash);
        var size = new FileInfo(objectPath).Length;
        await DeleteObjectAsync(hash, cancellationToken);
        bytesFreed += size;
    }
    
    return new CasGarbageCollectionResult
    {
        ObjectsDeleted = safeToDelete.Count,
        BytesFreed = bytesFreed,
        ObjectsScanned = allObjects.Count
    };
}
```

---

## 9. Configuration and Validation

### 9.1 CAS Configuration Model

```csharp
namespace GenHub.Core.Models.Storage;

public class CasConfiguration
{
    public string CasRootPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "cas-pool");
    public string HashAlgorithm { get; set; } = "SHA256";
    public TimeSpan GarbageCollectionGracePeriod { get; set; } = TimeSpan.FromDays(7);
    public long MaxCacheSizeBytes { get; set; } = 50L * 1024 * 1024 * 1024; // 50GB
    public bool EnableAutomaticGarbageCollection { get; set; } = true;
    public TimeSpan AutoGcInterval { get; set; } = TimeSpan.FromDays(1);
}
```

### 9.2 Integrity Validation

```csharp
public async Task<CasValidationResult> ValidateIntegrityAsync(CancellationToken cancellationToken)
{
    var results = new List<CasValidationIssue>();
    var objectPaths = Directory.GetFiles(_objectsDirectory, "*", SearchOption.AllDirectories);
    
    foreach (var objectPath in objectPaths)
    {
        var expectedHash = Path.GetFileName(objectPath);
        var actualHash = await ComputeFileHashAsync(objectPath, cancellationToken);
        
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new CasValidationIssue
            {
                ObjectPath = objectPath,
                ExpectedHash = expectedHash,
                ActualHash = actualHash,
                IssueType = CasValidationIssueType.HashMismatch
            });
        }
    }
    
    return new CasValidationResult { Issues = results };
}
```

---

## 10. Dependency Injection Registration

### 10.1 New Storage Module

```csharp
namespace GenHub.Infrastructure.DependencyInjection;

public static class StorageModule
{
    public static IServiceCollection AddStorageServices(this IServiceCollection services, IConfiguration configuration)
    {
        // CAS Configuration
        services.Configure<CasConfiguration>(configuration.GetSection("CAS"));
        
        // CAS Services
        services.AddSingleton<ICasStorage, CasStorage>();
        services.AddSingleton<ICasService, CasService>();
        services.AddTransient<CasReferenceTracker>();
        services.AddHostedService<CasMaintenanceService>(); // Background GC
        
        return services;
    }
}
```

---

## 11. Implementation Roadmap

### Phase 1: Core CAS Infrastructure (Week 1-2)
1. **ICasStorage Implementation**: Basic hash-based file storage
2. **ICasService Implementation**: High-level CAS operations
3. **Directory Structure Setup**: Create CAS pool organization
4. **Concurrency Primitives**: File locking and atomic operations

### Phase 2: Integration with File Operations (Week 3)
1. **IFileOperationsService Enhancement**: Add CAS-aware methods
2. **ManifestFileSourceType.Content**: Introduce new enum value
3. **Basic CAS Retrieval**: Enable workspace strategies to read from CAS

### Phase 3: Content Provider Integration (Week 4)
1. **Acquisition Phase Enhancement**: Store downloaded/extracted files in CAS
2. **Manifest Transformation**: Convert Package entries to Content entries
3. **Provider-Specific Integration**: Update HttpContentProvider, FileSystemContentProvider

### Phase 4: Workspace Strategy Enhancement (Week 5)
1. **Strategy Base Class Updates**: Add CAS-priority processing
2. **Strategy-Specific Logic**: Implement CAS behavior for each strategy
3. **Testing and Validation**: Ensure CAS integration works with all strategies

### Phase 5: Maintenance and Operations (Week 6)
1. **Reference Tracking**: Implement manifest and workspace reference tracking
2. **Garbage Collection**: Automated cleanup of unreferenced objects
3. **Integrity Validation**: Hash verification and corruption detection
4. **Background Services**: Automated maintenance tasks

---

## 12. Key Design Decisions

### 12.1 Hash Algorithm Choice
**Decision**: SHA-256
**Rationale**: Cryptographically secure, collision-resistant, widely supported
**Alternative Considered**: SHA-1 (faster but less secure), BLAKE3 (faster but newer)

### 12.2 Directory Sharding Strategy  
**Decision**: First 2 hex characters as subdirectory
**Rationale**: Balanced directory distribution, filesystem-friendly (256 subdirs max)
**Alternative Considered**: First 3 characters (4096 subdirs, may exceed filesystem limits)

### 12.3 Concurrency Model
**Decision**: File-based locking with atomic moves
**Rationale**: Cross-platform compatibility, simple implementation
**Alternative Considered**: Database-based coordination (added complexity)

### 12.4 Integration Approach
**Decision**: Enhance existing interfaces rather than replace
**Rationale**: Minimal disruption to existing codebase, gradual adoption
**Alternative Considered**: New parallel CAS-only interfaces (fragmentation risk)

---

## 13. Performance and Scale Considerations

**Disk Usage Optimization**: CAS eliminates redundant storage across all content
**Network Optimization**: Download once, reuse everywhere
**I/O Optimization**: Symbolic linking reduces file system pressure
**Scale Targets**: Support 50GB+ CAS pools with 100,000+ objects
**Concurrency**: Safe parallel access from multiple workspace preparations

This CAS subsystem transforms GenHub from a traditional launcher into an efficient content management platform, reducing storage requirements, improving performance, and providing a foundation for advanced features like incremental updates and distributed content delivery.
