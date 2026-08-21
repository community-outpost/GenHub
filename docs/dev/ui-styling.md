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
| `SurfaceBackground` / `WindowBackground` | Top-level window and view background | `#08080C` |
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

## Button classes

Use standardized button classes rather than ad-hoc button styling:

| Class | Usage |
|---|---|
| `Button.action-primary` | Main call to action (purple accent background, white text). |
| `Button.action-secondary` | Secondary action (`#1AFFFFFF` background with subtle border). |
| `Button.icon-btn-subtle` | Icon-only utility buttons (`Width="28"`, `Height="28"`, transparent hover). |
| `Button.tab-icon-btn` | Large square navigation tab buttons (`56x56`, `CornerRadius="12"`). |
| `Button.dialog-close-btn` | Modal and flyout close buttons. |

## Anti-patterns to avoid

- **Hardcoding hex values in XAML.** Never write `Background="#252525"` or `Foreground="#FFFFFF"`. Use dynamic theme resources.
- **Duplicating ComboBox or Expander templates.** Never copy-paste `ComboBox` or `Expander` template styles into local views.
- **Sharp full-bleed list items.** Avoid `CornerRadius="0"` on selectable list items. Use rounded inset pills.
- **Fuzzy text drop shadows.** Avoid `DropShadowEffect` on labels and headers. Use clean font weights and contrast.
- **Blocking overlays for primary navigation.** Do not use modal dimmer overlays when users need to interact with the main content while switching items.
- **Custom window chrome.** Always follow `docs/dev/window-styling.md` for native window integration.

## Checklist for new UI views

- [ ] All colors use `{DynamicResource ...}` from `ThemeResources.axaml`.
- [ ] Sidebars and master-detail panes use `SidebarLayout`.
- [ ] Dropdowns use standard `ComboBox` with global theme styling (no inline template copies).
- [ ] Collapsible sections use standard `Expander` card styling.
- [ ] List items use inset pill containers with 8px corner radii.
- [ ] Buttons use standard action or icon classes.
- [ ] Tested on dark theme and resizable window layouts.
