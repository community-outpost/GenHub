---
title: Color Converters
description: Converters that transform Avalonia Colors into brushes, opacity, or contrast
---

# Color Converters

These converters transform Avalonia `Color` values into brushes, opacity values, or contrast-appropriate text colors for UI binding scenarios.

## `ColorToBrushConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts `Color` objects or hex strings into `SolidColorBrush` objects
- **Supported Inputs**:
  - `Color` objects
  - Hex color strings (e.g., "#FF0000", "#3366CC")
  - Opacity adjustment via `ConverterParameter`
- **Return Type**: `SolidColorBrush` or `null`

### Basic Color Usage

```xml
<Border Background="{Binding ProfileColor, Converter={StaticResource ColorToBrushConverter}}" />
```

### Hex String Usage

```xml
<Rectangle Fill="{Binding '#4CAF50', Converter={StaticResource ColorToBrushConverter}}" />
```

### Opacity Adjustment

```xml
<!-- Use ConverterParameter to set opacity -->
<Border Background="{Binding BaseColor, Converter={StaticResource ColorToBrushConverter},
                             ConverterParameter='0.7'}" />
```

---

## `ColorBrightnessConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts a `Color` to its brightness value (0.0 to 1.0)
- **Formula**: `(R × 0.299) + (G × 0.587) + (B × 0.114) ÷ 255`
- **Return Type**: `double`

### Brightness Usage

```xml
<!-- Use for conditional styling based on color brightness -->
<TextBlock Foreground="{Binding BackgroundColor, Converter={StaticResource ColorBrightnessConverter}}" />
```

### Adaptive UI Example

```xml
<Style Selector="TextBlock.brightness-adaptive">
    <Setter Property="Foreground" Value="{Binding BackgroundColor, Converter={StaticResource ColorBrightnessConverter}}" />
    <Style Selector="^:brightness-adaptive[brightness>0.5]">
        <Setter Property="Foreground" Value="Black" />
    </Style>
    <Style Selector="^:brightness-adaptive[brightness&lt;=0.5]">
        <Setter Property="Foreground" Value="White" />
    </Style>
</Style>
```

---

## `ContrastTextColorConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Returns black or white brush depending on background color brightness
- **Logic**: If brightness > 0.5, returns black; otherwise returns white
- **Return Type**: `SolidColorBrush` (black or white)

### Contrast Usage

```xml
<TextBlock Foreground="{Binding BackgroundColor, Converter={StaticResource ContrastTextColorConverter}}" />
```

### String Input Support

```xml
<!-- Also works with hex color strings -->
<TextBlock Foreground="{Binding '#3366CC', Converter={StaticResource ContrastTextColorConverter}}" />
```

---

## `ProfileColorToOpacityConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Extracts the alpha channel from a `Color` and returns opacity (0.0 to 1.0)
- **Formula**: `color.A / 255.0`
- **Return Type**: `double`

### Opacity Usage

```xml
<Border Opacity="{Binding ProfileColor, Converter={StaticResource ProfileColorToOpacityConverter}}" />
```

### Real Usage in GameProfileCardView.axaml

```xml
<!-- Contrast text color for profile names and labels -->
<TextBlock Text="{Binding ProfileName}" 
           Foreground="{Binding ColorValue, Converter={StaticResource ContrastTextColorConverter}}" />

<!-- Semi-transparent text with opacity parameter -->
<TextBlock Text="{Binding Version}" 
           Foreground="{Binding ColorValue, Converter={StaticResource ContrastTextColorConverter}, ConverterParameter=0.8}" />

<!-- Profile card background with opacity -->
<Border Background="{Binding ColorValue, Converter={StaticResource ProfileColorToOpacityConverter}, ConverterParameter=0.6}" />
```

---

## `ContentTypeToBrushConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts a `ContentType` enum value into a solid accent brush for badges, card borders, and tags.
- **Color Mapping** (from `UiConstants`):
  - `ContentType.GameClient` → `ContentTypeGameClientColor` (#06B6D4 - Cyan)
  - `ContentType.Mod` → `ContentTypeModColor` (#A855F7 - Purple)
  - `ContentType.Patch` → `ContentTypePatchColor` (#F59E0B - Amber)
  - `ContentType.Map` / `ContentType.MapPack` → `ContentTypeMapColor` (#10B981 - Green)
  - `ContentType.Addon` → `ContentTypeAddonColor` (#EC4899 - Pink)
  - `ContentType.ModdingTool` / `ContentType.Executable` → `ContentTypeToolColor` (#38BDF8 - Light Blue)
  - `ContentType.ContentBundle` → `ContentTypeBundleColor` (#6366F1 - Indigo)
  - `ContentType.Mission` → `ContentTypeMissionColor` (#F97316 - Orange)
  - `ContentType.Skin` / `ContentType.LanguagePack` → `ContentTypeSkinColor` (#8B5CF6 - Violet)
- **Singleton**: `ContentTypeToBrushConverter.Instance`
- **Return Type**: `SolidColorBrush`

### Badge Border & Text Example

```xml
<Border BorderBrush="{Binding SearchResult.ContentType, Converter={x:Static converters:ContentTypeToBrushConverter.Instance}}">
    <TextBlock Text="{Binding SearchResult.ContentType}"
               Foreground="{Binding SearchResult.ContentType, Converter={x:Static converters:ContentTypeToBrushConverter.Instance}}" />
</Border>
```

---

## `ContentTypeToBadgeBackgroundConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts a `ContentType` enum value into a low-opacity, tinted background brush (alpha ~14.5% / 0x25) suitable for badge and tag pills.
- **Color Source**: Inherits the color defined by `UiConstants` for that content type with alpha set to 37 (0x25).
- **Singleton**: `ContentTypeToBadgeBackgroundConverter.Instance`
- **Return Type**: `SolidColorBrush`

### Badge Pill Example

```xml
<Border Background="{Binding SearchResult.ContentType, Converter={x:Static converters:ContentTypeToBadgeBackgroundConverter.Instance}}"
        BorderBrush="{Binding SearchResult.ContentType, Converter={x:Static converters:ContentTypeToBrushConverter.Instance}}"
        BorderThickness="1"
        CornerRadius="4"
        Padding="6,2">
    <TextBlock Text="{Binding SearchResult.ContentType}"
               Foreground="{Binding SearchResult.ContentType, Converter={x:Static converters:ContentTypeToBrushConverter.Instance}}"
               FontSize="11" />
</Border>
```
