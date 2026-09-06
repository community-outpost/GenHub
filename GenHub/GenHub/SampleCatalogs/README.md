# GenHub Sample Catalogs

Test data for exercising the **subscription** flow without depending on a live remote
publisher. The catalog uses real endpoints and publisher metadata from working publishers
and community sources — **TheSuperHackers**, **Generals Online**, **Community Outpost**,
**SWR Productions** (ModDB), and **L3-M** (GitHub) — plus composite bundles, so it routes
through the same manifest factories and dependency graph as production content.

## Files

| File | Purpose |
|------|---------|
| `genhub-test-catalog.catalog.json` | Combined `PublisherCatalog` (schema v1) with 12 content items spanning multiple publishers and tools, plus composite bundles and a multi-variant addon. |
| `generate-test-shortcuts.ps1`      | Windows (PowerShell). On Linux/macOS, delegates to the `.sh` if `bash` is on PATH. |
| `generate-test-shortcuts.sh`       | Cross-platform (bash 3.2+ / Git Bash / WSL / Linux / macOS). Emits native shortcuts for the host OS. |
| `README.md`                        | This file. |
| `.gitattributes`                   | Forces LF on `.sh` / `.desktop` / `.command` and CRLF on `.ps1`. |

Generated locally (gitignored — they embed absolute paths to **your** checkout):

| Platform | Scheme registration | Subscribe shortcut |
|----------|---------------------|--------------------|
| Windows  | `register-genhub-scheme.reg` | `Subscribe-Test-Catalog.url` |
| Linux    | `register-genhub-scheme.desktop` | `Subscribe-Test-Catalog.desktop` |
| macOS    | `register-genhub-scheme.app` | `Subscribe-Test-Catalog.command` + `.webloc` |

> Run one of the generators after cloning (or after the debug exe path changes). See **Setup**.

## Custom tab demonstration

The catalog includes 3 publisher-defined detail tabs (**Release briefing**, **Community guide**,
and **Sub-Addons & Tools**). Open any item in the Downloads detail view, then select
**Publisher** to see the tabbed card layout. The cards use the bundled faction-cover
images (`avares://GenHub/Assets/Covers/...`), so the demonstration is visible without
downloading third-party artwork. This is a display-only catalog example; it does not
change download, dependency, or profile behavior.

## What's in the catalog

All items live under one synthetic host publisher (`genhub-test-publishers`) so they can
reference **each other** as dependencies, which exercises dependency resolution:

| Content ID | Publisher mirrored | ContentType | Target game | Standalone | Depends on |
|------------|--------------------|-------------|-------------|:----------:|------------|
| `zerohour`                         | TheSuperHackers    | GameClient     | Zero Hour (1) | No  | EA ZH 1.04 |
| `60hz`                             | Generals Online    | GameClient     | Zero Hour (1) | No  | EA ZH 1.04 + QuickMatch maps |
| `community-patch`                  | Community Outpost  | GameClient     | Zero Hour (1) | No  | EA ZH 1.04 |
| `quickmatch-maps`                  | Generals Online    | MapPack        | Zero Hour (1) | No  | EA ZH 1.04 |
| `shockwave-mod-zerohour`           | SWR Productions    | Mod            | Zero Hour (1) | No  | EA ZH 1.04 |
| `cbpr`                             | Community Outpost  | Addon          | Zero Hour (1) | No  | EA ZH 1.04 |
| `lemon-controlbar`                 | L3-M               | Addon          | Zero Hour (1) | No  | EA ZH 1.04 (5 resolution variants) |
| `gent`                             | Community Outpost  | Addon          | Zero Hour (1) | No  | EA ZH 1.04 |
| `hleg`                             | Community Outpost  | Addon          | Zero Hour (1) | No  | EA ZH 1.04 |
| `bundle-thesuperhackers-latest-stack` | (bundle)        | ContentBundle  | Zero Hour (1) | Yes | EA ZH 1.04 + TSH ZH Client (`zerohour` latest) + GenTool (`gent` >=8.9) + Lemon Control Bar (`lemon-controlbar` >=1.3) + Legionnaire Hotkeys (`hleg` >=2026.07.01) |
| `bundle-community-outpost-stack`     | (bundle)        | ContentBundle  | Zero Hour (1) | Yes | EA ZH 1.04 + Community Outpost Client (`community-patch` >=2026.08.02) + GenTool (`gent` >=8.9) + Lemon Control Bar (`lemon-controlbar` >=1.3) + Legionnaire Hotkeys (`hleg` >=2026.07.01) |
| `bundle-generalsonline-complete-pack` | (bundle)        | ContentBundle  | Zero Hour (1) | Yes | EA ZH 1.04 + GO 60Hz Client (`60hz` >=081326) + QuickMatch Maps (`quickmatch-maps` >=081326) + GenTool (`gent` >=8.9) + Lemon Control Bar (`lemon-controlbar` >=1.3) + Legionnaire Hotkeys (`hleg` >=2026.07.01) |

`lemon-controlbar` is the multi-variant fixture: one card, five resolution artifacts (`720p` /
`900p` / `1080p` default / `1440p` / `4K`) on the `resolution` axis.

### Field notes (gotchas)

- **`targetGame` is a number, not a string.** `GameType` has no `JsonStringEnumConverter`, so the
  catalog deserializer (`JsonPublisherCatalogParser` → default `JsonSerializer`) reads it as the
  enum's integer value: `0 = Generals`, `1 = ZeroHour`, `2 = Unknown`. All 12 items in this catalog
  target Zero Hour (`1`).
- **`contentType` is a string** (`"GameClient"`, `"MapPack"`, `"Addon"`, `"Mod"`, `"ContentBundle"`).
  That enum is annotated with `[JsonConverter(typeof(JsonStringEnumConverter))]`.
- **`downloadUrl` is required on non-bundle artifacts.** In `ValidateCatalog`, any artifact missing
  a `downloadUrl` triggers a validation error.
- **`sha256` is optional.** If an artifact lacks a SHA256 checksum, `ValidateCatalog` logs a warning
  and the downloader skips integrity verification rather than failing catalog validation.
- **`ContentBundle` and dynamic releases can omit artifacts.** Bundles specify their contents through
  `dependencies`, while dynamic releases (e.g. `zerohour` with version `"latest"`) resolve their
  downloadable artifacts dynamically during discovery.
- **`isStandalone` controls visibility.** Items with `"isStandalone": false` are hidden from the
  main downloads grid but remain available in the catalog for dependency resolution and bundle
  composition. The 3 bundles have `"isStandalone": true` and appear directly as cards.

## How to use it

### Setup (one-time, per machine)

Generate shortcuts for **your** checkout. Both scripts resolve every path relative to their own
location, so they work no matter where the repo lives:

```powershell
# Windows (PowerShell) — produces .reg + .url
./generate-test-shortcuts.ps1            # add -Force to overwrite without prompting
./generate-test-shortcuts.ps1 -Config Release
```

```bash
# Git Bash / WSL / Linux / macOS
chmod +x ./generate-test-shortcuts.sh    # once, if git did not preserve the executable bit
./generate-test-shortcuts.sh             # CONFIG=Release ./generate-test-shortcuts.sh
FORCE=1 ./generate-test-shortcuts.sh     # overwrite without prompting
INSTALL=1 ./generate-test-shortcuts.sh   # Linux: also install the xdg protocol handler
# From another OS, generate that platform's files (paths still match THIS machine):
# PLATFORM=linux ./generate-test-shortcuts.sh
# PLATFORM=macos ./generate-test-shortcuts.sh
```

Build the platform project first (using the build-check script or IDE build):

```powershell
# Build using the build-safety script:
powershell -File scripts\build-check.ps1 -Mode build
```

| Platform | Project | Output |
|----------|---------|--------|
| Windows  | `GenHub/GenHub.Windows/GenHub.Windows.csproj` | `bin/<Config>/net8.0-windows/GenHub.Windows.exe` |
| Linux    | `GenHub/GenHub.Linux/GenHub.Linux.csproj`     | `bin/<Config>/net8.0/GenHub.Linux` |
| macOS    | `GenHub/GenHub.MacOS/GenHub.MacOS.csproj`     | `bin/<Config>/net8.0/GenHub.MacOS` |

`App.axaml.cs` parses `genhub://subscribe?url=...` from argv on **every** platform via
`CommandLineParser.ExtractSubscriptionUrl`. Windows also self-registers the scheme on launch
(`UriSchemeRegistrar`). Linux and macOS have no in-app registrar yet — the generated
`.desktop` / `.app` files fill that gap for local testing.

### `genhub://` deep link (tests the URI handler + confirmation dialog)

#### Windows

The `genhub://` scheme must be registered first. Pick **one** of:

- **Run GenHub once.** The app self-registers the scheme on every launch (`UriSchemeRegistrar`
  in `Program.Main`), idempotently pointing at the current exe.
- **Import `register-genhub-scheme.reg`** (double-click it) to register the scheme at the debug
  exe without launching the app. Useful for testing the very first click.

Then:

1. Build and launch GenHub (this registers the scheme).
2. Double-click `Subscribe-Test-Catalog.url`.
3. GenHub opens, shows the **Subscription Confirmation** dialog with the parsed publisher info,
   click **Subscribe**.
4. The publisher appears in the Downloads sidebar; its items show up in the content list.

#### Linux

1. Run `./generate-test-shortcuts.sh` (accept the install prompt, or re-run with `INSTALL=1`).
   That copies `register-genhub-scheme.desktop` to `~/.local/share/applications/genhub-scheme.desktop`
   and runs `xdg-mime default genhub-scheme.desktop x-scheme-handler/genhub`.
2. Double-click `Subscribe-Test-Catalog.desktop` (Ubuntu: right-click → **Allow Launching** the
   first time), or run `xdg-open "genhub://subscribe?url=..."`.
3. Confirm the subscription dialog as on Windows.

To register later by hand:

```bash
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$apps"
cp register-genhub-scheme.desktop "$apps/genhub-scheme.desktop"
update-desktop-database "$apps"
xdg-mime default genhub-scheme.desktop x-scheme-handler/genhub
```

#### macOS

1. Run `./generate-test-shortcuts.sh`. This builds `register-genhub-scheme.app` (AppleScript
   applet + `CFBundleURLSchemes = genhub`) and refreshes Launch Services.
2. Open `register-genhub-scheme.app` once so macOS binds `genhub://` to it.
3. Double-click `Subscribe-Test-Catalog.command` (always works; a Terminal window flashes),
   **or** `Subscribe-Test-Catalog.webloc` after the handler is registered.
4. Confirm the subscription dialog as on Windows.

The `.command` / `.desktop` / argv paths do **not** need the OS protocol handler —
`ExtractSubscriptionUrl` reads the `genhub://` string from the command line on any platform:

```bash
dotnet run --project <platform csproj> -- "genhub://subscribe?url=<catalog-url>"
```

The `.url` / `.webloc` / `xdg-open genhub://...` paths **do** need the handler.

The catalog URL is a `file://` path. If the `HttpClient` file-handler rejects `file://` on your
host, serve the catalog over HTTP instead:

```bash
cd GenHub/GenHub/SampleCatalogs && python -m http.server 8080
# then subscribe to: genhub://subscribe?url=http://localhost:8080/genhub-test-catalog.catalog.json
```

## Real endpoints referenced

| Publisher / Source | Endpoint used | Purpose |
|--------------------|---------------|---------|
| **TheSuperHackers** | `https://github.com/TheSuperHackers/GeneralsGameCode` | Source repository and weekly game code releases |
| **Generals Online** | `https://cdn.playgenerals.online/` | Portable 60Hz game client and QuickMatch map pack artifacts |
| **Generals Online** | `https://cdn.playgenerals.online/manifest.json` | Publisher referral catalog |
| **Community Outpost** | `https://legi.cc/gp2/dl.dat` | GenPatcher catalog / Community Outpost referral |
| **Community Outpost** | `https://legi.cc/gp2/f/cbpr.dat`, `gent.dat`, `hleg.dat` | Direct binary artifacts (Control Bar Pro, GenTool, Legionnaire Hotkeys) |
| **Community Outpost** | `https://legi.cc/patch` | Documentation and video showcase URLs |
| **SWR Productions** (ModDB) | `https://www.moddb.com/mods/cc-generals-shockwave/downloads/shockwave-125` | ShockWave Mod v1.25 distribution archive |
| **L3-M** (GitHub) | `https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/...` | Control Bar Pro Lemon Edition resolution variants (720p–4K) |

Theme colors, logos, and tags in the catalog mirror the constants in `GenHub.Core/Constants/`
(`SuperHackersConstants`, `GeneralsOnlineConstants`, `CommunityOutpostConstants`).
