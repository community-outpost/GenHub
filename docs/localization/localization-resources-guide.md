# GenHub Localization Resources Guide

This guide documents the localization resource structure for GenHub, including resource file organization, naming conventions, usage patterns, and best practices for translators and developers.

## Overview

GenHub uses the .NET resource system (`.resx` files) for localization, with satellite assemblies for different languages. The resource files are organized by functional area to make translation and maintenance easier.

## Resource File Structure

All resource files are located in `GenHub.Core/Resources/Strings/` and follow a hierarchical naming convention:

### UI Resources

**UI.Common.resx** - Common UI elements used throughout the application

- Button labels (Save, Cancel, OK, Apply, Close, etc.)
- Common status messages (Loading, Success, Error, etc.)
- Common actions (Copy, Paste, Import, Export, etc.)
- Example keys: `Button.Save`, `Status.Loading`, `Action.Import`

**UI.Navigation.resx** - Navigation-specific strings

- Tab names (Game Profiles, Downloads, Settings, etc.)
- Section headers
- Navigation labels
- Example keys: `Tab.GameProfiles`, `Tab.Downloads`, `Tab.Settings`

**UI.GameProfiles.resx** - Game profile management UI

- Profile loading status messages
- Game scanning messages
- Profile creation/editing labels
- Service availability messages
- Example keys: `Status.LoadingProfiles`, `Status.ScanningForGames`, `Profile.AutoCreated`

**UI.Settings.resx** - Settings page UI

- Setting labels and categories
- Theme options
- Path settings labels
- Dialog titles
- Download, CAS, and content settings
- Example keys: `Button.SaveSettings`, `Theme.Dark`, `Download.MaxConcurrentDownloads`

**UI.Updates.resx** - Update functionality UI

- Update check status messages
- Installation progress messages
- Version display labels
- Update-related errors
- Example keys: `Status.CheckingForUpdates`, `Button.InstallUpdate`, `Version.Current`

**UI.Tools.resx** - Tools management UI

- Tool plugin management strings
- Tool status messages
- Empty state messages
- Tool installation/removal dialogs
- Example keys: `Title.Tools`, `Button.AddTool`, `Status.ToolInstalledSuccess`

**UI.Downloads.resx** - Downloads UI

- Download section headers
- Download category labels
- Download status messages
- Coming soon labels
- Example keys: `Section.PrimaryDownloads`, `Category.GitHubBuilds`, `Status.Downloading`

### Error Resources

**Errors.Validation.resx** - Validation error messages

- Field validation (required, format, range)
- Settings validation
- Path and URL validation
- Culture validation
- Example keys: `RequiredField`, `InvalidFormat`, `PathDoesNotExist`

**Errors.Operations.resx** - Operation error messages

- General operation failures
- Settings operation errors
- File/path errors
- Profile and update errors
- Localization errors
- Example keys: `OperationFailed`, `FailedToSaveSettings`, `ErrorScanningForGames`

### Message Resources

**Messages.Success.resx** - Success messages

- Operation completion messages
- Save confirmations
- Initialization success messages
- Localization success messages
- Example keys: `SettingsSaved`, `ProfileCreated`, `OperationCompleted`

**Messages.Confirmations.resx** - Confirmation dialog messages

- Delete confirmations
- Reset confirmations
- Overwrite confirmations
- Exit/close confirmations
- Action confirmations
- Example keys: `DeleteProfile`, `ResetSettings`, `ExitApplication`

### Tooltip Resources

**Tooltips.resx** - UI tooltips and help text

- Button tooltips
- Settings tooltips
- Profile tooltips
- Navigation tooltips
- Icon tooltips
- Example keys: `Button.Save`, `Settings.Theme`, `Profile.CreateNew`

## Naming Conventions

### Resource Keys

Resource keys use hierarchical dot notation for organization:

```
Category.Subcategory.Identifier
```

Examples:

- `Button.Save` - Save button in common UI
- `UI.GameProfiles.List.Header.Name` - Name column header in game profiles list
- `Errors.Validation.RequiredField` - Required field validation error
- `Messages.Confirmations.DeleteProfile` - Delete profile confirmation

### Categories

Common categories used in keys:

- `Button` - Button labels
- `Status` - Status messages
- `Action` - Action labels
- `Tab` - Navigation tab names
- `Dialog` - Dialog titles
- `Error` - Error messages
- `Warning` - Warning messages
- `Icon` - Icon tooltips
- `Nav` - Navigation items
- `Settings` - Setting labels
- `Profile` - Profile-related strings

### Format Strings

Format strings use standard .NET string formatting with numbered placeholders:

```xml
<data name="Status.ScanComplete" xml:space="preserve">
  <value>Scan complete. Found {0} game installations</value>
  <comment>Status message shown when scan completes. {0} is the count of installations found</comment>
</data>
```

Usage in code:

```csharp
var message = _localizationService.GetString(
    StringResources.UiGameProfiles,
    "Status.ScanComplete",
    5); // Result: "Scan complete. Found 5 game installations"
```

## Using Resources in Code

### Basic String Retrieval

```csharp
using GenHub.Core.Interfaces.Localization;
using GenHub.Core.Resources.Strings;

public class MyViewModel
{
    private readonly ILocalizationService _localizationService;

    public MyViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public void SaveData()
    {
        // Get simple string
        var buttonText = _localizationService.GetString(
            StringResources.UiCommon,
            "Button.Save");

        // Get formatted string
        var statusMessage = _localizationService.GetString(
            StringResources.UiGameProfiles,
            "Status.ScanComplete",
            installationCount);
    }
}
```

### Available Resource Sets

Use the `StringResources` class constants for resource set names:

```csharp
StringResources.UiCommon              // UI.Common.resx
StringResources.UiNavigation          // UI.Navigation.resx
StringResources.UiGameProfiles        // UI.GameProfiles.resx
StringResources.UiSettings            // UI.Settings.resx
StringResources.UiUpdates             // UI.Updates.resx
StringResources.UiTools               // UI.Tools.resx
StringResources.UiDownloads           // UI.Downloads.resx
StringResources.ErrorsValidation      // Errors.Validation.resx
StringResources.ErrorsOperations      // Errors.Operations.resx
StringResources.MessagesSuccess       // Messages.Success.resx
StringResources.MessagesConfirmations // Messages.Confirmations.resx
StringResources.Tooltips              // Tooltips.resx
```

### Reactive Culture Changes

```csharp
public class MyViewModel : IDisposable
{
    private readonly ILocalizationService _localizationService;
    private IDisposable? _cultureSubscription;

    public MyViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;

        // Subscribe to culture changes
        _cultureSubscription = _localizationService.CultureChanged.Subscribe(culture =>
        {
            // Refresh UI when culture changes
            RefreshLocalizedStrings();
        });
    }

    private void RefreshLocalizedStrings()
    {
        // Re-fetch localized strings
        Title = _localizationService.GetString(StringResources.UiCommon, "Title");
    }

    public void Dispose()
    {
        _cultureSubscription?.Dispose();
    }
}
```

## Adding New Strings

### Step 1: Identify the Appropriate Resource File

Choose the resource file that best matches the string's purpose:

- UI elements → UI.*.resx
- Errors → Errors.*.resx
- Success messages → Messages.Success.resx
- Confirmations → Messages.Confirmations.resx
- Tooltips → Tooltips.resx

### Step 2: Add the Resource Entry

Add a new `<data>` entry to the `.resx` file:

```xml
<data name="YourKey" xml:space="preserve">
  <value>Your localized string here</value>
  <comment>Description for translators explaining context and parameters</comment>
</data>
```

### Step 3: Use in Code

```csharp
var text = _localizationService.GetString(
    StringResources.UiCommon,
    "YourKey");
```

## Best Practices for Developers

### DO

1. **Use descriptive key names** - `Button.SaveSettings` not `Btn1`
2. **Group related strings** - All buttons together, all errors together
3. **Add translator comments** - Explain context and parameters
4. **Use format parameters** - `"Hello {0}"` instead of string concatenation
5. **Keep format strings intact** - `"{0} of {1} items"` not `$"{current} of {total} items"`
6. **Use appropriate resource sets** - Don't put all strings in one file

### DON'T

1. **Don't extract debug/log messages** - Keep developer messages in English
2. **Don't extract exception messages** - Technical errors stay in English
3. **Don't extract technical identifiers** - Variable names, file paths, etc.
4. **Don't hardcode strings in UI** - Always use localization service
5. **Don't concatenate translated strings** - Use format strings instead

## Best Practices for Translators

### Context is Important

Always read the `<comment>` element to understand:

- Where the string is used
- What parameters represent
- Any character limits or constraints

### Format Strings

**Preserve placeholders** - Don't translate `{0}`, `{1}`, etc.

```xml
<!-- English -->
<value>Found {0} items in {1} seconds</value>

<!-- French - CORRECT -->
<value>Trouvé {0} éléments en {1} secondes</value>

<!-- French - WRONG (missing placeholders) -->
<value>Trouvé des éléments en secondes</value>
```

### Preserve Special Characters

- Keep `...` (ellipsis) in status messages
- Preserve `:` (colons) in labels
- Maintain capitalization patterns for buttons (Title Case vs Sentence case)

### UI Constraints

- Button labels should be concise
- Tooltips can be more descriptive
- Error messages should be clear and actionable

## Pluralization Guidelines

For counts, provide context in comments:

```xml
<data name="ItemCount" xml:space="preserve">
  <value>{0} item(s)</value>
  <comment>Count of items. For languages with complex plural rules, consider creating separate keys for singular/plural/other</comment>
</data>
```

For languages with complex plural rules (e.g., Russian, Arabic), consider creating separate keys:

- `ItemCount.Zero`
- `ItemCount.One`
- `ItemCount.Two`
- `ItemCount.Few`
- `ItemCount.Many`

## Testing Localization

Run the integration tests to verify resources are properly loaded:

```bash
dotnet test GenHub.Tests/GenHub.Tests.Core --filter StringResourcesTests
```

Key tests verify:

- All .resx files are embedded
- ResourceManagers can load from each namespace
- Strings are retrievable through LocalizationService
- Format strings work with parameters
- Satellite assemblies are generated

## Satellite Assembly Generation

Satellite assemblies are automatically generated during build. They are located in:

```
GenHub.Core/bin/Debug/net8.0/{culture}/GenHub.Core.resources.dll
```

For example, for French translations:

```
GenHub.Core/bin/Debug/net8.0/fr/GenHub.Core.resources.dll
```

## Adding a New Language (Phase 8-9)

1. Create language-specific `.resx` files:
   - `UI.Common.fr.resx` (French)
   - `UI.Navigation.fr.resx`
   - etc.

2. Translate all strings while preserving format placeholders

3. Build the project to generate satellite assemblies

4. Test with:

```csharp
await _localizationService.SetCulture("fr");
```

## Current Phase Status

**Phase 2 Complete** ✅

- 10 .resx files created with 278 English strings
- Resource configuration in GenHub.Core.csproj
- StringResources helper class
- LocalizationService updated with resource namespace support
- Integration tests for resource loading
- Documentation complete

**Phase 2.5 Complete** ✅ (String Audit)

- Comprehensive audit of all ViewModels and XAML views
- 2 new .resx files created (UI.Tools, UI.Downloads)
- 159 additional strings added across all resource files
- StringResources.cs updated with new resource namespaces

**Next Phase: Phase 3** - ViewModel Integration

- Update ViewModels to use LocalizationService
- Replace hardcoded strings with resource keys
- Implement reactive UI updates on culture change

## Resource Statistics

Total resource files: **12**
Total English strings: **437**

By resource file:

| Resource File | String Count |
|---------------|-------------|
| UI.Common.resx | 47 |
| UI.Navigation.resx | 9 |
| UI.GameProfiles.resx | 77 |
| UI.Settings.resx | 66 |
| UI.Updates.resx | 28 |
| UI.Tools.resx | 32 |
| UI.Downloads.resx | 18 |
| Errors.Validation.resx | 22 |
| Errors.Operations.resx | 43 |
| Messages.Success.resx | 29 |
| Messages.Confirmations.resx | 28 |
| Tooltips.resx | 38 |

By category:

- UI resources: ~277 strings
- Error messages: ~65 strings
- Success messages: ~29 strings
- Confirmations: ~28 strings
- Tooltips: ~38 strings

## Additional Resources

- [.NET Globalization and Localization](https://docs.microsoft.com/en-us/dotnet/core/extensions/globalization-and-localization)
- [Resource Files (.resx)](https://docs.microsoft.com/en-us/dotnet/framework/resources/creating-resource-files-for-desktop-apps)
- [Satellite Assemblies](https://docs.microsoft.com/en-us/dotnet/framework/resources/creating-satellite-assemblies-for-desktop-apps)
