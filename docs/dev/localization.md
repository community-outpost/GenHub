---
title: Localization
description: Resource, fallback, discovery, and live-binding conventions for GenHub translations
---

# Localization

GenHub localizes application UI text with .NET `.resx` resources. English is the neutral language embedded in `GenHub.dll`; translated resources are compiled into culture-specific satellite assemblies.

The foundation is intentionally small:

- `ILocalizationService` resolves and formats strings, exposes available cultures, and changes the active culture.
- `LocalizationService` uses .NET `ResourceManager` fallback and raises `INotifyPropertyChanged` notifications when the culture changes.
- `LocalizeExtension` gives Avalonia views a live binding to a resource key.
- `LocalizationModule` registers one shared service for every platform host.

Language selection, persisted preference, UI string migration, translation coverage, and right-to-left layout are separate feature concerns built on this foundation.

## Resource layout

The neutral English resource is:

```text
GenHub/GenHub/Resources/Localization/Strings.resx
```

Add translations beside it using a valid culture name:

```text
Strings.fr.resx
Strings.de.resx
Strings.ar.resx
Strings.pt-BR.resx
```

At build time, .NET creates a satellite assembly under the matching culture directory. GenHub discovers those directories at startup, so there is no manually maintained supported-language list.

If a translated resource omits a key, `ResourceManager` follows the normal culture hierarchy and ultimately uses the value from `Strings.resx`. If the key does not exist in any resource, GenHub logs the miss and displays the key so the problem remains visible.

## Resource keys

Use dot-separated keys that identify the feature and UI purpose:

```text
Settings.Appearance.Title
Settings.Appearance.Language.Label
GameProfiles.Create.Confirm
Downloads.Status.Queued
```

Keep keys stable after release. Add translator comments when context or placeholders are not obvious. Every translation of a formatted string must preserve the same numbered placeholders as the English resource.

Do not localize log templates, protocol values, manifest identifiers, command-line arguments, or other developer-facing technical strings.

## Merging concurrent changes

Every feature branch touches the same `Strings*.resx` files, so plain
line-based merging conflicts constantly, and git's built-in `merge=union`
is unsafe here: it aligns insertions at shared anchor lines and can
silently drop a `</data>` closing tag while reporting success. The
repository instead merges whole `<data name="...">` blocks by resource key
(`scripts/git_merge_resx.py`, selected by the `merge=resx` attribute in
`.gitattributes`): independent additions from both sides are both kept,
deletions are honored, and only a genuine same-key disagreement stops the
merge. Nobody needs to configure or invoke anything for pull requests; the
automation described below runs the driver server-side. (Local `git merge`
or `git rebase` operations on developer machines will still use standard
line-based merging unless the custom driver is configured locally.)

CI validates every `Strings*.resx` on each run (`scripts/validate_resx.py`):
well-formed XML with flat `<data>` blocks, no duplicate keys, exact key-set
parity across cultures, and preserved `{0}`-style placeholders (run locally
with `python scripts/validate_resx.py` on Windows or `python3 scripts/validate_resx.py`
on Linux/macOS).

### Automatic merges

GitHub never runs custom merge drivers, so the driver above cannot resolve
pull request conflicts server-side: without further help, one merged
localization pull request would leave every other open one showing
conflicts for a human to rebase. The `Resx Auto Merge` workflow
(`.github/workflows/resx-auto-merge.yml`) closes that gap. It runs
`scripts/auto_merge_development.py`, which merges `development` into each
open pull request with the driver configured and pushes only fully clean
results. Anything needing judgment is left for the author: history is
never rewritten (merge commits only, never force-pushes), and draft pull
requests, forks, and pull requests labeled `no-automerge` are skipped.

The workflow stays cheap with two gates. The job itself runs only when
the push touched `Strings*.resx` files or the merge tooling, and the
script then compares each pull request in memory first: pull requests
GitHub already shows as mergeable are never touched, pull requests conflicting outside
the managed resx directory are skipped for their author, and only
resx-only conflicts reach a real merge. Trigger it by hand from the
Actions tab (`workflow_dispatch`) if ever needed outside a push to
`development`.

> [!NOTE]
> GitHub Actions does not trigger workflow runs on pushes authenticated
> with the default `GITHUB_TOKEN`. To ensure PR check suites run automatically
> on freshened branches, repository maintainers can provide an `AUTO_MERGE_PAT`
> secret (a Personal Access Token or GitHub App installation token with `contents: write`).
> When running under the default `GITHUB_TOKEN`, the bot leaves a comment on the pull request
> notifying authors to trigger CI checks (e.g. by pushing a commit or closing/reopening).

## Avalonia views

Reference the markup namespace `clr-namespace:GenHub.Common.Markup` and bind the property to a key:

```xml
<UserControl xmlns:localization="clr-namespace:GenHub.Common.Markup">
    <TextBlock Text="{localization:Localize Settings.Appearance.Title}" />
</UserControl>
```

The extension binds through the application-scoped localization service (`LocalizeExtension`). When `SetCulture` changes the active culture, all localized indexer bindings are notified and refresh immediately without recreating the view or restarting GenHub.

For tool names or dynamically loaded plugin titles where the model is `ToolMetadata` or `IToolPlugin`, use `LocalizedToolNameConverter`:

```xml
<TextBlock Text="{Binding, Converter={StaticResource LocalizedToolNameConverter}}" />
```

## View models and services

Inject `ILocalizationService` when text must be produced in code:

```csharp
var title = localizationService.GetString("GameProfiles.Create.Title");
var status = localizationService.GetString("Downloads.Status.Progress", completed, total);
```

Only request cultures returned by `AvailableCultures`, and check the operation result:

```csharp
var result = localizationService.SetCulture(selectedCulture);
if (result.Failed)
{
    // Surface result.FirstError through the caller's normal error path.
}
```

Culture switching is synchronous because it performs no long-running I/O. Do not wrap it in `Task.Run`, block on a task, or introduce a reactive package solely for change notification.

The selected language is applied to `CurrentUICulture` and `DefaultThreadCurrentUICulture` for resource lookup. It does not replace `CurrentCulture`, so changing the UI language cannot silently alter unrelated regional number, date, parsing, or serialization behavior. Format arguments passed to `GetString` use the selected localization culture.

## Adding coverage

Every localization change should test the behavior it introduces. At minimum:

- a translated key resolves from the requested satellite assembly;
- an omitted translated key falls back to English;
- placeholders format correctly in the active culture;
- an unavailable culture fails without changing the current culture;
- a successful culture change refreshes live bindings;
- an invalid satellite assembly is ignored without aborting discovery of other languages.

## Language Selection & Persistence

User language preference is saved in `UserSettings` and persisted automatically by `IUserSettingsService`:

- **Model Property:** `UserSettings.Language` (string culture identifier, default: `"en"` defined in `LocalizationConstants.DefaultCultureName`).
- **ViewModel Integration:** `SettingsViewModel` exposes `AvailableLanguages` (`IReadOnlyList<LanguageOption>`) and `SelectedLanguage` (`LanguageOption?`).
  - `LanguageOption` encapsulates `(CultureInfo Culture, string DisplayName)`.
  - When `SelectedLanguage` changes, `SettingsViewModel` calls `_localizationService.SetCulture(selectedOption.Culture)`.
  - On `SaveSettingsCommand`, the selected culture's `Name` is written to `UserSettings.Language` and persisted to disk.
  - When resetting to defaults, `SelectedLanguage` resets to the default English culture (`en`).
  - `SettingsViewModel` listens to `_localizationService.PropertyChanged` on `CurrentCulture` to keep its dropdown synchronized if culture is changed elsewhere.

## Application Startup Flow

During application startup in `App.axaml.cs`:

1. `App.Initialize()` retrieves the persisted `UserSettings.Language`.
2. If configured and valid, `_localizationService.SetCulture(...)` is invoked before Avalonia XAML loading begins.
3. `Resources[LocalizationConstants.ResourceServiceKey]` is populated with `_localizationService`.
4. `AvaloniaXamlLoader.Load(this)` executes, resolving all `{localization:Localize ...}` markup bindings directly with the restored culture.

## Guidelines for Future Agents & Contributors

When adding new features, views, dialogs, or modifying existing ones, strictly follow these requirements:

1. **Extract every user-facing string to `Strings.resx`:**
   - Neutral English is maintained in `GenHub/GenHub/Resources/Localization/Strings.resx`.
   - Use hierarchical, semantic dot-separated keys:
     - Views: `<View>.<Section>.<Element>`, e.g., `Settings.Appearance.Language.Label`
     - Common/Shared: `Common.<Action>`, e.g., `Common.Save`, `Common.Cancel`
     - Tooltips: `<View>.<Control>.ToolTip` or `Tooltips.<Control>`
     - Errors/Validation: `<Feature>.Error.<Reason>` or `Validation.<Rule>`
2. **Never hardcode string literals:**
   - In XAML views: Bind with `{localization:Localize Key}`.
   - In C# ViewModels / Services: Call `_localizationService.GetString("Key")`.
3. **Preserve Case-Insensitive Key Uniqueness:**
   - MSBuild resource generators on Windows treat resource keys case-insensitively. Do not define keys that differ only in casing (e.g., avoid having both `Foo.Bar` and `Foo.bar`).
4. **Parameter Placeholders:**
   - Format strings with parameters must use standard indexed placeholders `{0}`, `{1}`, etc.
   - Example: `<value>Downloaded {0} of {1}</value>`
   - C# call: `_localizationService.GetString("Downloads.Status.Progress", completed, total)`
5. **Strict 1:1 Resx Key Parity Across All Languages:**
   - Every key present in `Strings.resx` must also exist in all satellite `.resx` files (`Strings.ar.resx`, `Strings.ru.resx`, etc.).
   - When adding, updating, or removing keys in `Strings.resx`, always apply the corresponding changes to all satellite files simultaneously with appropriate translations.
   - Never leave translated resources missing keys from the neutral file.
6. **Dynamic Collections & Navigation Sidebars:**
   - When navigation items or sidebars (such as `SettingsViewModel.Sections`) are generated from collections with localized titles, subscribe to `_localizationService.PropertyChanged` (checking for `CurrentCulture` or indexer changes) to update item titles dynamically upon culture switches without requiring an application restart.
7. **Tool & Plugin Localization:**
   - For dynamically loaded tools/plugins where models implement `ToolMetadata` or `IToolPlugin`, use `LocalizedToolNameConverter` (`{Binding, Converter={StaticResource LocalizedToolNameConverter}}`) in XAML. The converter checks for `Tools.Plugin.<ToolId>.Name` in resources, falling back to the plugin's metadata title.
8. **Adding New Language Translations (Future PRs):**
   - Create satellite resource files matching the culture name beside `Strings.resx`:
     - `Strings.de.resx` (German)
     - `Strings.fr.resx` (French)
     - `Strings.zh-Hans.resx` (Simplified Chinese)
     - `Strings.es.resx` (Spanish)
   - Ensure all keys from `Strings.resx` are copied and translated with strict 1:1 key parity.
   - At runtime, `LocalizationService` discovers satellite assemblies automatically and populates `AvailableCultures`, which will appear in the Settings Language selector without manual list edits.
