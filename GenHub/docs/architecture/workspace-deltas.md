# Workspace Delta Synchronization

This document explains how GenHub synchronizes the physical workspace on the user's disk when content changes occur in a profile.

## The Delta Analysis

When a profile is launched, the `WorkspaceManager` does not simply wipe and recreate the workspace (unless `ForceRecreate` is true). Instead, it uses the `WorkspaceReconciler` to compare the **Current State** (cached in `workspaces.json`) with the **Target State** (defined by the profile's `EnabledContentIds`).

### Delta Operations

The reconciler produces a list of `WorkspaceDelta` objects, each with one of the following operations:

1. **Skip**: The file exists in the workspace, has the correct hash, and belongs to a manifest that is still enabled. No action taken.
2. **Add**: The file is part of a newly enabled manifest and does not exist in the workspace. The strategy will create the link/copy.
3. **Update**: A file with the same relative path exists, but its hash differs (e.g., a new version of the same content). The strategy will replace the existing file.
4. **Remove**: The file belongs to a manifest that was disabled or replaced. The strategy will delete the link/copy.

## Strategy-Specific Behaviors

Each `IWorkspaceStrategy` implements the delta list differently:

| Strategy | Add/Update Implementation | Remove Implementation |
|----------|---------------------------|-----------------------|
| **SymlinkOnly** | Creates a symbolic link to the CAS object. | Deletes the symbolic link. |
| **HardLink** | Creates a hard link to the CAS object. | Deletes the hardlink. |
| **FullCopy** | Physically copies the file from CAS. | Deletes the physical file. |

## Why Workspace Synchronization is Deferred

Workspace reconciliation happens at **Launch Time** rather than **Edit Time** for several reasons:

1. **Performance**: Updating a workspace with thousands of files can be slow. We only want to do it when the user actually intends to play.
2. **Disk Space**: A user might have many profiles. Keeping all of them "in sync" physically would waste massive amounts of disk space.
3. **Atomicity**: If an update fails mid-way, the profile remains launchable (though validation might fail), rather than leaving the disk in an inconsistent state during normal app usage.

## Invalidation

The reconciliation service "invalidates" a workspace by clearing the `ActiveWorkspaceId` in the profile metadata. This forces `WorkspaceManager` to perform a full `PrepareWorkspaceAsync` call on the next launch, ensuring all deltas are processed.
