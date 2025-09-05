---
title: Boolean Converters
description: Converters that transform boolean values into UI-friendly representations
---

# Boolean Converters

These converters map `bool` values into colors, visibility states, or arbitrary values for UI binding scenarios.

## `BoolToColorConverter`

- **Purpose**: Converts a `bool` into a `SolidColorBrush` (default: Green for `true`, Red for `false`)
- **Constructor Parameters**:
  - `trueColor`: Color to use when value is `true` (default: `Colors.Green`)
  - `falseColor`: Color to use when value is `false` (default: `Colors.Red`)

### Basic Usage Example

```xml
<TextBlock Text="Online Status"
           Foreground="{Binding IsOnline, Converter={StaticResource BoolToColorConverter}}" />
```

### Custom Colors Example

```xml
<converters:BoolToColorConverter x:Key="CustomBoolToColor"
                                 TrueColor="Blue"
                                 FalseColor="Gray" />
```

---

## `BoolToStatusColorConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts a `bool` into a **status hex color string** (`#4CAF50` for true, `#F44336` for false)
- **Return Type**: `string` (hex color code)

### Status Color Usage

```xml
<Border Background="{Binding IsConnected, Converter={StaticResource BoolToStatusColorConverter}}" />
```

---

## `BoolToValueConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts a `bool` into **custom values** specified by `TrueValue` and `FalseValue` properties
- **Properties**:
  - `TrueValue`: Object to return when input is `true`
  - `FalseValue`: Object to return when input is `false`

### Value Converter Usage

```xml
<converters:BoolToValueConverter x:Key="BoolToTextConverter"
                                 TrueValue="Enabled"
                                 FalseValue="Disabled" />
<TextBlock Text="{Binding IsEnabled, Converter={StaticResource BoolToTextConverter}}" />
```

### Property Configuration

```xml
<converters:BoolToValueConverter x:Key="BoolToVisibilityConverter"
                                 TrueValue="Visible"
                                 FalseValue="Collapsed" />
<Button IsVisible="{Binding CanSave, Converter={StaticResource BoolToVisibilityConverter}}" />
```

---

## `BoolToVisibilityConverter`

- **Purpose**: Converts a `bool` into `"Visible"` or `"Collapsed"` string for Avalonia visibility binding
- **Return Type**: `string` ("Visible" or "Collapsed")

### Visibility Usage

```xml
<Button Content="Save"
        IsVisible="{Binding CanSave, Converter={StaticResource BoolToVisibilityConverter}}" />
```

---

## `InvertedBoolToVisibilityConverter`

- **Purpose**: Inverse of `BoolToVisibilityConverter` — `true` → "Collapsed", `false` → "Visible"
- **Return Type**: `string` ("Visible" or "Collapsed")

### Inverted Visibility Usage

```xml
<TextBlock Text="No results found"
           IsVisible="{Binding HasResults, Converter={StaticResource InvertedBoolToVisibilityConverter}}" />
```

### Real Usage Patterns

```xml
<!-- Status color indicators -->
<Border Background="{Binding IsConnected, Converter={StaticResource BoolToStatusColorConverter}}" />

<!-- Conditional visibility for buttons -->
<Button Content="Launch Game"
        IsVisible="{Binding CanLaunch, Converter={StaticResource BoolToVisibilityConverter}}" />

<!-- Inverted visibility for error messages -->
<TextBlock Text="Game not found"
           IsVisible="{Binding GameExists, Converter={StaticResource InvertedBoolToVisibilityConverter}}" />

<!-- Custom text values -->
<TextBlock Text="{Binding IsInstalled, Converter={StaticResource BoolToValueConverter},
                          ConverterParameter='Installed;Not Installed'}" />
```

### Real Usage in Game Profile Scenarios

```xml
<!-- Profile validation status -->
<Border Background="{Binding IsValid, Converter={StaticResource BoolToStatusColorConverter}}"
        CornerRadius="4" Padding="8">
    <TextBlock Text="{Binding IsValid, Converter={StaticResource BoolToValueConverter},
                              ConverterParameter='Valid Profile;Invalid Profile'}" />
</Border>

<!-- Launch button visibility -->
<Button Content="Launch"
        IsVisible="{Binding CanLaunch, Converter={StaticResource BoolToVisibilityConverter}}" />

<!-- Error message visibility -->
<TextBlock Text="Please select a game version"
           IsVisible="{Binding HasSelectedVersion, Converter={StaticResource InvertedBoolToVisibilityConverter}}" />
```

### Real Usage in GameProfileLauncherView.axaml

```xml
<!-- Edit mode background toggle using BoolToColorConverter with custom colors -->
<Border Background="{Binding IsEditMode, Converter={StaticResource EditModeToggleBackgroundConverter}, Mode=OneWay}" />
```

Where `EditModeToggleBackgroundConverter` is defined as:

```xml
<conv:BoolToColorConverter x:Key="EditModeToggleBackgroundConverter" TrueColor="#1E88E5" FalseColor="#333333" />
```

This demonstrates how `BoolToColorConverter` can be customized with specific colors for different UI contexts.
