---
title: Enum Converters
description: Converters that transform enum values into display strings, icons, or badges
---

# Enum Converters

These converters transform enum values into user-friendly display elements like icons, badges, and localized strings for UI presentation.

## `GameTypeToIconConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts `GameType` enum values into corresponding icon paths or resources
- **Supported Values**:
  - `GameType.CommandAndConquerGenerals` → Generals icon
  - `GameType.CommandAndConquerGeneralsZeroHour` → Zero Hour icon
  - `GameType.Custom` → Custom game icon
- **Return Type**: `string` (icon path/resource key)

### Icon Display

```xml
<Image Source="{Binding GameType, Converter={StaticResource GameTypeToIconConverter}}" />
```

### Icon with Fallback

```xml
<Image Source="{Binding SelectedGame.GameType, Converter={StaticResource GameTypeToIconConverter}}" 
       FallbackSource="avares://GenHub/Assets/Icons/default-game.png" />
```

---

## `SourceTypeToBadgeConverters`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts `ContentType` enum values into badge text or background colors for UI indicators
- **Supported Types**:
  - `ContentType.GameClient` → Amber background ("CAS" text)
  - `ContentType.Mod` → Blue background ("Mod" text)
  - Other types → Green background (type name text)
- **Return Types**:
  - `SourceTypeToBadgeBackgroundConverter`: `SolidColorBrush`
  - `SourceTypeToBadgeTextConverter`: `string`### Badge Display

```xml
<Border Background="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeBackgroundConverter}}"
        CornerRadius="12" Padding="8,4">
    <TextBlock Text="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeTextConverter}}"
               Foreground="White" FontSize="12" />
</Border>
```

### Styled Badge

```xml
<Border Background="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeBackgroundConverter}}"
        CornerRadius="8" Padding="6,2">
    <TextBlock Text="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeTextConverter}}"
               Foreground="White" FontSize="11" />
</Border>
```

---

## Usage Patterns

### Game Selection UI

```xml
<ItemsControl Items="{Binding AvailableGames}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border BorderBrush="Gray" BorderThickness="1" CornerRadius="4" Margin="4">
                <StackPanel Orientation="Horizontal">
                    <Image Source="{Binding GameType, Converter={StaticResource GameTypeToIconConverter}}" 
                           Width="32" Height="32" Margin="8" />
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="{Binding Name}" FontWeight="Bold" />
                        <TextBlock Text="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeTextConverter}}" 
                                   FontSize="10" Opacity="0.7" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### Profile Status Indicators

```xml
<StackPanel Orientation="Horizontal">
    <Border Background="{Binding Profile.ContentType, Converter={StaticResource SourceTypeToBadgeBackgroundConverter}}"
            CornerRadius="8" Padding="6,2">
        <TextBlock Text="{Binding Profile.ContentType, Converter={StaticResource SourceTypeToBadgeTextConverter}}"
                   Foreground="White" FontSize="11" />
    </Border>
    <Image Source="{Binding Profile.GameType, Converter={StaticResource GameTypeToIconConverter}}" 
           Width="24" Height="24" Margin="8,0" />
</StackPanel>
```

### Real Usage Patterns

```xml
<!-- Game type icons in profile cards -->
<Image Source="{Binding GameType, Converter={StaticResource GameTypeToIconConverter}}" />

<!-- Content type badges in content lists -->
<Border Background="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeBackgroundConverter}}"
        CornerRadius="4" Padding="4">
    <TextBlock Text="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeTextConverter}}" />
</Border>
```

### Real Usage in Game Profile Scenarios

```xml
<!-- Game type selection with icons -->
<ComboBox ItemsSource="{Binding AvailableGameTypes}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <Image Source="{Binding ., Converter={StaticResource GameTypeToIconConverter}}" Width="16" Height="16" />
                <TextBlock Text="{Binding .}" Margin="8,0,0,0" />
            </StackPanel>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>

<!-- Content type indicators -->
<ItemsControl ItemsSource="{Binding ContentItems}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Background="{Binding ContentType, Converter={StaticResource SourceTypeToBadgeBackgroundConverter}}"
                    CornerRadius="4" Padding="4">
                <TextBlock Text="{Binding Title}" />
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
