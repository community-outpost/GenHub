---
title: Converters Overview
description: Overview of Avalonia IValueConverter implementations in GenHub
---

# Converters Overview

GenHub provides a comprehensive suite of `IValueConverter` implementations for UI binding scenarios. These converters help transform data between ViewModels and UI elements.

## Categories

### [Boolean Converters](./bool-converters)

Converters that transform boolean values into UI-friendly representations like colors, visibility states, or custom values.

### [Null Converters](./null-converters)

Converters that handle null values and convert them to boolean or visibility states.

### [String Converters](./string-converters)

Converters that work with string values, converting them to booleans, images, or other formats.

### [Color Converters](./color-converters)

Converters that transform Colors into brushes, opacity values, or contrast-appropriate text colors.

### [Profile Converters](./profile-converters)

Converters specific to profile-related data like cover images and color opacity.

### [Enum Converters](./enum-converters)

Converters that map enum values (like GameType, ContentType) to UI elements like icons or badges.

### [Navigation Converters](./navigation-converters)

Converters for tab navigation, UI control binding, and navigation-related data transformations.

### [Data Type Converters](./data-type-converters)

Converters for numeric types, nullable values, and data type transformations for form inputs.

## Usage in GenHub

In GenHub, converters are registered locally in each XAML view where they're needed:

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

**GameProfileSettingsWindow.axaml:**

```xml
<!-- Status color for validation feedback -->
<TextBlock Foreground="{Binding IsShortcutPathValid, Converter={StaticResource BoolToStatusColorConverter}}" />

<!-- Conditional visibility for settings sections -->
<Border IsVisible="{Binding SelectedVersion, Converter={StaticResource NotNullConverter}}" />
```


### Registration Strategy

- **Local Registration**: Converters are registered in `UserControl.Resources` or `Window.Resources` of each view
- **No Global Registration**: No converters are registered globally in `App.axaml` to maintain modularity
- **On-Demand Loading**: Each view loads only the converters it actually uses
- **Consistent Naming**: Converter keys follow PascalCase naming convention matching the converter class name
