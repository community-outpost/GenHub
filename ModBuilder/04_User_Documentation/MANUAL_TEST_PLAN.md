# ModBuilder Manual Test Plan

**Purpose**: Verify all features work end-to-end
**Date**: March 20, 2026

---

## Prerequisites

1. ✅ Build succeeded with 0 errors
2. ✅ GenHub.Windows.exe exists in bin/Release
3. ✅ Sample project exists at `Z:\GeneralsHub\SampleProjects\ModBuilder\BasicMod`
4. ✅ Game installation detected (C&C Generals Zero Hour)

---

## Test 1: Load Sample Project

### Steps
1. Launch `Z:\GeneralsHub\GenHub\GenHub.Windows\bin\Release\net9.0-windows10.0.22621.0\win-x64\GenHub.Windows.exe`
2. Navigate to Tools → ModBuilder
3. Click "Open Project"
4. Select `Z:\GeneralsHub\SampleProjects\ModBuilder\BasicMod\BasicMod.mbproj`

### Expected Results
- ✅ Project loads without errors
- ✅ No "Resolved 0 files" error
- ✅ Config auto-discovered from `config/ModBundleItems.json`
- ✅ Simplified format converted automatically
- ✅ File count shows actual files (not 5)
- ✅ Status shows "Project loaded: BasicMod"

### If Failed
- Check logs for errors
- Verify config file exists
- Verify format conversion logic

---

## Test 2: File Manager - Browse Game Files

### Steps
1. With project loaded, expand "File Manager" section
2. Look at left side (Game Files)

### Expected Results
- ✅ Game installation path detected
- ✅ Directory tree shows game files
- ✅ Can expand folders
- ✅ Files show appropriate icons (📄 for INI, 🖼️ for TGA, etc.)
- ✅ Search box works
- ✅ File type filter works

### If Failed
- Check if game installation detected
- Check logs for errors
- Verify IGameInstallationService working

---

## Test 3: File Manager - Add Files to Project

### Steps
1. In Game Files tree (left), navigate to `Data/INI/Object/`
2. Select a file (e.g., `AmericaTank.ini`)
3. Click "Add Selected Files" button
4. Look at right side (Project Files)

### Expected Results
- ✅ File copied to `GameFilesEdited/Data/INI/Object/AmericaTank.ini`
- ✅ File appears in Project Files tree
- ✅ File status shows "Modified" (orange) or "New" (green)
- ✅ File count updates
- ✅ Notification shows "Files Added: Added 1 file"

### If Failed
- Check if file was actually copied
- Check logs for errors
- Verify directory structure preserved

---

## Test 4: File Manager - File Status Detection

### Steps
1. Add a file from game (as in Test 3)
2. Note the file status color
3. Edit the file in GameFilesEdited folder (change something)
4. Click "Refresh" button in File Manager

### Expected Results
- ✅ Unchanged file shows gray
- ✅ Modified file shows orange
- ✅ New file (not in game) shows green
- ✅ Status updates after refresh
- ✅ Modified count increases

### If Failed
- Check MD5 hash comparison logic
- Check logs for errors
- Verify file status detection

---

## Test 5: Config Editor - View Bundles

### Steps
1. Click "Edit Configuration" button
2. Look at "Bundle Items" tab

### Expected Results
- ✅ Dialog opens
- ✅ Shows existing bundle item "SampleTextures"
- ✅ Shows properties: Name, Display Name, File Count, etc.
- ✅ Can switch to "Bundle Packs" tab
- ✅ Add/Remove buttons visible

### If Failed
- Check if ConfigEditorDialog.axaml exists
- Check logs for errors
- Verify ViewModel binding

---

## Test 6: Config Editor - Add Bundle Item

### Steps
1. In Config Editor, click "Add Item" button
2. (Note: Detailed editing not yet implemented, so this may just add a placeholder)
3. Click "Save" button
4. Close dialog

### Expected Results
- ✅ New item added to list
- ✅ Save button works
- ✅ Dialog closes
- ✅ Changes saved to `config/ModBundleItems.json`
- ✅ Bundles reload in main view

### If Failed
- Check if JSON file updated
- Check logs for errors
- Verify save logic

---

## Test 7: Execute Build (Empty Project)

### Steps
1. With BasicMod loaded (no files added yet)
2. Check "Build" checkbox
3. Click "Execute Build" button
4. Watch build output console

### Expected Results
- ✅ Build starts
- ✅ Shows "Building stage: RawBundleItem"
- ✅ Shows "Processing X files" (where X > 0 if files exist)
- ✅ Build completes successfully
- ✅ No crashes
- ✅ No "Resolved 0 files" error

### If Failed
- Check logs for exact error
- Check if wildcards resolved
- Verify config loaded correctly

---

## Test 8: Execute Build (With Files)

### Steps
1. Add some game files using File Manager (Test 3)
2. Edit `config/ModBundleItems.json` to include those files:
   ```json
   {
     "BundleItems": [
       {
         "Name": "MyINIFiles",
         "SourceFiles": ["GameFilesEdited/Data/INI/**/*.ini"],
         "OutputFormat": "INI"
       }
     ]
   }
   ```
3. Reload project (close and reopen)
4. Check "Build" and "Release" checkboxes
5. Click "Execute Build"

### Expected Results
- ✅ Build processes files
- ✅ Shows progress in console
- ✅ Creates .big file in `.Release/` folder
- ✅ No errors
- ✅ File count matches expected

### If Failed
- Check if files were found
- Check wildcard patterns
- Check build output for errors
- Verify .big file created

---

## Test 9: Run Game

### Steps
1. With project loaded
2. Check "Run Game" checkbox
3. Click "Execute Build"

### Expected Results
- ✅ Build completes
- ✅ Game launches automatically
- ✅ No errors

### If Failed
- Check if game path detected
- Check logs for launch errors
- Verify IGameLauncher working

---

## Test 10: File Count Accuracy

### Steps
1. Load BasicMod project
2. Note file count in main view
3. Open File Manager
4. Note file counts (Total, Modified, New)
5. Add a file
6. Check if counts update

### Expected Results
- ✅ File count shows actual game files (not README, .mbproj, etc.)
- ✅ File Manager shows accurate counts
- ✅ Counts update when files added/removed
- ✅ Modified count accurate
- ✅ New count accurate

### If Failed
- Check file counting logic
- Check if system files excluded
- Verify FileManagerViewModel

---

## Test 11: Benchmark Against Python

### Steps
1. Navigate to `Z:\GeneralsGameData\Patch104pZH\`
2. Run Python ModBuilder (BuildInstall.bat)
3. Measure time and output
4. Load same project in C# ModBuilder
5. Execute build
6. Compare results

### Expected Results
- ✅ C# finds same files as Python
- ✅ C# processes files identically
- ✅ C# creates identical .big archives
- ✅ C# is 15-25% faster than Python

### If Failed
- Document differences
- Check config format compatibility
- Verify file processing logic

---

## Success Criteria

### Must Pass (Critical)
- [ ] Test 1: Load Sample Project
- [ ] Test 3: Add Files to Project
- [ ] Test 7: Execute Build (Empty)
- [ ] Test 8: Execute Build (With Files)
- [ ] Test 10: File Count Accuracy

### Should Pass (Important)
- [ ] Test 2: Browse Game Files
- [ ] Test 4: File Status Detection
- [ ] Test 5: View Bundles
- [ ] Test 9: Run Game

### Nice to Have (Optional)
- [ ] Test 6: Add Bundle Item (detailed editing not yet implemented)
- [ ] Test 11: Benchmark Against Python

---

## Known Limitations

1. **Detailed Bundle Editing**: Config editor shows bundles but detailed editing (wildcards, conversion settings) not yet implemented. Users can still edit JSON manually.

2. **Bundle Pack Editing**: Bundle pack editor shows packs but detailed editing not yet implemented.

3. **File Preview**: No file preview or comparison view yet.

4. **Drag and Drop**: No drag-and-drop support yet.

---

## If All Tests Pass

1. Mark ModBuilder as production-ready
2. Create user documentation
3. Create video tutorial
4. Deploy to users

---

## If Tests Fail

1. Document exact error
2. Provide steps to reproduce
3. Include logs and screenshots
4. Launch fix agents immediately

---

**Status**: Ready for manual testing
**Priority**: HIGH
**Estimated Time**: 30-45 minutes

---

*This test plan covers all critical functionality. Run through each test and document results.*
