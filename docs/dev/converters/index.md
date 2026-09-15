---
title: Converters Overview
description: Overview of Avalonia IValueConverter implementations in GenHub
---

# Converters Overview

GenHub provides a comprehensive suite of `IValueConverter` and `IMultiValueConverter` implementations for UI binding scenarios. These converters help transform data between ViewModels and UI elements.

## Categories

### [Boolean Converters](./bool-converters)

Converters that transform boolean values into UI-friendly representations like colors, visibility states, or custom values.

### [Null Converters](./null-converters)

Converters that handle null values and convert them to boolean or visibility states.

### [String Converters](./string-converters)

Converters that work with string values, converting them to booleans, images, or other formats.

### [Color Converters](./color-converters)

Converters that transform Colors or ContentTypes into brushes, opacity values, contrast-appropriate text colors, and badge background tints (`ContentTypeToBrushConverter`, `ContentTypeToBadgeBackgroundConverter`).

### [Profile Converters](./profile-converters)

Converters specific to profile-related data like cover images, color opacity, and profile selection multi-value binding (`ProfileSelectionConverter`).

### [Enum Converters](./enum-converters)

Converters that map enum values (like GameType, ContentType) to UI elements like icons or badges.

### [Navigation Converters](./navigation-converters)

Converters for tab navigation, UI control binding, and navigation-related data transformations.

### [Data Type Converters](./data-type-converters)

Converters for numeric types, nullable values, and data type transformations for form inputs.

## Usage in GenHub

In GenHub, converters are registered locally in each XAML view or accessed via singleton instances:

### Local Registration Pattern

```xml
<UserControl xmlns:conv="clr-namespace:GenHub.Infrastructure.Converters">
    <UserControl.Resources>
        <conv:ContrastTextColorConverter x:Key="ContrastTextColorConverter" />
        <conv:ProfileCoverConverter x:Key="ProfileCoverConverter" />
        <conv:StringToImageConverter x:Key="StringToImageConverter" />
        <conv:NullSafePropertyConverter x:Key="NullSafeConverter"/>
        <conv:BoolToValueConverter x:Key="NotConverter" TrueValue="False" FalseValue="True" />
    </UserControl.Resources>
</UserControl>
```

### Static Instance Pattern

Several modern converters provide static singleton instances for convenient zero-allocation XAML references:

```xml
<!-- Direct static singleton reference without local resource dictionary declaration -->
<Border Background="{Binding ContentType, Converter={x:Static conv:ContentTypeToBadgeBackgroundConverter.Instance}}" />
```

### Examples from the codebase

**GameProfileCardView.axaml:**

```xml
<!-- Profile color with opacity for semi-transparent backgrounds -->
<Border Background="{Binding ColorValue, Converter={StaticResource ProfileColorToOpacityConverter}, ConverterParameter=0.6}" />

<!-- Text color that contrasts with background -->
<TextBlock Foreground="{Binding ColorValue, Converter={StaticResource ContrastTextColorConverter}}" />

<!-- Safe property access for optional data -->
<TextBlock Text="{Binding BuildInfo.Compiler, Converter={StaticResource NullSafeConverter}, ConverterParameter='Unknown'}" />
```

**ContentCardView.axaml / ContentDetailView.axaml:**

```xml
<!-- Badge border and foreground colored by content type (via StaticResource) -->
<Border Classes="type-badge"
        BorderBrush="{Binding ContentType, Converter={StaticResource ContentTypeToBrushConverter}}">
    <TextBlock Text="{Binding ContentTypeDisplay}"
               Foreground="{Binding ContentType, Converter={StaticResource ContentTypeToBrushConverter}}" />
</Border>

<!-- Translucent tinted pill background in ContentDetailView -->
<Border Background="{Binding ContentType, Converter={StaticResource ContentTypeToBadgeBackgroundConverter}}"
        BorderBrush="{Binding ContentType, Converter={StaticResource ContentTypeToBrushConverter}}">
    <TextBlock Text="{Binding ContentType, Converter={StaticResource ContentTypeDisplayConverter}}"
               Foreground="{Binding ContentType, Converter={StaticResource ContentTypeToBrushConverter}}" />
</Border>
```
