---
title: Navigation Converters
description: Converters for tab navigation and UI control binding
---

# Navigation Converters

These converters handle tab navigation, UI control binding, and navigation-related data transformations.

## `NavigationTabConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts `NavigationTab` enum values to integers and booleans for XAML binding
- **Supported Operations**:
  - Enum to boolean comparison
  - Enum to integer conversion
  - Integer to enum conversion (ConvertBack)
- **Return Type**: `bool`, `int`, or `NavigationTab`

### Tab Selection Binding

```xml
<!-- Bind tab selection to boolean -->
<Button IsChecked="{Binding CurrentTab, Converter={StaticResource NavigationTabConverter},
                           ConverterParameter={x:Static enums:NavigationTab.Home}}" />
```

### Tab Index Binding

```xml
<!-- Bind tab index to integer -->
<TabControl SelectedIndex="{Binding CurrentTab, Converter={StaticResource NavigationTabConverter}}" />
```

### Two-Way Binding

```xml
<!-- Supports two-way binding for tab changes -->
<TabControl SelectedIndex="{Binding CurrentTab, Converter={StaticResource NavigationTabConverter},
                                   Mode=TwoWay}" />
```

---

## `TabIndexToVisibilityConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts tab indices to boolean visibility for conditional UI elements
- **Logic**: Returns `true` if the current tab index matches the parameter
- **Return Type**: `bool`

### Tab-Specific Content

```xml
<!-- Show content only on specific tab -->
<StackPanel IsVisible="{Binding SelectedTabIndex, Converter={StaticResource TabIndexToVisibilityConverter},
                                ConverterParameter='1'}">
    <TextBlock Text="Advanced Settings" />
</StackPanel>
```

### Multiple Tab Conditions

```xml
<!-- Show on multiple tabs using MultiBinding -->
<ContentControl>
    <ContentControl.IsVisible>
        <MultiBinding Converter="{StaticResource TabIndexToVisibilityConverter}">
            <Binding Path="SelectedTabIndex" />
            <Binding Source="1" /> <!-- Tab index to show on -->
        </MultiBinding>
    </ContentControl.IsVisible>
</ContentControl>
```

### Real Usage Patterns

```xml
<!-- Tab navigation with enum conversion -->
<TabControl SelectedIndex="{Binding CurrentTab, Converter={StaticResource NavigationTabConverter}}">
    <TabItem Header="General" />
    <TabItem Header="Advanced" />
    <TabItem Header="Settings" />
</TabControl>

<!-- Tab-specific content visibility -->
<StackPanel IsVisible="{Binding SelectedTabIndex, Converter={StaticResource TabIndexToVisibilityConverter}, ConverterParameter='1'}">
    <TextBlock Text="Advanced Settings Panel" />
</StackPanel>

<!-- Conditional UI based on tab selection -->
<Border IsVisible="{Binding CurrentTab, Converter={StaticResource NavigationTabConverter}, ConverterParameter={x:Static enums:NavigationTab.Settings}}">
    <TextBlock Text="Settings Content" />
</Border>
```
