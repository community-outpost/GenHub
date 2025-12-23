# GenHub Localization System Architecture Design

## Table of Contents

1. [Technology Evaluation](#1-technology-evaluation)
2. [System Architecture Design](#2-system-architecture-design)
3. [Avalonia Integration Strategy](#3-avalonia-integration-strategy)
4. [Developer Experience](#4-developer-experience)
5. [Translator Experience](#5-translator-experience)
6. [Implementation Phases](#6-implementation-phases)
7. [Technical Specifications](#7-technical-specifications)
8. [Testing Strategy](#8-testing-strategy)
9. [Open Questions and Future Considerations](#9-open-questions-and-future-considerations)

---

## 1. Technology Evaluation

### 1.1 Selected Approach: .NET Resource Files (.resx)

**Overview**: Traditional .NET localization using ResX files compiled into satellite assemblies.

**Strengths**:

- ✅ **Officially recommended** by Avalonia documentation
- ✅ **Native .NET framework** support with ResourceManager
- ✅ **Strong tooling** in Visual Studio, Rider, and ResXManager
- ✅ **Design-time XAML IntelliSense** for resource keys
- ✅ **Type-safe generated classes** (e.g., `Resources.UI_Common_Save`)
- ✅ **Built-in parameter substitution** via `string.Format`
- ✅ **Satellite assemblies** for clean deployment and optional language packs
- ✅ **Professional translation workflows** (XLIFF export/import)
- ✅ **Cultural formatting** (dates, numbers, currency) automatic

**Challenges and Mitigations**:

| Challenge | Mitigation |
|-----------|------------|
| XML-based format for translators | Provide ResXManager GUI tool (cross-platform) |
| Runtime language switching complexity | Custom `LocalizationService` wrapping `ResourceManager` |
| Version control noise | Git attributes for .resx files, focus PRs on satellite assemblies |
| Community contribution barrier | Detailed guide, ResXManager, validation tools |
| Linux tooling | ResXManager runs on Linux via .NET, provide CLI alternative |

**Decision**: The benefits of .resx (native support, tooling, type safety) outweigh the challenges, especially with proper mitigation strategies.

### 1.2 Alternative: JSON-Based Localization

**Why Not JSON**: While JSON is more accessible to non-technical users, .resx provides:

- Native Avalonia support (less custom code)
- Better tooling ecosystem
- Industry-standard translation workflows
- Type-safe resource access
- Compiled resources (slightly better performance)

**Verdict**: .resx is the better fit for a production-quality application, despite JSON's simplicity advantage.

### 1.3 Alternative: XAML Resource Dictionaries

**Why Not**: XAML dictionaries don't scale well, lack parameter substitution, and have poor validation.

**Verdict**: Not suitable for comprehensive localization.

---

## 2. System Architecture Design

### 2.1 Service Layer Architecture

#### 2.1.1 Core Interfaces

```csharp
namespace GenHub.Core.Interfaces.Localization;

/// <summary>
/// Provides localization services for retrieving translated strings with runtime language switching.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the currently active culture.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Gets an observable that emits whenever the language/culture changes.
    /// Subscribe to this for reactive UI updates.
    /// </summary>
    IObservable<CultureInfo> CultureChanged { get; }

    /// <summary>
    /// Gets all available cultures (languages) in the application.
    /// </summary>
    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    /// <summary>
    /// Changes the active culture/language.
    /// </summary>
    /// <param name="culture">The culture to activate.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    OperationResult<bool> SetCulture(CultureInfo culture);

    /// <summary>
    /// Changes the active culture by language code (e.g., "en", "de").
    /// </summary>
    /// <param name="languageCode">The language code.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    OperationResult<bool> SetLanguage(string languageCode);

    /// <summary>
    /// Gets a localized string from the specified resource set.
    /// </summary>
    /// <param name="resourceSet">The resource set name (e.g., "UI.Common").</param>
    /// <param name="key">The resource key (e.g., "Save").</param>
    /// <returns>The localized string, or the key if not found.</returns>
    string GetString(string resourceSet, string key);

    /// <summary>
    /// Gets a localized string with parameter substitution.
    /// </summary>
    /// <param name="resourceSet">The resource set name.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Arguments for string.Format.</param>
    /// <returns>The formatted localized string.</returns>
    string GetString(string resourceSet, string key, params object[] args);

    /// <summary>
    /// Tries to get a localized string, returning success status.
    /// </summary>
    bool TryGetString(string resourceSet, string key, out string value);
}

/// <summary>
/// Represents information about an available language.
/// </summary>
public class LanguageInfo
{
    /// <summary>Gets the culture info.</summary>
    public required CultureInfo Culture { get; init; }

    /// <summary>Gets the language code (e.g., "en", "de").</summary>
    public string Code => Culture.TwoLetterISOLanguageName;

    /// <summary>Gets the native language name (e.g., "English", "Deutsch").</summary>
    public string NativeName => Culture.NativeName;

    /// <summary>Gets the English language name (e.g., "English", "German").</summary>
    public string EnglishName => Culture.EnglishName;

    /// <summary>Gets whether this is the default fallback language.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Gets whether a satellite assembly exists for this culture.</summary>
    public bool HasSatelliteAssembly { get; init; }
}
```

#### 2.1.2 Resource Manager Abstraction

```csharp
namespace GenHub.Core.Interfaces.Localization;

/// <summary>
/// Manages resource managers for different resource sets.
/// </summary>
public interface IResourceManagerProvider
{
    /// <summary>
    /// Gets a ResourceManager for the specified resource set.
    /// </summary>
    /// <param name="resourceSet">The resource set name (e.g., "UI.Common").</param>
    /// <returns>The ResourceManager instance.</returns>
    ResourceManager GetResourceManager(string resourceSet);

    /// <summary>
    /// Discovers all available satellite assembly cultures.
    /// </summary>
    /// <returns>List of available cultures.</returns>
    IReadOnlyList<CultureInfo> DiscoverAvailableCultures();

    /// <summary>
    /// Refreshes all resource managers (useful after culture change).
    /// </summary>
    void RefreshResourceManagers();
}
```

#### 2.1.3 Dependency Injection Integration

**Service Lifetimes**:

- `ILocalizationService`: **Singleton** - Single instance for app-wide culture management
- `IResourceManagerProvider`: **Singleton** - Caches ResourceManager instances
- Resource classes (e.g., `Resources`): **Static** - Auto-generated by .NET

**Registration Pattern**:

```csharp
public static class LocalizationModule
{
    public static IServiceCollection AddLocalizationServices(
        this IServiceCollection services,
        IConfigurationProviderService configProvider)
    {
        // Register resource manager provider
        services.AddSingleton<IResourceManagerProvider, ResourceManagerProvider>();

        // Register localization service
        services.AddSingleton<ILocalizationService>(provider =>
        {
            var resourceProvider = provider.GetRequiredService<IResourceManagerProvider>();
            var logger = provider.GetRequiredService<ILogger<LocalizationService>>();
            var userSettings = provider.GetRequiredService<IUserSettingsService>();

            // Get default culture from user settings or app config
            var defaultCulture = GetCultureFromSettings(userSettings, configProvider);

            return new LocalizationService(
                resourceProvider,
                defaultCulture,
                logger);
        });

        return services;
    }

    private static CultureInfo GetCultureFromSettings(
        IUserSettingsService userSettings,
        IConfigurationProviderService configProvider)
    {
        var languageCode = userSettings.Get().PreferredLanguage 
            ?? configProvider.GetDefaultLanguage();

        try
        {
            return CultureInfo.GetCultureInfo(languageCode);
        }
        catch
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }
}
```

### 2.2 Resource Organization

#### 2.2.1 Project Structure

```
GenHub/
├── GenHub.Core/
│   ├── Resources/
│   │   ├── Strings/                    # All .resx files
│   │   │   ├── UI.Common.resx          # English (neutral/default)
│   │   │   ├── UI.Common.de.resx       # German
│   │   │   ├── UI.Common.fr.resx       # French
│   │   │   ├── UI.Common.es.resx       # Spanish
│   │   │   ├── UI.GameProfiles.resx    # English
│   │   │   ├── UI.GameProfiles.de.resx # German
│   │   │   ├── Errors.Validation.resx  # English
│   │   │   ├── Errors.Validation.de.resx
│   │   │   └── ...
│   │   └── Strings.Designer.cs         # Auto-generated (if using custom)
│   └── Localization/
│       └── LocalizationKeys.cs         # Helper class (optional)
```

**Build Configuration** (`GenHub.Core.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Generate satellite resource assemblies for each culture -->
    <SatelliteResourceLanguages>en;de;fr;es</SatelliteResourceLanguages>
  </PropertyGroup>

  <ItemGroup>
    <!-- All .resx files are automatically included and compiled -->
    <EmbeddedResource Update="Resources\Strings\**\*.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <CustomToolNamespace>GenHub.Core.Resources</CustomToolNamespace>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```

#### 2.2.2 Resource File Naming Convention

**Pattern**: `<Namespace>.<Category>.resx`

**Examples**:

- `UI.Common.resx` → Common UI strings (Save, Cancel, etc.)
- `UI.GameProfiles.resx` → Game Profiles view strings
- `UI.Settings.resx` → Settings view strings
- `UI.Navigation.resx` → Navigation tab strings
- `Errors.Validation.resx` → Validation error messages
- `Errors.Launch.resx` → Game launch error messages
- `Messages.Success.resx` → Success notification messages
- `Tooltips.resx` → Tooltip texts

**Culture-Specific Naming**:

- `UI.Common.resx` → English (neutral culture, fallback)
- `UI.Common.de.resx` → German
- `UI.Common.fr.resx` → French
- `UI.Common.es.resx` → Spanish

#### 2.2.3 Resource File Content Structure

**UI.Common.resx** (English - Neutral):

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Save" xml:space="preserve">
    <value>Save</value>
    <comment>Button text for save action</comment>
  </data>
  <data name="Cancel" xml:space="preserve">
    <value>Cancel</value>
    <comment>Button text for cancel action</comment>
  </data>
  <data name="Delete" xml:space="preserve">
    <value>Delete</value>
    <comment>Button text for delete action</comment>
  </data>
  <data name="Edit" xml:space="preserve">
    <value>Edit</value>
    <comment>Button text for edit action</comment>
  </data>
  <data name="Create" xml:space="preserve">
    <value>Create</value>
    <comment>Button text for create action</comment>
  </data>
  <data name="Browse" xml:space="preserve">
    <value>Browse...</value>
    <comment>Button text for file browser</comment>
  </data>
  <data name="Refresh" xml:space="preserve">
    <value>Refresh</value>
    <comment>Button text for refresh action</comment>
  </data>
</root>
```

**UI.GameProfiles.resx** (English):

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Title" xml:space="preserve">
    <value>Game Profiles</value>
    <comment>Title for Game Profiles view</comment>
  </data>
  <data name="NoProfiles" xml:space="preserve">
    <value>No game profiles found. Create one to get started!</value>
    <comment>Message shown when no profiles exist</comment>
  </data>
  <data name="CreateProfile" xml:space="preserve">
    <value>Create Profile</value>
    <comment>Button text to create a new profile</comment>
  </data>
  <data name="LaunchGame" xml:space="preserve">
    <value>Launch Game</value>
    <comment>Button text to launch a game</comment>
  </data>
  <data name="ProfileName" xml:space="preserve">
    <value>Profile Name</value>
    <comment>Label for profile name field</comment>
  </data>
</root>
```

**Errors.Launch.resx** (English with parameters):

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="ProfileNotFound" xml:space="preserve">
    <value>Game profile '{0}' not found</value>
    <comment>Error when profile doesn't exist. Parameter: {0} = profile name</comment>
  </data>
  <data name="WorkspacePreparationFailed" xml:space="preserve">
    <value>Failed to prepare workspace: {0}</value>
    <comment>Error during workspace preparation. Parameter: {0} = error details</comment>
  </data>
  <data name="ProcessStartFailed" xml:space="preserve">
    <value>Failed to start game process</value>
    <comment>Error when game process won't start</comment>
  </data>
</root>
```

#### 2.2.4 Auto-Generated Resource Classes

**.NET automatically generates** strongly-typed resource classes:

**Conceptual output** (actual is in satellite assemblies):

```csharp
namespace GenHub.Core.Resources
{
    internal class UI_Common
    {
        private static ResourceManager resourceMan;
        private static CultureInfo resourceCulture;

        internal static ResourceManager ResourceManager 
        {
            get 
            {
                if (resourceMan == null)
                {
                    resourceMan = new ResourceManager(
                        "GenHub.Core.Resources.Strings.UI.Common", 
                        typeof(UI_Common).Assembly);
                }
                return resourceMan;
            }
        }

        internal static string Save 
        {
            get { return ResourceManager.GetString("Save", resourceCulture); }
        }

        internal static string Cancel 
        {
            get { return ResourceManager.GetString("Cancel", resourceCulture); }
        }
        
        // ... other properties
    }
}
```

### 2.3 Fallback Mechanism

#### 2.3.1 Native .NET Resource Fallback

**.NET provides built-in fallback**:

1. **Requested Culture** (e.g., `de-DE`)
2. **Neutral Culture** (e.g., `de`)
3. **Neutral Resource** (e.g., English from `UI.Common.resx`)

**Example**:

- User selects German (`de-DE`)
- ResourceManager looks for `UI.Common.de-DE.resx` (doesn't exist)
- Falls back to `UI.Common.de.resx` (exists, uses this)
- If `de.resx` is missing a key, falls back to `UI.Common.resx` (English)

**No custom fallback logic needed** - .NET handles it automatically!

#### 2.3.2 Missing Translation Handling

**Development Mode**:

```csharp
public class LocalizationService : ILocalizationService
{
    public string GetString(string resourceSet, string key)
    {
        var resourceManager = _resourceProvider.GetResourceManager(resourceSet);
        
        try
        {
            var value = resourceManager.GetString(key, CultureInfo.CurrentUICulture);
            
            if (string.IsNullOrEmpty(value))
            {
                _logger.LogWarning(
                    "Missing resource: {ResourceSet}.{Key} in culture {Culture}",
                    resourceSet, key, CultureInfo.CurrentUICulture.Name);
                
                return $"[{resourceSet}.{key}]"; // Visual indicator
            }
            
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting resource: {ResourceSet}.{Key}", 
                resourceSet, key);
            
            return $"[{resourceSet}.{key}]";
        }
    }
}
```

### 2.4 Dynamic Language Discovery

#### 2.4.1 Satellite Assembly Discovery

**Automatic Discovery** via assembly scanning:

```csharp
public class ResourceManagerProvider : IResourceManagerProvider
{
    private readonly Dictionary<string, ResourceManager> _resourceManagers = new();
    private IReadOnlyList<CultureInfo>? _availableCultures;

    public IReadOnlyList<CultureInfo> DiscoverAvailableCultures()
    {
        if (_availableCultures != null)
            return _availableCultures;

        var cultures = new List<CultureInfo>();
        var assembly = typeof(ResourceManagerProvider).Assembly;

        // Get all satellite assemblies
        var satelliteCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Where(culture => 
            {
                try
                {
                    // Check if satellite assembly exists
                    var satelliteAssembly = assembly.GetSatelliteAssembly(culture);
                    return satelliteAssembly != null;
                }
                catch
                {
                    return false;
                }
            })
            .ToList();

        // Add neutral/invariant culture (English)
        cultures.Add(CultureInfo.InvariantCulture);
        cultures.Add(CultureInfo.GetCultureInfo("en-US"));
        
        // Add discovered satellite cultures
        cultures.AddRange(satelliteCultures);

        _availableCultures = cultures
            .DistinctBy(c => c.TwoLetterISOLanguageName)
            .OrderBy(c => c.EnglishName)
            .ToList();

        return _availableCultures;
    }

    public ResourceManager GetResourceManager(string resourceSet)
    {
        if (_resourceManagers.TryGetValue(resourceSet, out var manager))
            return manager;

        // Create ResourceManager for this set
        var baseName = $"GenHub.Core.Resources.Strings.{resourceSet}";
        var assembly = typeof(ResourceManagerProvider).Assembly;

        manager = new ResourceManager(baseName, assembly);
        _resourceManagers[resourceSet] = manager;

        return manager;
    }
}
```

#### 2.4.2 Language Metadata

**Create `LanguageInfo` from discovered cultures**:

```csharp
public IReadOnlyList<LanguageInfo> AvailableLanguages
{
    get
    {
        var cultures = _resourceProvider.DiscoverAvailableCultures();
        
        return cultures.Select(culture => new LanguageInfo
        {
            Culture = culture,
            IsDefault = culture.TwoLetterISOLanguageName == "en",
            HasSatelliteAssembly = IsSatelliteAssemblyAvailable(culture)
        }).ToList();
    }
}

private bool IsSatelliteAssemblyAvailable(CultureInfo culture)
{
    try
    {
        var assembly = typeof(LocalizationService).Assembly;
        var satellite = assembly.GetSatelliteAssembly(culture);
        return satellite != null;
    }
    catch
    {
        return false;
    }
}
```

### 2.5 Runtime Language Switching

#### 2.5.1 Culture Switching Strategy

**Thread-Wide Culture Change**:

```csharp
public class LocalizationService : ILocalizationService
{
    private readonly Subject<CultureInfo> _cultureChangedSubject = new();
    private CultureInfo _currentCulture;

    public CultureInfo CurrentCulture => _currentCulture;

    public IObservable<CultureInfo> CultureChanged => 
        _cultureChangedSubject.AsObservable();

    public OperationResult<bool> SetCulture(CultureInfo culture)
    {
        try
        {
            // Validate culture is available
            if (!IsLanguageAvailable(culture))
            {
                return OperationResult<bool>.CreateFailure(
                    $"Culture '{culture.Name}' is not available");
            }

            // Set thread cultures
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            _currentCulture = culture;

            // Refresh all resource managers to pick up new culture
            _resourceProvider.RefreshResourceManagers();

            // Notify subscribers on UI thread
            RxApp.MainThreadScheduler.Schedule(() =>
                _cultureChangedSubject.OnNext(culture));

            _logger.LogInformation(
                "Culture changed to: {Culture}", culture.Name);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.CreateFailure(
                $"Culture switch failed: {ex.Message}");
        }
    }

    public OperationResult<bool> SetLanguage(string languageCode)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageCode);
            return SetCulture(culture);
        }
        catch (CultureNotFoundException ex)
        {
            return OperationResult<bool>.CreateFailure(
                $"Invalid language code: {languageCode}");
        }
    }

    private bool IsLanguageAvailable(CultureInfo culture)
    {
        return AvailableLanguages.Any(l => 
            l.Culture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName);
    }
}
```

#### 2.5.2 Resource Manager Refresh

```csharp
public class ResourceManagerProvider : IResourceManagerProvider
{
    public void RefreshResourceManagers()
    {
        // ResourceManager automatically uses current thread culture
        // No explicit refresh needed, but we clear cache to be safe
        foreach (var manager in _resourceManagers.Values)
        {
            manager.ReleaseAllResources();
        }
    }
}
```

#### 2.5.3 ViewModel Integration Pattern

**Reactive Property Updates**:

```csharp
public partial class GameProfilesViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly IResourceManagerProvider _resourceProvider;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _createButtonText;

    [ObservableProperty]
    private string _noProfilesMessage;

    public GameProfilesViewModel(ILocalizationService localization)
    {
        _localization = localization;

        // Initialize with current culture
        UpdateLocalizedStrings();

        // Subscribe to culture changes
        _localization.CultureChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateLocalizedStrings());
    }

    private void UpdateLocalizedStrings()
    {
        Title = _localization.GetString("UI.GameProfiles", "Title");
        CreateButtonText = _localization.GetString("UI.GameProfiles", "CreateProfile");
        NoProfilesMessage = _localization.GetString("UI.GameProfiles", "NoProfiles");
    }
}
```

---

## 3. Avalonia Integration Strategy

### 3.1 XAML Binding with Resource Extensions

#### 3.1.1 Static Resource Binding (Simple Approach)

**Using x:Static** with generated resource classes:

```xaml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:res="clr-namespace:GenHub.Core.Resources;assembly=GenHub.Core"
             x:Class="GenHub.Features.GameProfiles.Views.GameProfilesView">

    <!-- Direct binding to static resource properties -->
    <StackPanel>
        <TextBlock Text="{x:Static res:UI_GameProfiles.Title}" 
                   FontSize="24" />
        
        <Button Content="{x:Static res:UI_Common.Create}"
                Command="{Binding CreateCommand}" />
        
        <Button Content="{x:Static res:UI_Common.Save}"
                Command="{Binding SaveCommand}" />
    </StackPanel>
</UserControl>
```

**Limitation**: Static bindings don't update on culture change. Need reactive approach.

#### 3.1.2 ViewModel Property Binding (Recommended for Dynamic)

**For runtime language switching**, expose properties:

```xaml
<UserControl xmlns:vm="using:GenHub.Features.GameProfiles.ViewModels"
             x:DataType="vm:GameProfilesViewModel">

    <StackPanel>
        <!-- Binds to ViewModel properties that update on culture change -->
        <TextBlock Text="{Binding Title}" 
                   FontSize="24" />
        
        <Button Content="{Binding CreateButtonText}"
                Command="{Binding CreateCommand}" />
        
        <TextBlock Text="{Binding NoProfilesMessage}"
                   IsVisible="{Binding !HasProfiles}" />
    </StackPanel>
</UserControl>
```

#### 3.1.3 Custom Markup Extension (Advanced)

**Dynamic resource binding** with custom extension:

```csharp
namespace GenHub.Common.MarkupExtensions;

/// <summary>
/// Markup extension for dynamic localized resources that update on culture change.
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string ResourceSet { get; set; }
    public string Key { get; set; }

    public LocalizeExtension(string resourceSet, string key)
    {
        ResourceSet = resourceSet;
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var localization = AppLocator.GetServiceOrDefault<ILocalizationService>();
        
        if (localization == null)
            return $"[{ResourceSet}.{Key}]";

        // Create reactive binding that updates on culture change
        var binding = localization.CultureChanged
            .Select(_ => localization.GetString(ResourceSet, Key))
            .StartWith(localization.GetString(ResourceSet, Key));

        return binding.ToBinding();
    }
}
```

**Usage**:

```xaml
<UserControl xmlns:ext="using:GenHub.Common.MarkupExtensions">
    <Button Content="{ext:Localize ResourceSet=UI.Common, Key=Save}" />
</UserControl>
```

### 3.2 Design-Time Preview Support

**Generated resource classes work in design-time**:

```xaml
<UserControl xmlns:res="clr-namespace:GenHub.Core.Resources;assembly=GenHub.Core"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             d:DesignWidth="800" d:DesignHeight="600">

    <!-- IntelliSense works for resource keys -->
    <TextBlock Text="{x:Static res:UI_Common.Save}" />
</UserControl>
```

**ViewModel design-time support**:

```csharp
public class DesignTimeGameProfilesViewModel : GameProfilesViewModel
{
    public DesignTimeGameProfilesViewModel() 
        : base(new DesignTimeLocalizationService())
    {
        // Populated with design-time data
    }
}
```

### 3.3 Parameter Substitution in XAML

**ViewModel handles formatting**:

```csharp
public string DownloadProgressText
{
    get
    {
        var template = _localization.GetString("Messages.Info", "Downloading");
        return string.Format(template, CurrentFileName, DownloadPercentage);
    }
}

// Alternative using service directly
public string GetProfileNotFoundError(string profileName)
{
    return _localization.GetString("Errors.Launch", "ProfileNotFound", profileName);
}
```

```xaml
<TextBlock Text="{Binding DownloadProgressText}" />
```

---

## 4. Developer Experience

### 4.1 Adding New Translatable Strings

**Step-by-Step Workflow**:

1. **Open appropriate .resx file** in Visual Studio/Rider (e.g., `UI.GameProfiles.resx`)

2. **Add new entry** in Resource Editor:
   - Name: `NewFeatureTitle`
   - Value: `New Feature`
   - Comment: `Title for new feature section`

3. **Build project** to generate updated resource class

4. **Use in ViewModel**:

```csharp
private void UpdateLocalizedStrings()
{
    NewFeatureTitle = _localization.GetString("UI.GameProfiles", "NewFeatureTitle");
}
```

5. **Bind in XAML**:

```xaml
<TextBlock Text="{Binding NewFeatureTitle}" />
```

### 4.2 IntelliSense and Compile-Time Checking

**Resource Editor** provides:

- ✅ IntelliSense for resource keys
- ✅ Compile-time checking (missing keys cause build errors)
- ✅ Refactoring support (rename keys safely)
- ✅ Preview of all translations side-by-side

**Type-Safe Access**:

```csharp
// Using generated class directly (type-safe)
var saveText = UI_Common.Save;

// Using service (flexible, supports runtime switching)
var saveText = _localization.GetString("UI.Common", "Save");
```

### 4.3 Visual Studio Resource Editor

**Features**:

- Side-by-side view of all cultures
- Easy copy from neutral to culture-specific
- Search and filter
- Validation (missing translations highlighted)
- String freeze warnings

### 4.4 Migration Strategy for Existing Hardcoded Strings

**Phase 1: Extraction Tool**

```csharp
// PowerShell script to find hardcoded strings
Get-ChildItem -Path . -Filter *.cs -Recurse | 
    Select-String -Pattern '"[A-Z][a-z].*"' | 
    Where-Object { $_.Line -notmatch "namespace|using|//" } |
    Export-Csv hardcoded-strings.csv
```

**Phase 2: Create .resx Entries**

- Review CSV
- Add entries to appropriate .resx files
- Categorize by feature area

**Phase 3: Replace in Code**

- Replace literals with `_localization.GetString()` calls
- Update ViewModels to expose localized properties
- Test each replacement

---

## 5. Translator Experience

### 5.1 ResXManager - The Translator's Tool

**ResXManager** is a free, open-source tool for editing .resx files:

**Features**:

- ✅ Grid view of all resource files and cultures
- ✅ Side-by-side comparison
- ✅ Missing translation highlighting
- ✅ Inline editing
- ✅ Export to Excel for external translation
- ✅ Import from Excel
- ✅ Visual Studio extension + standalone app
- ✅ Cross-platform (.NET-based)

**Installation**:

```bash
# Visual Studio Extension
Install from VS Marketplace: "ResXManager"

# Standalone Application
dotnet tool install -g ResXResourceManager
```

**Usage Workflow**:

1. Open GenHub solution in ResXManager
2. See all resource files in grid view
3. Filter to see untranslated entries
4. Edit inline or export to Excel
5. Save changes (updates .resx files)
6. Commit to git

### 5.2 Community Contribution Workflow

**Simplified Workflow for Contributors**:

1. **Fork GenHub repository**

2. **Install ResXManager**:

   ```bash
   dotnet tool install -g ResXResourceManager
   ```

3. **Open project in ResXManager**:

   ```bash
   resxmanager GenHub/GenHub.sln
   ```

4. **Select language to translate**:
   - Choose from dropdown (e.g., "German - de")
   - See all missing translations highlighted in red

5. **Translate**:
   - Edit cells directly in grid
   - Copy English text as reference
   - Save frequently

6. **Export for external translation** (optional):
   - File → Export to Excel
   - Send to translator
   - File → Import from Excel when done

7. **Test locally**:

   ```bash
   dotnet build
   dotnet run --project GenHub/GenHub.Windows
   # Change language in Settings to verify
   ```

8. **Submit Pull Request**:
   - Only changed `.de.resx` files (or your language)
   - PR description: "Add German translation"
   - CI validates completeness

### 5.3 Translation Validation

**Automated Validation** in CI:

```csharp
public class ResourceValidator
{
    public ValidationResult ValidateTranslations()
    {
        var issues = new List<ValidationIssue>();
        
        // Get all neutral .resx files
        var neutralResources = Directory.GetFiles(
            "Resources/Strings", "*.resx", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f, @"\.[a-z]{2}\.resx$"))
            .ToList();

        foreach (var neutralFile in neutralResources)
        {
            var neutralKeys = GetResourceKeys(neutralFile);
            
            // Find all culture-specific files
            var baseName = Path.GetFileNameWithoutExtension(neutralFile);
            var directory = Path.GetDirectoryName(neutralFile);
            var cultureFiles = Directory.GetFiles(directory!, $"{baseName}.*.resx");

            foreach (var cultureFile in cultureFiles)
            {
                var cultureKeys = GetResourceKeys(cultureFile);
                var culture = GetCultureFromFileName(cultureFile);

                // Check for missing keys
                var missingKeys = neutralKeys.Except(cultureKeys).ToList();
                if (missingKeys.Any())
                {
                    issues.Add(new ValidationIssue(
                        $"Missing {missingKeys.Count} translations in {culture}",
                        ValidationSeverity.Warning,
                        cultureFile));
                }

                // Check for extra keys (not in neutral)
                var extraKeys = cultureKeys.Except(neutralKeys).ToList();
                if (extraKeys.Any())
                {
                    issues.Add(new ValidationIssue(
                        $"Extra {extraKeys.Count} keys in {culture} (not in neutral)",
                        ValidationSeverity.Info,
                        cultureFile));
                }
            }
        }

        return new ValidationResult(issues);
    }

    private HashSet<string> GetResourceKeys(string resxFile)
    {
        using var reader = new ResXResourceReader(resxFile);
        return reader.Cast<DictionaryEntry>()
            .Select(e => e.Key.ToString())
            .ToHashSet();
    }
}
```

### 5.4 Translation Guide Documentation

**Create `docs/TRANSLATION_GUIDE.md`**:

```markdown
# GenHub Translation Guide

## Getting Started

### Prerequisites
- Git and GitHub account
- .NET 8 SDK
- ResXManager tool

### Setup
1. Fork the GenHub repository
2. Clone your fork locally
3. Install ResXManager:
   ```bash
   dotnet tool install -g ResXResourceManager
   ```

## Creating a New Translation

### Step 1: Check if language exists

Look in `GenHub/GenHub.Core/Resources/Strings/` for your language code.

### Step 2: Open in ResXManager

```bash
cd GenHub
resxmanager GenHub.sln
```

### Step 3: Add your language

- Click "Add Culture" button
- Select your language (e.g., "de - German")
- All resource files get `.de.resx` versions

### Step 4: Translate

- Red cells = missing translation
- Click cell to edit
- English text is in first column for reference
- Save frequently (Ctrl+S)

### Step 5: Test

```bash
dotnet build
dotnet run --project GenHub/GenHub.Windows
```

- Go to Settings → Language
- Select your language
- Verify all text displays correctly

### Step 6: Submit

- Commit only `.XX.resx` files (your language)
- Push to your fork
- Open Pull Request
- Title: "Add [Language] translation"

## Tips

### Context

- Read comments in English resources
- Check the application to see where text appears
- Ask in PR if unsure about context

### Parameters

- Keep `{0}`, `{1}` placeholders in same positions
- Example: `"Profile '{0}' created"` → `"Profil '{0}' erstellt"`

### Special Characters

- Preserve `&` for access keys: `"&Save"` → `"&Speichern"`
- Keep `...` (ellipsis) in button text
- Maintain line breaks `\n` if present

### Consistency

- Use ResXManager's search to find how terms were translated before
- Be consistent with terminology (e.g., "Profile" always translates the same)

## Questions?

Open an issue or ask in Discord!

---

## 6. Implementation Phases

### Phase 1: Core Infrastructure (PR #1)

**Objective**: Establish .resx-based localization service layer.

**Deliverables**:
1. Create `ILocalizationService` interface
2. Create `IResourceManagerProvider` interface
3. Implement `LocalizationService` class
4. Implement `ResourceManagerProvider` class
5. Create `LanguageInfo` model
6. Add `LocalizationModule` for DI registration
7. Update `UserSettings` to include `PreferredLanguage`
8. Unit tests for core service logic

**Dependencies**: None

**Files Changed**:
- `GenHub.Core/Interfaces/Localization/ILocalizationService.cs` (new)
- `GenHub.Core/Interfaces/Localization/IResourceManagerProvider.cs` (new)
- `GenHub.Core/Services/Localization/LocalizationService.cs` (new)
- `GenHub.Core/Services/Localization/ResourceManagerProvider.cs` (new)
- `GenHub.Core/Models/Localization/LanguageInfo.cs` (new)
- `GenHub/GenHub/Infrastructure/DependencyInjection/LocalizationModule.cs` (new)
- `GenHub.Core/Models/Common/UserSettings.cs` (modified)
- `GenHub.Tests.Core/Localization/LocalizationServiceTests.cs` (new)

**Testing**:
- Unit tests for service methods
- Mock ResourceManager for testing
- Test culture switching logic
- Test satellite assembly discovery

**Acceptance Criteria**:
- ✅ Can discover available cultures
- ✅ Can switch cultures programmatically
- ✅ Culture change triggers observable event
- ✅ All unit tests pass

**Estimated Effort**: 2-3 days

---

### Phase 2: Resource Files Structure (PR #2)

**Objective**: Create .resx file structure and English resources.

**Deliverables**:
1. Create resource file organization in `GenHub.Core/Resources/Strings/`
2. Create `UI.Common.resx` with common UI strings
3. Create `UI.Navigation.resx` with navigation strings
4. Create `UI.GameProfiles.resx` with Game Profiles strings
5. Create `UI.Settings.resx` with Settings strings
6. Create `UI.Downloads.resx` with Downloads strings
7. Create `Errors.Validation.resx` with validation errors
8. Create `Errors.Launch.resx` with launch errors
9. Create `Messages.Success.resx` with success messages
10. Configure project to build satellite assemblies
11. Extract existing hardcoded strings to resources

**Dependencies**: Phase 1

**Files Changed**:
- `GenHub.Core/Resources/Strings/UI.Common.resx` (new)
- `GenHub.Core/Resources/Strings/UI.Navigation.resx` (new)
- `GenHub.Core/Resources/Strings/UI.GameProfiles.resx` (new)
- `GenHub.Core/Resources/Strings/UI.Settings.resx` (new)
- `GenHub.Core/Resources/Strings/Errors.Validation.resx` (new)
- `GenHub.Core/Resources/Strings/Errors.Launch.resx` (new)
- `GenHub.Core/Resources/Strings/Messages.Success.resx` (new)
- `GenHub.Core/GenHub.Core.csproj` (modified - add resource configuration)

**Testing**:
- Verify resources compile correctly
- Test resource access via ResourceManager
- Validate all keys are accessible

**Acceptance Criteria**:
- ✅ All .resx files created with English text
- ✅ Project builds satellite assemblies
- ✅ Resources accessible via ResourceManager
- ✅ All existing UI strings have resource entries

**Estimated Effort**: 3-4 days (includes string extraction)

---

### Phase 3: ViewModel Integration (PR #3)

**Objective**: Integrate localization service with ViewModels.

**Deliverables**:
1. Update `ViewModelBase` with localization helper methods
2. Update `MainViewModel` to use localized strings
3. Update `GameProfilesViewModel` to use localized strings
4. Update `SettingsViewModel` to use localized strings
5. Update `DownloadsViewModel` to use localized strings
6. Implement reactive string updates on culture change
7. Add design-time localization service

**Dependencies**: Phase 2

**Files Changed**:
- `GenHub/GenHub/Common/ViewModels/ViewModelBase.cs` (modified)
- `GenHub/GenHub/Common/ViewModels/MainViewModel.cs` (modified)
- `GenHub/GenHub/Features/GameProfiles/ViewModels/GameProfilesViewModel.cs` (modified)
- `GenHub/GenHub/Features/Settings/ViewModels/SettingsViewModel.cs` (modified)
- `GenHub/GenHub/Features/Downloads/ViewModels/DownloadsViewModel.cs` (modified)

**Testing**:
- Manual testing of each ViewModel
- Verify strings display correctly
- Test reactive updates (not yet functional, but structure in place)

**Acceptance Criteria**:
- ✅ All ViewModels use `ILocalizationService`
- ✅ Localized properties exposed for binding
- ✅ Reactive subscription pattern implemented
- ✅ Design-time services work

**Estimated Effort**: 3-4 days

---

### Phase 4: XAML Binding (PR #4)

**Objective**: Update XAML views to use localized bindings.

**Deliverables**:
1. Update all XAML files to bind to ViewModel localized properties
2. Remove all hardcoded strings from XAML
3. Implement `LocalizeExtension` markup extension (optional)
4. Test design-time preview in all views

**Dependencies**: Phase 3

**Files Changed**:
- `GenHub/GenHub/Common/Views/MainWindow.axaml` (modified)
- `GenHub/GenHub/Features/GameProfiles/Views/*.axaml` (modified)
- `GenHub/GenHub/Features/Settings/Views/*.axaml` (modified)
- `GenHub/GenHub/Features/Downloads/Views/*.axaml` (modified)
- `GenHub/GenHub/Common/MarkupExtensions/LocalizeExtension.cs` (new, optional)

**Testing**:
- Visual testing of all views
- Verify no hardcoded strings in XAML
- Test design-time preview
- Verify compiled bindings work

**Acceptance Criteria**:
- ✅ All XAML uses bindings (no hardcoded text)
- ✅ Design-time preview works
- ✅ All views render correctly
- ✅ Compiled bindings functional

**Estimated Effort**: 2-3 days

---

### Phase 5: Settings UI for Language Selection (PR #5)

**Objective**: Add language selector to Settings.

**Deliverables**:
1. Add language selection ComboBox to Settings view
2. Implement language change command in SettingsViewModel
3. Display language metadata (native name, completion %)
4. Persist language selection to UserSettings
5. Apply selected language on app startup

**Dependencies**: Phase 4

**Files Changed**:
- `GenHub/GenHub/Features/Settings/ViewModels/SettingsViewModel.cs` (modified)
- `GenHub/GenHub/Features/Settings/Views/SettingsView.axaml` (modified)
- `GenHub/GenHub/App.axaml.cs` (modified - load language on startup)

**Testing**:
- Manual testing of language switching
- Verify all UI updates immediately
- Test persistence across restarts
- Test invalid language handling

**Acceptance Criteria**:
- ✅ User can select language from Settings
- ✅ All UI updates on language change
- ✅ Selection persists across restarts
- ✅ Language metadata displays correctly

**Estimated Effort**: 2 days

---

### Phase 6: German Translation (PR #6)

**Objective**: Add complete German translation.

**Deliverables**:
1. Create all `.de.resx` files
2. Translate all strings to German
3. Add validation tests for German completeness
4. Update CI to validate translations
5. Test German language in application

**Dependencies**: Phase 5

**Files Changed**:
- `GenHub.Core/Resources/Strings/UI.Common.de.resx` (new)
- `GenHub.Core/Resources/Strings/UI.Navigation.de.resx` (new)
- `GenHub.Core/Resources/Strings/UI.GameProfiles.de.resx` (new)
- `GenHub.Core/Resources/Strings/UI.Settings.de.resx` (new)
- `GenHub.Core/Resources/Strings/Errors.Validation.de.resx` (new)
- `GenHub.Core/Resources/Strings/Errors.Launch.de.resx` (new)
- All other resource files with `.de.resx` versions

**Testing**:
- Native speaker review (if available)
- Automated completeness validation
- Visual testing with German selected
- Test parameter substitution

**Acceptance Criteria**:
- ✅ All resource files have German versions
- ✅ 100% translation completeness
- ✅ No parameter mismatches
- ✅ Visual review passed

**Estimated Effort**: 3-4 days (with native speaker help)

---

### Phase 7: Additional Languages (PR #7)

**Objective**: Add French and Spanish translations.

**Deliverables**:
1. Create all `.fr.resx` files (French)
2. Create all `.es.resx` files (Spanish)
3. Validate translations
4. Update documentation for community contributions

**Dependencies**: Phase 6

**Files Changed**:
- `GenHub.Core/Resources/Strings/*.fr.resx` (new)
- `GenHub.Core/Resources/Strings/*.es.resx` (new)
- `docs/TRANSLATION_GUIDE.md` (new)
- `.github/workflows/validate-translations.yml` (new)

**Testing**:
- Automated validation
- Visual testing
- Community review

**Acceptance Criteria**:
- ✅ French and Spanish translations available
- ✅ CI validates translations
- ✅ Translation guide published
- ✅ Community can contribute

**Estimated Effort**: 4-5 days (assumes community help or translation service)

---

### Phase 8: Documentation (PR #8)

**Objective**: Complete all documentation.

**Deliverables**:
1. Update architecture documentation
2. Create developer localization guide
3. Update coding style guide
4. Add code examples
5. Create troubleshooting guide

**Dependencies**: Phase 7

**Files Changed**:
- `docs/architecture/localization-system-design.md` (this document)
- `docs/dev/localization.md` (new)
- `docs/TRANSLATION_GUIDE.md` (enhanced)
- `CONTRIBUTING.md` (updated)

**Acceptance Criteria**:
- ✅ Comprehensive documentation exists
- ✅ Examples are clear and work
- ✅ Contributors can follow guides

**Estimated Effort**: 2 days

---

## 7. Technical Specifications

### 7.1 Resource Key Naming Conventions

**Format**: Use descriptive PascalCase names within each resource file.

**Examples**:
```

UI.Common.resx:

- Save
- Cancel
- Delete
- BrowseButton (if needed to distinguish from Browse command)

UI.GameProfiles.resx:

- Title
- NoProfiles
- CreateProfile
- LaunchGame
- ProfileCount

Errors.Validation.resx:

- Required
- InvalidPath
- ProfileNameExists

```

### 7.2 Parameter Substitution

**Standard `string.Format` syntax**:

```xml
<!-- In .resx file -->
<data name="ProfileNotFound">
  <value>Game profile '{0}' not found</value>
  <comment>Parameter: {0} = profile name</comment>
</data>

<data name="Downloading">
  <value>Downloading {0}... ({1}%)</value>
  <comment>Parameters: {0} = filename, {1} = percentage</comment>
</data>
```

**Usage**:

```csharp
var error = _localization.GetString("Errors.Launch", "ProfileNotFound", profileName);
var progress = _localization.GetString("Messages.Info", "Downloading", fileName, percentage);
```

### 7.3 Pluralization

**Separate keys approach**:

```xml
<data name="ProfileCountNone">
  <value>No profiles</value>
</data>
<data name="ProfileCountOne">
  <value>1 profile</value>
</data>
<data name="ProfileCountMany">
  <value>{0} profiles</value>
</data>
```

**Helper method**:

```csharp
public string GetPlural(string resourceSet, string keyBase, int count, params object[] args)
{
    var key = count switch
    {
        0 => $"{keyBase}None",
        1 => $"{keyBase}One",
        _ => $"{keyBase}Many"
    };
    
    return GetString(resourceSet, key, args);
}
```

### 7.4 Culture/Locale Handling

**Culture Format**:

- Use specific cultures where possible: `en-US`, `de-DE`, `fr-FR`
- Fallback to neutral cultures: `en`, `de`, `fr`
- Thread culture set globally on language change

### 7.5 Thread Safety

**Service is thread-safe**:

- Culture change uses lock
- ResourceManager is thread-safe by design
- Observable notifications marshalled to UI thread

### 7.6 Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| `GetString()` | < 1 µs | ResourceManager dictionary lookup |
| `GetString()` with formatting | < 10 µs | Includes `string.Format` |
| Culture switch | < 100 ms | Includes ResourceManager refresh |
| UI update after switch | < 300 ms | ViewModels update properties |
| Satellite assembly load | < 50 ms | Lazy loaded on demand |

---

## 8. Testing Strategy

### 8.1 Unit Tests

**LocalizationServiceTests.cs**:

```csharp
public class LocalizationServiceTests
{
    [Fact]
    public void GetString_ValidKey_ReturnsTranslation()
    {
        var service = CreateService();
        var result = service.GetString("UI.Common", "Save");
        Assert.Equal("Save", result);
    }

    [Fact]
    public void SetCulture_ValidCulture_UpdatesCurrentCulture()
    {
        var service = CreateService();
        var germanCulture = CultureInfo.GetCultureInfo("de-DE");
        
        var result = service.SetCulture(germanCulture);
        
        Assert.True(result.Success);
        Assert.Equal("de", service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void CultureChanged_Observable_EmitsOnSwitch()
    {
        var service = CreateService();
        CultureInfo? emittedCulture = null;
        service.CultureChanged.Subscribe(c => emittedCulture = c);

        service.SetLanguage("de");

        Assert.NotNull(emittedCulture);
        Assert.Equal("de", emittedCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void GetString_WithParameters_FormatsCorrectly()
    {
        var service = CreateService();
        var result = service.GetString("Errors.Launch", "ProfileNotFound", "TestProfile");
        Assert.Contains("TestProfile", result);
    }
}
```

### 8.2 Integration Tests

**Translation Completeness Tests**:

```csharp
[Theory]
[InlineData("de-DE")]
[InlineData("fr-FR")]
[InlineData("es-ES")]
public void CultureResources_HaveAllRequiredKeys(string cultureName)
{
    var culture = CultureInfo.GetCultureInfo(cultureName);
    var validator = new ResourceValidator();
    
    var result = validator.ValidateCulture(culture);
    
    Assert.True(result.IsValid, 
        $"Missing keys in {cultureName}: {string.Join(", ", result.MissingKeys)}");
}
```

### 8.3 CI/CD Validation

**GitHub Actions** (`.github/workflows/validate-translations.yml`):

```yaml
name: Validate Translations

on:
  pull_request:
    paths:
      - 'GenHub.Core/Resources/Strings/**/*.resx'

jobs:
  validate:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Install ResXManager CLI
        run: dotnet tool install -g ResXResourceManager
      
      - name: Build Project
        run: dotnet build GenHub/GenHub.Core/GenHub.Core.csproj
      
      - name: Run Translation Tests
        run: dotnet test GenHub/GenHub.Tests.Core --filter Category=Translation
      
      - name: Check Translation Completeness
        run: |
          # Custom script to validate all cultures have all keys
          pwsh -File scripts/validate-translations.ps1
```

---

## 9. Open Questions and Future Considerations

### 9.1 ResX vs ResJSON Tool

**Question**: Should we create a custom tool to convert between .resx and JSON for easier community contribution?

**Recommendation**: Start with ResXManager. If community requests simpler workflow, build converter tool in Phase 9.

### 9.2 Right-to-Left (RTL) Languages

**Question**: When to add RTL support (Arabic, Hebrew)?

**Recommendation**: Design is RTL-ready (Avalonia supports it). Add when community contributes RTL translations.

### 9.3 Automated Translation

**Question**: Use automated translation (e.g., Azure Translator) for initial drafts?

**Recommendation**: Yes for bootstrapping new languages, but always require native speaker review before merging.

### 9.4 XLIFF Export/Import

**Question**: Support XLIFF format for professional translation services?

**Recommendation**: ResXManager supports XLIFF. Document the workflow in Phase 7-8.

### 9.5 Localization for Content Metadata

**Question**: How to handle user-generated content (mod descriptions, etc.)?

**Recommendation**: Separate system. App localization covers UI only. Content metadata stays in source language.

---

## Conclusion

This .resx-based localization architecture provides a **production-ready, officially-supported solution** for GenHub that:

1. **Follows Avalonia Best Practices**: Uses recommended .resx approach
2. **Provides Excellent Tooling**: Visual Studio, Rider, and ResXManager support
3. **Enables Runtime Switching**: Custom service layer provides dynamic culture changes
4. **Maintains Type Safety**: Strongly-typed resource classes
5. **Supports Community**: Clear workflow with ResXManager tool
6. **Scales Gracefully**: Satellite assemblies, lazy loading, efficient lookups

**Key Advantages Over JSON**:

- ✅ Native .NET framework integration
- ✅ Industry-standard translation workflows
- ✅ Better IDE support
- ✅ Compiled resources (type safety + performance)

**Mitigations for .resx Concerns**:

- 📦 ResXManager provides excellent UX for translators
- 🔄 Custom service enables runtime switching
- 📝 Clear documentation lowers contribution barrier
- ✅ Automated validation ensures quality

**Next Steps**:

1. Review and approve this architecture
2. Create GitHub issues for each phase
3. Begin Phase 1 implementation
4. Install ResXManager and familiarize team

---
