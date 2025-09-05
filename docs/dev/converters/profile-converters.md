---
title: Profile Converters
description: Converters for game profile data transformation and display
---

# Profile Converters

These converters handle game profile-specific data transformations, including cover images, color opacity, and profile-related UI elements.

## `ProfileCoverConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts profile cover paths or URIs into displayable images
- **Supported Inputs**:
  - File paths (string)
  - URIs (string)
  - `null` values (returns fallback image)
- **Return Type**: `Bitmap` or fallback image

### Basic Usage

```xml
<Image Source="{Binding CoverPath, Converter={StaticResource ProfileCoverConverter}}" />
```

### Fallback Handling

```xml
<!-- Automatically handles missing covers -->
<Image Source="{Binding Profile.CoverUri, Converter={StaticResource ProfileCoverConverter}}" />
```

### Common Scenarios

- **Profile Selection UI**: Display profile covers in lists or grids
- **Game Launch Screen**: Show selected profile's cover image
- **Fallback Images**: Graceful handling of missing or invalid cover files

---

## `ProfileColorToOpacityConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Extracts opacity from profile colors for UI transparency effects
- **Formula**: `color.A / 255.0`
- **Return Type**: `double`

### Opacity Effects

```xml
<Border Opacity="{Binding ProfileColor, Converter={StaticResource ProfileColorToOpacityConverter}}" />
```

### Overlay Effects

```xml
<!-- Create semi-transparent overlays -->
<Rectangle Fill="{Binding ProfileColor, Converter={StaticResource ColorToBrushConverter}}" 
           Opacity="{Binding ProfileColor, Converter={StaticResource ProfileColorToOpacityConverter}}" />
```

---

## Usage Patterns

### Profile Card Layout

```xml
<Border Background="{Binding ProfileColor, Converter={StaticResource ColorToBrushConverter}}"
        Opacity="{Binding ProfileColor, Converter={StaticResource ProfileColorToOpacityConverter}}">
    <StackPanel>
        <Image Source="{Binding CoverPath, Converter={StaticResource ProfileCoverConverter}}" 
               Width="120" Height="90" />
        <TextBlock Text="{Binding ProfileName}" 
                   Foreground="{Binding ProfileColor, Converter={StaticResource ContrastTextColorConverter}}" />
    </StackPanel>
</Border>
```

### Profile Selection Grid

```xml
<ItemsControl Items="{Binding GameProfiles}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Background="{Binding Color, Converter={StaticResource ColorToBrushConverter}}"
                    CornerRadius="8" Margin="4">
                <StackPanel>
                    <Image Source="{Binding CoverUri, Converter={StaticResource ProfileCoverConverter}}" />
                    <TextBlock Text="{Binding Name}" 
                               Foreground="{Binding Color, Converter={StaticResource ContrastTextColorConverter}}" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### Real Usage in GameProfileCardView.axaml

```xml
<!-- Profile cover image display -->
<Image Source="{Binding CoverImagePath, Converter={StaticResource ProfileCoverConverter}}" />

<!-- Profile card background with opacity -->
<Border Background="{Binding ColorValue, Converter={StaticResource ProfileColorToOpacityConverter}, ConverterParameter=0.6}" />
```

### Real Usage Patterns

```xml
<!-- Profile selection UI -->
<ItemsControl Items="{Binding GameProfiles}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Background="{Binding Color, Converter={StaticResource ColorToBrushConverter}}"
                    Opacity="{Binding Color, Converter={StaticResource ProfileColorToOpacityConverter}}">
                <StackPanel>
                    <Image Source="{Binding CoverUri, Converter={StaticResource ProfileCoverConverter}}" />
                    <TextBlock Text="{Binding Name}" 
                               Foreground="{Binding Color, Converter={StaticResource ContrastTextColorConverter}}" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
