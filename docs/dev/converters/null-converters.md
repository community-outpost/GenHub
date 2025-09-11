---
title: Null Converters
description: Converters that handle null values and convert them to boolean or visibility states
---

# Null Converters

These converters handle null values and convert them to boolean states or visibility for UI binding scenarios.

## `NotNullConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts any value to `true` if it's not null, `false` if it is null
- **Return Type**: `bool`

### Basic Usage

```xml
<TextBlock Text="Has Value"
           IsVisible="{Binding SelectedItem, Converter={StaticResource NotNullConverter}}" />
```

---

## `NotNullOrEmptyConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts a value to `true` if it's not null and not an empty string
- **Return Type**: `bool`
- **Logic**: Returns `true` if value is not null, or if it's a string and not empty

### String/Object Usage

```xml
<Button Content="Search"
        IsEnabled="{Binding SearchQuery, Converter={StaticResource NotNullOrEmptyConverter}}" />
```

### Examples

```xml
<!-- String property -->
<TextBlock IsVisible="{Binding UserName, Converter={StaticResource NotNullOrEmptyConverter}}" />

<!-- Object property -->
<Border IsVisible="{Binding SelectedProfile, Converter={StaticResource NotNullConverter}}" />
```

---

## `NullToVisibilityConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts null values to `"Collapsed"` and non-null values to `"Visible"`
- **Return Type**: `string` ("Visible" or "Collapsed")

### Visibility Usage

```xml
<TextBlock Text="No selection"
           IsVisible="{Binding SelectedItem, Converter={StaticResource NullToVisibilityConverter}}" />
```

### Inverted Logic

For the inverse (show when null, hide when not null), you can combine with other converters or create a custom one.

---

## `NullSafePropertyConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Safely handles null values in property binding, returning a fallback value
- **Parameters**:
  - `ConverterParameter`: Optional fallback string to return for null values

### Property Binding Usage

```xml
<TextBlock Text="{Binding User.Name, Converter={StaticResource NullSafePropertyConverter},
                          ConverterParameter='Unknown User'}" />
```

### Two-Way Binding

This converter supports two-way binding, making it useful for editable fields:

```xml
<TextBox Text="{Binding User.Description, Converter={StaticResource NullSafePropertyConverter},
                        ConverterParameter='No description'}" />
```

### Real Usage in GameProfileCardView.axaml

```xml
<!-- Show version info only when available -->
<TextBlock Text="{Binding VersionId}" 
           IsVisible="{Binding VersionId, Converter={StaticResource NotNullOrEmptyConverter}}" />

<!-- Show source type badge conditionally -->
<Border IsVisible="{Binding SourceTypeName, Converter={StaticResource NotNullOrEmptyConverter}}">
    <TextBlock Text="{Binding SourceTypeName}" />
</Border>

<!-- Show workflow info when present -->
<StackPanel IsVisible="{Binding WorkflowNumber, Converter={StaticResource NotNullOrEmptyConverter}}">
    <TextBlock Text="Workflow:" />
    <TextBlock Text="{Binding WorkflowNumber}" />
</StackPanel>

<!-- Show PR info conditionally -->
<StackPanel IsVisible="{Binding PullRequestNumber, Converter={StaticResource NotNullOrEmptyConverter}}">
    <TextBlock Text="PR:" />
    <TextBlock Text="{Binding PullRequestNumber}" />
</StackPanel>

<!-- Show commit info when available -->
<StackPanel IsVisible="{Binding CommitSha, Converter={StaticResource NotNullOrEmptyConverter}}">
    <TextBlock Text="Commit:" />
    <TextBlock Text="{Binding CommitSha}" />
</StackPanel>

<!-- Show build info section -->
<Border IsVisible="{Binding BuildInfo, Converter={StaticResource NotNullConverter}}">
    <StackPanel>
        <!-- Compiler info with null safety -->
        <TextBlock IsVisible="{Binding BuildInfo, Converter={StaticResource NullSafeConverter}, ConverterParameter=Compiler, FallbackValue=False}">
            Compiler: <TextBlock Text="{Binding BuildInfo.Compiler}" />
        </TextBlock>
        
        <!-- Configuration info -->
        <TextBlock IsVisible="{Binding BuildInfo, Converter={StaticResource NullSafeConverter}, ConverterParameter=Configuration, FallbackValue=False}">
            Config: <TextBlock Text="{Binding BuildInfo.Configuration}" />
        </TextBlock>
        
        <!-- Build flags -->
        <TextBlock IsVisible="{Binding BuildInfo, Converter={StaticResource NullSafeConverter}, ConverterParameter=HasTFlag, FallbackValue=False}">
            T-Flag: <TextBlock Text="{Binding BuildInfo.HasTFlag}" />
        </TextBlock>
    </StackPanel>
</Border>
```

### Real Usage in GameProfileSettingsWindow.axaml

```xml
<!-- Show version details when selected -->
<Border IsVisible="{Binding SelectedVersion, Converter={StaticResource NotNullConverter}, FallbackValue=False}">
    <TextBlock Text="{Binding SelectedVersion.Name}" />
</Border>

<!-- Show build date when available -->
<StackPanel IsVisible="{Binding SelectedVersion.BuildDate, Converter={StaticResource NotNullConverter}}">
    <TextBlock Text="Built:" />
    <TextBlock Text="{Binding SelectedVersion.BuildDate}" />
</StackPanel>

<!-- Show source type info -->
<Border IsVisible="{Binding SelectedVersion.SourceType, Converter={StaticResource NotNullConverter}}">
    <TextBlock Text="{Binding SelectedVersion.SourceType}" />
</Border>
```
