# CAS Reference Tracking & Lifecycle

This document details how GenHub manages the lifecycle of physical files in the Content-Addressable Storage (CAS) system using reference tracking.

## How Reference Tracking Works

GenHub does not use a central database for CAS references. Instead, it uses **Reference Files (`.refs`)** stored in the CAS root directory under the `refs/` folder.

### Reference File Locations
- `refs/manifests/{ManifestId}.refs`: Hashes required by a specific game manifest.
- `refs/workspaces/{WorkspaceId}.refs`: Hashes physically present in a prepared workspace.

### The Tracking Lifecycle

1. **Storage**: When `ContentStorageService.StoreContentAsync` is called, it triggers `CasReferenceTracker.TrackManifestReferencesAsync`.
2. **Preparation**: When `WorkspaceManager.PrepareWorkspaceAsync` hydrating a workspace, it triggers `TrackWorkspaceReferencesAsync`.
3. **Removal**: When a manifest is deleted or a workspace is cleaned up, the corresponding `.refs` file must be deleted via `UntrackManifestAsync` or `UntrackWorkspaceAsync`.
4. **Collection**: The `CasService.RunGarbageCollectionAsync` process:
    - Scans all `.refs` files to build a "Live Set" of hashes.
    - Scans the physical CAS storage for all files.
    - Deletes files not in the Live Set that are older than the **7-day grace period**.

## Why Blobs Persist (Common Pitfalls)

| Issue | Cause | Solution |
|-------|-------|----------|
| **Ghost References** | A manifest was deleted from the pool but its `.refs` file was left behind. | Ensure `UntrackManifestAsync` is called in the delete flow. |
| **Workspace Pins** | A workspace exists for an old profile configuration, "pinning" those files in CAS. | `ActiveWorkspaceId` must be cleared/cleaned during reconciliation. |
| **Grace Period** | Files are unreferenced but haven't reached the 7-day age threshold. | Use "Force GC" in Settings for immediate cleanup. |

## Best Practices for Developers

- **Always Untrack**: If you remove a manifest file from the disk, you MUST call the reference tracker to remove its `.refs` file.
- **Metadata vs. Content**: Renaming a manifest (ID change) counts as a "New Manifest + Delete Old". Both steps must be tracked.
- **Avoid Manual Deletion**: Never delete files directly from the CAS `objects/` directory. Use the Garbage Collection service instead.
