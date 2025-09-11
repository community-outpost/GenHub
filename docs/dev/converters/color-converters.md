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

### Real Usage in GameProfileSettingsWindow.axaml

```xml
<!-- Profile settings contrast text -->
<TextBlock Text="{Binding SettingName}" 
           Foreground="{Binding ColorValue, Converter={StaticResource ProfileSettings_ContrastTextColorConverter}, Mode=OneWay}" />

<!-- Dimmed text with opacity -->
<TextBlock Text="{Binding Description}" 
           Foreground="{Binding ColorValue, Converter={StaticResource ProfileSettings_ContrastTextColorConverter}, ConverterParameter=0.7, Mode=OneWay}" />

<!-- Fallback for invalid colors -->
<TextBlock Text="{Binding Status}" 
           Foreground="{Binding ColorValue, Converter={StaticResource ProfileSettings_ContrastTextColorConverter}, FallbackValue=White}" />
```

### Real Usage in GameProfileLauncherView.axaml

```xml
<!-- Status color for validation -->
<TextBlock Text="Path Status" 
           Foreground="{Binding IsShortcutPathValid, Converter={StaticResource BoolToStatusColorConverter}}" />
```
