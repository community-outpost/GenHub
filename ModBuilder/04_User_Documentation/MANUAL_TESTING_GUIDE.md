# ModBuilder Manual Testing Guide

**Purpose**: Step-by-step guide to verify ModBuilder works correctly

---

## Prerequisites

1. Build GenHub in Release mode: ✅ DONE (0 errors)
2. Launch GenHub.Windows.exe
3. Navigate to Tools → ModBuilder

---

## Test 1: Project Creation

### Steps
1. Click "New Project" button
2. Enter project name: "TestMod"
3. Choose location: `C:\Temp\ModBuilderTest\`
4. Click OK/Create

### Expected Results
- ✅ Project created without crash
- ✅ Folder structure created:
  ```
  TestMod/
  ├── TestMod.mbproj
  ├── GameFilesEdited/
  │   ├── Data/INI/
  │   ├── Data/Audio/
  │   ├── Data/Scripts/
  │   ├── Art/Textures/
  │   └── Art/W3D/
  ├── config/
  │   ├── ModBundleItems.json
  │   ├── ModBundlePacks.json
  │   └── README.txt
  └── ReleaseFiles/
  ```
- ✅ README files in each folder
- ✅ UI shows project loaded

### If Failed
- Check logs for errors
- Check if folders were created
- Check if .mbproj file exists

---

## Test 2: UI Buttons

### Steps
1. Click "Open Project Folder"
2. Click "Open GameFilesEdited Folder"
3. Click "Open Build Output"

### Expected Results
- ✅ Explorer opens to correct folder
- ✅ No crashes
- ✅ Folders exist

### If Failed
- Check if folders exist
- Check logs for errors

---

## Test 3: Add Test Files

### Steps
1. Navigate to `TestMod/GameFilesEdited/Art/Textures/`
2. Copy a test .tga file (or create dummy file)
3. Return to ModBuilder UI

### Expected Results
- ✅ File exists in folder
- ✅ ModBuilder can see the file (check logs)

---

## Test 4: Execute Build (Empty Project)

### Steps
1. Click "Execute Build" button
2. Watch build output console

### Expected Results
- ✅ No crash
- ✅ Build output shows:
  ```
  Building stage: RawBundleItem
  Processing 0 files for stage RawBundleItem
  Build pipeline completed with success=True
  ```
- ✅ No threading errors
- ✅ Build completes successfully

### If Failed
- Check logs for "Call from invalid thread"
- Check for exceptions
- Document exact error

---

## Test 5: Execute Build (With Files)

### Steps
1. Add test files to GameFilesEdited/
2. Edit config/ModBundleItems.json:
   ```json
   {
     "BundleItems": [
       {
         "Name": "TestTextures",
         "SourceFiles": ["GameFilesEdited/Art/Textures/**/*.tga"],
         "OutputFormat": "DDS"
       }
     ]
   }
   ```
3. Edit config/ModBundlePacks.json:
   ```json
   {
     "BundlePacks": [
       {
         "Name": "TestMod",
         "Items": ["TestTextures"],
         "OutputFile": ".Release/TestMod.big"
       }
     ]
   }
   ```
4. Click "Execute Build"

### Expected Results
- ✅ Build processes files
- ✅ Shows progress in console
- ✅ Creates .big file in .Release/
- ✅ No crashes

### If Failed
- Check if files were processed
- Check build output for errors
- Check if .big file created

---

## Test 6: Error Handling

### Steps
1. Try to load non-existent project
2. Try to build with invalid config
3. Try to execute build with missing files

### Expected Results
- ✅ Friendly error messages
- ✅ No crashes
- ✅ Clear explanation of what went wrong

---

## Test 7: Threading Stability

### Steps
1. Create project
2. Execute build
3. Immediately click other buttons
4. Try to load another project during build

### Expected Results
- ✅ No "Call from invalid thread" errors
- ✅ UI remains responsive
- ✅ No crashes

---

## Test 8: Config Editor (If Implemented)

### Steps
1. Click "Edit Configuration" button (if exists)
2. Try to add bundle item
3. Try to save changes

### Expected Results
- ✅ Dialog opens
- ✅ Can add/edit items
- ✅ Changes save to JSON

---

## Checklist

After completing all tests, verify:

- [ ] Build succeeds (0 errors)
- [ ] Can create project
- [ ] Project structure created
- [ ] Can execute build (empty)
- [ ] Can execute build (with files)
- [ ] No threading crashes
- [ ] All UI buttons work
- [ ] Error handling works
- [ ] Config files created
- [ ] README files helpful

---

## Report Results

Document:
1. Which tests passed ✅
2. Which tests failed ❌
3. Exact error messages
4. Screenshots of issues
5. Steps to reproduce problems

---

**Status**: Ready for manual testing
**Next**: Run through all tests and report results
