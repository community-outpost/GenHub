# Code Review Comments

reviewed, 2 comments

Edit Code Review Agent Settings | Greptile

GenHub/GenHub/Features/Tools/ViewModels/PublisherStudioViewModel.cs
Comment on lines +54 to +56
    public bool IsSetupComplete =>
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Id) &&
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Name);
@greptile-apps
greptile-apps bot
3 days ago
potential null reference on CurrentProject

Suggested change
    public bool IsSetupComplete =>
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Id) &&
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Name);
    public bool IsSetupComplete =>
        CurrentProject != null &&
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Id) &&
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Name);
Prompt To Fix With AI
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/DropboxHostingProvider.cs
    public DropboxHostingProvider(ILogger<DropboxHostingProvider> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
@greptile-apps
greptile-apps bot
3 days ago
HttpClient created manually without disposal mechanism - implement IDisposable or use IHttpClientFactory

Prompt To Fix With AI
@undead2146    Reply...
coderabbitai[bot]
coderabbitai bot requested changes 2 days ago
coderabbitai bot
left a comment
Actionable comments posted: 144

Caution

Some comments are outside the diff and can’t be posted inline due to platform limitations.

⚠️ Outside diff range comments (19)
GenHub/GenHub/Features/Content/Services/ContentResolvers/AODMapsResolver.cs (3)
44-89: ⚠️ Potential issue | 🟡 Minor

Remove [TEMP] debug logging before merging.

Multiple debug logging statements are explicitly marked as temporary (lines 44-48, 64, 76-89). These should be removed or converted to Debug/Trace level logging before merging to avoid polluting production logs.

Particularly, the loop at lines 85-88 that enumerates all sections could generate excessive log output.

🧹 Suggested cleanup
149-149: ⚠️ Potential issue | 🟡 Minor

Default .zip extension won't apply for extensionless URLs.

Path.GetExtension returns an empty string "" (not null) when the path has no extension. The null-coalescing operator ?? only handles null, so URLs without extensions will result in an empty FileType instead of the intended .zip default.

🐛 Proposed fix
151-151: 🧹 Nitpick | 🔵 Trivial

Unnecessary null-conditional operator.

The item parameter is non-nullable (ContentSearchResult item), so item?.SourceUrl is redundant. Use item.SourceUrl directly.

♻️ Proposed fix
GenHub/GenHub.Tests/GenHub.Tests.Core/Common/Services/ConfigurationProviderServiceTests.cs (2)
707-721: ⚠️ Potential issue | 🟡 Minor

Test case name doesn’t match input scenario (null vs empty list).
Line 709 sets ContentDirectories to new() (empty list), so this no longer tests the “null user setting” path described by the test name/summary. If the intent is to cover the null branch, set it to null; if the intent is to cover empty list behavior, rename the test and add a separate null test.

✅ Suggested fix (restore null case)
748-763: ⚠️ Potential issue | 🟡 Minor

Test case name doesn’t match input scenario (null vs empty list).
Line 752 initializes GitHubDiscoveryRepositories with new() (empty list), which doesn’t exercise the “null user setting” branch described by the test. Either switch to null or rename/add a separate test for empty lists.

✅ Suggested fix (restore null case)
GenHub/GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineDiscoverer.cs (2)
46-49: 🧹 Nitpick | 🔵 Trivial

Consider removing the unused static Dispose method.

This static method does nothing and doesn't implement IDisposable. If resource cleanup was intended, it should be an instance method implementing the interface. Since IHttpClientFactory manages HttpClient lifetime externally, no disposal is needed here—consider removing this method to reduce confusion.

199-199: 🧹 Nitpick | 🔵 Trivial

Consider using JSON serialization instead of string interpolation.

If version contains characters like quotes or backslashes, the resulting JSON will be malformed. While version strings are typically simple, using a JSON serializer would be more robust.

Example using System.Text.Json
GenHub/GenHub/Features/Content/Services/Publishers/CNCLabsManifestFactory.cs (2)
55-65: 🧹 Nitpick | 🔵 Trivial

Consider caching SlugHelper instance and logging exceptions.

Two minor improvements:

SlugHelper is instantiated on every call. Since it's stateless, consider creating a static instance to avoid repeated allocations.
The empty catch block silently swallows exceptions. Consider logging at debug/trace level for diagnosability.
♻️ Proposed refactor
106-109: 🧹 Nitpick | 🔵 Trivial

Consider logging URI parsing failures.

The empty catch block silently swallows URI parsing errors. While the fallback behavior is correct, consider logging at debug level for diagnosability when download URLs have unexpected formats.

GenHub/GenHub.Tests/GenHub.Tests.Core/Features/Manifest/ContentManifestBuilderTests.cs (1)
46-52: ⚠️ Potential issue | 🟡 Minor

Orphan XML documentation comment for non-existent field.

Lines 46-48 contain an XML summary comment for "Mock for the playwright service," but there is no corresponding field declaration. This creates a dangling documentation block that doesn't document anything and may cause confusion or XML documentation warnings.

🧹 Proposed fix: remove the orphan comment
GenHub/GenHub/Features/Content/Services/GitHub/GitHubArtifactResolver.cs (3)
95-103: ⚠️ Potential issue | 🟡 Minor

Validate that the resolved download URL is not null or empty.

If both ArchiveDownloadUrl and DownloadUrl are null or empty, downloadUrl will be invalid, potentially causing downstream failures or creating a manifest with an unusable file entry.

🛡️ Proposed fix to add URL validation
142-150: ⚠️ Potential issue | 🟡 Minor

Same issue: Validate the resolved download URL before use.

This is the same pattern as the fallback path. Apply the same validation to prevent invalid manifest entries.

🛡️ Proposed fix
62-152: 🧹 Nitpick | 🔵 Trivial

Consider extracting common manifest-building logic to reduce duplication.

The fallback path (lines 66-106) and normal path (lines 118-152) share significant structural overlap: both create a content name, instantiate a manifest builder, chain identical With* methods, resolve a download URL, and add a remote file. Extracting shared logic into a helper method would improve maintainability and reduce the risk of the two paths diverging unintentionally.

♻️ Sketch of possible refactor
GenHub/GenHub/Features/Content/Services/ContentResolvers/CNCLabsMapResolver.cs (1)
231-243: 🧹 Nitpick | 🔵 Trivial

Use CNCLabsConstants.PublisherWebsite consistently.

Lines 234 and 240-242 hardcode "<https://www.cnclabs.com"> while Line 201 correctly uses CNCLabsConstants.PublisherWebsite. This inconsistency could cause issues if the base URL changes.

♻️ Proposed fix
GenHub/GenHub/Features/Content/Services/GitHub/GitHubReleasesDiscoverer.cs (1)
87-92: ⚠️ Potential issue | 🟡 Minor

Avoid hardcoding the SuperHackers logo for all GitHub releases.
This discoverer is generic; a fixed publisher logo will misbrand other repos. Consider a GitHub default logo or repo/publisher-specific mapping.

GenHub/GenHub/Features/GameProfiles/ViewModels/GameSettingsViewModel.cs (1)
1022-1097: ⚠️ Potential issue | 🟠 Major

Missing TSH field mapping breaks settings.json round‑trip.
Line 1024 onward: ApplyGameSettings only maps GO fields, but CreateGameSettings (Line 1079+) writes TSH fields. That means TSH values loaded from settings.json are ignored and will be overwritten on save, causing silent data loss.

✅ Suggested fix (map TSH fields in ApplyGameSettings)
GenHub/GenHub/Features/Launching/GameLauncher.cs (1)
1087-1131: ⚠️ Potential issue | 🟠 Major

Guard against overwriting settings.json when profile lacks custom settings.
Line 1113: ApplyGameSettingsAsync always writes a new GameSettings object built from profile defaults. For legacy profiles where GO/TSH fields are null, this can overwrite existing settings.json values with defaults on every launch.

🛡️ Suggested minimal guard
GenHub/GenHub.Core/Interfaces/GameSettings/IGameSettingsService.cs (1)
56-67: ⚠️ Potential issue | 🟡 Minor

Update XML docs to reflect GameSettings terminology.
The summaries still say “GeneralsOnline-specific” even though the API is now GameSettings-based.

✍️ Suggested doc fix
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/CNCLabsMapDiscoverer.cs (1)
GenHub/GenHub.Core/Constants/PublisherDocumentation.cs
Comment on lines +14 to +15
    /// `<summary>`Title for hosting setup section.</summary>
    public const string HostingSetupTitle = "Hosting Setup";
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Inconsistent XML documentation on members.

HostingSetupTitle and HostingSetupDescription have XML doc summaries, but HostingSetupHelp and all other constants do not. Since SA1600 is suppressed, consider either removing the existing summaries for consistency or adding summaries to all constants.

Also applies to: 25-25

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Constants/RegistryConstants.cs
Comment on lines +19 to +22
    /// The registry key for Intel Graphics Driver (Class GUID).
    /// </summary>
    public const string IntelGfxDriverKey = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E968-E325-11CE-BFC1-08002BE10318}\0000";

@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Avoid hardcoding the Intel class subkey instance (0000).
The 0000 instance is not stable across machines/drivers, so this path can fail. Prefer storing the class GUID root and enumerate subkeys (0000, 0001, …) at runtime.

🔧 Suggested adjustment
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Models/Publishers/HostingState.cs
    /// `<summary>`
    /// When the project was last published.
    /// </summary>
    public DateTime LastPublished { get; set; }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Use timezone-safe/nullable timestamps for persisted state.
Persisted DateTime without an explicit offset can serialize ambiguously, and the default 0001-01-01 can look like a real value. Consider DateTimeOffset? (UTC) or nullable DateTime to represent “never published/updated.”

🕒 Suggested adjustment
Also applies to: 66-66

🤖 Prompt for AI Agents
@undead2146    Reply...
...b/GenHub.Tests/GenHub.Tests.Core/Features/GameProfiles/ViewModels/DownloadsViewModelTests.cs
Comment on lines 29 to +32
        var discoverer = new GitHubTopicsDiscoverer(
            new Mock<IGitHubApiClient>().Object,
            new Mock<ILogger<GitHubTopicsDiscoverer>>().Object);
            new Mock<ILogger<GitHubTopicsDiscoverer>>().Object,
            new Mock<IMemoryCache>().Object);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Mock for IMemoryCache requires setup to avoid NullReferenceException.

GetOrCreateAsync is an extension method that internally calls CreateEntry() on the IMemoryCache interface. A plain mock without setup returns null for CreateEntry(), which will cause a NullReferenceException when the extension method tries to configure the cache entry.

Consider using a real MemoryCache instance or setting up the mock properly:

Proposed fix: Use a real MemoryCache instance
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Assets/Styles/ThemeResources.axaml
    <StreamGeometry x:Key="AddIcon">M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z</StreamGeometry>
    <StreamGeometry x:Key="SaveIcon">M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z</StreamGeometry>
    <StreamGeometry x:Key="PublishIcon">M5,4V6H19V4H5M5,14H9V20H15V14H19L12,7L5,14Z</StreamGeometry>
    <StreamGeometry x:Key="CatalogIcon">M17,17H7V15H17M14,10H7V12H14M7,7H17V5H7M3,3V21H21V3</StreamGeometry>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

CatalogIcon path appears incomplete.

The path M3,3V21H21V3 does not close the rectangle shape. This will render as three lines instead of a closed border. The path should likely end with H3Z or just Z to close the shape properly.

🔧 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Assets/Styles/ThemeResources.axaml
Comment on lines +95 to +103
    <SolidColorBrush x:Key="PSDeepPurpleBrush" Color="{StaticResource PSDeepPurple}"/>
    <SolidColorBrush x:Key="PSNavyBlueBrush" Color="{StaticResource PSNavyBlue}"/>
    <SolidColorBrush x:Key="PSAccentPurpleBrush" Color="{StaticResource PSAccentPurple}"/>
    <SolidColorBrush x:Key="PSAccentNavyBrush" Color="{StaticResource PSAccentNavy}"/>
    <SolidColorBrush x:Key="PSGlassBackgroundBrush" Color="{StaticResource PSGlassBackground}"/>
    <SolidColorBrush x:Key="PSGlassBorderBrush" Color="{StaticResource PSGlassBorder}"/>
    <SolidColorBrush x:Key="PSSuccessBrush" Color="{StaticResource PSSuccessGreen}"/>
    <SolidColorBrush x:Key="PSWarningBrush" Color="{StaticResource PSWarningOrange}"/>
    <SolidColorBrush x:Key="PSErrorBrush" Color="{StaticResource PSErrorRed}"/>
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Missing PSAccentCyanBrush for consistency.

PSAccentPurple and PSAccentNavy both have corresponding brushes, but PSAccentCyan (defined at Line 87) does not. Consider adding PSAccentCyanBrush to maintain consistency and avoid requiring consumers to create their own brush.

💡 Proposed addition
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Assets/Styles/ThemeResources.axaml
Comment on lines +105 to +124
    <!-- Publisher Studio Gradients -->
    <LinearGradientBrush x:Key="PSPrimaryGradient" StartPoint="0%,0%" EndPoint="100%,100%">
        <GradientStop Color="#8B5CF6" Offset="0"/>
        <GradientStop Color="#3B82F6" Offset="1"/>
    </LinearGradientBrush>

    <LinearGradientBrush x:Key="PSNavyGradient" StartPoint="0%,0%" EndPoint="100%,100%">
        <GradientStop Color="#1E3A5F" Offset="0"/>
        <GradientStop Color="#0F172A" Offset="1"/>
    </LinearGradientBrush>

    <LinearGradientBrush x:Key="PSGlassGradient" StartPoint="0%,0%" EndPoint="0%,100%">
        <GradientStop Color="#20FFFFFF" Offset="0"/>
        <GradientStop Color="#08FFFFFF" Offset="1"/>
    </LinearGradientBrush>

    <LinearGradientBrush x:Key="PSSuccessGradient" StartPoint="0%,0%" EndPoint="100%,100%">
        <GradientStop Color="#22C55E" Offset="0"/>
        <GradientStop Color="#16A34A" Offset="1"/>
    </LinearGradientBrush>
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider using StaticResource for gradient colors.

The gradient brushes use hardcoded hex values that duplicate colors already defined as resources (e.g., #8B5CF6 = PSAccentPurple, #3B82F6 = PSAccentNavy, #22C55E = PSSuccessGreen). Referencing the color resources improves maintainability—if a color changes, you only update it in one place.

💡 Example refactor for PSPrimaryGradient
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Assets/Styles/ThemeResources.axaml
Comment on lines +126 to +133
    <!-- Publisher Studio Icons -->
    <StreamGeometry x:Key="PersonIcon">M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z</StreamGeometry>
    <StreamGeometry x:Key="LinkIcon">M3.9,12C3.9,10.29 5.29,8.9 7,8.9H11V7H7A5,5 0 0,0 2,12A5,5 0 0,0 7,17H11V15.1H7C5.29,15.1 3.9,13.71 3.9,12M8,13H16V11H8V13M17,7H13V8.9H17C18.71,8.9 20.1,10.29 20.1,12C20.1,13.71 18.71,15.1 17,15.1H13V17H17A5,5 0 0,0 22,12A5,5 0 0,0 17,7Z</StreamGeometry>
    <StreamGeometry x:Key="DownloadIcon">M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z</StreamGeometry>
    <StreamGeometry x:Key="UploadIcon">M9,16V10H5L12,3L19,10H15V16H9M5,20V18H19V20H5Z</StreamGeometry>
    <StreamGeometry x:Key="RefreshIcon">M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z</StreamGeometry>
    <StreamGeometry x:Key="ShareIcon">M18,16.08C17.24,16.08 16.56,16.38 16.04,16.85L8.91,12.7C8.96,12.47 9,12.24 9,12C9,11.76 8.96,11.53 8.91,11.3L15.96,7.19C16.5,7.69 17.21,8 18,8A3,3 0 0,0 21,5A3,3 0 0,0 18,2A3,3 0 0,0 15,5C15,5.24 15.04,5.47 15.09,5.7L8.04,9.81C7.5,9.31 6.79,9 6,9A3,3 0 0,0 3,12A3,3 0 0,0 6,15C6.79,15 7.5,14.69 8.04,14.19L15.16,18.34C15.11,18.55 15.08,18.77 15.08,19C15.08,20.61 16.39,21.91 18,21.91C19.61,21.91 20.92,20.61 20.92,19A2.92,2.92 0 0,0 18,16.08Z</StreamGeometry>
    <StreamGeometry x:Key="SettingsIcon">M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z</StreamGeometry>
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Duplicate section comment may cause confusion.

The comment "Publisher Studio Icons" appears at both Line 67 and Line 126. Consider differentiating them (e.g., "Publisher Studio - Action Icons" vs "Publisher Studio - Navigation Icons") or consolidating all PS icons into a single section for easier maintenance.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/FileSystemDiscoverer.cs
Comment on lines +67 to +68
        // [TEMP] DEBUG: FileSystemDiscoverer constructor
        _logger.LogInformation("[TEMP] FileSystemDiscoverer constructor called");
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Remove or downgrade the temporary constructor log.
Info-level “[TEMP]” logging is noisy for production and suggests debug-only tracing. Prefer LogDebug (or remove it before release).

Proposed change
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/FileSystemDiscoverer.cs
Comment on lines +91 to +96
        // [TEMP] DEBUG: DiscoverAsync entry point
        _logger.LogInformation(
            "[TEMP] FileSystemDiscoverer.DiscoverAsync called - SearchTerm: '{Search}', ContentType: {ContentType}, TargetGame: {TargetGame}",
            query.SearchTerm,
            query.ContentType,
            query.TargetGame);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Avoid logging raw search terms at Information level.
SearchTerm can include user-entered or sensitive data; logging it at Information risks privacy/compliance issues. Use LogDebug and avoid raw values (e.g., log length or presence).

Proposed change
Also applies to: 161-161

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/FileSystemDiscoverer.cs
                    TargetGame = manifest.TargetGame,
                    ProviderName = SourceName,
                    AuthorName = manifest.Publisher?.Name ?? GameClientConstants.UnknownVersion,
                    AuthorName = manifest.Publisher?.Name ?? "Unknown",
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Avoid hard-coded “Unknown” for author fallback.
Consider using a shared constant or localized resource to keep UI text consistent and translatable across the app.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentResolvers/CNCLabsMapResolver.cs
Comment on lines +47 to +51
        // [TEMP] DEBUG: ResolveAsync entry point
        logger.LogInformation(
            "[TEMP] CNCLabsMapResolver.ResolveAsync called - Item: {Name}, SourceUrl: {Url}",
            discoveredItem?.Name,
            discoveredItem?.SourceUrl);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Remove temporary debug logging before merge.

This code is explicitly marked as [TEMP] and logs at Information level, which is too noisy for production. Debug statements like this should be removed or downgraded to Trace/Debug level.

🧹 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentResolvers/CNCLabsMapResolver.cs
Comment on lines 100 to 104
            if (!mapId.HasValue)
            {
                logger.LogWarning("Invalid or missing map ID in resolver metadata for {Url}", discoveredItem.SourceUrl);
                return OperationResult<ContentManifest>.CreateFailure("Invalid map ID in resolver metadata");
                 logger.LogWarning("Invalid or missing map ID in resolver metadata for {Url}", discoveredItem.SourceUrl);
                 return OperationResult<ContentManifest>.CreateFailure("Invalid map ID in resolver metadata");
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Logic error: Resolution fails unnecessarily when mapId is missing but download URL was parsed.

The current flow:

Parse mapDetails from HTML (may include valid DownloadUrl)
Use mapId fallback only if DownloadUrl is empty
Fail if DownloadUrl is still empty
Fail if mapId is missing ← This blocks valid resolutions
If the HTML parsing at Line 83 successfully extracts a DownloadUrl, resolution should succeed regardless of whether mapId exists in metadata. The mapId check should only gate the fallback path, not the entire resolution.

🐛 Proposed fix
If mapId is actually required by downstream logic (e.g., the manifest factory), move this validation earlier—before parsing—to fail fast.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineDiscoverer.cs
Comment on lines 122 to 126
            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = list,
                TotalItems = list.Count,
                HasMoreItems = false,
                Items = results,
                HasMoreItems = false, // API returns all items at once
            });
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Deferred execution risk: materialize the enumerable before returning.

The results variable is a lazy IEnumerable (especially after the Where filter on lines 117-119). Assigning it directly to Items means:

Multiple enumerations by the caller will re-execute the filtering logic each time.
Any exception thrown during enumeration will occur outside the try-catch block, bypassing the error handling.
Materialize the results to avoid these issues.

Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GitHub/GitHubArtifactResolver.cs
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GitHub;
namespace GenHub.Features.Content.Services.ContentResolvers;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

🧩 Analysis chain
Move GitHubArtifactResolver.cs to the ContentResolvers directory and register it in DI.

The namespace has been updated to GenHub.Features.Content.Services.ContentResolvers, but the file remains in the GitHub directory. This creates a mismatch with the established pattern where other resolvers (e.g., LocalManifestResolver, AODMapsResolver) are located in the ContentResolvers directory. Additionally, GitHubArtifactResolver is not registered in ContentPipelineModule.AddGitHubPipeline(), unlike other resolvers, leaving the class unused in the application.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GitHub/GitHubReleasesDiscoverer.cs
Comment on lines +63 to +70
                var releases = (await gitHubClient.GetReleasesAsync(owner, repo, cancellationToken))?.ToList();

                if (releases != null)
                {
                    foreach (var release in releases)
                    // Apply pagination: Skip and Take
                    var paginatedReleases = releases.Skip(query.Skip).Take(query.Take);

                    foreach (var release in paginatedReleases)
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Apply search filtering before pagination to avoid missing matches.

Skip/Take is applied before filtering by SearchTerm, so pages can be empty even when later releases match. Filter first, then paginate.

🛠️ Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GitHub/GitHubReleasesDiscoverer.cs
Comment on lines +116 to +125
        // Calculate if there are more items available
        var hasMoreItems = results.Count >= query.Take;

        return errors.Count > 0 && results.Count == 0
            ? OperationResult<ContentDiscoveryResult>.CreateFailure(errors)
            : OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                TotalItems = results.Count,
                HasMoreItems = false,
                HasMoreItems = hasMoreItems,
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

HasMoreItems/TotalItems are now inaccurate with pagination.
results.Count is only the page size. With filtering/pagination, TotalItems should reflect total matches and HasMoreItems should compare against Skip + Take.

🛠️ Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GitHub/GitHubResolver.cs
Comment on lines +193 to 209
    /// <param name="repo">The repository name.</param>
    /// `<returns>`Publisher type identifier for factory resolution.</returns>
    private static string DeterminePublisherType(string owner)
    private static string DeterminePublisherType(string owner, string repo)
    {
        // Check for known publishers that have custom manifest factories
        if (owner.Equals("thesuperhackers", StringComparison.OrdinalIgnoreCase))
        {
            return "thesuperhackers";
        }

        // Future: Add more publisher detection logic
        // if (repo.Contains("generalsonline", StringComparison.OrdinalIgnoreCase))
        //     return "generalsonline";

        // Default to generic GitHub publisher
        return "github";
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused repo parameter in DeterminePublisherType.

The method signature was updated to accept repo, but this parameter is never used in the method body. The commented-out code at lines 203-205 suggests future intent, but currently this violates YAGNI and creates a misleading API.

Either implement the repo-based logic now or remove the parameter until it's needed.

Option 1: Remove unused parameter
Option 2: Implement the repo-based logic
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GitHub/GitHubResolver.cs
Comment on lines +370 to +374

    private (ContentType Type, bool IsInferred) InferContentType(string repo, string? releaseName, string? description)
    {
        return GitHubInferenceHelper.InferContentType(repo, releaseName);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Dead code: private method never called with unused parameter.

InferContentType is a private method that is not invoked anywhere within this class. Additionally, the description parameter is completely ignored—the method simply delegates to GitHubInferenceHelper.InferContentType(repo, releaseName).

Remove this dead code or integrate it into the resolution workflow if it's intended to be used.

🗑️ Proposed fix: Remove dead code
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Parsers/AODMapsPageParser.cs
Comment on lines +83 to +102
        // [TEMP] Log original URL
        logger.LogInformation("[TEMP] MakeAbsoluteUrl processing: {Url}", url);

        // Fix for PashaCNC links - they are dead, replace with current domain
        if (url.Contains("pashacnc.com", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Detected dead pashacnc.com link, replacing with aodmaps.com: {Url}", url);
            logger.LogWarning("[TEMP] Detected dead pashacnc.com link, replacing with aodmaps.com: {Url}", url);
            url = url.Replace("pashacnc.com", "aodmaps.com", StringComparison.OrdinalIgnoreCase);
            url = url.Replace("www.pashacnc.com", "aodmaps.com", StringComparison.OrdinalIgnoreCase);
        }

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("[TEMP] Returning absolute URL: {Url}", url);
            return url;
        }

        return $"{AODMapsConstants.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
        var absUrl = $"{AODMapsConstants.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
        logger.LogInformation("[TEMP] Converted relative to absolute: {Url}", absUrl);
        return absUrl;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Downgrade/remove TEMP info-level URL logs to avoid log noise and potential URL leakage.
These logs execute for every URL normalization and can be very noisy at Info/Warn; they may also expose query tokens in URLs. Prefer Debug level (and remove the “[TEMP]” marker) before release.

🔧 Proposed update (reduce verbosity + remove TEMP marker)
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Parsers/AODMapsPageParser.cs
Comment on lines +241 to 246
    public Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);
        return ParseInternal(url, document);
        var document = browsingContext.OpenAsync(req => req.Content(html), cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(ParseInternal(url, document));
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Convert ParseAsync to true async to prevent potential deadlocks and respect cancellation.

The method blocks on BrowsingContext.OpenAsync() using GetAwaiter().GetResult(), which defeats the async contract and can cause deadlocks in UI contexts. This pattern appears in the codebase elsewhere (e.g., PlaywrightService correctly uses await). Change the method to async and properly await the operation:

-    public Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
-    {
-        var browsingContext = BrowsingContext.New(Configuration.Default);
-        var document = browsingContext.OpenAsync(req => req.Content(html), cancellationToken).GetAwaiter().GetResult();
-        return Task.FromResult(ParseInternal(url, document));
-    }
+    public async Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
+    {
+        var browsingContext = BrowsingContext.New(Configuration.Default);
+        var document = await browsingContext
+            .OpenAsync(req => req.Content(html), cancellationToken)
+            .ConfigureAwait(false);
+        return ParseInternal(url, document);
+    }
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Publishers/CNCLabsManifestFactory.cs
Comment on lines +33 to +46
    [GeneratedRegex(@"[^a-z0-9]", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorRegex();

    private static string SlugifyAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return ManifestConstants.UnknownAuthor;
        }

        // Remove all non-alphanumeric characters and convert to lowercase
        var slug = AuthorRegex().Replace(author, string.Empty).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(slug) ? ManifestConstants.UnknownAuthor : slug;
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
SlugifyAuthor method is unused dead code and should be removed.

The SlugifyAuthor() method and AuthorRegex() are defined but never called. The manifest is built using only publisherId, contentName, and releaseDate (line 181), while the author field from the details object is ignored. Remove this method unless author slugification needs to be integrated into the manifest building logic.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Publishers/CNCLabsManifestFactory.cs
Comment on lines 147 to 157
    public async Task<ContentManifest> CreateManifestAsync(
        object details)
        object details,
        CancellationToken cancellationToken = default)
    {
        if (details is not ParsedContentDetails mapDetails)
        if (details is not MapDetails mapDetails)
        {
            throw new ArgumentException($"Details must be of type {nameof(ParsedContentDetails)}", nameof(details));
            throw new ArgumentException($"Details must be of type {nameof(MapDetails)}", nameof(details));
        }

        return await CreateManifestInternalAsync(mapDetails);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

cancellationToken is not propagated to internal async operations.

The cancellationToken parameter is accepted but never passed to CreateManifestInternalAsync or subsequent async calls like AddRemoteFileAsync. This breaks the cancellation contract—callers cannot cancel the operation.

🐛 Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Publishers/CNCLabsManifestFactory.cs
Comment on lines +201 to +211
            _logger.LogInformation(
                "[TEMP] CNCLabsManifestFactory - Adding file: {FileName} from URL: {Url}",
                fileName,
                details.DownloadUrl);

            await builder.AddRemoteFileAsync(
                fileName,
                details.DownloadUrl,
                details.RefererUrl);
                ContentSourceType.ContentAddressable);

            _logger.LogInformation("[TEMP] CNCLabsManifestFactory - File added to manifest with CAS storage");
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Remove or finalize temporary logging statements.

The [TEMP] prefixed log statements indicate development/debug code that should not be merged to production. Either remove these logs or convert them to appropriate log levels (LogDebug or LogTrace) with proper, non-prefixed messages.

🧹 Proposed cleanup
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/GameProfiles/Infrastructure/GameProcessManager.cs
Comment on lines 392 to 398
                await Task.Run(() => process.Kill(entireProcessTree: true), cancellationToken);

                // Wait for the process to actually exit to ensure file handles are released
                // and system state is updated before returning.
                await Task.Run(() => process.WaitForExit(TimeSpan.FromSeconds(5)), cancellationToken);

                _logger.LogInformation("[Terminate] Process {ProcessId} terminated successfully", processId);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Handle WaitForExit timeout before logging success.

process.WaitForExit(TimeSpan.FromSeconds(5)) returns false when the process is still alive, but Line 398 logs success and proceeds to dispose anyway. That can leave the process running while you stop tracking it. Check the return value and log/return failure (or retry/kill) when the timeout elapses.

✅ Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Interfaces/IHostingProvider.cs
Comment on lines +150 to +176
public class HostingUploadResult
{
    /// `<summary>`
    /// Gets or sets the public URL where the file can be accessed.
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// `<summary>`
    /// Gets or sets the direct download URL (may differ from public URL).
    /// </summary>
    public string DirectDownloadUrl { get; set; } = string.Empty;

    /// `<summary>`
    /// Gets or sets the file ID within the hosting provider (for management).
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// `<summary>`
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// `<summary>`
    /// Gets or sets the SHA256 hash of the uploaded file.
    /// </summary>
    public string? Sha256Hash { get; set; }
}
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider adding IEquatable<HostingUploadResult> or override Equals/GetHashCode for result comparison.

If consumers need to compare upload results (e.g., for caching or deduplication), the default reference equality may not suffice. This is optional depending on usage patterns.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/PublisherStudioTool.cs
Comment on lines +33 to +49
    public Control CreateControl()
    {
        _view = new PublisherStudioView();
        // DataContext will be set in OnActivated after ViewModel is resolved
        return _view;
    }

    /// <inheritdoc/>
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _viewModel = serviceProvider.GetRequiredService<PublisherStudioViewModel>();

        if (_view != null)
        {
            // Set DataContext after ViewModel is properly resolved from DI
            _view.DataContext = _viewModel;
        }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Fix lifecycle ordering: OnActivated runs before CreateControl, causing DataContext to never be set.

In ToolsViewModel.cs (lines 362-363), the host calls OnActivated() before CreateControl():

newValue.OnActivated(_serviceProvider);       // First
CurrentToolControl = newValue.CreateControl(); // Then
When OnActivated executes, _view is null (not yet created), so the if (_view != null) check fails and DataContext is never assigned. When CreateControl runs next, _view exists but the code doesn't attempt to set DataContext. Result: DataContext remains unset, breaking ViewModel binding.

Set DataContext in CreateControl when _viewModel already exists:

Fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/DropboxHostingProvider.cs
Comment on lines +26 to +40
public class DropboxHostingProvider : IHostingProvider
{
    private const string DropboxApiUrl = "<https://api.dropboxapi.com/2";>
    private const string DropboxContentUrl = "<https://content.dropboxapi.com/2";>
    private const string PublisherFolderPath = "/GenHub_Publisher";

    private readonly ILogger<DropboxHostingProvider> _logger;
    private readonly HttpClient _httpClient;
    private string? _accessToken;

    public DropboxHostingProvider(ILogger<DropboxHostingProvider> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

HttpClient is never disposed - implement IDisposable.

The HttpClient created in the constructor is never disposed. This is a resource leak that can exhaust socket connections over time. The class should implement IDisposable to properly clean up the HttpClient.

🐛 Proposed fix to implement IDisposable
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/DropboxHostingProvider.cs
        try
        {
            // Verify the token by getting current account info
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Mutating DefaultRequestHeaders on shared HttpClient is not thread-safe.

Setting DefaultRequestHeaders.Authorization modifies the shared HttpClient state. If multiple authentication attempts occur concurrently, or if the same provider instance is used across different auth contexts, this could cause race conditions. Consider passing the authorization header per-request instead.

🔒️ Proposed fix to use per-request headers
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/DropboxHostingProvider.cs
Comment on lines +253 to +263
    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // For Dropbox, updating is the same as uploading with overwrite mode
        return await UploadFileAsync(fileStream, fileName, null, progress, cancellationToken);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

UpdateFileAsync ignores the fileId parameter.

The method accepts fileId but delegates to UploadFileAsync using only fileName. If the intent is to update a specific file by ID, this implementation is incorrect. If updating by path is intentional, the parameter should be removed or the contract clarified.

📝 Proposed fix to clarify behavior
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/DropboxHostingProvider.cs
Comment on lines +265 to +275
    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalogJson));
        var fileName = $"catalog-{publisherId}.json";
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Dispose the MemoryStream in UploadCatalogAsync.

Similar to the Google Drive provider, wrap the MemoryStream in a using statement.

♻️ Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/GitHubHostingProvider.cs
Comment on lines +298 to +302
            var gist = await _client.Gist.Get(fileId);
            if (gist == null)
            {
                return OperationResult<HostingUploadResult>.CreateFailure($"Gist not found: {fileId}");
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Unreachable null check: Gist.Get throws NotFoundException, never returns null.

The Octokit library's Gist.Get method throws NotFoundException when the gist doesn't exist rather than returning null. This null check will never be true.

🐛 Proposed fix to remove unreachable code
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/GitHubHostingProvider.cs
Comment on lines +325 to +327
            // Get the raw URL for the updated file
            var updatedFile = updatedGist.Files[fileName];
            var rawUrl = updatedFile.RawUrl;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Potential KeyNotFoundException when accessing updated gist file.

If the file name in the update doesn't match an existing file in the gist, or if the update fails silently, accessing updatedGist.Files[fileName] could throw. Consider using TryGetValue or validating the file exists.

🛡️ Proposed defensive fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/GoogleDriveHostingProvider.cs
Comment on lines +31 to +44
public class GoogleDriveHostingProvider : IHostingProvider
{
    private const string ApplicationName = "GenHub Publisher Studio";
    private const string PublisherFolderName = "GenHub_Publisher";
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    private readonly ILogger<GoogleDriveHostingProvider> _logger;
    private DriveService? _driveService;
    private string? _publisherFolderId;

    public GoogleDriveHostingProvider(ILogger<GoogleDriveHostingProvider> logger)
    {
        _logger = logger;
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider implementing IDisposable on the provider class.

DriveService implements IDisposable. If the provider instance is abandoned without calling SignOutAsync, the DriveService won't be disposed. Implementing IDisposable ensures proper cleanup.

♻️ Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/GoogleDriveHostingProvider.cs
Comment on lines +300 to +310
    /// <inheritdoc />
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalogJson));
        var fileName = $"catalog-{publisherId}.json";
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Dispose the MemoryStream in UploadCatalogAsync.

While MemoryStream doesn't hold unmanaged resources, wrapping it in a using statement is a good practice for consistency and if the implementation ever changes.

♻️ Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/HostingProviderFactory.cs
Comment on lines +37 to +40
    public IHostingProvider? GetProvider(string providerId)
    {
        return _providers.FirstOrDefault(p => p.ProviderId == providerId);
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider case-insensitive provider ID comparison.

If providerId could come from user input or external configuration, a case-insensitive comparison would be more robust.

♻️ Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/HostingStateManager.cs
Comment on lines +70 to +83
    public string GetStateFilePath(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
        {
            throw new ArgumentException("Project path cannot be empty", nameof(projectPath));
        }

        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            projectDir = ".";
        }

        return Path.Combine(projectDir, StateFileName);
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Handle directory paths defensively.

If callers pass a directory instead of a .genhub-project file path, Path.GetDirectoryName will resolve to the parent directory, which can save state in the wrong location. Consider guarding for directory inputs.

♻️ Suggested tweak
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/HostingStateManager.cs
Comment on lines +94 to +126
    public async Task<OperationResult<HostingState?>> LoadStateAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var stateFilePath = GetStateFilePath(projectPath);

            if (!File.Exists(stateFilePath))
            {
                _logger.LogDebug("No hosting state file found at {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
            var state = JsonSerializer.Deserialize<HostingState>(json, JsonOptions);

            if (state == null)
            {
                _logger.LogWarning("Failed to deserialize hosting state from {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            _logger.LogInformation(
                "Loaded hosting state: Provider={Provider}, Definition={HasDef}, Catalogs={CatalogCount}",
                state.ProviderId,
                state.Definition != null,
                state.Catalogs.Count);

            return OperationResult<HostingState?>.CreateSuccess(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load hosting state from {ProjectPath}", projectPath);
            return OperationResult<HostingState?>.CreateFailure($"Failed to load hosting state: {ex.Message}");
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Handle cancellation and null collections after deserialization.

Cancellation currently surfaces as an error, and state.Catalogs can be null if JSON contains null, leading to a potential NullReferenceException in logging.

✅ Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/HostingStateManager.cs
Comment on lines +131 to +151
    public async Task<OperationResult<bool>> SaveStateAsync(string projectPath, HostingState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var stateFilePath = GetStateFilePath(projectPath);

            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(stateFilePath, json, cancellationToken);

            _logger.LogInformation(
                "Saved hosting state to {Path}: Provider={Provider}, Catalogs={CatalogCount}",
                stateFilePath,
                state.ProviderId,
                state.Catalogs.Count);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save hosting state to {ProjectPath}", projectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save hosting state: {ex.Message}");
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Make writes atomic to avoid corrupting hosting_state.json.

A crash or cancellation during WriteAllTextAsync can leave a partially-written file, breaking subsequent loads. Use a temp file + atomic replace/move and treat cancellation as a normal exit.

🛠️ Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/Hosting/ManualHostingProvider.cs
Comment on lines +142 to +145
        // Accept any valid HTTPS URL
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Comment/code mismatch: comment says "HTTPS" but code also accepts HTTP.

The comment on line 142 states "Accept any valid HTTPS URL" but the code also accepts Uri.UriSchemeHttp. Either update the comment or restrict to HTTPS only if that was the intent.

📝 Proposed fix to align comment with code
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
coderabbitai[bot]
coderabbitai bot requested changes 2 days ago
coderabbitai bot
left a comment
Review continued from previous batch...

GenHub/Directory.Packages.props
Comment on lines +17 to +18
    <PackageVersion Include="Google.Apis.Auth" Version="1.68.0" />
    <PackageVersion Include="Google.Apis.Drive.v3" Version="1.68.0.3605" />
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Update Google API package versions—current selections appear outdated and contain a version that does not exist.

Google.Apis.Drive.v3 version 1.68.0.3605 does not exist in the NuGet registry; nearby versions in the 1.68.0.x range are 1.68.0.3601, 1.68.0.3608, or 1.68.0.3627. Both packages are significantly outdated (5+ versions behind current): latest stable versions are Google.Apis.Auth 1.73.0 (updated Nov 20, 2025) and Google.Apis.Drive.v3 1.73.0.3996 (updated Dec 16, 2025). While no public security advisories were found for the specified versions, upgrading to current releases is recommended.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Constants/ExternalUrls.cs
Comment on lines +11 to +16
    public const string IntelDriverDownload = "<https://www.intel.com/content/www/us/en/download-center/home";>

    /// `<summary>`
    /// Download URL for DirectX 8.1 / 9.0c runtime files used by GenPatcher.
    /// </summary>
    public const string DirectXRuntimeDownload = "<https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Use stable Microsoft Download Center landing page instead of direct download hash for DirectX, and correct the Intel URL.

The DirectX constant references a direct download URL with an embedded hash (download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe), which is brittle and may break if Microsoft reorganizes its download infrastructure. Use the stable landing page instead:

<https://www.microsoft.com/en-us/download/details.aspx?id=8109>
The Intel URL is missing the .html extension. Correct it to:

<https://www.intel.com/content/www/us/en/download-center/home.html>
Store the stable landing page URLs rather than direct download links to ensure the constants remain valid if Microsoft or Intel restructures their download pages.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Helpers/GameSettingsMapper.cs
Comment on lines 160 to +221
    /// `<summary>`
    /// Applies settings from a GameProfile to a GeneralsOnlineSettings object.
    /// Creates a GameSettings object from a GameProfile.
    /// Used by GameLauncher to prepare settings.json for launch.
    /// </summary>
    /// <param name="profile">The GameProfile source.</param>
    /// <param name="settings">The GeneralsOnlineSettings to populate.</param>
    public static void ApplyToGeneralsOnlineSettings(GameProfile profile, GeneralsOnlineSettings settings)
    /// `<returns>`The created GameSettings object.</returns>
    public static GameSettings CreateGameSettingsFromProfile(GameProfile profile)
    {
        // GeneralsOnline settings - use null-coalescing with model defaults
        // This ensures predictable behavior: always set a value, never rely on constructor defaults
        settings.ShowFps = profile.GoShowFps ?? false;
        settings.ShowPing = profile.GoShowPing ?? true;
        settings.ShowPlayerRanks = profile.GoShowPlayerRanks ?? true;
        settings.AutoLogin = profile.GoAutoLogin ?? false;
        settings.RememberUsername = profile.GoRememberUsername ?? true;
        settings.EnableNotifications = profile.GoEnableNotifications ?? true;
        settings.EnableSoundNotifications = profile.GoEnableSoundNotifications ?? true;
        settings.ChatFontSize = profile.GoChatFontSize ?? 12;

        // Camera settings
        settings.CameraMaxHeightOnlyWhenLobbyHost = profile.GoCameraMaxHeightOnlyWhenLobbyHost ?? 310.0f;
        settings.CameraMinHeight = profile.GoCameraMinHeight ?? 310.0f;
        settings.CameraMoveSpeedRatio = profile.GoCameraMoveSpeedRatio ?? 1.5f;

        // Chat settings
        settings.ChatDurationSecondsUntilFadeOut = profile.GoChatDurationSecondsUntilFadeOut ?? 30;

        // Debug settings
        settings.DebugVerboseLogging = profile.GoDebugVerboseLogging ?? false;

        // Render settings
        settings.RenderFpsLimit = profile.GoRenderFpsLimit ?? 144;
        settings.RenderLimitFramerate = profile.GoRenderLimitFramerate ?? true;
        settings.RenderStatsOverlay = profile.GoRenderStatsOverlay ?? true;

        // Social notification settings
        settings.SocialNotificationFriendComesOnlineGameplay = profile.GoSocialNotificationFriendComesOnlineGameplay ?? true;
        settings.SocialNotificationFriendComesOnlineMenus = profile.GoSocialNotificationFriendComesOnlineMenus ?? true;
        settings.SocialNotificationFriendGoesOfflineGameplay = profile.GoSocialNotificationFriendGoesOfflineGameplay ?? true;
        settings.SocialNotificationFriendGoesOfflineMenus = profile.GoSocialNotificationFriendGoesOfflineMenus ?? true;
        settings.SocialNotificationPlayerAcceptsRequestGameplay = profile.GoSocialNotificationPlayerAcceptsRequestGameplay ?? true;
        settings.SocialNotificationPlayerAcceptsRequestMenus = profile.GoSocialNotificationPlayerAcceptsRequestMenus ?? true;
        settings.SocialNotificationPlayerSendsRequestGameplay = profile.GoSocialNotificationPlayerSendsRequestGameplay ?? true;
        settings.SocialNotificationPlayerSendsRequestMenus = profile.GoSocialNotificationPlayerSendsRequestMenus ?? true;

        // TSH settings (that exist in settings.json) - use null-coalescing with defaults
        settings.ArchiveReplays = profile.TshArchiveReplays ?? false;
        settings.MoneyTransactionVolume = profile.TshMoneyTransactionVolume ?? 50;
        settings.ShowMoneyPerMinute = profile.TshShowMoneyPerMinute ?? false;
        settings.PlayerObserverEnabled = profile.TshPlayerObserverEnabled ?? false;
        settings.SystemTimeFontSize = profile.TshSystemTimeFontSize ?? 12;
        settings.NetworkLatencyFontSize = profile.TshNetworkLatencyFontSize ?? 12;
        settings.RenderFpsFontSize = profile.TshRenderFpsFontSize ?? 12;
        settings.ResolutionFontAdjustment = profile.TshResolutionFontAdjustment ?? -100;
        settings.CursorCaptureEnabledInFullscreenGame = profile.TshCursorCaptureEnabledInFullscreenGame ?? false;
        settings.CursorCaptureEnabledInFullscreenMenu = profile.TshCursorCaptureEnabledInFullscreenMenu ?? false;
        settings.CursorCaptureEnabledInWindowedGame = profile.TshCursorCaptureEnabledInWindowedGame ?? false;
        settings.CursorCaptureEnabledInWindowedMenu = profile.TshCursorCaptureEnabledInWindowedMenu ?? false;
        settings.ScreenEdgeScrollEnabledInFullscreenApp = profile.TshScreenEdgeScrollEnabledInFullscreenApp ?? false;
        settings.ScreenEdgeScrollEnabledInWindowedApp = profile.TshScreenEdgeScrollEnabledInWindowedApp ?? false;
        return new GameSettings
        {
            // GeneralsOnline settings - use null-coalescing with model defaults
            ShowFps = profile.GoShowFps ?? false,
            ShowPing = profile.GoShowPing ?? true,
            ShowPlayerRanks = profile.GoShowPlayerRanks ?? true,
            AutoLogin = profile.GoAutoLogin ?? false,
            RememberUsername = profile.GoRememberUsername ?? true,
            EnableNotifications = profile.GoEnableNotifications ?? true,
            EnableSoundNotifications = profile.GoEnableSoundNotifications ?? true,
            ChatFontSize = profile.GoChatFontSize ?? 12,

            // Camera settings
            CameraMaxHeightOnlyWhenLobbyHost = profile.GoCameraMaxHeightOnlyWhenLobbyHost ?? 310.0f,
            CameraMinHeight = profile.GoCameraMinHeight ?? 310.0f,
            CameraMoveSpeedRatio = profile.GoCameraMoveSpeedRatio ?? 1.5f,

            // Chat settings
            ChatDurationSecondsUntilFadeOut = profile.GoChatDurationSecondsUntilFadeOut ?? 30,

            // Debug settings
            DebugVerboseLogging = profile.GoDebugVerboseLogging ?? false,

            // Render settings
            RenderFpsLimit = profile.GoRenderFpsLimit ?? 144,
            RenderLimitFramerate = profile.GoRenderLimitFramerate ?? true,
            RenderStatsOverlay = profile.GoRenderStatsOverlay ?? true,

            // Social notification settings
            SocialNotificationFriendComesOnlineGameplay = profile.GoSocialNotificationFriendComesOnlineGameplay ?? true,
            SocialNotificationFriendComesOnlineMenus = profile.GoSocialNotificationFriendComesOnlineMenus ?? true,
            SocialNotificationFriendGoesOfflineGameplay = profile.GoSocialNotificationFriendGoesOfflineGameplay ?? true,
            SocialNotificationFriendGoesOfflineMenus = profile.GoSocialNotificationFriendGoesOfflineMenus ?? true,
            SocialNotificationPlayerAcceptsRequestGameplay = profile.GoSocialNotificationPlayerAcceptsRequestGameplay ?? true,
            SocialNotificationPlayerAcceptsRequestMenus = profile.GoSocialNotificationPlayerAcceptsRequestMenus ?? true,
            SocialNotificationPlayerSendsRequestGameplay = profile.GoSocialNotificationPlayerSendsRequestGameplay ?? true,
            SocialNotificationPlayerSendsRequestMenus = profile.GoSocialNotificationPlayerSendsRequestMenus ?? true,

            // TSH settings (that exist in settings.json) - use null-coalescing with defaults
            ArchiveReplays = profile.TshArchiveReplays ?? false,
            MoneyTransactionVolume = profile.TshMoneyTransactionVolume ?? 50,
            ShowMoneyPerMinute = profile.TshShowMoneyPerMinute ?? false,
            PlayerObserverEnabled = profile.TshPlayerObserverEnabled ?? false,
            SystemTimeFontSize = profile.TshSystemTimeFontSize ?? 12,
            NetworkLatencyFontSize = profile.TshNetworkLatencyFontSize ?? 12,
            RenderFpsFontSize = profile.TshRenderFpsFontSize ?? 12,
            ResolutionFontAdjustment = profile.TshResolutionFontAdjustment ?? -100,
            CursorCaptureEnabledInFullscreenGame = profile.TshCursorCaptureEnabledInFullscreenGame ?? false,
            CursorCaptureEnabledInFullscreenMenu = profile.TshCursorCaptureEnabledInFullscreenMenu ?? false,
            CursorCaptureEnabledInWindowedGame = profile.TshCursorCaptureEnabledInWindowedGame ?? false,
            CursorCaptureEnabledInWindowedMenu = profile.TshCursorCaptureEnabledInWindowedMenu ?? false,
            ScreenEdgeScrollEnabledInFullscreenApp = profile.TshScreenEdgeScrollEnabledInFullscreenApp ?? false,
            ScreenEdgeScrollEnabledInWindowedApp = profile.TshScreenEdgeScrollEnabledInWindowedApp ?? false,
        };
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Align defaults with GameSettings record to avoid inconsistent behavior.

Several defaults here (e.g., cursor capture flags, observer, font sizes, fullscreen edge scroll, money transaction volume) diverge from the defaults in GenHub.Core.Models.GameSettings.GameSettings. This means a profile-derived settings.json can behave differently than a default-loaded settings.json. Please align to the record’s defaults or update the record if these new defaults are intended.

✅ Suggested fix using record defaults
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Models/Providers/CatalogDependency.cs
Comment on lines +8 to +51
public class CatalogDependency
{
    /// `<summary>`
    /// Gets or sets the publisher ID of the dependency.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    /// `<summary>`
    /// Gets or sets the content ID within the publisher's catalog.
    /// </summary>
    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    /// `<summary>`
    /// Gets or sets the version constraint (e.g., ">=1.0.0", "^2.0", "1.5.0").
    /// </summary>
    [JsonPropertyName("versionConstraint")]
    public string? VersionConstraint { get; set; }

    /// `<summary>`
    /// Gets or sets a value indicating whether the dependency is optional.
    /// </summary>
    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }

    /// `<summary>`
    /// Gets or sets a hint for where to find this dependency (catalog URL).
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string? CatalogUrl { get; set; }

    /// `<summary>`
    /// Gets or sets the URL to the provider definition for this dependency.
    /// Recommended over CatalogUrl for robust discovery.
    /// </summary>
    [JsonPropertyName("definitionUrl")]
    public string? DefinitionUrl { get; set; }

    /// `<summary>`
    /// Gets or sets a list of manifest IDs that conflict with this dependency.
    /// </summary>
    [JsonPropertyName("conflictsWith")]
    public List<string> ConflictsWith { get; set; } = [];
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider adding a ContentType property.

The model lacks a ContentType property, which forces consumers like GenericCatalogResolver to hardcode ContentType.Mod for all dependencies. Adding this property would enable accurate dependency resolution:

+    /// `<summary>`
+    /// Gets or sets the content type of the dependency.
+    /// </summary>
+    [JsonPropertyName("contentType")]
+    public ContentType? ContentType { get; set; }
+
     /// `<summary>`
     /// Gets or sets a list of manifest IDs that conflict with this dependency.
     /// </summary>
🤖 Prompt for AI Agents
@undead2146    Reply...
...ts/GenHub.Tests.Core/Features/Content/Services/Catalog/GenericCatalogManifestFactoryTests.cs
Comment on lines +132 to +140
        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider using a shared cleanup helper or finally block.

Multiple tests have identical cleanup patterns with empty catch blocks. This silently swallows all exceptions including unexpected ones. Consider extracting to a helper method with specific exception handling, or using a test fixture with IDisposable.

Also applies to: 188-196, 214-222, 305-313, 393-401, 434-441

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Tests/GenHub.Tests.Core/Features/GameSettings/GameSettingsServiceTests.cs
Comment on lines +389 to +415

    /// `<summary>`
    /// Should return success with defaults when GameSettings file does not exist.
    /// </summary>
    /// `<returns>`A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadGameSettingsAsync_Should_ReturnSuccessWithDefaults_WhenFileDoesNotExist()
    {
        // Arrange
        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Setup(x => x.GetGameSettingsFilePath()).Returns("nonexistent_settings.json");

        // Act
        var result = await mockService.Object.LoadGameSettingsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        // Verify default values
        Assert.True(result.Data.ShowPing);
        Assert.False(result.Data.ShowFps);
        Assert.Equal(12, result.Data.ChatFontSize);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Avoid writing defaults to the working directory in this test.

LoadGameSettingsAsync saves defaults when the file is missing; using a relative path here can create a stray file and introduce cross-test pollution. Prefer a temp path and clean it up.

🧹 Proposed fix (temp path + cleanup)
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Tests/GenHub.Tests.Core/Features/Tools/PublisherStudioIntegrationTests.cs
using GenHub.Core.Models.Publishers;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.DependencyInjection;
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused using directive.

Microsoft.Extensions.DependencyInjection is imported but not used in this file.

🧹 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/App.axaml.cs
Comment on lines +277 to +284
        else if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var url = command[IpcCommands.SubscribePrefix.Length..];
            logger?.LogInformation("Received IPC subscribe command for URL: {Url}", url);

            // Handle subscription
            SafeFireAndForget(HandleSubscribeCommandAsync(url), "HandleSubscribeCommandAsync");
        }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Guard against empty IPC subscribe payloads.
If the IPC command is just subscribe: (or whitespace), the dialog opens with an empty URL. Short‑circuit with a warning before calling the handler.

🔧 Suggested fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Common/Views/MainWindow.axaml.cs

        foreach (var file in files)
        {
            var filePath = file.Path.LocalPath;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Potential null reference: file.Path may be null for certain storage items.

IStorageItem.Path can return null for items that don't have a local file system path (e.g., virtual files). Add a null check to avoid a NullReferenceException.

🛡️ Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Common/Views/MainWindow.axaml.cs
Comment on lines +55 to +62
                            if (Avalonia.Application.Current is App app)
                            {
                                // Call the internal subscription handler
                                // We use reflection or make it public if needed,
                                // but App already has SingleInstance handle logic.
                                // Actually we can just call the public method if we add it.
                                await app.HandleSubscribeCommandAsync(url);
                            }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Remove developer notes from production code.

The comments on lines 57-60 appear to be internal developer notes about implementation decisions. These should be removed or converted to proper documentation if the intent needs to be preserved.

♻️ Proposed cleanup
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Common/Views/MainWindow.axaml.cs
Comment on lines +66 to +69
                catch
                {
                    // Ignore invalid files
                }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Silent exception swallowing may hide important errors.

The empty catch block silently ignores all exceptions, including unexpected ones like OutOfMemoryException or AccessDeniedException. Consider logging the exception or at minimum catching only expected exceptions (JsonException, IOException).

🛡️ Proposed fix with specific exception handling
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogManifestFactory.cs
Comment on lines +61 to +69
        var builtInPublishers = new[]
        {
            "Steam", "steam",
            "EA", "Origin", "ea", "origin",
            "Ultimate", "ultimate",
            "TheSuperHackers", "thesuperhackers",
            "GeneralsOnline", "generalsonline",
            "CommunityOutpost", "communityoutpost",
        };
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Redundant cased entries in built-in publishers list.

Since the comparison uses StringComparison.OrdinalIgnoreCase (Line 73), maintaining both cased versions is unnecessary and adds maintenance overhead.

         var builtInPublishers = new[]
         {
-            "Steam", "steam",
-            "EA", "Origin", "ea", "origin",
-            "Ultimate", "ultimate",
-            "TheSuperHackers", "thesuperhackers",
-            "GeneralsOnline", "generalsonline",
-            "CommunityOutpost", "communityoutpost",
+            "Steam",
+            "EA",
+            "Origin",
+            "Ultimate",
+            "TheSuperHackers",
+            "GeneralsOnline",
+            "CommunityOutpost",
         };
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogManifestFactory.cs
Comment on lines +168 to +174
            Metadata = new ContentMetadata
            {
                Description = original.Metadata.Description,
                Tags = new List<string>(original.Metadata.Tags),
                IconUrl = original.Metadata.IconUrl,
                ScreenshotUrls = new List<string>(original.Metadata.ScreenshotUrls),
            },
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Potential null reference when accessing Metadata properties.

CloneManifest accesses original.Metadata.Description, Tags, IconUrl, and ScreenshotUrls without null-checking Metadata. If Metadata is null, this will throw a NullReferenceException.

-            Metadata = new ContentMetadata
-            {
-                Description = original.Metadata.Description,
-                Tags = new List<string>(original.Metadata.Tags),
-                IconUrl = original.Metadata.IconUrl,
-                ScreenshotUrls = new List<string>(original.Metadata.ScreenshotUrls),
-            },
+            Metadata = original.Metadata != null
+                ? new ContentMetadata
+                {
+                    Description = original.Metadata.Description,
+                    Tags = new List<string>(original.Metadata.Tags ?? []),
+                    IconUrl = original.Metadata.IconUrl,
+                    ScreenshotUrls = new List<string>(original.Metadata.ScreenshotUrls ?? []),
+                }
+                : new ContentMetadata(),
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogManifestFactory.cs
Comment on lines +203 to +219
    private static string GetModTargetDirectory(ContentManifest manifest)
    {
        // Generate a mod directory name from the manifest ID
        // Format: Mods/{publisher-id}.{content-id}/
        var idParts = manifest.Id.Value.Split('.');
        if (idParts.Length >= 5)
        {
            // ID format: {schema}.{version}.{publisher}.{type}.{name}
            var publisher = idParts[2];
            var name = idParts[^1]; // Last part is content name
            return $"Mods/{publisher}.{name}/";
        }

        // Fallback: use manifest name
        var safeName = manifest.Name.Replace(" ", "_").ToLowerInvariant();
        return $"Mods/{safeName}/";
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
Remove unused method GetModTargetDirectory.

This method is not called anywhere in the codebase and can be safely deleted.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogResolver.cs
                    screenshotUrls: contentItem.Metadata?.ScreenshotUrls?.ToList());

            // Add primary artifact for download
            var primaryArtifact = release.Artifacts.FirstOrDefault(a => a.IsPrimary) ?? release.Artifacts.First();
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Potential exception when Artifacts collection is empty.

If release.Artifacts is empty, First() will throw InvalidOperationException. Consider adding validation or using FirstOrDefault() with a null check.

-            var primaryArtifact = release.Artifacts.FirstOrDefault(a => a.IsPrimary) ?? release.Artifacts.First();
+            var primaryArtifact = release.Artifacts.FirstOrDefault(a => a.IsPrimary)
+                ?? release.Artifacts.FirstOrDefault();
+
+            if (primaryArtifact == null)
+            {
+                return OperationResult<ContentManifest>.CreateFailure("Release has no artifacts");
+            }
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogResolver.cs
Comment on lines +107 to +111
                var dependencyId = ManifestIdGenerator.GeneratePublisherContentId(
                    dependency.PublisherId,
                    ContentType.Mod, // Default to Mod for catalog dependencies if not specified
                    dependency.ContentId,
                    ExtractVersionNumber(dependency.VersionConstraint ?? "0"));
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Hardcoded ContentType.Mod may lose dependency type information.

All dependencies are treated as ContentType.Mod regardless of their actual type. If CatalogDependency could specify a content type, this would provide more accurate manifest generation.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogResolver.cs
Comment on lines +137 to +145
    private static int ExtractVersionNumber(string version)
    {
        if (int.TryParse(new string([.. version.Where(char.IsDigit)]), out var result))
        {
            return result;
        }

        return 0;
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

🧩 Analysis chain
Extract ExtractVersionNumber to a shared utility class.

This method is duplicated in both GenericCatalogResolver.cs (lines 137-145) and GenericCatalogDiscoverer.cs (lines 70-78) with identical implementations. Move it to a shared utility class to follow DRY principles.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostDiscoverer.cs
Comment on lines +405 to +406
                Description = metadata.Description,
                Version = metadata.Version,
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

🧩 Analysis chain
Null assignment to non-nullable Version property.

The change at line 406 assigns metadata.Version (which is nullable: string?) directly to ContentSearchResult.Version (which is non-nullable: string with default string.Empty). This can assign null to a property that doesn't accept null values, creating a potential null reference issue.

Line 405 is safe—ContentSearchResult.Description is properly annotated as nullable (string?).

Restore the fallback value for Version: Version = metadata.Version ?? string.Empty

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostResolver.cs
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
Remove the unused GenHub.Core.Extensions import.

This using directive is not utilized in the file—no extension methods from this namespace are invoked. While the namespace exists elsewhere in the project, it has no purpose here.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentResolvers/ModDBResolver.cs
Comment on lines +63 to +64
            // Store the parsed page in the discovered item for UI display
            discoveredItem.SetData(parsedPage);
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Mutating input parameter may cause unexpected side effects.

SetData mutates the discoveredItem input parameter. This side effect could surprise callers and makes the method harder to reason about. Consider returning the parsed data alongside the manifest or documenting this mutation clearly.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineJsonCatalogParser.cs
Comment on lines 65 to +66
                    var apiResponse = JsonSerializer.Deserialize<GeneralsOnlineApiResponse>(
                        dataElement.GetRawText(),
                        _jsonOptions);
                        dataElement.GetRawText());
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Default JsonSerializer will fail with PascalCase upstream JSON.

The deserialization at lines 65-66 uses default options (case-sensitive, strict [JsonPropertyName] matching). If the manifest JSON from upstream uses PascalCase keys like "Version" instead of "version", the apiResponse.Version will remain empty and no releases will be returned. The test file includes a test for PascalCase (ParseAsync_WithPascalCaseJson_ParsesCorrectly) which would currently fail. Add PropertyNameCaseInsensitive = true to JsonSerializerOptions or document what case convention upstream APIs must follow.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Publishers/AODMapsManifestFactory.cs
Comment on lines +110 to +114
        logger.LogInformation(
            "[TEMP] AODMapsManifestFactory - Adding file: {FileName} from URL: {Url}",
            fileName,
            details.DownloadUrl);
        logger.LogInformation("[TEMP] Referer URL: {Referer}", details.RefererUrl);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Remove [TEMP] logging statements before merging.

These debug logging statements with [TEMP] prefix appear to be development artifacts and should be removed or converted to appropriate log levels before merging to production.

Proposed fix: Remove or convert TEMP logging
Also applies to: 123-123

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Publishers/AODMapsManifestFactory.cs
Comment on lines +131 to +137
    private static string NormalizeAuthorForPublisherId(string author)
    {
        if (string.IsNullOrWhiteSpace(author)) return "unknown";
        var slugHelper = new SlugHelper();
        var normalized = slugHelper.GenerateSlug(author).Replace("-", string.Empty);
        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused method: NormalizeAuthorForPublisherId is defined but never called.

This private method was added but isn't invoked anywhere in the class. It appears to be dead code, possibly left over from refactoring.

Remove unused method
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Publishers/ModDBManifestFactory.cs
Comment on lines +151 to +190
        // 7. Add the download files
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add primary file
        var primaryFileName = ExtractFileNameFromUrl(details.DownloadUrl);
        logger.LogInformation("[TEMP] ModDBManifestFactory - Adding primary file: {FileName} from URL: {Url}", primaryFileName, details.DownloadUrl);

        manifest = await manifest.AddRemoteFileAsync(
            fileName,
            primaryFileName,
            details.DownloadUrl,
            ContentSourceType.RemoteDownload);
            ContentSourceType.ContentAddressable,
            isExecutable: false,
            permissions: null);

        addedUrls.Add(details.DownloadUrl);

        // Add any additional files discovered on the page (e.g. patches, mirrors, addons)
        if (details.AdditionalFiles != null)
        {
            foreach (var file in details.AdditionalFiles)
            {
                if (string.IsNullOrEmpty(file.DownloadUrl) || addedUrls.Contains(file.DownloadUrl))
                    continue;

                var fileName = !string.IsNullOrEmpty(file.Name) ? file.Name : ExtractFileNameFromUrl(file.DownloadUrl);

                logger.LogInformation("[TEMP] ModDBManifestFactory - Adding additional file: {FileName} from URL: {Url}", fileName, file.DownloadUrl);

                manifest = await manifest.AddRemoteFileAsync(
                    fileName,
                    file.DownloadUrl,
                    ContentSourceType.ContentAddressable,
                    isExecutable: false,
                    permissions: null);

                addedUrls.Add(file.DownloadUrl);
            }
        }

        logger.LogInformation("[TEMP] ModDBManifestFactory - {Count} total files added to manifest with CAS storage", addedUrls.Count);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Guard against invalid URLs and filename collisions when adding multiple files.
If multiple files resolve to the same fallback name (or URL is invalid), AddRemoteFileAsync can overwrite or fail. Ensure URLs are valid and filenames are unique per manifest entry.

🔧 Proposed fix (validate URLs + ensure unique filenames)
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/CommunityOutpostFilterViewModel.cs
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused import: Microsoft.Extensions.Logging.

The Microsoft.Extensions.Logging namespace is imported but ILogger is not used in this class.

Remove unused import
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/CommunityOutpostFilterViewModel.cs
Comment on lines +17 to +23
public partial class CommunityOutpostFilterViewModel() : FilterPanelViewModelBase
{
    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

InitializeContentTypeFilters() is never called - filter options will be empty.

The InitializeContentTypeFilters() method (lines 89-100) populates the ContentTypeFilters collection, but it's never invoked. The collection is initialized as empty on line 23 and remains empty, making the content type filter non-functional.

Proposed fix: Call InitializeContentTypeFilters in constructor
Also applies to: 89-100

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/GitHubFilterViewModel.cs
Comment on lines +105 to +119
    partial void OnSelectedTopicChanged(string? value) { }

    partial void OnSelectedAuthorChanged(string? value) { }

    [RelayCommand]
    private void SelectTopic(FilterOption option)
    {
        SelectedTopic = string.IsNullOrEmpty(option.Value) ? null : option.Value;
    }

    [RelayCommand]
    private void SelectAuthor(FilterOption option)
    {
        SelectedAuthor = string.IsNullOrEmpty(option.Value) ? null : option.Value;
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Missing NotifyFiltersChanged() call when topic/author selection changes.

The SelectTopic and SelectAuthor commands modify SelectedTopic/SelectedAuthor but don't notify the filter system of changes. The partial methods OnSelectedTopicChanged and OnSelectedAuthorChanged are empty.

Compare with CommunityOutpostFilterViewModel where OnSelectedContentTypeChanged calls NotifyFiltersChanged().

Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/MissingDependencyItemViewModel.cs
Comment on lines +30 to +36
    public MissingDependencyItemViewModel(MissingDependency dependency)
    {
        Name = dependency.Dependency.Name ?? "Unknown Dependency";
        VersionConstraint = GetVersionConstraint(dependency);
        IsOptional = dependency.Dependency.IsOptional;
        CanAutoInstall = dependency.CanAutoInstall;
        Status = CanAutoInstall ? "Available" : "Not found in subscriptions";
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Add a null guard for dependency.
Without a check, a null argument will throw later with a less clear NRE.

🛡️ Suggested fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/DownloadsView.axaml
Comment on lines +41 to +140
        <ScrollViewer IsVisible="{Binding !IsBrowserVisible}">
            <Grid Margin="20">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

            <!-- Header Section -->
            <StackPanel Grid.Row="0" VerticalAlignment="Center" HorizontalAlignment="Center" Margin="0,0,0,30">
                <TextBlock Text="{Binding Title}"
                           FontSize="28"
                           FontWeight="SemiBold"
                           Foreground="White"
                           HorizontalAlignment="Center"
                           Margin="0,0,0,10" />
                <TextBlock Text="{Binding Description}"
                           FontSize="16"
                           Foreground="#AAAAAA"
                           HorizontalAlignment="Center" />
                <!-- Header Section -->
                <StackPanel Grid.Row="0" VerticalAlignment="Center" HorizontalAlignment="Center" Margin="0,0,0,30">
                    <TextBlock Text="{Binding Title}"
                               FontSize="28"
                               FontWeight="SemiBold"
                               Foreground="White"
                               HorizontalAlignment="Center"
                               Margin="0,0,0,10" />
                    <TextBlock Text="{Binding Description}"
                               FontSize="16"
                               Foreground="#AAAAAA"
                               HorizontalAlignment="Center" />

            </StackPanel>
                </StackPanel>

            <!-- Main Content Area -->
            <ScrollViewer Grid.Row="1">
                <StackPanel Margin="0,0,0,20">
                <!-- Main Content Area -->
                <ScrollViewer Grid.Row="1">
                    <StackPanel Margin="0,0,0,20">

                    <!-- Unified Publisher Catalog -->
                    <ItemsControl ItemsSource="{Binding PublisherCards}" HorizontalAlignment="Center">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel Orientation="Horizontal" HorizontalAlignment="Center" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <views:PublisherCardView DataContext="{Binding}" Margin="10" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
                        <!-- Unified Publisher Catalog -->
                        <ItemsControl ItemsSource="{Binding PublisherCards}" HorizontalAlignment="Center">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <WrapPanel Orientation="Horizontal" HorizontalAlignment="Center" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <views:PublisherCardView DataContext="{Binding}" Margin="10" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <!-- Additional Features Section (Placeholder for future) -->
                        <StackPanel HorizontalAlignment="Center" Margin="0,40,0,0">
                            <TextBlock Text="Coming Soon"
                                       FontSize="20"
                                       FontWeight="SemiBold"
                                       Foreground="#555555"
                                       HorizontalAlignment="Center"
                                       Margin="0,0,0,20" />

                            <WrapPanel Orientation="Horizontal" HorizontalAlignment="Center" MaxWidth="800" Opacity="0.5">
                                <!-- Mod Downloads -->
                                <Border Classes="feature-card">
                                    <StackPanel HorizontalAlignment="Center" Spacing="10">
                                        <PathIcon Data="M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.22,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.22,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z"
                                                  Width="32" Height="32"
                                                  Foreground="#666666"
                                                  HorizontalAlignment="Center" />
                                        <TextBlock Text="Mod Downloads"
                                                   FontSize="16"
                                                   FontWeight="SemiBold"
                                                   Foreground="#666666"
                                                   HorizontalAlignment="Center" />
                                    </StackPanel>
                                </Border>

                                <!-- Maps -->
                                <Border Classes="feature-card">
                                    <StackPanel HorizontalAlignment="Center" Spacing="10">
                                        <PathIcon Data="M12,2L13.09,8.26L22,9L17,14L18.18,23L12,19.77L5.82,23L7,14L2,9L10.91,8.26L12,2Z"
                                                  Width="32" Height="32"
                                                  Foreground="#666666"
                                                  HorizontalAlignment="Center" />
                                        <TextBlock Text="Maps"
                                                   FontSize="16"
                                                   FontWeight="SemiBold"
                                                   Foreground="#666666"
                                                   HorizontalAlignment="Center" />
                                    </StackPanel>
                                </Border>

                                <!-- Tools -->
                                <Border Classes="feature-card">
                                    <StackPanel HorizontalAlignment="Center" Spacing="10">
                                        <PathIcon Data="M22.7,19L13.6,9.9C14.5,7.6 14,4.9 12.1,3C10.1,1 7.1,0.6 4.7,1.7L9,6L6,9L1.6,4.7C0.4,7.1 0.9,10.1 2.9,12.1C4.8,14 7.5,14.5 9.8,13.6L18.9,22.7C19.3,23.1 19.9,23.1 20.3,22.7L22.6,20.4C23.1,20 23.1,19.3 22.7,19Z"
                                                  Width="32" Height="32"
                                                  Foreground="#666666"
                                                  HorizontalAlignment="Center" />
                                        <TextBlock Text="Tools"
                                                   FontSize="16"
                                                   FontWeight="SemiBold"
                                                   Foreground="#666666"
                                                   HorizontalAlignment="Center" />
                                    </StackPanel>
                                </Border>
                            </WrapPanel>
                        </StackPanel>
                    </StackPanel>
                </ScrollViewer>
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

🧩 Analysis chain
Consider removing the inner ScrollViewer to simplify layout.

The inner ScrollViewer at Line 66 may be redundant if the content (ItemsControl and feature cards) doesn't exceed the available height. Removing it simplifies the visual tree without functional loss. Avalonia's scroll chaining (enabled by default) prevents dead zones, so nested scrollers aren't problematic by themselves, but flattening to a single scroll container when possible keeps the layout cleaner.

Suggested refactor
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/SubscriptionConfirmationDialog.axaml
Comment on lines +69 to +86
                    <!-- Error State -->
                    <StackPanel IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                                VerticalAlignment="Center"
                                Margin="10">
                        <TextBlock Text="⚠ Error"
                                   Foreground="#FF5555"
                                   FontWeight="Bold"
                                   FontSize="18"
                                   HorizontalAlignment="Center" />
                        <TextBlock Text="{Binding ErrorMessage}"
                                   Foreground="#FF9999"
                                   TextWrapping="Wrap"
                                   TextAlignment="Center"
                                   Margin="0,10,0,0" />
                    </StackPanel>

                    <!-- Details State -->
                    <ScrollViewer IsVisible="{Binding !IsLoading}">
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Details panel may display alongside error state.

The error state (Line 70) is visible when ErrorMessage is not null or empty, while the details state (Line 86) is visible when !IsLoading. When loading completes with an error, both panels will be visible simultaneously since IsLoading will be false and ErrorMessage will be set.

Consider adding an additional condition to the details panel:

-                    <ScrollViewer IsVisible="{Binding !IsLoading}">
+                    <ScrollViewer IsVisible="{Binding !IsLoading}">
+                        <!-- Add MultiBinding or computed property to hide when ErrorMessage is set -->
Or expose a computed property like ShowDetails in the ViewModel that returns !IsLoading && string.IsNullOrEmpty(ErrorMessage).

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/SubscriptionConfirmationDialog.axaml.cs
Comment on lines +32 to +43
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is SubscriptionConfirmationViewModel vm)
        {
            // Set up a way to close the window from the VM
            vm.RequestClose = (result) => Close(result);

            // Start initialization
            await vm.InitializeAsync();
        }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Handle exceptions from InitializeAsync() to prevent UI crashes.
async void methods don't propagate exceptions to callers—instead, unhandled exceptions are posted to the UI dispatcher and can terminate the application. Wrap the initialization in a try/catch to gracefully recover if the async work fails.

🛠️ Proposed fix (guard async initialization)
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Settings/Views/SettingsView.axaml
  <UserControl.Resources>
    <converters:NullableIntConverter x:Key="NullableIntConverter" />
    <converters:NullableDoubleConverter x:Key="NullableDoubleConverter" />
    <converters:TrustLevelToColorConverter x:Key="TrustLevelToColorConverter" />
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Inconsistent converter usage: resource defined but static instance used.

Line 15 defines TrustLevelToColorConverter as a keyed resource (x:Key="TrustLevelToColorConverter"), but line 879 uses x:Static converters:TrustLevelToColorConverter.Instance. This creates an unused resource declaration.

Either:

Remove line 15 if the converter exposes a static Instance property, or
Use {StaticResource TrustLevelToColorConverter} at line 879 instead of x:Static.
Option 1: Remove unused resource (if Instance property exists)
Option 2: Use the resource instead of static instance
Also applies to: 879-879

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Settings/Views/SettingsView.axaml.cs
        if (DataContext is SettingsViewModel vm)
        {
            vm.IsViewVisible = true;
            vm.LoadSubscriptionsCommand.Execute(null);
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider checking CanExecute before executing the command.

Using .Execute(null) directly bypasses the CanExecute check. If the command has execution preconditions, consider using:

if (vm.LoadSubscriptionsCommand.CanExecute(null))
{
    vm.LoadSubscriptionsCommand.Execute(null);
}
Alternatively, if LoadSubscriptionsCommand is an IAsyncRelayCommand, calling ExecuteAsync would be more explicit about the async nature, though fire-and-forget is acceptable here for view initialization.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Interfaces/IPublisherStudioDialogService.cs
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;

namespace GenHub.Features.Tools.Services;
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Namespace doesn't match the folder structure.

The file is located in Features/Tools/Interfaces/ but the namespace is GenHub.Features.Tools.Services. Consider aligning the namespace with the folder structure:

-namespace GenHub.Features.Tools.Services;
+namespace GenHub.Features.Tools.Interfaces;
Or move the file to the Services folder if that's the intended location.

📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioService.cs
Comment on lines +113 to +119

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            var json = JsonSerializer.Serialize(project, options);
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider using shared JsonSerializerOptions instances.

Multiple methods create new JsonSerializerOptions instances with similar configurations. Creating these once as static readonly fields improves performance and ensures consistency.

♻️ Proposed shared options
Also applies to: 141-145, 282-287

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioService.cs
Comment on lines +213 to +221

            // Use the catalog parser to validate JSON structure
            var json = JsonSerializer.Serialize(catalog);
            var parseResult = await _catalogParser.ParseCatalogAsync(json, cancellationToken);

            if (!parseResult.Success)
            {
                return OperationResult<bool>.CreateFailure(parseResult);
            }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Redundant serialization for validation.

The catalog is serialized to JSON only to be immediately parsed again by _catalogParser.ParseCatalogAsync. If the parser can accept a PublisherCatalog object directly, this round-trip is unnecessary. If not, consider whether the structural validation above is sufficient.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioService.cs
Comment on lines +252 to +257
        try
        {
            if (project.Catalog.Publisher == null)
            {
                return Task.FromResult(OperationResult<string>.CreateFailure("Publisher profile is missing"));
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Missing null check for project.Catalog.

The code checks project.Catalog.Publisher == null but doesn't first verify that project.Catalog itself is not null, which could cause a NullReferenceException.

🐛 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/ViewModels/ArtifactUrlStatus.cs
Comment on lines +51 to +58
    public ArtifactUrlStatus(ReleaseArtifact artifact, string contentName, string version)
    {
        _artifact = artifact;
        ContentName = contentName;
        ReleaseVersion = version;
        ArtifactName = artifact.Filename;
        Validate();
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Missing null validation for artifact parameter.

The constructor should validate that artifact is not null to prevent NullReferenceException when accessing _artifact properties.

     public ArtifactUrlStatus(ReleaseArtifact artifact, string contentName, string version)
     {
+        ArgumentNullException.ThrowIfNull(artifact);
         _artifact = artifact;
         ContentName = contentName;
         ReleaseVersion = version;
         ArtifactName = artifact.Filename;
         Validate();
     }
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/ViewModels/ContentLibraryViewModel.cs
Comment on lines +69 to +75
    public ContentLibraryViewModel(
        PublisherStudioProject project,
        PublisherStudioViewModel parentViewModel,
        ILogger logger,
        IPublisherStudioDialogService dialogService)
        : this(project, project.Catalogs.FirstOrDefault() ?? new NamedCatalog { Id = "default", Name = "Content", Catalog = project.Catalog }, parentViewModel, logger, dialogService)
    {
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Register fallback catalog in project.Catalogs to ensure persistence.

When project.Catalogs is empty, the convenience constructor creates a fallback NamedCatalog but doesn't add it to the list. Since only project.Catalogs is serialized, the fallback catalog's edits are lost on the next session, and features expecting all active catalogs in Catalogs (like publishing and multi-catalog UI) won't include it.

Add the suggested guard to project.Catalogs after delegating to the main constructor:

Suggested fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/ViewModels/ContentLibraryViewModel.cs
Comment on lines +81 to +88
    private void LoadContent()
    {
        ContentItems.Clear();
        foreach (var item in _activeCatalog.Catalog.Content)
        {
            ContentItems.Add(item);
        }
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Prevent UI desync between ContentItems and the active catalog.

ContentItems is a separate collection from _activeCatalog.Catalog.Content. If other components mutate the catalog, the UI can go stale. Consider binding ContentItems directly to the catalog collection or subscribing to CollectionChanged to keep them in sync.

🤖 Prompt for AI Agents
@undead2146    Reply...
coderabbitai[bot]
coderabbitai bot requested changes 2 days ago
coderabbitai bot
left a comment
Review continued from previous batch...

GenHub/GenHub.Core/Interfaces/Publishers/IPublisherDefinitionService.cs
Comment on lines +7 to +10
/// `<summary>`
/// Service for fetching and processing publisher definitions.
/// Handles the "Tier 3" URL layer - fetching provider.json to discover catalog locations.
/// </summary>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Documentation inconsistency: "Tier 3" should be "Tier 1".

The XML comment references "Tier 3 URL layer" for fetching provider.json, but the PR description defines the 3-tier architecture as:

Tier 1 (Definition): publisher.json
Tier 2 (Catalog): catalog.json
Tier 3 (Artifacts): immutable binaries
This service handles Tier 1, not Tier 3.

📝 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Models/Providers/PublisherSubscription.cs
Comment on lines +1 to +7
using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

Missing using directive for List<T>.

The file uses List<SubscribedCatalogEntry> on line 96 but doesn't include using System.Collections.Generic;. This will cause a compilation error.

Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
...GenHub.Tests.Core/Features/Content/Services/Catalog/CrossPublisherDependencyResolverTests.cs
Comment on lines +229 to +267
    [Fact]
    public async Task FetchExternalCatalogAsync_ValidUrl_ReturnsCatalog()
    {
        // Arrange
        var resolver = CreateResolver();
        _ = "<https://example.com/catalog.json";>
        var catalogJson = """
            {
                "$schemaVersion": 1,
                "publisher": {
                    "id": "test-publisher",
                    "name": "Test Publisher"
                },
                "content": []
            }
            """;

        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile { Id = "test-publisher", Name = "Test Publisher" },
        };

        _catalogParserMock.Setup(p => p.ParseCatalogAsync(catalogJson, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        // We can't easily mock HttpClient, so we'll test the parsing logic path
        // This test verifies the integration with the catalog parser

        // Since we can't mock HttpClient.GetStringAsync directly without a wrapper,
        // we'll verify the method signature and catalog parser integration
        // In a real scenario, you'd use IHttpClientFactory with a mocked HttpMessageHandler

        // For now, we'll test the catalog parser integration is correct
        // Act
        var parseResult = await _catalogParserMock.Object.ParseCatalogAsync(catalogJson);

        // Assert
        Assert.True(parseResult.Success);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Test does not actually test FetchExternalCatalogAsync.

The test name suggests it validates FetchExternalCatalogAsync, but it only verifies the mock's ParseCatalogAsync method. The actual FetchExternalCatalogAsync method on the resolver is never called.

Consider either:

Renaming the test to reflect what it actually tests (catalog parser integration)
Setting up an HttpMessageHandler mock to properly test the HTTP fetch path
Converting to an integration test with a test server
🤖 Prompt for AI Agents
@undead2146    Reply...
...GenHub.Tests.Core/Features/Content/Services/Catalog/CrossPublisherDependencyResolverTests.cs
    {
        // Arrange
        var resolver = CreateResolver();
        _ = "<https://example.com/catalog.json";>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Remove unused variable.

The URL string is assigned to a discard but was likely intended to be used in the test.

🐛 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
...GenHub.Tests.Core/Features/Content/Services/Catalog/CrossPublisherDependencyResolverTests.cs
Comment on lines +272 to +291
    [Fact]
    public void FetchExternalCatalogAsync_HttpError_ReturnsFailure()
    {
        // This test documents the expected behavior.
        // In production, the resolver should handle network errors gracefully.
        // The actual implementation uses IHttpClientFactory which should be tested
        // with integration tests using a test server.

        // For unit testing purposes, we verify the error handling path exists
        // by checking that the method signature supports cancellation tokens
        var resolver = CreateResolver();

        // Verify the method accepts CancellationToken
        var method = typeof(CrossPublisherDependencyResolver).GetMethod("FetchExternalCatalogAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Contains(parameters, p => p.ParameterType == typeof(CancellationToken));

        Assert.True(true, "FetchExternalCatalogAsync properly accepts CancellationToken for cancellation support");
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Reflection-based signature test provides minimal value.

This test only verifies that a method with CancellationToken parameter exists via reflection. It doesn't test any actual behavior. Consider removing or replacing with a proper integration test.

🤖 Prompt for AI Agents
@undead2146    Reply...
...GenHub.Tests.Core/Features/Content/Services/Catalog/CrossPublisherDependencyResolverTests.cs
Comment on lines +366 to +372
        // Assert
        Assert.True(result.Success);
        var missingDep = Assert.Single(result.Data ?? []);

        // The dependency should have ResolvableContent set, making CanAutoInstall true
        // Note: The actual implementation depends on FindDependencyContentAsync being called
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Test assertion is incomplete.

The comment indicates the test should verify CanAutoInstall is true when ResolvableContent is set, but the assertion only checks that a single dependency is returned. The mock setup suggests FindDependencyContentAsync would need to be called, but there's no verification of CanAutoInstall value.

💚 Proposed fix to complete the assertion
🤖 Prompt for AI Agents
@undead2146    Reply...
...ts/GenHub.Tests.Core/Features/Content/Services/Catalog/GenericCatalogManifestFactoryTests.cs
{
    private readonly Mock<ILogger<GenericCatalogManifestFactory>> _loggerMock;
    private readonly GenericCatalogManifestFactory _factory;
    private readonly ITestOutputHelper _output;
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused field _output.

The ITestOutputHelper _output field is injected but never used in any test method. Either use it for diagnostic output or remove it.

🧹 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
...ts/GenHub.Tests.Core/Features/Content/Services/Catalog/GenericCatalogManifestFactoryTests.cs
Comment on lines +426 to +431
        // Assert
        Assert.All(enrichedManifest.Files, f =>
        {
            Assert.DoesNotContain("\\", f.RelativePath);
            Assert.Contains("/", f.RelativePath);
        });
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Test assertion may fail on single-file directories.

The test asserts that all file paths contain a forward slash, but CreateTestDirectoryWithFiles() creates files both at the root level (test.txt, test.bin) and in a subdirectory. Root-level files won't have a / in their relative path (e.g., "test.txt"), so this assertion will fail.

🐛 Proposed fix to adjust assertion
🤖 Prompt for AI Agents
@undead2146    Reply...
...ts/GenHub.Tests.Core/Features/Content/Services/Publishers/PublisherDefinitionServiceTests.cs
Comment on lines +20 to +47
public class PublisherDefinitionServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IPublisherCatalogParser> _catalogParserMock;
    private readonly Mock<ILogger<PublisherDefinitionService>> _loggerMock;
    private readonly PublisherDefinitionService _service;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;

    /// `<summary>`
    /// Initializes a new instance of the <see cref="PublisherDefinitionServiceTests"/> class.
    /// </summary>
    public PublisherDefinitionServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _catalogParserMock = new Mock<IPublisherCatalogParser>();
        _loggerMock = new Mock<ILogger<PublisherDefinitionService>>();

        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        _service = new PublisherDefinitionService(
            _httpClientFactoryMock.Object,
            _catalogParserMock.Object,
            _loggerMock.Object);
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider implementing IDisposable for HttpClient cleanup.

The test class creates an HttpClient instance (line 38) that wraps the mocked handler but is never disposed. While this won't cause issues in most test runners, implementing IDisposable is a good practice for resource cleanup.

Suggested improvement
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Tests/GenHub.Tests.Core/Features/Tools/PublisherStudioIntegrationTests.cs
Comment on lines +82 to +88
        var content = new CatalogContentItem
        {
            Id = "awesome-mod",
            Name = "Awesome Mod",
            Description = "A test mod",
        };
        project.Catalog.Content.Add(content);
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Test content item has no releases, inconsistent with validation rules.

The CatalogContentItem added to the catalog has no releases, but ValidateCatalogAsync in PublisherStudioService requires at least one release per content item. This test passes only because ValidateCatalogAsync is not called and the catalog parser mock returns success unconditionally. Consider adding a release to make this a more realistic integration test.

💚 Proposed enhancement for realistic test data
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/JsonPublisherCatalogParser.cs
Comment on lines +106 to +109
                if (string.IsNullOrWhiteSpace(content.Name))
                {
                    errors.Add($"Content item '{content.Id}' is missing name");
                }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Minor: Error message may reference empty ID.

When content.Id is missing (line 101-104 adds an error for that), the subsequent error at line 108 uses content.Id which would be empty/null, producing a less informative message like "Content item '' is missing name".

Consider using the index as fallback:

Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/JsonPublisherCatalogParser.cs
Comment on lines +158 to +171
    /// <inheritdoc />
    public bool VerifySignature(string catalogJson, PublisherCatalog catalog)
    {
        // TODO: Implement catalog signature verification
        // For now, signatures are optional
        if (string.IsNullOrEmpty(catalog.Signature))
        {
            _logger.LogDebug("No signature present in catalog");
            return true;
        }

        _logger.LogWarning("Signature verification not yet implemented");
        return true;
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Security stub: Signature verification always passes.

The VerifySignature method always returns true, even when a signature is present. This is documented with a TODO, but shipping this could allow tampered catalogs to pass validation if callers rely on this method for integrity checks.

Consider either:

Returning false when a signature is present but cannot be verified (fail-closed).
Adding a clear warning in the method documentation that callers should not rely on this for security until implemented.
Would you like me to open an issue to track the implementation of catalog signature verification?

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/PublisherCatalogRefreshService.cs
Comment on lines +24 to +49
    public async Task<OperationResult<bool>> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var subsResult = await subscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (!subsResult.Success) return OperationResult<bool>.CreateFailure(subsResult);

            var subscriptions = subsResult.Data!;
            var tasks = subscriptions.Select(s => RefreshPublisherAsync(s.PublisherId, cancellationToken));

            var results = await Task.WhenAll(tasks);
            var failures = results.Where(r => !r.Success).ToList();

            if (failures.Count > 0)
            {
                logger.LogWarning("Refreshed catalogs with {FailureCount} failures", failures.Count);
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh all catalogs");
            return OperationResult<bool>.CreateFailure($"Refresh failed: {ex.Message}");
        }
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

RefreshAllAsync returns success even when all refreshes fail.

The method logs a warning when failures occur but always returns CreateSuccess(true). Consider returning a partial failure or including failure details in the result.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/PublisherCatalogRefreshService.cs
            subscription.AvatarUrl = parseResult.Data?.Publisher.AvatarUrl ?? subscription.AvatarUrl;
            subscription.PublisherName = parseResult.Data?.Publisher.Name ?? subscription.PublisherName;

            await subscriptionStore.UpdateSubscriptionAsync(subscription, cancellationToken);
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Result of UpdateSubscriptionAsync is not checked.

If the update fails, the operation silently continues and returns success. Consider checking the result and logging or propagating the failure.

♻️ Proposed fix to check update result
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/ModDBDiscoverer.cs
Comment on lines +25 to +27
    private static readonly SemaphoreSlim _browserLock = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Static Playwright and browser resources are never disposed.

_playwright and _browser are static fields that are initialized but never cleaned up. This causes a resource leak since Playwright holds native browser processes. Consider implementing IAsyncDisposable or using a hosted service pattern with proper lifecycle management.

🔧 Suggested approach
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/ModDBDiscoverer.cs
Comment on lines +366 to +370
                catch
                {
                    // Ignore parse errors for individual items
                }
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Empty catch block silently swallows parse errors.

Failing to parse individual items is silently ignored without any logging. This makes debugging difficult when items are unexpectedly missing from results.

🐛 Proposed fix to log parse failures
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentDiscoverers/ModDBDiscoverer.cs
Comment on lines +372 to +406
            // NEW LOGGING:
            _logger.LogInformation("[ModDB] Pagination Logic Starting...");
            var pagesDiv = document.QuerySelector("div.pages");
            if (pagesDiv != null)
            {
                _logger.LogInformation("[ModDB] found div.pages. Html content length: {Length}", pagesDiv.InnerHtml.Length);
                var allLinks = pagesDiv.QuerySelectorAll("a");
                foreach (var link in allLinks)
                {
                    _logger.LogDebug(ex, "Failed to parse content item");
                    _logger.LogInformation("[ModDB] Link in pages: Text='{Text}', Href='{Href}', Class='{Class}'", link.TextContent?.Trim(), link.GetAttribute("href"), link.ClassName);
                }
            }
            else
            {
                _logger.LogWarning("[ModDB] div.pages NOT FOUND");
            }

            // Check for pagination "next" button
            // ModDB typically has a 'a.next' or 'span.next' inside a div.pages
            var nextLink = document.QuerySelector("div.pages a.next") ?? document.QuerySelector("a.next");

            if (nextLink == null)
            {
                 _logger.LogWarning("[ModDB] NEXT LINK IS NULL. Trying broader search...");
                 var anyNext = document.QuerySelectorAll("a").FirstOrDefault(a => a.TextContent != null && a.TextContent.Contains("next", StringComparison.OrdinalIgnoreCase));
                 if (anyNext != null)
                 {
                     _logger.LogInformation("[ModDB] Found a link containing 'next' (but not matching selector): Text='{Text}', Href='{Href}', Class='{Class}'", anyNext.TextContent, anyNext.GetAttribute("href"), anyNext.ClassName);
                 }
            }
            else
            {
                _logger.LogInformation("[ModDB] Found next link via selector: {Url}", nextLink.GetAttribute("href"));
            }

@coderabbitai
coderabbitai bot
2 days ago
🛠️ Refactor suggestion | 🟠 Major

Remove or reduce verbosity of debug logging.

This section contains extensive debug logging (logging every link found in pagination, HTML content lengths, etc.) that appears to be left over from development. This will create noisy logs in production.

♻️ Proposed cleanup
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/ContentResolvers/ModDBResolver.cs
Comment on lines +44 to +48
        // [TEMP] DEBUG: ResolveAsync entry point
        _logger.LogInformation(
            "[TEMP] ModDBResolver.ResolveAsync called - Item: {Name}, SourceUrl: {Url}",
            discoveredItem?.Name,
            discoveredItem?.SourceUrl);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Remove temporary debug logging before merge.

The [TEMP] prefixed debug logging should be removed before merging to the target branch. Consider using conditional compilation or a debug-level log that can be filtered in production.

🧹 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Parsers/ModDBPageParser.cs
Comment on lines +99 to +103
        var downloadUrl = linkEl?.GetAttribute("href");
        if (!string.IsNullOrEmpty(downloadUrl) && !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            downloadUrl = "https:" + downloadUrl;
        }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider extracting URL normalization to a helper method.

The pattern of checking !url.StartsWith("http") and prepending "https:" is repeated in multiple places (lines 100-103, 229-232, 244-247, 282-285, 316-318, 365-368, 723-726, 863-866). Extract to a reusable helper to reduce duplication and ensure consistent behavior.

♻️ Proposed helper method
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Parsers/ModDBPageParser.cs
Comment on lines +628 to +634
    /// <inheritdoc />
    public Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = browsingContext.OpenAsync(req => req.Content(html), cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(ParseInternal(url, document));
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Blocking call in async method can cause deadlocks.

Using .GetAwaiter().GetResult() inside an async method defeats the purpose of async and can cause deadlocks in UI or ASP.NET contexts. Since the method signature is already async Task<ParsedWebPage>, use await instead.

🐛 Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/CatalogTabViewModel.cs
Comment on lines +47 to +58
    public static CatalogTabViewModel CreateAllTab() => new()
    {
        CatalogId = "_all",
        CatalogName = "All",
        CatalogUrl = string.Empty,
        IsSelected = true
    };

    /// `<summary>`
    /// Gets whether this is the "All" tab.
    /// </summary>
    public bool IsAllTab => CatalogId == "_all";
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider centralizing the “_all” tab id as a constant.

Avoids string duplication across factory and predicate.

♻️ Suggested small refactor
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/StaticPublisherFilterViewModel.cs
Comment on lines +97 to +108
    private void InitializeContentTypeFilters()
    {
        ContentTypeFilters =
        [
            new ContentTypeFilterItem(ContentType.GameClient, "GameClient"),
            new ContentTypeFilterItem(ContentType.Mod, "Mod"),
            new ContentTypeFilterItem(ContentType.Patch, "Patch"),
            new ContentTypeFilterItem(ContentType.Addon, "Addon"),
            new ContentTypeFilterItem(ContentType.MapPack, "MapPack"),
            new ContentTypeFilterItem(ContentType.LanguagePack, "LanguagePack"),
        ];
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Consider user-friendly display names.

The display names currently match the enum names verbatim (e.g., "GameClient", "MapPack"). For better UX, consider using spaced/formatted names:

Suggested improvement
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/DependencyPromptDialog.axaml
Comment on lines +126 to +132
                                <!-- Empty State -->
                                <TextBlock Text="No missing dependencies"
                                           FontSize="12"
                                           Foreground="#888888"
                                           HorizontalAlignment="Center"
                                           Margin="15,20"
                                           IsVisible="{Binding Dependencies, Converter={x:Static ObjectConverters.IsNull}}" />
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
Empty-state message won't appear for empty collections.

ObjectConverters.IsNull only detects null values, not empty collections. Since Dependencies is initialized as an empty ObservableCollection (not null), the "No missing dependencies" message will fail to display when the collection is empty, leaving users with a blank list panel. Add a HasNoDependencies property to the view model (returning MissingCount == 0 or Dependencies.Count == 0) and bind to that instead.

🔧 Suggested binding change
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/DownloadsBrowserView.axaml
                            Margin="8,0,0,0"
                            VerticalAlignment="Center" />

                    <!-- Older Versions Toggle (Removed) -->
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Remove stale comment.

The comment <!-- Older Versions Toggle (Removed) --> references removed functionality. Either remove the comment or add context about why it was removed if it's intentionally documented.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/DownloadsBrowserView.axaml
Comment on lines +264 to +282
                            <TextBlock Text="⟳"
                                       FontSize="24"
                                       Foreground="#9C27B0"
                                       Margin="0,0,10,0">
                                <TextBlock.Styles>
                                    <Style Selector="TextBlock">
                                        <Style.Animations>
                                            <Animation Duration="0:0:1" IterationCount="Infinite">
                                                <KeyFrame KeyTime="0:0:0">
                                                    <Setter Property="RotateTransform.Angle" Value="0"/>
                                                </KeyFrame>
                                                <KeyFrame KeyTime="0:0:1">
                                                    <Setter Property="RotateTransform.Angle" Value="360"/>
                                                </KeyFrame>
                                            </Animation>
                                        </Style.Animations>
                                    </Style>
                                </TextBlock.Styles>
                            </TextBlock>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Spinner animation may not rotate around center.

The RotateTransform.Angle animation is applied, but without a RenderTransformOrigin set to "0.5,0.5", the TextBlock will rotate around its top-left corner instead of its center.

🐛 Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/FilterPanelView.axaml
Comment on lines +90 to +101
                        <Button Classes="filter-toggle"
                                Content="Downloads"
                                Command="{Binding SetSectionCommand}"
                                CommandParameter="{x:Static filters:ModDBSection.Downloads}" />
                        <Button Classes="filter-toggle"
                                Content="Addons"
                                Command="{Binding SetSectionCommand}"
                                CommandParameter="{x:Static filters:ModDBSection.Addons}" />
                        <Button Classes="filter-toggle"
                                Content="Mods"
                                Command="{Binding SetSectionCommand}"
                                CommandParameter="{x:Static filters:ModDBSection.Mods}" />
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Style mismatch: Button used where ToggleButton style is defined.

The ModDB section toggle buttons (lines 90-101) use <Button Classes="filter-toggle">, but the styles defined in lines 13-38 target ToggleButton.filter-toggle. These buttons won't receive the intended styling (background colors, checked states, etc.).

Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/FilterPanelView.axaml
Comment on lines +121 to +134
                        <!-- Addon Category -->
                        <StackPanel>
                            <TextBlock Classes="filter-label" Text="Addon Category" />
                            <ComboBox Classes="filter-dropdown"
                                      ItemsSource="{Binding AddonCategoryOptions}"
                                      SelectedValue="{Binding SelectedAddonCategory, Mode=TwoWay}"
                                      SelectedValueBinding="{Binding Value}">
                                <ComboBox.ItemTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding DisplayName}" />
                                    </DataTemplate>
                                </ComboBox.ItemTemplate>
                            </ComboBox>
                        </StackPanel>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Addon Category dropdown always visible.

The Addon Category dropdown (lines 122-134) doesn't have an IsVisible binding, so it's always visible regardless of the selected section. Based on the pattern of other dropdowns (Category has ShowCategoryFilter, License has ShowLicenseFilter), this should likely have visibility controlled by the selected section.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/FilterPanelView.axaml
Comment on lines +319 to +340
                            <ToggleButton Classes="filter-toggle" Content="All" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter={x:Null}}" Command="{Binding SetPlayerCountCommand}" CommandParameter="{x:Null}" />
                            <ToggleButton Classes="filter-toggle" Content="2" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='2 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="2 Players" />
                            <ToggleButton Classes="filter-toggle" Content="3" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='3 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="3 Players" />
                            <ToggleButton Classes="filter-toggle" Content="4" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='4 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="4 Players" />
                            <ToggleButton Classes="filter-toggle" Content="5" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='5 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="5 Players" />
                            <ToggleButton Classes="filter-toggle" Content="6" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='6 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="6 Players" />
                            <ToggleButton Classes="filter-toggle" Content="7" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='7 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="7 Players" />
                            <ToggleButton Classes="filter-toggle" Content="8" IsChecked="{Binding SelectedPlayerCount, Converter={StaticResource EqualityConverter}, ConverterParameter='8 Players'}" Command="{Binding SetPlayerCountCommand}" CommandParameter="8 Players" />
                        </StackPanel>
                    </StackPanel>

                    <!-- Categories -->
                    <StackPanel>
                        <TextBlock Classes="filter-label" Text="Categories" />
                        <WrapPanel Orientation="Horizontal">
                            <ToggleButton Classes="filter-toggle" Content="Any" IsChecked="{Binding SelectedCategory, Converter={StaticResource EqualityConverter}, ConverterParameter={x:Null}}" Command="{Binding SetCategoryCommand}" CommandParameter="{x:Null}" />
                            <ToggleButton Classes="filter-toggle" Content="Compstomp" IsChecked="{Binding SelectedCategory, Converter={StaticResource EqualityConverter}, ConverterParameter='Compstomp'}" Command="{Binding SetCategoryCommand}" CommandParameter="Compstomp" />
                            <ToggleButton Classes="filter-toggle" Content="AOA" IsChecked="{Binding SelectedCategory, Converter={StaticResource EqualityConverter}, ConverterParameter='AOA'}" Command="{Binding SetCategoryCommand}" CommandParameter="AOA" />
                            <ToggleButton Classes="filter-toggle" Content="Race" IsChecked="{Binding SelectedCategory, Converter={StaticResource EqualityConverter}, ConverterParameter='Race'}" Command="{Binding SetCategoryCommand}" CommandParameter="Race" />
                            <ToggleButton Classes="filter-toggle" Content="Air" IsChecked="{Binding SelectedCategory, Converter={StaticResource EqualityConverter}, ConverterParameter='Air'}" Command="{Binding SetCategoryCommand}" CommandParameter="Air" />
                            <ToggleButton Classes="filter-toggle" Content="Contra" IsChecked="{Binding SelectedCategory, Converter={StaticResource EqualityConverter}, ConverterParameter='Contra'}" Command="{Binding SetCategoryCommand}" CommandParameter="Contra" />
                        </WrapPanel>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

Missing resource: EqualityConverter not defined.

The AODMaps filter template references {StaticResource EqualityConverter} multiple times (lines 319-327, 334-340), but this converter is not defined in the UserControl.Resources section. This will cause a runtime exception when the AODMaps filter is displayed.

Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/PublisherSidebarView.axaml
Comment on lines +95 to +104
                                        <Border Width="22" Height="22"
                                                CornerRadius="11"
                                                Background="#25FFFFFF"
                                                IsVisible="{Binding LogoSource, Converter={x:Static StringConverters.IsNullOrEmpty}}">
                                            <TextBlock Text="{Binding DisplayName, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                                                       FontSize="10"
                                                       Foreground="White"
                                                       HorizontalAlignment="Center"
                                                       VerticalAlignment="Center" />
                                        </Border>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

Bug: Fallback icon displays "True"/"False" instead of initials.

The Text binding on line 99 uses StringConverters.IsNotNullOrEmpty which returns a boolean. This will display "True" or "False" as the fallback text instead of the publisher's initials.

Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/PublisherSidebarView.axaml
            </ItemsControl>

            <!-- Subscribed Publishers -->
            <StackPanel IsVisible="{Binding SubscribedPublishers.Count}">
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Use a converter or computed property to bind int count to bool visibility.

Avalonia does not automatically convert int to bool for bindings. The current binding {Binding SubscribedPublishers.Count} requires explicit conversion. Use one of these approaches:

Add a computed property HasSubscribedPublishers that returns true when count > 0, then bind to that
Use Avalonia's double-bang operator: {Binding !!SubscribedPublishers.Count} (converts non-zero to true)
Create a custom IValueConverter if you need different logic
Proposed fixes
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/PublisherSidebarView.axaml
Comment on lines +161 to +170
                                             <Border Width="22" Height="22"
                                                     CornerRadius="11"
                                                     Background="#25FFFFFF"
                                                     IsVisible="{Binding LogoSource, Converter={x:Static StringConverters.IsNullOrEmpty}}">
                                                 <TextBlock Text="S"
                                                            FontSize="10"
                                                            Foreground="White"
                                                            HorizontalAlignment="Center"
                                                            VerticalAlignment="Center" />
                                             </Border>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Inconsistency: Subscribed publishers fallback shows hardcoded "S".

The subscribed publishers section uses a hardcoded "S" for the fallback icon (line 165), while the Core Providers section attempts to show dynamic text (even though that's currently buggy). Consider using consistent logic between both sections - likely showing the first character of the publisher name.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Settings/ViewModels/SettingsViewModel.cs
Comment on lines +1696 to +1720
    [RelayCommand]
    private async Task LoadSubscriptionsAsync()
    {
        try
        {
            IsLoadingSubscriptions = true;
            var result = await _subscriptionStore.GetSubscriptionsAsync();
            if (result.Success && result.Data != null)
            {
                Subscriptions.Clear();
                foreach (var sub in result.Data.OrderBy(s => s.PublisherName))
                {
                    Subscriptions.Add(sub);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load subscriptions");
        }
        finally
        {
            IsLoadingSubscriptions = false;
        }
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Handle failed subscription loads with user feedback and stale-state reset.

If GetSubscriptionsAsync fails or returns Data == null, the UI keeps the previous list and the user gets no feedback.

🔧 Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Settings/ViewModels/SettingsViewModel.cs
Comment on lines +1747 to +1772
    [RelayCommand]
    private async Task ToggleSubscriptionTrustAsync(PublisherSubscription subscription)
    {
        if (subscription == null) return;

        try
        {
            var newTrust = subscription.TrustLevel == TrustLevel.Trusted
                ? TrustLevel.Untrusted
                : TrustLevel.Trusted;

            var result = await _subscriptionStore.UpdateTrustLevelAsync(subscription.PublisherId, newTrust);
            if (result.Success)
            {
                subscription.TrustLevel = newTrust;

                // Force UI update if needed (PublisherSubscription should implement INotifyPropertyChanged)
                // If it doesn't, we might need a wrapper VM or manual notification
                OnPropertyChanged(nameof(Subscriptions));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update trust level");
        }
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Surface trust-update failures to the user.

Currently, failures are silently logged only. Consider notifying the user when the update fails.

🔧 Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioDialogService.cs
Comment on lines +56 to +64
    public async Task<PublisherReferral?> ShowAddReferralDialogAsync()
    {
        // Get available publishers from subscriptions (for now, we'll include static known publishers)
        // TODO: Inject IPublisherSubscriptionStore and fetch actual subscriptions
        var availablePublishers = GetKnownPublishers();

        return await ShowDialogAsync<AddReferralDialogViewModel, AddReferralDialogView, PublisherReferral>(
            (Action<PublisherReferral> callback) => new AddReferralDialogViewModel(callback, availablePublishers));
    }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

TODO: Inject IPublisherSubscriptionStore.

The TODO on line 59 notes that actual subscriptions should be fetched instead of using static known publishers. This would allow users to reference their existing subscriptions when creating referrals.

Would you like me to open an issue to track the implementation of dynamic publisher fetching from IPublisherSubscriptionStore?

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioDialogService.cs
Comment on lines +69 to +79
    private static List<PublisherReferralOption> GetKnownPublishers()
    {
        return new List<PublisherReferralOption>
        {
            new() { PublisherId = "moddb", PublisherName = "ModDB", CatalogUrl = "<https://api.moddb.com/catalog.json"> },
            new() { PublisherId = "cnclabs", PublisherName = "CNC Labs", CatalogUrl = "<https://github.com/CnC-Labs/mods-catalog/raw/main/catalog.json"> },
            new() { PublisherId = "generals", PublisherName = "Generals", CatalogUrl = "<https://example.com/generals/catalog.json"> },

            // Add more known publishers as needed
        };
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Placeholder URL will fail in production.

Line 75 contains a placeholder URL <https://example.com/generals/catalog.json> that will fail when users try to add this publisher as a referral. Either remove this entry or replace with a valid URL.

Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioDialogService.cs
Comment on lines +176 to +184

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
           await window.ShowDialog(mainWindow);
        }

        return await tcs.Task;
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

Task may never complete when mainWindow is null.

In ShowWizardAsync, if GetMainWindow() returns null (lines 177-178), the method proceeds directly to await tcs.Task (line 183) without ever calling tcs.TrySetResult(). This will cause the calling code to hang indefinitely.

Compare with ShowDialogAsync (line 145) which correctly handles this case.

Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/Services/PublisherStudioService.cs
Comment on lines +177 to +182
            // Validate publisher ID format (lowercase, alphanumeric, hyphens)
            if (!System.Text.RegularExpressions.Regex.IsMatch(catalog.Publisher.Id, "^[a-z0-9-]+$"))
            {
                return OperationResult<bool>.CreateFailure(
                    "Publisher ID must be lowercase alphanumeric with hyphens only");
            }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Cache the Regex for better performance.

Regex.IsMatch with a string pattern creates a new Regex instance on each call. For validation that may be called frequently, use a compiled static Regex.

♻️ Proposed fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/ViewModels/Dialogs/AddArtifactDialogViewModel.cs
Comment on lines +154 to +159
        if (HasErrors)
        {
            ValidationError = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));
            IsValid = false;
            return;
        }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

ErrorMessage from ValidationResult can be null.

GetErrors() returns ValidationResult objects whose ErrorMessage property is nullable. Joining them directly with Select(e => e.ErrorMessage) could produce null entries in the resulting string.

🛠️ Proposed fix
Also applies to: 191-197

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/ViewModels/Dialogs/AddArtifactDialogViewModel.cs
Comment on lines +162 to +167
        if (UseExistingUrl && !Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri))
        {
            ValidationError = "Please enter a valid download URL";
            IsValid = false;
            return;
        }
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused variable uri after validation.

The uri variable is created during URL validation but never used. If you only need to validate format, consider using the result directly or removing the out variable.

♻️ Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Tools/ViewModels/Dialogs/AddArtifactDialogViewModel.cs
Comment on lines +186 to +189
    private void Close()
    {
        _onArtifactCreated(null!);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Passing null! to a non-nullable callback is a code smell.

Using null! to suppress the compiler warning for passing null to Action<ReleaseArtifact> defeats nullability analysis. Callers expecting a non-null ReleaseArtifact will encounter a NullReferenceException if they don't guard against this.

Consider changing the callback signature to Action<ReleaseArtifact?> to make cancellation explicit, or use a separate cancellation mechanism.

🛠️ Proposed fix - make callback nullable
🤖 Prompt for AI Agents
@undead2146    Reply...
coderabbitai[bot]
coderabbitai bot requested changes 2 days ago
coderabbitai bot
left a comment
Review continued from previous batch...

GenHub/GenHub.Core/Models/Providers/PublisherDefinition.cs
Comment on lines +41 to +71
    [JsonPropertyName("catalogUrl")]
    public string CatalogUrl
    {
        get => Catalogs.Count > 0 ? Catalogs[0].Url : string.Empty;
        set
        {
            if (Catalogs.Count == 0)
            {
                Catalogs.Add(new CatalogEntry { Id = "default", Name = "Content" });
            }
            Catalogs[0].Url = value;
        }
    }

    /// `<summary>`
    /// Gets or sets alternate catalog URLs for redundancy.
    /// Computed property for convenience - accesses Catalogs[0].
    /// </summary>
    [JsonPropertyName("catalogMirrors")]
    public List<string> CatalogMirrors
    {
        get => Catalogs.Count > 0 ? Catalogs[0].Mirrors : [];
        set
        {
            if (Catalogs.Count == 0)
            {
                Catalogs.Add(new CatalogEntry { Id = "default", Name = "Content" });
            }
            Catalogs[0].Mirrors = value;
        }
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Computed properties with JSON serialization may cause deserialization conflicts.

CatalogUrl and CatalogMirrors are both marked with [JsonPropertyName] and stored as computed views over Catalogs[0]. During deserialization:

If JSON contains both catalogUrl and catalogs, the order of property assignment is undefined, potentially causing data inconsistency.
During serialization, both catalogUrl and catalogs[0].url are written, creating redundant data.
Consider using [JsonIgnore] on the computed properties and keeping them as convenience accessors only, or use a custom converter.

🔧 Proposed fix: ignore computed properties during serialization
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Models/Providers/PublisherDefinition.cs
Comment on lines +60 to +62
    public List<string> CatalogMirrors
    {
        get => Catalogs.Count > 0 ? Catalogs[0].Mirrors : [];
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

CatalogMirrors getter returns new list instance when Catalogs is empty.

When Catalogs.Count == 0, the getter returns [] (a new empty list each call). Any modifications by the caller won't persist, which may be unexpected. Consider returning a consistent reference or documenting this behavior.

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub.Core/Services/Publishers/PublisherDefinitionService.cs
Comment on lines +237 to +241
            foreach (var catalogEntry in definition.Catalogs)
            {
                var urlsToTry = new List<string> { catalogEntry.Url };
                urlsToTry.AddRange(catalogEntry.Mirrors);

@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Null‑guard catalog mirrors before AddRange

Catalog definitions are external input; if Mirrors deserializes to null, AddRange throws and stops catalog fetch. Guard for null before adding.

🛡️ Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
...ests/GenHub.Tests.Core/Features/Content/Services/Catalog/SubscriptionFlowIntegrationTests.cs
Comment on lines +71 to +74
        // Arrange - Create a subscription store with a temp file
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        var store = new PublisherSubscriptionStore(_storeLoggerMock.Object, mockConfigProvider.Object);

@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

🧩 Analysis chain
Configure the config provider mock to return a valid application data path

The PublisherSubscriptionStore constructor immediately calls GetApplicationDataPath() on the config provider (line 43 of the store). With an unconfigured mock, this returns null, which causes Path.Combine() to throw an ArgumentNullException before the test can even run. Configure the mock to return a temporary directory path so the store can initialize and perform file operations.

Example fix:

var mockConfigProvider = new Mock<IConfigurationProviderService>();
mockConfigProvider.Setup(x => x.GetApplicationDataPath())
    .Returns(Path.Combine(Path.GetTempPath(), "GenHub-Test"));
var store = new PublisherSubscriptionStore(_storeLoggerMock.Object, mockConfigProvider.Object);
Also applies to: 222-224, 253-255

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/CrossPublisherDependencyResolver.cs
Comment on lines +114 to +121
            // Check size limit
            if (response.Content.Headers.ContentLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            var catalogJson = await response.Content.ReadAsStringAsync(cancellationToken);
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Content-Length check can be bypassed; actual read is unbounded.

The Content-Length header may be absent, spoofed, or inaccurate. When ContentLength is null, the check passes, but ReadAsStringAsync will still read the entire response into memory, potentially causing OOM with a malicious or oversized response.

🛡️ Proposed fix: Use GetStringAsync with HttpCompletionOption or stream with size limit
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/CrossPublisherDependencyResolver.cs
Comment on lines +160 to +170
            // Extract publisher ID from dependency ID
            // Dependency ID format: schemaVersion.userVersion.publisher.contentType.contentName
            var idParts = dependency.Id.Value.Split('.');
            if (idParts.Length < 5)
            {
                return OperationResult<ContentSearchResult?>.CreateFailure(
                    $"Invalid dependency ID format: {dependency.Id}");
            }

            var publisherId = idParts[2];
            var contentName = idParts[4];
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Fragile dependency ID parsing with magic indices.

The parsing assumes a fixed format schemaVersion.userVersion.publisher.contentType.contentName with exactly 5 parts. This is brittle if IDs contain additional dots (e.g., in version strings or content names) or if the format evolves.

Consider extracting this logic to a dedicated parser method or using a well-defined ID structure with explicit delimiters or a structured type.

♻️ Suggested approach
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/CrossPublisherDependencyResolver.cs
Comment on lines +213 to +225
            // Get the latest release
            var latestRelease = matchingContent.Releases
                .Where(r => r.IsLatest && !r.IsPrerelease)
                .OrderByDescending(r => r.ReleaseDate)
                .FirstOrDefault();

            if (latestRelease == null)
            {
                _logger.LogWarning(
                    "No stable release found for content {ContentName}",
                    contentName);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Release selection may miss valid stable releases.

The filter requires both IsLatest and !IsPrerelease. If the publisher hasn't set IsLatest on any release, or only on a prerelease, no release will be found even if stable versions exist. Consider falling back to the most recent stable release by date when no explicit IsLatest stable release exists.

🔧 Proposed fallback logic
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogDiscoverer.cs
Comment on lines +41 to +62
    private static bool MatchesQuery(CatalogContentItem content, ContentSearchQuery query)
    {
        // Filter by game type
        if (query.TargetGame.HasValue && content.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        // Filter by content type
        if (query.ContentType.HasValue && content.ContentType != query.ContentType.Value)
        {
            return false;
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchLower = query.SearchTerm.ToLowerInvariant();
            if (!content.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase) &&
                !content.Description.Contains(searchLower, StringComparison.OrdinalIgnoreCase) &&
                !content.Tags.Any(t => t.Contains(searchLower, StringComparison.OrdinalIgnoreCase)))
            {
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Protect filtering and tag iteration against null catalog fields

Catalog JSON is external; if Name, Description, or Tags deserialize as null, MatchesQuery and tag iteration will throw. Add null coalescing to keep discovery resilient.

🛡️ Suggested fix
Also applies to: 246-250

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/PublisherSubscriptionStore.cs
Comment on lines +43 to +44
        var appDataPath = _configurationProvider.GetApplicationDataPath();
        _subscriptionsFilePath = Path.Combine(appDataPath, "subscriptions.json");
@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Hardcoded filename instead of using constant.

The filename "subscriptions.json" is hardcoded, but CatalogConstants.SubscriptionFileName exists for this purpose (as shown in the relevant code snippets). Use the constant for consistency.

♻️ Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Catalog/PublisherSubscriptionStore.cs
Comment on lines +240 to +261
    private async Task<PublisherSubscriptionCollection> LoadSubscriptionsAsync(CancellationToken cancellationToken)
    {
        // Return cached if available
        if (_cachedSubscriptions != null)
        {
            return _cachedSubscriptions;
        }

        if (!File.Exists(_subscriptionsFilePath))
        {
            _logger.LogInformation("Subscriptions file not found, creating new collection");
            _cachedSubscriptions = new PublisherSubscriptionCollection();
            return _cachedSubscriptions;
        }

        var json = await File.ReadAllTextAsync(_subscriptionsFilePath, cancellationToken);
        _cachedSubscriptions = JsonSerializer.Deserialize<PublisherSubscriptionCollection>(json)
            ?? new PublisherSubscriptionCollection();

        _logger.LogDebug("Loaded {Count} subscriptions from file", _cachedSubscriptions.Subscriptions.Count);
        return _cachedSubscriptions;
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Cache read in LoadSubscriptionsAsync is not protected by lock.

LoadSubscriptionsAsync is called from both read methods (without lock) and write methods (with lock). The method reads and writes _cachedSubscriptions without synchronization:

Line 243-246: null check and return can race with a concurrent write updating the cache.
Line 251 & 256: Setting _cachedSubscriptions can race with concurrent reads.
While reference assignments are atomic in C#, a read method could observe a partially updated state if called during the window between file write and cache update in SaveSubscriptionsAsync.

🔧 Suggested improvement: use volatile or lock for reads
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Tools/PlaywrightService.cs
Comment on lines +39 to +41
        var context = await _browser.NewContextAsync(contextOptions);
        return await context.NewPageAsync();
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

BrowserContext is leaked; page close doesn't dispose the context.

CreatePageAsync creates a new BrowserContext for each page, but the callers (e.g., FetchHtmlAsync) only call page.CloseAsync(), which does not dispose the parent context. This causes resource accumulation over time.

🐛 Proposed fix: close context after page, or return context for caller management
Also applies to: 63-67

🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Tools/PlaywrightService.cs
Comment on lines +85 to +101
    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        if (_playwright != null)
        {
            _playwright.Dispose();
            _playwright = null;
        }

        // Note: _browserLock is static and lives for app lifetime; not disposed here
        GC.SuppressFinalize(this);
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🔴 Critical

DisposeAsync disposes shared static resources, breaking other service instances.

The _playwright and _browser fields are static (shared across all instances), but DisposeAsync is an instance method that nullifies and disposes them. If this service is registered with scoped or transient lifetime, the first instance to dispose will break all other instances.

Consider either:

Making the service a singleton with application-lifetime management
Using reference counting for the static resources
Moving disposal to application shutdown only
🐛 Minimal fix: prevent instance-level disposal of static resources
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/Services/Tools/PlaywrightService.cs
Comment on lines +206 to +210
            {
                // Unsubscribe and close
                page.Download -= DownloadHandler;
                await page.CloseAsync();
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Context not closed in DownloadFileAsync finally block.

Similar to FetchHtmlAsync, the context created for download handling is not closed. The page is closed but its parent context leaks.

🐛 Proposed fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Content/ViewModels/Catalog/SubscriptionConfirmationViewModel.cs
Comment on lines +63 to +73
    [ObservableProperty]
    private ObservableCollection<SelectableCatalogEntry> _availableCatalogs = new();

    private PublisherCatalog? _parsedCatalog;
    private PublisherDefinition? _parsedDefinition;
    private bool _isProviderDefinition;

    /// `<summary>`
    /// Gets whether this is a multi-catalog definition.
    /// </summary>
    public bool HasMultipleCatalogs => AvailableCatalogs.Count > 1;
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Keep multi‑catalog UI state in sync and prevent empty selections

HasMultipleCatalogs is a computed property that won't notify UI when AvailableCatalogs is populated, preventing the multi‑catalog picker from displaying. Additionally, CanConfirm is set to true unconditionally (line 119) regardless of selected catalogs, allowing provider-definition subscriptions with zero catalogs enabled.

Update HasMultipleCatalogs after populating catalogs and validate that at least one catalog is selected before permitting confirmation.

🛠️ Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/ContentDetailViewModel.cs
Comment on lines +414 to +498
            // 2. Prepare Temp Directory
            var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "Downloads", manifest.Id.Value);
            Directory.CreateDirectory(tempDir);

            // 3. Download Files
            DownloadStatusMessage = "Downloading files...";
            var remoteFiles = manifest.Files.Where(f => f.SourceType == ContentSourceType.RemoteDownload).ToList();

            if (manifest.Files.Count == 0)
            {
                _logger.LogWarning("Manifest contains no files");
                DownloadStatusMessage = "Error: Manifest has no files";
                return;
            }

            if (remoteFiles.Count == 0)
            {
                _logger.LogInformation("No remote files to download in manifest (content might be pre-downloaded or in CAS)");
            }

            foreach (var file in remoteFiles)
            {
                if (string.IsNullOrEmpty(file.SourcePath)) continue;

                // Use RelativePath if available, otherwise extract from SourcePath
                var fileName = !string.IsNullOrEmpty(file.RelativePath)
                    ? Path.GetFileName(file.RelativePath)
                    : Path.GetFileName(file.SourcePath);
                var targetPath = Path.Combine(tempDir, fileName);

                DownloadStatusMessage = $"Downloading {fileName}...";

                var downloadResult = await _downloadService.DownloadFileAsync(
                    new Uri(file.SourcePath),
                    targetPath,
                    null,
                    new Progress<DownloadProgress>(p =>
                    {
                        // Map 0-100 progress
                         Avalonia.Threading.Dispatcher.UIThread.Post(() => DownloadProgress = (int)p.Percentage);
                    }),
                    cancellationToken);

                if (!downloadResult.Success)
                {
                    _logger.LogError("Failed to download file {Url}: {Error}", file.SourcePath, downloadResult.FirstError);
                    DownloadStatusMessage = $"Error downloading {fileName}";
                    return;
                }
            }

            // 4. Store Manifest
            // Store the manifest in the pool
            DownloadStatusMessage = "Storing manifest...";
            if (_serviceProvider.GetService(typeof(IContentManifestPool)) is not IContentManifestPool manifestPool)
            {
                _logger.LogError("IContentManifestPool service not available");
                DownloadStatusMessage = "Error: Manifest storage service not available";
                return;
            }

            // Pass the temp directory as the source directory
            var addResult = await manifestPool.AddManifestAsync(manifest, tempDir, null, cancellationToken);

            if (!addResult.Success)
            {
                _logger.LogError("Failed to store manifest: {Error}", addResult.FirstError);
                DownloadStatusMessage = $"Error: {addResult.FirstError}";
                return;
            }

            // 5. Cleanup (Optional, assuming pool copied files or moved them.
            // If pool copies, we delete. If moves, directory might be empty or gone.
            // Safe to try delete if exists.)
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp download directory {Dir}", tempDir);
            }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Temp download directories are leaked on error paths.
After creating tempDir, several early returns (download/manifest failures) bypass cleanup. Wrap the download block in try/finally so the temp directory is removed even when the operation fails.

💡 Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/ContentDetailViewModel.cs
Comment on lines +528 to +540
    private async Task DownloadFileAsync(WebFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
        {
            _logger.LogWarning("Cannot download file: invalid file or missing download URL");
            return;
        }

        try
        {
            _logger.LogInformation("Downloading individual file: {FileName} from {Url}", file.Name, file.DownloadUrl);
            await DownloadAsync(cancellationToken);
        }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

DownloadFileAsync ignores the requested file.
The command logs the file info but calls DownloadAsync, which downloads the full content instead of the selected file. Download file.DownloadUrl directly (or rename the command to reflect the full download).

💡 Suggested fix
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/ContentGridItemViewModel.cs
Comment on lines +56 to +63
    public ContentGridItemViewModel(ContentSearchResult searchResult, ILogger<ContentGridItemViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        SearchResult = searchResult;
        _logger = logger;
        _logger?.LogInformation("[TEMP] ContentGridItemViewModel created - Item: {Name}, Provider: {Provider}", searchResult.Name, searchResult.ProviderName);
        _ = LoadIconAsync();
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

🧩 Analysis chain
Hook CollectionChanged to notify when variants are added or removed

HasVariants is a computed property derived from Variants.Count, but when items are added/removed from the ObservableCollection, the property notification doesn't fire. The collection mutation doesn't reassign the _variants field, so the [ObservableProperty] setter never executes. Subscribe to CollectionChanged and call OnPropertyChanged(nameof(HasVariants)) to ensure bindings stay current.

🛠️ Suggested fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/DependencyPromptViewModel.cs
Comment on lines +46 to +60
    public DependencyPromptViewModel(
        string contentName,
        string contentVersion,
        System.Collections.Generic.IEnumerable<MissingDependency> missingDependencies,
        Action<DependencyDecision> onDecision)
    {
        ContentName = contentName;
        ContentVersion = contentVersion;
        _onDecision = onDecision ?? throw new ArgumentNullException(nameof(onDecision));

        var missingList = missingDependencies.ToList();
        MissingCount = missingList.Count;
        OptionalCount = missingList.Count(d => d.Dependency.IsOptional);
        CanAutoInstallAll = missingList.All(d => d.CanAutoInstall);

@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Guard null dependency list and avoid enabling auto-install when empty

missingDependencies.ToList() will throw if null, and All(...) makes CanAutoInstallAll true when the list is empty. Add a null guard (or treat null as empty) and require at least one dependency before enabling auto-install.

🛡️ Suggested fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/DownloadsBrowserViewModel.cs
Comment on lines +148 to +197
    partial void OnSelectedPublisherChanged(PublisherItemViewModel? value)
    {
        // Update selection state
        foreach (var publisher in Publishers)
        {
            publisher.IsSelected = publisher == value;
        }

        // Clear previous filter state
        if (CurrentFilterViewModel != null)
        {
            CurrentFilterViewModel.FiltersApplied -= OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared -= OnFiltersCleared;
            CurrentFilterViewModel.ClearFilters();
        }

        // Switch filter panel
        if (value != null && _filterViewModels.TryGetValue(value.PublisherId, out var filterVm))
        {
            CurrentFilterViewModel = filterVm;
            CurrentFilterViewModel.FiltersApplied += OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared += OnFiltersCleared;
        }
        else
        {
            CurrentFilterViewModel = null;
        }

        // Reset pagination state
        CurrentPage = 1;
        CanLoadMore = false;
        CanLoadOlderReleases = false;
        _superHackersLoadedCount = 0;

        // Clear content items when switching publishers
        ContentItems.Clear();

        // Close detail view
        SelectedContent = null;

        // Clear catalog tabs when switching publishers (will be repopulated if needed)
        CatalogTabs.Clear();
        SelectedCatalogTab = null;
        OnPropertyChanged(nameof(ShowCatalogTabs));

        // Only load content if this publisher hasn't been loaded yet
        if (value != null && !_loadedPublishers.Contains(value.PublisherId))
        {
            _ = RefreshContentAsync();
        }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟠 Major

Switching back to a previously loaded publisher leaves the grid empty.
ContentItems is cleared on every publisher change, but refresh is skipped for publishers already in _loadedPublishers. Returning to a previously visited publisher therefore clears items and skips reload. Either always refresh on selection or cache/restore per‑publisher content.

💡 Suggested fix (always refresh on selection)
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/AODMapsFilterViewModel.cs
Comment on lines +1 to +8
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

@coderabbitai
coderabbitai bot
2 days ago
🧹 Nitpick | 🔵 Trivial

Unused import.

System.Collections.ObjectModel is imported but not used in this file.

🧹 Remove unused import
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/CNCLabsFilterViewModel.cs
Comment on lines +21 to +95
    [ObservableProperty]
    private GameType? _targetGame = GameType.ZeroHour;

    /// `<summary>`
    /// Gets the collection of map tag filter items.
    /// </summary>
    public ObservableCollection<MapTagFilterItem> MapTagFilters { get; } =
    [
        new MapTagFilterItem(CNCLabsConstants.TagCramped, "1", "Map size"),
        new MapTagFilterItem(CNCLabsConstants.TagSpacious, "2", "Map size"),
        new MapTagFilterItem(CNCLabsConstants.TagWellBalanced, "3", "Layout"),
        new MapTagFilterItem(CNCLabsConstants.TagMoneyMap, "4", "Economy"),
        new MapTagFilterItem(CNCLabsConstants.TagDetailed, "5", "Quality"),
        new MapTagFilterItem(CNCLabsConstants.TagCustomScripted, "6", "Features"),
        new MapTagFilterItem(CNCLabsConstants.TagSymmetric, "7", "Layout"),
        new MapTagFilterItem(CNCLabsConstants.TagArtOfDefense, "8", "Mode"),
        new MapTagFilterItem(CNCLabsConstants.TagMultiplayerOnly, "9", "Mode"),
        new MapTagFilterItem(CNCLabsConstants.TagAsymmetric, "10", "Layout"),
        new MapTagFilterItem(CNCLabsConstants.TagNoobFriendly, "11", "Difficulty"),
        new MapTagFilterItem(CNCLabsConstants.TagVeteranSuitable, "12", "Difficulty"),
        new MapTagFilterItem(CNCLabsConstants.TagFunMap, "13", "Style"),
        new MapTagFilterItem(CNCLabsConstants.TagArtOfAttack, "14", "Mode"),
        new MapTagFilterItem(CNCLabsConstants.TagShellMap, "15", "Type"),
        new MapTagFilterItem(CNCLabsConstants.TagPortedMissionToZH, "16", "Type"),
        new MapTagFilterItem(CNCLabsConstants.TagCustomCoded, "17", "Features"),
        new MapTagFilterItem(CNCLabsConstants.TagCoopMission, "18", "Mode"),
    ];

    [ObservableProperty]
    private ContentType? _selectedContentType = ContentType.Map; // Default to Map

    [ObservableProperty]
    private int? _numberOfPlayers;

    /// `<summary>`
    /// Gets the available player count options.
    /// </summary>
    public ObservableCollection<PlayerOption> PlayerOptions { get; } =
    [
        new PlayerOption(CNCLabsConstants.PlayerOptionAny, null),
        new PlayerOption(CNCLabsConstants.PlayerOption1Player, 1),
        new PlayerOption(CNCLabsConstants.PlayerOption2Players, 2),
        new PlayerOption(CNCLabsConstants.PlayerOption3Players, 3),
        new PlayerOption(CNCLabsConstants.PlayerOption4Players, 4),
        new PlayerOption(CNCLabsConstants.PlayerOption5Players, 5),
        new PlayerOption(CNCLabsConstants.PlayerOption6Players, 6),
    ];

    [ObservableProperty]
    private PlayerOption? _selectedPlayerOption;

    partial void OnSelectedPlayerOptionChanged(PlayerOption? value)
    {
        NumberOfPlayers = value?.Value;
    }

    /// `<summary>`
    /// Gets the collection of content-type filter items (Patch, Map, etc.).
    /// </summary>
    public ObservableCollection<ContentTypeFilterItem> ContentTypeFilters { get; } =
    [
        new ContentTypeFilterItem(ContentType.Map, CNCLabsConstants.ContentTypeMaps) { IsSelected = true },
        new ContentTypeFilterItem(ContentType.Mission, CNCLabsConstants.ContentTypeMissions),
    ];

    /// `<summary>`
    /// Gets the active (selected) map tags.
    /// </summary>
    public IEnumerable<string> ActiveTags => MapTagFilters
        .Where(t => t.IsSelected)
        .Select(t => t.Tag);

    /// <inheritdoc />
    public override bool HasActiveFilters => MapTagFilters.Any(t => t.IsSelected) || TargetGame.HasValue || NumberOfPlayers.HasValue;

@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
HasActiveFilters incorrectly reports true at initialization due to default values.

TargetGame defaults to GameType.ZeroHour and SelectedContentType defaults to ContentType.Map — both non-null — causing HasActiveFilters to always return true even when no filters are actually selected by the user. Additionally, changes to TargetGame, SelectedContentType, NumberOfPlayers, and MapTagFilterItem.IsSelected don't automatically trigger HasActiveFilters updates.

Fix:

Add [NotifyPropertyChangedFor(nameof(HasActiveFilters))] to _targetGame, _selectedContentType, and _numberOfPlayers
Exclude default values from the HasActiveFilters logic:
Check TargetGame != GameType.ZeroHour instead of just TargetGame.HasValue
Check SelectedContentType != ContentType.Map instead of just SelectedContentType.HasValue
Add a constructor to subscribe to MapTagFilterItem.IsSelected changes and call NotifyFiltersChanged() to update HasActiveFilters
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/ViewModels/Filters/ModDBFilterViewModel.cs
Comment on lines +16 to +122
    [ObservableProperty]
    private ModDBSection _selectedSection = ModDBSection.Downloads;

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private string? _selectedAddonCategory;

    [ObservableProperty]
    private string? _selectedLicense;

    [ObservableProperty]
    private string? _selectedTimeframe;

    /// `<summary>`
    /// Initializes a new instance of the <see cref="ModDBFilterViewModel"/> class.
    /// </summary>
    public ModDBFilterViewModel()
    {
        InitializeDownloadsFilters();
        InitializeAddonsFilters();
        InitializeTimeframeOptions();
        InitializeLicenseOptions();
    }

    /// <inheritdoc />
    public override string PublisherId => ModDBConstants.PublisherType;

    /// `<summary>`
    /// Gets the available category options.
    /// </summary>
    public ObservableCollection<FilterOption> CategoryOptions { get; } = [];

    /// `<summary>`
    /// Gets the available addon category options.
    /// </summary>
    public ObservableCollection<FilterOption> AddonCategoryOptions { get; } = [];

    /// `<summary>`
    /// Gets the available license options.
    /// </summary>
    public ObservableCollection<FilterOption> LicenseOptions { get; } = [];

    /// `<summary>`
    /// Gets the available timeframe options.
    /// </summary>
    public ObservableCollection<FilterOption> TimeframeOptions { get; } = [];

    /// <inheritdoc />
    public override bool HasActiveFilters =>
        !string.IsNullOrEmpty(SelectedCategory) ||
        !string.IsNullOrEmpty(SelectedAddonCategory) ||
        !string.IsNullOrEmpty(SelectedLicense) ||
        !string.IsNullOrEmpty(SelectedTimeframe);

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        // Set the section for URL building
        baseQuery.ModDBSection = SelectedSection switch
        {
            ModDBSection.Mods => "mods",
            ModDBSection.Addons => "addons",
            _ => "downloads",
        };

        // Apply Category filter (for Downloads and Mods sections)
        if (!string.IsNullOrEmpty(SelectedCategory))
        {
            baseQuery.ModDBCategory = SelectedCategory;
        }

        // Apply Addon Category filter
        if (!string.IsNullOrEmpty(SelectedAddonCategory))
        {
            // For Addons section, use "category" param; for Downloads/Mods, use "categoryaddon"
            baseQuery.ModDBAddonCategory = SelectedAddonCategory;
        }

        // Apply License filter (Addons section only)
        if (!string.IsNullOrEmpty(SelectedLicense))
        {
            baseQuery.ModDBLicense = SelectedLicense;
        }

        // Apply Timeframe filter
        if (!string.IsNullOrEmpty(SelectedTimeframe))
        {
            baseQuery.ModDBTimeframe = SelectedTimeframe;
        }

        return baseQuery;
    }

    /// <inheritdoc />
    public override void ClearFilters()
    {
        SelectedCategory = null;
        SelectedAddonCategory = null;
        SelectedLicense = null;
        SelectedTimeframe = null;
        NotifyFiltersChanged();
        OnFiltersCleared();
    }
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
Add [NotifyPropertyChangedFor(nameof(HasActiveFilters))] to filter properties.

When users select filters via SelectCategory, SelectAddonCategory, SelectLicense, or SelectTimeframe commands, the corresponding properties change but HasActiveFilters is not notified. The UI indicator won't reflect the filter state until ClearFilters() is called. Wire each property to notify HasActiveFilters using the attribute.

Suggested fix
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/ContentDetailView.axaml
Comment on lines +58 to +65
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Text="{Binding AuthorName}" Foreground="#AAAAAA" />
                            <TextBlock Text="•" Foreground="#444444" />
                            <TextBlock Text="{Binding ProviderName}" Foreground="#AAAAAA" />
                            <TextBlock Text="•" Foreground="#444444" />
                            <TextBlock Text="{Binding Version, StringFormat='v{0}'}" Foreground="#AAAAAA"
                                       IsVisible="{Binding Version, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                        </StackPanel>
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

Trailing separator dot visible when Version is empty.

The • separator before the Version TextBlock is always rendered. When Version is null/empty, the separator remains visible, resulting in a trailing dot: "Author • Provider •".

🎨 Proposed fix: wrap version with  its separator
📝 Committable suggestion
🤖 Prompt for AI Agents
@undead2146    Reply...
GenHub/GenHub/Features/Downloads/Views/ContentDetailView.axaml
Comment on lines +156 to +162
                                                <Button Grid.Column="1" Content="Download"
                                                        Command="{Binding $parent[UserControl].((vm:ContentDetailViewModel)DataContext).DownloadFileCommand}"
                                                        CommandParameter="{Binding}"
                                                        Background="{DynamicResource ButtonBackground}" Foreground="{DynamicResource PrimaryButtonForeground}"
                                                        CornerRadius="6" Padding="16,8"
                                                        VerticalAlignment="Center"
                                                        IsVisible="False" />
@coderabbitai
coderabbitai bot
2 days ago
⚠️ Potential issue | 🟡 Minor

🧩 Analysis chain
Remove hardcoded IsVisible="False" or add a TODO comment if this per-file download feature is not yet implemented.

The Download button for individual files is hardcoded to always be hidden. The DownloadFileCommand exists and is bound correctly, but the button itself will never be visible. If this is intentional pending completion of the feature, add a TODO comment. If it should be functional, either bind IsVisible to an appropriate condition or implement the command fully (currently it delegates to the full content download rather than downloading the individual file).

🤖 Prompt for AI Agents
@undead2146    Reply...
This branch has not been deployed

