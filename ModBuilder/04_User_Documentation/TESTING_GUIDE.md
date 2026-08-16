# ModBuilder - Quick Testing Guide

**Date**: March 19, 2026
**Status**: Ready for Testing

---

## Quick Start

1. **Launch Application**
   ```bash
   cd Z:\GeneralsHub\GenHub
   dotnet run --project GenHub\GenHub.csproj
   ```

2. **Navigate to ModBuilder**
   - Click "Tools" in main navigation
   - Select "ModBuilder" from tools list

3. **Expected Behavior**
   - Should show ProjectDashboardView (no project loaded)
   - Should see "Recent Projects" section
   - Should see "New Project" and "Open Project" buttons

---

## Test Scenarios

### Scenario 1: Create New Project
1. Click "New Project" button
2. Choose location and name (e.g., `TestMod.mbproj`)
3. **Expected**: ModBuilderView shows with project loaded
4. **Verify**: Project name shows in top bar
5. **Verify**: Bundle packs section is visible

### Scenario 2: Execute Build
1. Select bundle packs (check checkboxes)
2. Click "Execute Build" button
3. **Expected**: BuildProgressOverlay shows
4. **Expected**: Progress bar updates
5. **Expected**: Build log shows output
6. **Expected**: Build completes successfully
7. **Verify**: No crashes

### Scenario 3: Save Project
1. Make changes to project (add/remove bundles)
2. Click "Save" button
3. **Expected**: Success notification
4. **Expected**: Changes persisted to .mbproj file

### Scenario 4: Close Project
1. Click "Close Project" button (if available)
2. **Expected**: ProjectDashboardView shows
3. **Expected**: Project added to recent projects list

### Scenario 5: Open Recent Project
1. Click on recent project in list
2. **Expected**: ModBuilderView shows with project loaded
3. **Expected**: All project data loaded correctly

---

## Common Issues

### Issue: Application Crashes on Execute Build
**Cause**: Build configuration missing or invalid
**Fix**: Ensure project has valid bundles.json in Configs/ directory

### Issue: ProjectDashboardView Not Showing
**Cause**: IsProjectLoaded property not updating
**Fix**: Check ModBuilderViewModel.IsProjectLoaded binding

### Issue: Build Progress Not Updating
**Cause**: Progress reporting not wired
**Fix**: Check BuildProgressOverlay bindings

---

## Debug Commands

### Check Build Status
```bash
cd Z:\GeneralsHub\GenHub
dotnet build GenHub\GenHub.csproj
```

### Run with Logging
```bash
cd Z:\GeneralsHub\GenHub
dotnet run --project GenHub\GenHub.csproj --verbosity detailed
```

### Clean and Rebuild
```bash
cd Z:\GeneralsHub\GenHub
dotnet clean
dotnet build
```

---

## Expected File Structure

### New Project Structure
```
TestMod/
├── TestMod.mbproj              # Project file
├── Configs/                    # Bundle configurations
│   └── bundles.json
├── GameFilesEdited/            # Modified game files
│   └── Data/
├── .Build/                     # Build cache
└── .Release/                   # Output archives
```

### .mbproj File Format
```json
{
  "version": "1.0",
  "name": "TestMod",
  "description": "Test mod",
  "gameInstallationId": null,
  "createdAt": "2026-03-19T...",
  "lastModified": "2026-03-19T...",
  "projectVersion": "1.0.0",
  "directories": {
    "configs": "Configs",
    "gameFilesEdited": "GameFilesEdited",
    "build": ".Build",
    "release": ".Release"
  },
  "bundleConfigs": ["bundles.json"]
}
```

---

## Success Indicators

### Visual Indicators
- ✅ ProjectDashboardView shows when no project loaded
- ✅ ModBuilderView shows when project loaded
- ✅ Build progress overlay shows during build
- ✅ Build log shows output
- ✅ Success notification on build complete

### Functional Indicators
- ✅ Can create new project
- ✅ Can open existing project
- ✅ Can execute build without crashes
- ✅ Can save project changes
- ✅ Can close project
- ✅ Recent projects list updates

---

## Troubleshooting

### Build Fails Immediately
1. Check project has valid bundles.json
2. Check GameFilesEdited/ directory exists
3. Check build directory permissions
4. Check external tools are available

### UI Not Responding
1. Check DataContext is set correctly
2. Check bindings in XAML
3. Check ViewModel properties are updating
4. Check for exceptions in logs

### View Not Switching
1. Check IsProjectLoaded property
2. Check visibility bindings
3. Check Panel container has both views
4. Check ModBuilderToolPlugin.CreateControl()

---

## Contact

**Issues**: Report in GitHub Issues
**Documentation**: See INVESTIGATION_REPORT.md
**Status**: See COMPLETE_EXECUTION_REPORT.md

---

**Status**: Ready for Testing
**Last Updated**: March 19, 2026
