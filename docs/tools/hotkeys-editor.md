# Visual Hotkeys Editor (GenHotkeys)

The Visual Hotkeys Editor provides an integrated, graphical tool within GenHub for customizing unit, structure, upgrade, and ability hotkeys for both Command & Conquer: Generals and Zero Hour.

## Overview

Hotkeys in C&C Generals and Zero Hour are traditionally encoded within string tables (`generals.csf`) and INI definition files. Modifying them manually is tedious and error-prone. The GenHotkeys tool enables players to:

- Inspect and customize hotkeys across all factions (USA, China, GLA, and Zero Hour generals)
- Visually identify conflicting hotkeys across shared command sets
- Generate stamped icon overlays that render key letter badges onto command button textures
- Export configurations directly as standalone `.big` addons managed through GenHub's Content Addressable Storage (CAS)

---

## Key Features

- **Faction & Category Filtering**: Quickly filter commands by faction (USA, China, GLA, Boss, Laser, Superweapon, Air Force, Tank, Infantry, Nuke, Toxin, Stealth, Demo) and category (Buildings, Units, Upgrades, Abilities).
- **Conflict Detection**: Real-time validation highlights conflicting hotkey assignments on the same unit or command set, with a **Next Conflict** navigation button.
- **Profile Management**: Create, clone, rename, and delete custom hotkey profiles saved locally in JSON format.
- **Icon Overlay Generator**: Renders hotkey letter stamps into TARGA (`.tga`) textures for command buttons, providing in-game visual reminders on the command bar.
- **Addon Integration**: Packages custom hotkeys directly into a `.big` file and registers it as an Addon in GenHub for one-click attachment to game profiles.

---

## Technical Details

### `.big` Archive Priority & Load Order

When an addon is exported, GenHub packages the customized string table and button textures into an archive formatted as:

```
!Hotkeys_<ProfileName>_<Game>.big
```

C&C Generals and Zero Hour mount `.big` files in alphabetical order at startup. The leading exclamation point (`!`) ensures that hotkey archives take precedence over vanilla BIG archives (`INIFiles.big`, `English.big`, `Textures.big`), guaranteeing that customized hotkeys and overlay textures reliably override vanilla assets.

### Language & String Table Replacement Caveat

> [!IMPORTANT]
> The hotkeys editor generates string definitions located at `Data/English/generals.csf` inside the `.big` archive.
>
> - **English Installations**: Custom hotkeys, bracketed hotkey markers (`[K]`), and tooltips replace vanilla text seamlessly.
> - **Localized (Non-English) Installations**: If loaded into non-English game installations (such as German, French, or Chinese versions), the high-priority `.big` file will supply English strings for the command bar and units. Users playing on non-English clients should be aware that game tooltips and unit names will display in English when the hotkey addon is active.

### Addon Lifecycle & Update Flow

- **First Export**: Clicking **Create Addon** builds the `.big` archive in temporary storage, copies it into GenHub CAS, and creates a local content manifest.
- **Subsequent Exports**: When modifications are made to an existing profile, the button dynamically transitions to **Update Addon**. Exporting updates the existing manifest in place via `UpdateLocalContentManifestAsync`, refreshing content hashes and keeping game profile associations intact without leaving orphaned manifests in the content pool.
