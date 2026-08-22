---
title: UI Styling and Design System Standards
description: Guidelines, semantic theme tokens, and component patterns for Avalonia UI in GenHub
---

# UI styling and design system standards

This document defines the mandatory UI standards and design patterns for Avalonia UI views in GenHub. Following these rules ensures visual consistency, theme support, and maintainability across all platforms.

## Core principles

1. **No hardcoded color hexes.** Views and controls must never define inline hex colors like `#1A1A1A` or `#9C27B0`. All colors must reference semantic theme tokens in `ThemeResources.axaml` using `{DynamicResource TokenName}`.
2. **Use shared controls.** Do not build one-off sidebars, search boxes, or card containers. Use existing controls in `GenHub.Common.Controls` (like `SidebarLayout`).
3. **Inset pill navigation.** Sidebars and lists use inset rounded pills with consistent margins and padding, not full-bleed rectangles with sharp corners.
4. **Theme support.** Colors must adapt dynamically when switching between factions, profiles, or themes.

## Semantic theme tokens

All tokens are defined in `GenHub/GenHub/Assets/Styles/ThemeResources.axaml`.

### Surface tokens

| Resource key | Purpose | Standard dark value |
|---|---|---|
| `WindowBackground` / `SurfaceBackgroundBrush` | Top-level window and view background | `#08080C` |
| `CardBackground` / `SurfaceCardBrush` | Content cards and list containers | `#111118` |
| `DetailsBackground` / `SurfaceElevatedBrush` | Elevated flyouts, dialogs, dropdowns, and side panels | `#181822` |
| `SurfaceHoverBrush` | Hover state background for rows and cards | `#222230` |

### Border tokens

| Resource key | Purpose | Standard dark value |
|---|---|---|
| `BorderBrush` / `BorderSubtleBrush` | Standard container borders and dividers | `#282838` |
| `BorderHighlightBrush` | Focused or hovered element borders | `#3F3F5A` |
| `SidebarGlassBorder` | Sidebar divider and outer borders | `#334527A0` |

### Text tokens

| Resource key | Purpose | Standard dark value |
|---|---|---|
| `TextPrimary` | Headings, primary labels, and active item text | `#F0F0F8` |
| `TextSecondary` | Subtitles, captions, and secondary metadata | `#9A9AB0` |
| `TextMuted` | Disabled text, placeholders, and subtle hints | `#656578` |

### Accent and faction tokens

| Resource key | Purpose | Default value |
|---|---|---|
| `AccentBrush` / `SystemAccentColorBrush` | Primary action buttons and focus indicators | `#A855F7` |
| `PrimaryButtonBackground` | Main call-to-action button surface | `#A855F7` |
| `GeneralsFactionBrush` | Generals faction identity | `#BD5A0F` |
| `ZeroHourFactionBrush` | Zero Hour faction identity | `#1B6575` |
| `SuccessBrush` / `StatusSuccessBrush` | Success status badges and notifications | `#10B981` |
| `WarningBrush` | Warning banners and alerts | `#FFA500` |
| `ErrorBrush` / `StatusErrorBrush` | Error banners and validation errors | `#EF4444` |

### Scrollbar tokens

| Resource key | Purpose | Default value |
|---|---|---|
| `ScrollbarTrackBrush` | ScrollBar track background surface | `Transparent` |
| `ScrollbarThumbBrush` | Standard inactive scrollbar thumb | `#38384D` |
| `ScrollbarThumbHoverBrush` | Hovered scrollbar thumb | `#585876` |
| `ScrollbarThumbPressedBrush` | Active/dragging scrollbar thumb | `#A855F7` (`{DynamicResource AccentBrush}`) |

## Sidebar pattern (SidebarLayout)

The standard component for split layouts and sidebar navigation is `GenHub.Common.Controls.SidebarLayout`.

```xml
<controls:SidebarLayout PaneTitle="Installed Tools"
                        ItemsSource="{Binding InstalledTools}"
                        SelectedItem="{Binding SelectedTool, Mode=TwoWay}"
                        IsPaneOpen="{Binding IsPaneOpen, Mode=TwoWay}"
                        ItemTemplate="{StaticResource ToolItemTemplate}">
    <!-- PaneHeader: Action buttons or search boxes placed above the list -->
    <controls:SidebarLayout.PaneHeader>
        ...
    </controls:SidebarLayout.PaneHeader>

    <!-- PaneFooter: Utility actions placed at the bottom of the list -->
    <controls:SidebarLayout.PaneFooter>
        ...
    </controls:SidebarLayout.PaneFooter>

    <!-- Main Content Area -->
    <Grid>
        ...
    </Grid>
</controls:SidebarLayout>
```

### Item template rules

Item templates inside sidebars must use inset rounded rows:

- Set `Margin="8,2"` and `Padding="10,8"` on item containers.
- Set `CornerRadius="8"` on interactive item borders.
- Include a dedicated icon container (`Width="20"` or `Width="24"`).
- Provide primary text and optional secondary metadata text.

```xml
<DataTemplate x:Key="ToolItemTemplate" DataType="interfaces:IToolPlugin">
    <Border Margin="8,2" Padding="10,8" CornerRadius="8">
        <Grid ColumnDefinitions="Auto,*" VerticalAlignment="Center">
            <material:MaterialIcon Grid.Column="0"
                                   Kind="Tools"
                                   Width="20"
                                   Height="20"
                                   Foreground="{DynamicResource AccentBrush}"
                                   Margin="0,0,12,0" />
            <StackPanel Grid.Column="1" Spacing="2" VerticalAlignment="Center">
                <TextBlock Text="{Binding Metadata.Name}"
                           FontWeight="SemiBold"
                           FontSize="13"
                           Foreground="{DynamicResource TextPrimary}" />
                <TextBlock Text="{Binding Metadata.Version, StringFormat='v{0}'}"
                           FontSize="11"
                           Foreground="{DynamicResource TextSecondary}" />
            </StackPanel>
        </Grid>
    </Border>
</DataTemplate>
```

## Selection dropdowns (ComboBox)

All selection dropdowns automatically inherit the global style from `GenHub/GenHub/Assets/Styles/ComboBoxStyles.axaml` via `App.axaml`:

- **Container:** Rounded 8px corners (`CornerRadius="8"`), `MinHeight="36"`, background bound to `{DynamicResource SurfaceElevatedBrush}` with subtle 1px border `{DynamicResource BorderBrush}`.
- **Hover & Focus:** Background transitions to `{DynamicResource SurfaceHoverBrush}`, border highlights to `{DynamicResource BorderHighlightBrush}` on hover and `{DynamicResource AccentBrush}` on focus/open.
- **Glyph:** Vector chevron (`Data="M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z"`) that rotates 180 degrees smoothly when the dropdown opens.
- **Popup menu:** Elevated surface with rounded 8px corners, internal 4px padding, and drop shadow (`BoxShadow="0 10 28 0 #99000000"`).
- **Items:** Inset rounded items (`Margin="2,1"`, `CornerRadius="6"`, `Padding="12,8"`) with accent pill selection highlights.

> [!IMPORTANT]
> Never write inline `ComboBox` control templates or duplicate `ComboBox` styles inside individual feature views. Always rely on the global `ComboBoxStyles.axaml` resource.

## Accordion sections (Expander)

Collapsible sections and settings groups inherit the global style from `GenHub/GenHub/Assets/Styles/ExpanderStyles.axaml` via `App.axaml`:

- **Container:** Framed as an elevated card (`CornerRadius="8"`, `Background="{DynamicResource CardBackground}"`, `BorderBrush="{DynamicResource BorderBrush}"`, `BorderThickness="1"`).
- **Header:** Full-width clickable header button with pointer-over feedback (`{DynamicResource SurfaceHoverBrush}`).
- **Divider:** Subtle bottom border (`{DynamicResource BorderBrush}`) separates the header from the expanded body when `IsExpanded="True"`.
- **Content:** Padded body container that organizes nested controls cleanly.

## Scrollbars (ScrollBar & ScrollViewer)

All scrollbars automatically inherit global theme styling from `GenHub/GenHub/Assets/Styles/ScrollbarStyles.axaml` via `App.axaml`:

- **Thickness:** Compact 8px width (vertical) and 8px height (horizontal) for a clean, non-intrusive modern footprint.
- **Track Direction:** Vertical tracks use `IsDirectionReversed="True"` (top to bottom), while horizontal tracks use `IsDirectionReversed="False"` (left to right).
- **Thumb:** Rounded pill thumb (`CornerRadius="4"`) bound to `{DynamicResource ScrollbarThumbBrush}` with smooth 150ms background brush transitions to hover (`{DynamicResource ScrollbarThumbHoverBrush}`) and pressed (`{DynamicResource ScrollbarThumbPressedBrush}`) states.
- **Track Buttons:** Completely transparent and borderless repeat buttons that do not obstruct content.
- **ScrollViewer Best Practices:**
  - Explicitly set `VerticalScrollBarVisibility="Auto"` and `HorizontalScrollBarVisibility="Disabled"` on vertical content viewers to prevent unwanted horizontal shifts.
  - Never wrap components that already have internal scrolling (such as `MarkdownScrollViewer` or `DataGrid`) in an outer `ScrollViewer`.

## Dynamic accent color themes

GenHub supports live hot-swappable accent color palettes managed by `IThemeService`:

- **Preset Palettes (12 Themes):**
  1. `Purple` — Void Purple (Default) (`#A855F7`)
  2. `Generals` — Generals Orange (`#F97316`)
  3. `ZeroHour` — Zero Hour Cyan (`#06B6D4`)
  4. `Emerald` — Emerald Green (`#10B981`)
  5. `Crimson` — Crimson Red (`#EF4444`)
  6. `Amber` — Cyber Amber (`#F59E0B`)
  7. `Cobalt` — Cobalt Blue (`#3B82F6`)
  8. `Rose` — Neon Rose (`#EC4899`)
  9. `Tiberium` — Tiberium Lime (`#84CC16`)
  10. `Teal` — Deep Teal (`#14B8A6`)
  11. `Indigo` — Electric Indigo (`#6366F1`)
  12. `Ruby` — Blood Ruby (`#F43F5E`)
- **Live Updating:** Mutating `Application.Current.Resources[...]` updates all active views and open windows immediately without application restart.
- **Dynamic Semantic Tokens:**
  - `AccentBrush` / `AccentColor` — Primary theme accent.
  - `AccentLightBrush` / `AccentLightColor` — Highlight and pointer-over state.
  - `AccentDarkBrush` / `AccentDarkColor` — Pressed or deep container state.
  - `AccentGlowBrush` / `AccentGlowColor` — Soft aura and glow gradients.
  - `AccentBadgeBackgroundBrush` / `AccentBadgeForegroundColor` — Low-opacity badge fills and high-contrast labels.
  - `AccentTintBackgroundBrush` — Subtle 15% tint for active pill navigation tabs and selected buttons.
  - `PrimaryGradientBrush` — Two-stop linear gradient from light to dark accent.
  - `SidebarItemSelectedBackground` / `SidebarItemSelectedBorder` — Theme-matched sidebar selection styling.

> [!CAUTION]
> **Never define local `AccentColor` or `AccentBrush` overrides in `<UserControl.Resources>` or `<Window.Resources>`.**
> Defining a local `AccentColor` resource overrides the global theme dictionary, causing views (such as tab bars, buttons, or badges) to remain stuck on hardcoded colors when users switch palettes. Always resolve colors from `Application.Current.Resources` via `{DynamicResource AccentBrush}`.

## Dropdown styling (ComboBox & ComboBoxItem)

All dropdowns inherit styles from `GenHub/GenHub/Assets/Styles/ComboBoxStyles.axaml`:

- **Item Template:** `ComboBoxItem` uses a custom `ControlTemplate` with `x:Name="PART_ContentPresenter"` and 6px rounded corners.
- **Hover on Unselected:** Highlights with `{DynamicResource SurfaceHoverBrush}`.
- **Selected State:** Outlined with `{DynamicResource AccentBrush}` and filled with soft `{DynamicResource AccentBadgeBackgroundBrush}`.
- **Hover on Selected:** Filled with vibrant `{DynamicResource AccentBrush}` and high-contrast white text.

## Tab and pill buttons (RadioButton.TabButton & Button.pill-tab)

For game selection tabs, replay category toggles, or filter pills:

- **Style:** Inset rounded pill (`CornerRadius="8"`, `Padding="16,8"`).
- **Pointer-over:** Soft hover highlight `{DynamicResource SurfaceHoverBrush}` or `#10FFFFFF`.
- **Checked / Active State:** Background bound to `{DynamicResource AccentBrush}` (or `{DynamicResource AccentTintBackgroundBrush}` with `{DynamicResource AccentBrush}` border), with foreground `White`.

## Button classes

Use standardized button classes rather than ad-hoc button styling:

| Class | Usage |
|---|---|
| `Button.action-primary` | Main call to action (theme accent background, white text). |
| `Button.action-secondary` | Secondary action (`#1AFFFFFF` background with subtle border). |
| `Button.icon-btn-subtle` | Icon-only utility buttons (`Width="28"`, `Height="28"`, transparent hover). |
| `Button.tab-icon-btn` | Large square navigation tab buttons (`56x56`, `CornerRadius="12"`). |
| `Button.dialog-close-btn` | Modal and flyout close buttons. |

## Anti-patterns to avoid

- **Hardcoding hex values in XAML.** Never write `Background="#252525"` or `Foreground="#FFFFFF"`. Use dynamic theme resources.
- **Local Accent Resource Shadows.** Never define `<SolidColorBrush x:Key="AccentColor" ...>` in local controls.
- **Duplicating ComboBox, Expander, or ScrollBar templates.** Never copy-paste `ComboBox`, `Expander`, or `ScrollBar` template styles into local views.
- **Nested ScrollViewers.** Never nest a `ScrollViewer` inside another `ScrollViewer` or wrap controls that manage their own scrolling.
- **Sharp full-bleed list items.** Avoid `CornerRadius="0"` on selectable list items. Use rounded inset pills.
- **Fuzzy text drop shadows.** Avoid `DropShadowEffect` on labels and headers. Use clean font weights and contrast.
- **Blocking overlays for primary navigation.** Do not use modal dimmer overlays when users need to interact with the main content while switching items.
- **Custom window chrome.** Always follow `docs/dev/window-styling.md` for native window integration.

## Checklist for new UI views

- [ ] All colors use `{DynamicResource ...}` from `ThemeResources.axaml`.
- [ ] No local `AccentColor` or `AccentBrush` definitions shadowing global theme tokens.
- [ ] Sidebars and master-detail panes use `SidebarLayout`.
- [ ] Dropdowns use standard `ComboBox` with global theme styling (no inline template copies).
- [ ] Collapsible sections use standard `Expander` card styling.
- [ ] Scrollable views configure `VerticalScrollBarVisibility="Auto"` and `HorizontalScrollBarVisibility="Disabled"`.
- [ ] List items use inset pill containers with 8px corner radii.
- [ ] Buttons use standard action or icon classes.
- [ ] Tested on dark theme and resizable window layouts.
