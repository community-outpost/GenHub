# ModBuilder Code Analysis Report

**Analysis Date:** 2026-03-20
**Scope:** Runtime behavior analysis without GUI execution
**Focus:** Execution flows, threading safety, error handling, null safety

---

## Executive Summary

Analyzed ModBuilder codebase to predict runtime behavior and identify potential issues. Found **23 potential issues** across threading, error handling, null safety, and file I/O categories. Most issues are **LOW to MEDIUM severity** with proper mitigation strategies identified.

**Critical Findings:**
- ✅ Threading safety: Generally good with Dispatcher.UIThread usage
- ⚠️ Null safety: Several potential NullReferenceException risks
- ⚠️ Error handling: Some async methods lack complete error handling
- ✅ File I/O: Well-structured with proper async patterns

---

## Section 1: Project Creation Flow

### Call Graph: NewProjectAsync()

```
ModBuilderViewModel.NewProjectAsync()
├─ TopLevel.GetTopLevel() → Could return null
├─ StorageProvider.SaveFilePickerAsync() → User cancellation possible
├─ ProjectConfigService.CreateProjectAsync()
│  ├─ Validates projectPath and projectName
│  ├─ FileExistsCached() → Checks if project exists
│  ├─ CreateProjectDirectoryStructureAsync()
│  │  └─ Directory.CreateDirectory() for each folder
│  ├─ SaveProjectAsync()
│  │  ├─ Directory.CreateDirectory() for parent
│  │  └─ JsonSerializer.SerializeAsync()
│  └─ CreateSampleFilesAsync()
│     └─ File.WriteAllTextAsync() for README/config
├─ ProjectStructureGenerator.GenerateProjectStructureAsync()
│  ├─ CreateFolderStructureAsync()
│  │  └─ Directory.CreateDirectory() for 14 folders
│  ├─ CreateConfigFilesAsync()
│  │  ├─ WriteJsonFileAsync("ModBundleItems.json")
│  │  └─ WriteJsonFileAsync("ModBundlePacks.json")
│  └─ CreateReadmeFilesAsync()
│     └─ File.WriteAllTextAsync() for 7 README files
├─ LoadProjectDataAsync()
│  ├─ ConfigurationLoaderService.LoadConfigurationAsync()
│  ├─ Dispatcher.UIThread.InvokeAsync() → Populate Bundles
│  └─ Dispatcher.UIThread.Post() → Notify commands
└─ ProjectConfigService.AddToRecentProjectsAsync()
   ├─ GetRecentProjectsAsync()
   └─ SaveRecentProjectsAsync()
```

### Potential Failure Points

1. **TopLevel.GetTopLevel() returns null** (Line 413)
   - **Risk:** NullReferenceException when accessing topLevel.StorageProvider
   - **Likelihood:** LOW (only if MainWindow is not initialized)
   - **Mitigation:** Early return if null (already present)

2. **User cancels file picker** (Line 428)
   - **Risk:** file is null, method returns early
   - **Likelihood:** HIGH (user action)
   - **Mitigation:** Already handled with null check

3. **Directory creation fails** (ProjectConfigService:629-636)
   - **Risk:** UnauthorizedAccessException, IOException
   - **Likelihood:** MEDIUM (permissions, disk full)
   - **Mitigation:** Try-catch present, returns failure result

4. **JSON serialization fails** (ProjectConfigService:284)
   - **Risk:** JsonException, IOException
   - **Likelihood:** LOW (valid object structure)
   - **Mitigation:** Try-catch present in SaveProjectAsync

5. **File write fails** (ProjectStructureGenerator:156)
   - **Risk:** UnauthorizedAccessException, IOException
   - **Likelihood:** MEDIUM (permissions, disk full)
   - **Mitigation:** No try-catch in CreateReadmeFilesAsync

6. **Configuration loading fails** (ModBuilderViewModel:1050)
   - **Risk:** FileNotFoundException, JsonException
   - **Likelihood:** MEDIUM (missing/corrupt config)
   - **Mitigation:** Try-catch in LoadProjectDataAsync

### Risk Assessment

| Risk | Severity | Likelihood | Impact |
|------|----------|------------|--------|
| TopLevel null | LOW | LOW | App crash |
| Directory creation fails | MEDIUM | MEDIUM | Project creation fails |
| File write fails | MEDIUM | MEDIUM | Incomplete project structure |
| Config loading fails | MEDIUM | MEDIUM | Project loads without bundles |

---

## Section 2: Build Execution Flow

### Call Graph: BuildAsync()

```
ModBuilderViewModel.BuildAsync()
├─ Validation: CurrentProject != null
├─ Initialize build state
│  ├─ IsBuildRunning = true
│  ├─ _buildCancellationTokenSource = new()
│  ├─ _buildStopwatch.Restart()
│  └─ Dispatcher.UIThread.InvokeAsync() → Clear UI
├─ Load configuration
│  ├─ CurrentProject.Configuration (if exists)
│  └─ ConfigurationLoaderService.LoadConfigurationAsync() (if needed)
├─ Get selected bundle packs
│  └─ Bundles.Where(b => b.IsSelected).Select(b => b.Name)
├─ BuildEngineService.ExecuteBuildAsync()
│  ├─ _buildLock.WaitAsync() → Ensure single build
│  ├─ ValidateBuildStructure()
│  │  ├─ Check directories exist
│  │  └─ Check tools exist
│  ├─ BuildCacheService.LoadCacheAsync()
│  │  ├─ Try MessagePack format first
│  │  └─ Fallback to JSON format
│  ├─ Stage 1: Scan Files
│  │  └─ Parallel.ForEachAsync() → Scan all source files
│  ├─ Stage 2: Process Files
│  │  └─ Parallel.ForEachAsync() → Convert/copy files
│  │     ├─ FileHashRegistryService.IsFileIrrelevant()
│  │     ├─ BuildCacheService.GetCachedStatus()
│  │     └─ FileConversionService.ConvertFileAsync()
│  │        ├─ ImageConversionService (for images)
│  │        ├─ StringTableConversionService (for .str/.csf)
│  │        ├─ TextProcessingService (for .ini/.txt)
│  │        └─ Direct copy (for others)
│  ├─ Stage 3: Create Archives
│  │  └─ ArchiveService.CreateArchiveAsync()
│  │     ├─ Parallel.ForEachAsync() → Pre-load files
│  │     └─ ZipArchive.CreateEntry() for each file
│  ├─ Stage 4: Generate Manifests
│  │  └─ Write metadata JSON files
│  └─ Stage 5: Save Cache
│     └─ BuildCacheService.SaveCacheAsync()
├─ Update UI with results
│  ├─ AppendBuildLog() → Dispatcher.UIThread.Post()
│  └─ NotificationService.ShowSuccess/ShowError()
└─ Cleanup
   ├─ IsBuildRunning = false
   └─ _buildCancellationTokenSource?.Dispose()
```

### Threading Analysis

**UI Thread Operations (Correct):**
- Line 741: `Dispatcher.UIThread.InvokeAsync()` → Clear build log
- Line 1109: `Dispatcher.UIThread.Post()` → Append log messages
- Line 1140: `Dispatcher.UIThread.Post()` → Notify command state
- Line 1173: `Dispatcher.UIThread.Post()` → Update UI properties

**Background Operations (Correct):**
- Line 782: `BuildEngineService.ExecuteBuildAsync()` → Runs on background thread
- All file I/O uses `ConfigureAwait(false)` → Prevents UI thread blocking

**Potential Threading Issues:**

1. **BuildProgress property updates** (Line 1122-1128)
   - **Issue:** OnBuildProgress() sets properties directly without Dispatcher
   - **Location:** ModBuilderViewModel.cs:1120-1134
   - **Risk:** Cross-thread property access
   - **Severity:** MEDIUM
   - **Fix:** Wrap in `Dispatcher.UIThread.Post()`

2. **CurrentProject property access** (Line 756)
   - **Issue:** Accessed from background thread without synchronization
   - **Location:** ModBuilderViewModel.cs:756
   - **Risk:** Race condition if project changes during build
   - **Severity:** LOW (IsBuildRunning prevents changes)
   - **Fix:** Already mitigated by CanBuild() check

### Potential Failure Points

1. **Configuration is null** (Line 766-769)
   - **Risk:** Build proceeds with empty configuration
   - **Likelihood:** MEDIUM (new project without config)
   - **Mitigation:** Creates default BuildConfiguration

2. **No bundles selected** (Line 775-778)
   - **Risk:** Empty selectedPacks list
   - **Likelihood:** MEDIUM (user error)
   - **Mitigation:** Build engine should validate

3. **Build engine throws exception** (Line 782)
   - **Risk:** Unhandled exception crashes build
   - **Likelihood:** LOW (try-catch present)
   - **Mitigation:** Try-catch at lines 809-824

4. **Cancellation during build** (Line 809-814)
   - **Risk:** OperationCanceledException
   - **Likelihood:** HIGH (user action)
   - **Mitigation:** Properly caught and handled

5. **File conversion fails** (FileConversionService:87-95)
   - **Risk:** Exception during image/file conversion
   - **Likelihood:** MEDIUM (corrupt files, missing tools)
   - **Mitigation:** Try-catch returns failure result

### Risk Assessment

| Risk | Severity | Likelihood | Impact |
|------|----------|------------|--------|
| BuildProgress threading | MEDIUM | HIGH | UI update errors |
| Empty configuration | LOW | MEDIUM | Build with defaults |
| No bundles selected | LOW | MEDIUM | Empty build output |
| File conversion fails | MEDIUM | MEDIUM | Partial build |
| Cancellation | LOW | HIGH | Clean abort |

---

## Section 3: Issues Found

### Category A: Threading Safety Issues

#### Issue A1: BuildProgress Updates Without Dispatcher
- **Location:** ModBuilderViewModel.cs:1120-1134
- **Description:** OnBuildProgress() sets observable properties directly without Dispatcher
- **Severity:** MEDIUM
- **Likelihood:** HIGH (called from background thread)
- **Proposed Fix:**
```csharp
private void OnBuildProgress(BuildProgress progress)
{
    Dispatcher.UIThread.Post(() =>
    {
        BuildProgress = progress;
        BuildStage = progress.CurrentStage.ToString();
        CurrentFile = progress.CurrentFile;
        ProcessedFiles = progress.ProcessedFiles;
        TotalFiles = progress.TotalFiles;
        PercentComplete = progress.PercentComplete;
        EstimatedTimeRemaining = progress.EstimatedTimeRemaining;
    });

    if (!string.IsNullOrEmpty(progress.CurrentFile))
    {
        AppendBuildLog($"{progress.CurrentStage}: {progress.CurrentFile}");
    }
}
```

#### Issue A2: Property Change Notifications in Partial Methods
- **Location:** ModBuilderViewModel.cs:1136-1191
- **Description:** Partial methods call NotifyCanExecuteChanged() which may not be thread-safe
- **Severity:** LOW
- **Likelihood:** MEDIUM
- **Proposed Fix:** Already wrapped in Dispatcher.UIThread.Post() ✅

### Category B: Null Safety Issues

#### Issue B1: TopLevel Could Be Null
- **Location:** ModBuilderViewModel.cs:413
- **Description:** TopLevel.GetTopLevel() could return null
- **Severity:** LOW
- **Likelihood:** LOW (only during initialization)
- **Proposed Fix:** Already handled with early return ✅

#### Issue B2: CurrentProject.Configuration Null Access
- **Location:** ModBuilderViewModel.cs:582-585
- **Description:** Accesses CurrentProject.Configuration without null check
- **Severity:** LOW
- **Likelihood:** LOW (checked at method entry)
- **Proposed Fix:** Add null check before access

#### Issue B3: Path.GetDirectoryName() Could Return Null
- **Location:** Multiple locations (ProjectStructureGenerator:22, ProjectConfigService:105, etc.)
- **Description:** Path.GetDirectoryName() can return null for root paths
- **Severity:** MEDIUM
- **Likelihood:** LOW (valid project paths)
- **Proposed Fix:** Add null checks after Path.GetDirectoryName()

#### Issue B4: File Picker Returns Null
- **Location:** ModBuilderViewModel.cs:428, 497
- **Description:** User can cancel file picker
- **Severity:** LOW
- **Likelihood:** HIGH (user action)
- **Proposed Fix:** Already handled with null checks ✅

#### Issue B5: Configuration Deserialization Returns Null
- **Location:** ConfigurationLoaderService.cs:54-59
- **Description:** JsonSerializer.Deserialize could return null
- **Severity:** MEDIUM
- **Likelihood:** LOW (valid JSON)
- **Proposed Fix:** Already handled with null check and exception ✅

#### Issue B6: LoadProjectDataAsync Configuration Null
- **Location:** ModBuilderViewModel.cs:1061
- **Description:** CurrentProject.Configuration?.Items accessed without null check
- **Severity:** LOW
- **Likelihood:** LOW (null-conditional operator used)
- **Proposed Fix:** Already safe with ?. operator ✅

### Category C: Error Handling Issues

#### Issue C1: CreateReadmeFilesAsync No Try-Catch
- **Location:** ProjectStructureGenerator.cs:115-158
- **Description:** File.WriteAllTextAsync() can throw IOException
- **Severity:** MEDIUM
- **Likelihood:** MEDIUM (permissions, disk full)
- **Proposed Fix:**
```csharp
private static async Task CreateReadmeFilesAsync(string projectDir, CancellationToken cancellationToken)
{
    var readmeFiles = new[] { /* ... */ };

    foreach (var (path, content) in readmeFiles)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log warning but continue - README files are not critical
            logger.LogWarning(ex, "Failed to create README file: {Path}", path);
        }
    }
}
```

#### Issue C2: CreateConfigFilesAsync No Try-Catch
- **Location:** ProjectStructureGenerator.cs:67-113
- **Description:** WriteJsonFileAsync() can throw IOException
- **Severity:** HIGH
- **Likelihood:** MEDIUM (permissions, disk full)
- **Proposed Fix:** Wrap in try-catch and propagate exception (config files are critical)

#### Issue C3: LoadProjectDataAsync Swallows Exceptions
- **Location:** ModBuilderViewModel.cs:1097-1101
- **Description:** Catches all exceptions and shows generic error
- **Severity:** LOW
- **Likelihood:** LOW (good for user experience)
- **Proposed Fix:** Already acceptable - logs error and shows notification ✅

#### Issue C4: OpenProjectFolder No Validation
- **Location:** ModBuilderViewModel.cs:895
- **Description:** Path.GetDirectoryName() could return null
- **Severity:** LOW
- **Likelihood:** LOW (valid project paths)
- **Proposed Fix:** Add null check after Path.GetDirectoryName()

#### Issue C5: BuildAsync Configuration Loading
- **Location:** ModBuilderViewModel.cs:758-764
- **Description:** ConfigurationLoaderService.LoadConfigurationAsync() could throw
- **Severity:** MEDIUM
- **Likelihood:** MEDIUM (missing/corrupt config)
- **Proposed Fix:** Already wrapped in outer try-catch ✅

### Category D: File I/O Issues

#### Issue D1: FileExistsCached Cache Invalidation
- **Location:** ProjectConfigService.cs:737-740
- **Description:** Cache never invalidated except manually
- **Severity:** LOW
- **Likelihood:** LOW (files rarely deleted externally)
- **Proposed Fix:** Add cache expiration or file watcher

#### Issue D2: Concurrent File Access
- **Location:** BuildEngineService.cs (Parallel.ForEachAsync)
- **Description:** Multiple threads could access same file
- **Severity:** LOW
- **Likelihood:** LOW (different files processed)
- **Proposed Fix:** Already safe - each file processed once ✅

#### Issue D3: Directory.Delete Recursive
- **Location:** ModBuilderViewModel.cs:851
- **Description:** Directory.Delete(recursive: true) can fail if files are locked
- **Severity:** MEDIUM
- **Likelihood:** MEDIUM (build output in use)
- **Proposed Fix:** Already wrapped in try-catch ✅

#### Issue D4: File.Exists vs FileExistsCached
- **Location:** Multiple locations
- **Description:** Inconsistent use of File.Exists vs FileExistsCached
- **Severity:** LOW
- **Likelihood:** N/A (performance optimization)
- **Proposed Fix:** Document when to use each

### Category E: Configuration Loading Issues

#### Issue E1: Missing Configuration File
- **Location:** ConfigurationLoaderService.cs:44-48
- **Description:** Throws FileNotFoundException if config missing
- **Severity:** MEDIUM
- **Likelihood:** MEDIUM (new project, deleted file)
- **Proposed Fix:** Already throws exception - caller should handle ✅

#### Issue E2: Invalid JSON Format
- **Location:** ConfigurationLoaderService.cs:69-73
- **Description:** Throws InvalidOperationException for invalid JSON
- **Severity:** MEDIUM
- **Likelihood:** MEDIUM (manual editing)
- **Proposed Fix:** Already throws exception with context ✅

#### Issue E3: Wildcard Resolution Failure
- **Location:** ConfigurationLoaderService.cs:402-448
- **Description:** ResolveWildcardPatternAsync() catches exceptions and returns empty list
- **Severity:** LOW
- **Likelihood:** LOW (logs error)
- **Proposed Fix:** Already acceptable - logs error and continues ✅

#### Issue E4: Configuration Validation Warnings
- **Location:** ConfigurationLoaderService.cs:216-235
- **Description:** Missing directories/tools logged as warnings, not errors
- **Severity:** LOW
- **Likelihood:** MEDIUM (expected during setup)
- **Proposed Fix:** Already acceptable - warnings don't block build ✅

### Category F: UI State Management Issues

#### Issue F1: Command State Updates
- **Location:** ModBuilderViewModel.cs:1088-1095
- **Description:** Multiple NotifyCanExecuteChanged() calls in sequence
- **Severity:** LOW
- **Likelihood:** N/A (performance)
- **Proposed Fix:** Batch updates if performance issue arises

#### Issue F2: Observable Collection Updates
- **Location:** ModBuilderViewModel.cs:1058-1072
- **Description:** Bundles.Clear() and Add() in loop
- **Severity:** LOW
- **Likelihood:** N/A (performance)
- **Proposed Fix:** Use ReplaceRange if available

#### Issue F3: Property Change Cascades
- **Location:** ModBuilderViewModel.cs:1152-1166
- **Description:** Partial methods trigger additional property changes
- **Severity:** LOW
- **Likelihood:** N/A (by design)
- **Proposed Fix:** Already acceptable - intentional cascading ✅

---

## Section 4: Recommendations

### High Priority (Fix Before Release)

1. **Fix BuildProgress Threading (Issue A1)**
   - Wrap OnBuildProgress() property updates in Dispatcher.UIThread.Post()
   - **Impact:** Prevents cross-thread UI updates
   - **Effort:** 5 minutes

2. **Add Try-Catch to CreateConfigFilesAsync (Issue C2)**
   - Config files are critical for project functionality
   - **Impact:** Prevents silent failures during project creation
   - **Effort:** 10 minutes

3. **Add Null Checks After Path.GetDirectoryName() (Issue B3)**
   - Multiple locations need validation
   - **Impact:** Prevents NullReferenceException
   - **Effort:** 15 minutes

### Medium Priority (Fix Soon)

4. **Add Try-Catch to CreateReadmeFilesAsync (Issue C1)**
   - README files are non-critical but should log failures
   - **Impact:** Better error visibility
   - **Effort:** 10 minutes

5. **Validate Empty Bundle Selection (Issue in Build Flow)**
   - Check if selectedPacks is empty before build
   - **Impact:** Better user feedback
   - **Effort:** 5 minutes

6. **Add Cache Expiration to FileExistsCached (Issue D1)**
   - Prevent stale cache entries
   - **Impact:** Improved reliability
   - **Effort:** 30 minutes

### Low Priority (Nice to Have)

7. **Batch Command State Updates (Issue F1)**
   - Reduce NotifyCanExecuteChanged() calls
   - **Impact:** Minor performance improvement
   - **Effort:** 20 minutes

8. **Document File.Exists vs FileExistsCached (Issue D4)**
   - Add XML comments explaining when to use each
   - **Impact:** Code maintainability
   - **Effort:** 10 minutes

9. **Add Unit Tests for Error Paths**
   - Test exception handling in all services
   - **Impact:** Increased confidence
   - **Effort:** 4-8 hours

### Code Quality Improvements

10. **Add Null-Forgiving Operators**
    - Use `!` operator where null is impossible
    - **Impact:** Cleaner code, fewer warnings
    - **Effort:** 30 minutes

11. **Add ConfigureAwait(false) Consistently**
    - Already present in most places, audit remaining
    - **Impact:** Performance consistency
    - **Effort:** 15 minutes

12. **Add XML Documentation**
    - Document all public methods and properties
    - **Impact:** Better IntelliSense
    - **Effort:** 2-4 hours

---

## Section 5: Testing Recommendations

### Manual Testing Checklist

**Project Creation:**
- [ ] Create project with valid path
- [ ] Create project with existing path (should fail)
- [ ] Create project with invalid characters in name
- [ ] Create project on read-only drive (should fail gracefully)
- [ ] Cancel file picker dialog
- [ ] Create project with very long path (>260 chars on Windows)

**Project Loading:**
- [ ] Load valid project
- [ ] Load project with missing config files
- [ ] Load project with corrupt JSON
- [ ] Load project from read-only location
- [ ] Load project while another is open
- [ ] Load project from recent projects list

**Build Execution:**
- [ ] Build with all bundles selected
- [ ] Build with no bundles selected
- [ ] Build with missing source files
- [ ] Build with corrupt image files
- [ ] Cancel build mid-execution
- [ ] Build while output directory is open in Explorer
- [ ] Build with insufficient disk space

**Error Scenarios:**
- [ ] Delete project directory while project is open
- [ ] Modify config file externally during build
- [ ] Remove write permissions on build directory
- [ ] Fill disk during build
- [ ] Kill process during file write

### Automated Testing Recommendations

1. **Unit Tests for Services**
   - ProjectConfigService: Create, Load, Save, Validate
   - ConfigurationLoaderService: Load, Merge, Validate
   - BuildEngineService: Execute, Cancel, Error handling
   - FileConversionService: All conversion types

2. **Integration Tests**
   - Full project creation flow
   - Full build execution flow
   - Configuration loading and merging
   - File conversion pipeline

3. **Threading Tests**
   - Concurrent property updates
   - Cancellation during build
   - Multiple builds in sequence

4. **Error Handling Tests**
   - IOException during file operations
   - JsonException during deserialization
   - UnauthorizedAccessException during directory creation
   - OperationCanceledException during build

---

## Conclusion

The ModBuilder codebase is **well-structured** with good separation of concerns and proper async patterns. Most potential issues are **LOW severity** and already have mitigation strategies in place.

**Key Strengths:**
- ✅ Consistent use of Dispatcher.UIThread for UI updates
- ✅ Proper async/await patterns with ConfigureAwait(false)
- ✅ Comprehensive error handling in most services
- ✅ Good use of Result pattern for operation outcomes
- ✅ Proper cancellation token propagation

**Key Weaknesses:**
- ⚠️ OnBuildProgress() updates properties without Dispatcher (HIGH PRIORITY FIX)
- ⚠️ Some file operations lack try-catch blocks
- ⚠️ Path.GetDirectoryName() null checks missing in some places

**Overall Risk Level:** LOW to MEDIUM

With the recommended fixes applied, the codebase should be **production-ready** with minimal runtime issues.

---

## Appendix: File Locations

**ViewModels:**
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\ViewModels\ModBuilderViewModel.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\ViewModels\ConfigEditorViewModel.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\ViewModels\SettingsPanelViewModel.cs`

**Services:**
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\BuildEngineService.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\ProjectConfigService.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\ConfigurationLoaderService.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\ProjectStructureGenerator.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\FileConversionService.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\BuildCacheService.cs`
- `Z:\GeneralsHub\GenHub\GenHub\Features\Tools\ModBuilder\Services\ArchiveService.cs`

**Interfaces:**
- `Z:\GeneralsHub\GenHub\GenHub.Core\Interfaces\Tools\ModBuilder\*.cs`

---

**Report Generated:** 2026-03-20
**Analyst:** enowX Labs AI Assistant
**Review Status:** Ready for Developer Review
