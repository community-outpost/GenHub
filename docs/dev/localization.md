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

## Avalonia views

Reference the markup namespace and bind the property to a key:

```xml
<UserControl xmlns:localization="clr-namespace:GenHub.Common.Markup">
    <TextBlock Text="{localization:Localize Settings.Appearance.Title}" />
</UserControl>
```

The extension binds through the application-scoped localization service. When `SetCulture` succeeds, all localized indexer bindings are notified and refresh without recreating the view or restarting GenHub.

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

## Adding coverage

Every localization change should test the behavior it introduces. At minimum:

- a translated key resolves from the requested satellite assembly;
- an omitted translated key falls back to English;
- placeholders format correctly in the active culture;
- an unavailable culture fails without changing the current culture;
- a successful culture change refreshes live bindings.
