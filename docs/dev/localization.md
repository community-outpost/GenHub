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
`development`.\n
> [!NOTE]
> GitHub Actions does not trigger workflow runs on pushes authenticated
> with the default `GITHUB_TOKEN`, and the default token cannot push changes
> that touch `.github/workflows/*` (which require the `workflows: write` scope).
> To ensure PR check suites run automatically on freshened branches and workflow
> changes can be merged, repository maintainers can provide an `AUTO_MERGE_PAT`
> secret (a Personal Access Token or GitHub App installation token with `contents: write`
> and `workflows: write`). When running under the default `GITHUB_TOKEN`, the bot leaves a
> comment on the pull request notifying authors to trigger CI checks (e.g. by pushing a commit or closing/reopening).

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
