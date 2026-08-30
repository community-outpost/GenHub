# BalancePatch Sample Project

A sample ModBuilder project demonstrating a clean, tournament-focused balance mod for Command & Conquer: Generals & Zero Hour.

## Structure
- `Configs/ModBundleItems.json`: Defines the `GameplayINIs` bundle item that collects all INI files from `GameFilesEdited/Data/INI/`.
- `Configs/ModBundlePacks.json`: Defines the `BalancePatch` bundle pack that creates `BalancePatch.big`.
- `GameFilesEdited/Data/INI/`: Contains modified `GameData.ini`, `Armor.ini`, and `Weapon.ini`.

## How to Test
1. Open this project in ModBuilder via `Open Project` -> `SampleProjects/ModBuilder/BalancePatch/BalancePatch.mbproj`.
2. Observe `GameplayINIs` in Bundle Items and `BalancePatch` in Bundle Packs.
3. Check the `Build` action and click `Execute Build`.
4. Check `.Build/bundles/GameplayINIs.big` and `.Release/BalancePatch.zip`.
