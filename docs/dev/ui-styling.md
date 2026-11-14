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

## Text inputs and form density

Ordinary text inputs opt into `FormFieldStyles.axaml` with `Classes="form-field"`.
Use `FormFieldFill` and `FormFieldBorder` so inputs remain visible at rest, with semantic text colors and 8px input corners,
36px minimum height, 13px type and `10,8` padding. Hover lifts the fill and strengthens the border. Focus and validation states
retain the original Fluent editor template; password, selection, read-only and
multiline behavior stay with the consumer.

Use `Classes="form-field form-compact"` for dense editor/search fields: 32px
minimum height, 12px type and `8,6` padding. Dropdowns use the same form metrics,
with `form-compact` for dense contexts. Do not duplicate field paint or template
state styles in individual views. Layout, multiline height, maximum dropdown
height and semantic option templates stay with the consumer.

The text style is deliberately opt-in so embedded text editors inside numeric
scrubbers and code-editor controls keep their component templates and geometry.
Code/raw-text editors, numeric scrubbing and special item previews are distinct
families, not ordinary text forms. Never remove their behavior to match form
presentation.

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

See [the consistency rollout](./ui-consistency-rollout.md) for adoption scope and
remaining screen families.

## Button classes

Ordinary actions use the global `Assets/Styles/ButtonStyles.axaml`, loaded by
`App.axaml`. Choose one text role and optionally `action-compact`. Add `icon-action` to a
semantic role for an icon-only primary or destructive action; its 32px target
does not replace the semantic colors. Use `icon-action` alone for neutral utilities. These are styles on
standard Avalonia buttons: command enablement, keyboard activation and focus remain
owned by Avalonia. Do not copy the styles into a feature view.

| Class | Use | Metrics |
|---|---|---|
| `action-primary` | Main action, usually one per form or dialog | Darker live accent; white ink; minimum 36px high; 16×8 padding |
| `action-secondary` | Cancel, browse, retry, and other supporting actions | Transparent surface and subtle border; same metrics |
| `action-danger` | Delete or replace an existing item | Subtle red tint, red outline and pale red ink; same metrics |
| `icon-action` | Utility actions with no visible caption | Transparent at rest; minimum 32×32 target; 16px glyph |
| `action-compact` | Add to a text role in dense toolbars | Minimum 32px high; 12×6 padding; 12px type |

Default action type is 13px medium, with semibold primary actions. Ordinary action
buttons use `ActionCornerRadius` (999px, clamped to a capsule by Avalonia). Utility
icons use the same rule to form circular targets. Cards and inputs retain their
existing radii; pill-shaped actions do not imply pill-shaped containers.
Use minimum sizes so translated labels can grow. Wrap long captions in a
`TextBlock` with `TextWrapping="Wrap"` when horizontal space is constrained.
Do not override a role's background, foreground, font, padding, or interaction
states locally. Layout properties such as alignment and margins stay with the
consumer. Specialized split buttons form one capsule with square adjoining edges and shared
`ToolActionBarStyles`. Put the gradient on a `Border.split-action` around both
hit targets; the segments remain transparent at rest. Their hover/pressed
overlays preserve the continuous gradient. Keep each target independently
focusable and retain the arrow divider.

```xml
<Button Classes="action-primary"
        Command="{Binding InstallCommand}"
        Content="{localization:Localize Updates.Button.InstallUpdate}" />
<Button Classes="icon-action"
        Command="{Binding CloseCommand}"
        AutomationProperties.Name="{localization:Localize Common.Button.Close}"
        ToolTip.Tip="{localization:Localize Common.Button.Close}">
    <PathIcon Data="{StaticResource CloseIcon}" />
</Button>
```

Primary actions use `ActionOnAccentBrush` (white) on dedicated
`ActionPrimaryRestBrush`, `ActionPrimaryHoverBrush` and
`ActionPrimaryPressedBrush` gradients. These action fills are separate from the
brighter navigation accent. `ActionButtonPalette.CreateBrushes` derives restrained
two-stop gradients for each live palette and preserves the selected purple
treatment. Tests verify at least 4.5:1 white-text contrast at both stops and
intermediate shades across every shipped palette and state.

Secondary actions are transparent with restrained outlines and medium-weight
labels. Destructive actions use a red tint and outline rather than a dominant
solid red fill. Disabled actions mute both captions and glyphs. Keyboard focus
retains its visible border; opacity or scaling is not the only state indicator.

### Dialog hierarchy and layout

Normal dialogs use `DialogPadding` (24px) around content and
`DialogHeaderSpacing` (16px below the header). Keep exterior shadow margins
separate from content padding. Use the opt-in roles from `DialogStyles.axaml`:

| Class | Role |
|---|---|
| `dialog-title` | 20px semibold primary title, wrapping when translated |
| `dialog-section` | 15px semibold section heading |
| `dialog-label` | 13px semibold secondary field label |
| `dialog-body` | 13px primary explanatory copy |
| `dialog-hint` | 12px secondary supporting copy |
| `dialog-error` | 12px wrapping semantic validation error |

Use a header/body/footer grid. The body scrolls independently; validation and
actions occupy real footer rows, with 12px between actions. Do not float framed
button bars over the form or reserve arbitrary bottom padding to hide overlap.
Constrained action captions must wrap within their targets. Small confirmation
dialogs grow for translated titles while keeping long message bodies bounded
and scrollable. Native title bars and dense editor workspaces remain separate
component families rather than inheriting dialog content titles.

### Icons

Use existing semantic geometry resources in `ThemeResources.axaml` for common
actions (`AddIcon`, `EditIcon`, `DeleteIcon`, `DownloadIcon`, `RefreshIcon`,
`CloseIcon`, and so on). Existing Material icons and `ToolIcon.*` resources may
remain where they represent specialized concepts; do not add another icon
package or paste duplicate path data for an existing semantic action.

- Action glyphs: 16px (`ActionIconSize`); section glyphs: 20px (`SectionIconSize`);
  navigation glyphs: 24px (`NavigationIconSize`).
- Let glyphs and action captions inherit the button foreground. A fixed white
  child icon bypasses palette contrast and disabled styling.
- Icon-only buttons require a localized accessible name and a matching tooltip.
- Game artwork, publisher logos, and other brand graphics keep their identity.
- Navigation pills, selectable cards, media transport, hotkey keys and hero Play
  actions are separate component families. Do not mechanically apply action
  roles to them.

### Component showcase and verification

Run the standalone developer gallery (no game or application services start):

```sh
dotnet run --project GenHub/GenHub.UI.Showcase
```

It uses the production resources and offers every shipped accent palette,
default and compact actions, disabled controls, utility icons, and long Russian
and Arabic captions. Hover, press, Tab and Enter work normally; resize the window
to inspect wrapping. Labels in this developer tool are samples, not application
localization resources.

For a reproducible headless image:

```sh
dotnet run --project GenHub/GenHub.UI.Showcase -- --screenshot /absolute/path/components.png
```

For a UI change, compare representative before/after views, check keyboard focus
and activation, disabled command behavior, live palette changes and long captions.
A gallery image verifies shared controls; it does not replace screenshots of the
migrated screens or native Windows/Linux/macOS verification.

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

## Neutral reading surfaces and cards

Use `Border.surface-card` for an opaque card shell with semantic background,
border and `CardCornerRadius`. Add `surface-content` for the standard 16px inset,
`surface-section` for a 24px form-section inset, and `surface-interactive` only
when the whole card is an interactive target. Media and whole-card buttons may
keep their padding on the inner content/target so the shell does not double it.

Artwork and profile identity belong in previews, headers, selection indicators
and accent stripes. Form reading surfaces stay opaque and neutral when the
profile color changes, including very bright colors. Do not apply opacity to a
whole form header: it also fades its text and actions. Use `TextPrimary` and
`TextSecondary` for labels/help instead of reducing text opacity over artwork.

Catalog cards retain their existing restrained shading and depth. Shared surface
roles do not require flattening an established component that already looks good.
Profile artwork and a light identity tint remain in a bounded header; reading
panels stay opaque. Form fills and borders remain visible at rest, and profile
navigation retains large icons and clear inactive targets.

Visual consistency is a quality constraint, not a reason to flatten a successful component.
Keep the original catalog-card depth, crisp main selection outline and 32px profile
navigation glyphs with visible inactive targets. Preserve improved dialog layout
and readable form panels while keeping restrained artwork and tint in profile headers.

### Alignment and curvature

Single-line, non-wrapping `form-field` editors center both text and watermark in the allocated height. Multiline and wrapped editors keep their first line at the top inset. Preserve local layout overrides for specialized editors.

Action captions and glyphs center their own line/icon boxes within the shared action content. Avoid per-screen baseline nudges or transforms. Profile navigation no longer lifts selected/hovered targets above their row.

Top-level tabs, Studio tabs and profile icon navigation use `ActionCornerRadius` like pill actions. Square profile targets therefore become circles. Fields retain `InputCornerRadius` and reading cards retain `CardCornerRadius`; inset sidebar rows and editor underlines remain distinct layouts. Preview boards must use the app's bundled Inter font and the same scale when comparing navigation families.

Scrolling authoring dialogs opt into `dialog-scroll` to reserve a 12px right gutter beside input borders. Fluent validation captions use the same error brush as field outlines and dialog error hints.
