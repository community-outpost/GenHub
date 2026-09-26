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
5. **No Unicode Emojis.** Never use emojis in UI views, button labels, badges, dialogs, tooltips, or notifications. Use clean semantic text, theme brush indicators, or vector SVG StreamGeometry `PathIcon` controls from application resources.
6. **Toast notifications for user feedback.** Never create ad-hoc status labels, status bars, or inline `StatusMessage` TextBlocks to report success, failure, or action completion. All user feedback, operation completions, warnings, and errors must be dispatched through `INotificationService` as toast notifications.
7. **No hardcoded text strings (Mandatory Localization).** Views and controls must never include raw hardcoded English text in `Text`, `Content`, `Header`, `Title`, `Watermark`, or `ToolTip.Tip` attributes. All user-facing strings must be defined in `GenHub/GenHub/Resources/Localization/Strings.resx` and bound via `{localization:Localize ResourceKey}`.

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

- **Container:** Rounded 8px corners (`CornerRadius="8"`), `MinHeight="36"`, background bound to `{DynamicResource CardBackground}` with subtle 1px border `{DynamicResource BorderBrush}`.
- **Hover & Focus:** Background transitions to `{DynamicResource SurfaceElevatedBrush}`, border highlights to `{DynamicResource BorderHighlightBrush}` on hover and `{DynamicResource AccentBrush}` on focus/open.
- **Glyph:** Vector chevron (`Data="M7 10l5 5 5-5z"`) that rotates 180 degrees smoothly when the dropdown opens.
- **Popup menu:** Elevated surface with rounded 8px corners, internal 4px padding, and drop shadow (`BoxShadow="0 10 28 0 #99000000"`).
- **Items:** Inset rounded items (`Margin="0,1"`, `CornerRadius="6"`, `Padding="12,8"`) with accent pill selection highlights.

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

> [!CAUTION]
> **NEVER SET `Padding` DIRECTLY ON `<ScrollViewer>` — ALWAYS USE INNER CONTAINER MARGINS (MINIMUM 48px-64px BOTTOM CLEARANCE)**
>
> In Avalonia UI (11.x), setting `Padding` directly on `<ScrollViewer>` passes that padding to its internal `ScrollContentPresenter`. Avalonia does **NOT** incorporate bottom or right padding into the scrollable `Extent` measurement. Consequently, the scrollbar reaches its maximum offset prematurely, permanently clipping the bottom of the content outside the viewport!
>
> In addition, GenHub's main window hosts a persistent floating version watermark in the bottom-right corner of `MainView` (`HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,10,5"`). Views without sufficient bottom clearance will have bottom-right elements (such as primary submit buttons or privacy text) covered or obstructed by this watermark.
>
> **Mandatory Rule:**
> 1. Keep `<ScrollViewer>` free of any `Padding`.
> 2. Apply all padding/margins to the direct root child container (e.g., `<StackPanel Margin="24,24,24,64" ...>`).
> 3. Always provide at least **48px to 64px bottom margin** on the inner container.
>
> ```xml
> <!-- ❌ BAD: Avalonia extent bug clips bottom content; version overlay covers submit buttons -->
> <ScrollViewer Padding="24" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
>     <StackPanel Spacing="16">
>         <!-- Bottom button here will be cut off or obscured -->
>         <Button Content="Join network" />
>     </StackPanel>
> </ScrollViewer>
>
> <!-- ✔️ GOOD: ScrollViewer measures full extent cleanly; 64px bottom margin clears window edge & watermark -->
> <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
>     <StackPanel Spacing="16" Margin="24,24,24,64">
>         <!-- Bottom button fully visible and comfortably clickable -->
>         <Button Content="Join network" />
>     </StackPanel>
> </ScrollViewer>
> ```

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
  - `AccentBadgeBackgroundBrush` / `AccentBadgeForegroundBrush` — Low-opacity badge fills and high-contrast labels.
  - `AccentTintBackgroundBrush` — Subtle 15% tint for active pill navigation tabs and selected buttons.
  - `PrimaryGradientBrush` — Two-stop linear gradient from light to dark accent.
  - `SidebarItemSelectedBackground` / `SidebarItemSelectedBorder` — Theme-matched sidebar selection styling.

> [!CAUTION]
> **Never define local `AccentColor` or `AccentBrush` overrides in `<UserControl.Resources>` or `<Window.Resources>`.**
> Defining a local `AccentColor` resource overrides the global theme dictionary, causing views (such as tab bars, buttons, or badges) to remain stuck on hardcoded colors when users switch palettes. Always resolve colors from `Application.Current.Resources` via `{DynamicResource AccentBrush}`.

## Dropdown styling (ComboBox & ComboBoxItem)

All dropdowns inherit styles from `GenHub/GenHub/Assets/Styles/ComboBoxStyles.axaml`:

- **Item Template:** `ComboBoxItem` uses a custom `ControlTemplate` with `Border#ItemBorder` and inner `ContentPresenter x:Name="PART_ContentPresenter"` with 6px rounded corners.
- **Hover on Unselected:** Highlights row with `{DynamicResource AccentTintBackgroundBrush}`.
- **Selected State:** Outlined with `{DynamicResource AccentBrush}` and filled with soft `{DynamicResource AccentBadgeBackgroundBrush}`.
- **Hover on Selected:** Highlights row with `{DynamicResource AccentTintBackgroundBrush}` and outlined with `{DynamicResource AccentBrush}`.

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

## User feedback and toast notifications (INotificationService)

A common anti-pattern when building desktop UI is placing a passive status label or `TextBlock` (e.g. `<TextBlock Text="{Binding StatusMessage}" />`) at the bottom of a view or dialog. Status labels are easily overlooked, introduce visual clutter, and fragment user experience across features.

In GenHub, **all user feedback, action confirmations, operation completions, warnings, and error alerts must use the central `INotificationService` toast notification system**.

### Why Toast Notifications?

- **High Visibility:** Animated toasts appear at the top/bottom corner of the window where users naturally see them.
- **Auto-dismissal:** Toasts dismiss automatically according to standardized timeouts (`NotificationDurations`), avoiding stale status strings lingering indefinitely.
- **Persistent Feed:** All notifications are automatically recorded in the notification history feed (the bell icon in the window title bar), allowing users to review previous notifications.
- **Actionable:** Notifications support actions (e.g. undo, open folder, view logs) directly from the toast.

### Implementation Pattern

ViewModels should accept `INotificationService` via primary constructor injection and dispatch notifications for all user-initiated actions and operation outcomes:

```csharp
public partial class MyFeatureViewModel(
    IMyService myService,
    INotificationService notificationService,
    ILogger<MyFeatureViewModel> logger) : ObservableObject
{
    [RelayCommand]
    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = await myService.SaveAsync(cancellationToken);
        if (result.Success)
        {
            notificationService.ShowSuccess(
                "Settings Saved",
                "Your configuration has been updated successfully.",
                NotificationDurations.Short);
        }
        else
        {
            notificationService.ShowError(
                "Save Failed",
                $"Unable to save settings: {string.Join(", ", result.Errors)}",
                NotificationDurations.Medium);
        }
    }
}
```

### Standard Notification Durations

Always use constants from `GenHub.Core.Constants.NotificationDurations`:
- `NotificationDurations.Short` (3s): Quick feedback for frequent user actions (e.g., hotkey assigned, toggle changed, item copied).
- `NotificationDurations.Medium` (5s): Standard notifications (e.g., profile created, download finished, settings saved).
- `NotificationDurations.Long` (6s): Important notifications or messages requiring reading.
- `NotificationDurations.VeryLong` (10s): Complex messages or interactive toasts with action buttons.
- `NotificationDurations.Critical` (15s): Error notifications requiring user attention or manual intervention.

## Anti-patterns to avoid

- **Inline status labels / textblocks.** Never add `<TextBlock Text="{Binding StatusMessage}" />` or status bars to report action success, errors, or feedback. All feedback must use `INotificationService` toast notifications.
- **Hardcoding hex values in XAML.** Never write `Background="#252525"` or `Foreground="#FFFFFF"`. Use dynamic theme resources.
- **Local Accent Resource Shadows.** Never define `<SolidColorBrush x:Key="AccentColor" ...>` in local controls.
- **Duplicating ComboBox, Expander, or ScrollBar templates.** Never copy-paste `ComboBox`, `Expander`, or `ScrollBar` template styles into local views.
- **Setting `Padding` on `ScrollViewer`.** Never apply `Padding` directly to `<ScrollViewer>`. Due to Avalonia's `ScrollContentPresenter` extent calculation bug, bottom content will be cut off. Always set `Margin="24,24,24,64"` on the inner content container.
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
- [ ] Scrollable views configure `VerticalScrollBarVisibility="Auto"` and `HorizontalScrollBarVisibility="Disabled"` without direct `Padding` on `<ScrollViewer>`.
- [ ] Direct child container of `ScrollViewer` has at least 48px-64px bottom margin (e.g. `Margin="24,24,24,64"`) to guarantee bottom visibility and avoid occlusion by the floating version overlay.
- [ ] List items use inset pill containers with 8px corner radii.
- [ ] Buttons use standard action or icon classes.
- [ ] User action feedback, completions, warnings, and errors use `INotificationService` toasts (no inline `StatusMessage` labels).
- [ ] Tested on dark theme and resizable window layouts.

## Localization in views (LocalizeExtension)

All user-facing strings must be bound using the `LocalizeExtension` markup extension. This ensures strings update dynamically when the active language changes at runtime, without recreating views or restarting the application.

### XAML Namespace Declaration

Declare the localization markup namespace on the root `<UserControl>` or `<Window>`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             ...
             xmlns:localization="clr-namespace:GenHub.Common.Markup">
```

### Usage Patterns

#### Text and Labels
```xml
<TextBlock Text="{localization:Localize Settings.Appearance.Title}"
           Classes="SectionTitle" />
```

#### Buttons and Controls
```xml
<Button Content="{localization:Localize Common.Button.Close}"
        Command="{Binding SaveCommand}" />
```

#### Expanders and Section Headers
```xml
<Expander Header="{localization:Localize Settings.Appearance.Title}">
    ...
</Expander>
```

#### TextBoxes and Search Fields (Watermarks)
```xml
<TextBox Watermark="{localization:Localize Downloads.Browser.SearchWatermark}"
         Text="{Binding SearchQuery, Mode=TwoWay}" />
```

#### ToolTips
```xml
<Button ToolTip.Tip="{localization:Localize Navigation.Settings}">
    <material:MaterialIcon Kind="Cog" />
</Button>
```

#### Selection Controls (e.g. Language Selector)
```xml
<ComboBox ItemsSource="{Binding AvailableLanguages}"
          SelectedItem="{Binding SelectedLanguage, Mode=TwoWay}"
          HorizontalAlignment="Stretch">
    <ComboBox.ItemTemplate>
        <DataTemplate DataType="settingsModels:LanguageOption">
            <TextBlock Text="{Binding DisplayName}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

> [!IMPORTANT]
> **Never hardcode string literals in XAML.** If you add a new UI element, always add its resource entry to `GenHub/GenHub/Resources/Localization/Strings.resx` using a hierarchical dot-separated key (`<Feature>.<Context>.<Element>`), and test that it renders correctly in Avalonia views.
