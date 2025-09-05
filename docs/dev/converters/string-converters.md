---
title: String Converters
description: Converters that work with string values for UI binding scenarios
---

# String Converters

These converters work with string values, converting them to booleans, images, or other formats for UI binding.

## `StringToBoolConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts string values to boolean based on whether they have content
- **Parameters**:
  - `ConverterParameter`: Set to "invert" to invert the logic (empty string = true)
- **Return Type**: `bool`

### Basic Usage

```xml
<Button IsEnabled="{Binding SearchText, Converter={StaticResource StringToBoolConverter}}" />
```

### Inverted Logic

```xml
<TextBlock IsVisible="{Binding ErrorMessage, Converter={StaticResource StringToBoolConverter},
                               ConverterParameter='invert'}" />
```

---

## `StringToImageConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts string file paths to Avalonia `Bitmap` objects for image display
- **Supported Formats**:
  - Local file paths
  - `avares://` URIs (embedded resources)
  - Web URLs (returns null for async loading)
- **Return Type**: `Bitmap` or `null`

### File Path Usage

```xml
<Image Source="{Binding ImagePath, Converter={StaticResource StringToImageConverter}}" />
```

### Embedded Resource Usage

```xml
<Image Source="{Binding 'avares://GenHub/Assets/Icons/default.png',
                        Converter={StaticResource StringToImageConverter}}" />
```

### Error Handling

The converter gracefully handles invalid paths by returning `null`, preventing binding errors.

### Real Usage in GameProfileCardView.axaml

```xml
<!-- Profile icon display -->
<Image Source="{Binding IconPath, Converter={StaticResource StringToImageConverter}}" 
       Width="40" Height="40" />
```

### Real Usage in GameProfileSettingsWindow.axaml

```xml
<!-- Profile icon in settings -->
<Image Source="{Binding IconPath, Converter={StaticResource StringToImageConverter}, Mode=OneWay}" />

<!-- Game version icons -->
<Image Source="{Binding Path, Converter={StaticResource StringToImageConverter}}" />

<!-- Multiple icon displays for different game assets -->
<Image Source="{Binding Path, Converter={StaticResource StringToImageConverter}}" />
<Image Source="{Binding Path, Converter={StaticResource StringToImageConverter}}" />
<Image Source="{Binding Path, Converter={StaticResource StringToImageConverter}}" />
```

### Real Usage Patterns

```xml
<!-- Build preset visibility -->
<Border IsVisible="{Binding BuildPreset, Converter={StaticResource NotNullOrEmptyConverter}}">
    <TextBlock Text="{Binding BuildPreset}" />
</Border>

<!-- Command line arguments display -->
<Border IsVisible="{Binding CommandLineArguments, Converter={StaticResource NotNullOrEmptyConverter}}">
    <TextBlock Text="{Binding CommandLineArguments}" />
</Border>
```
