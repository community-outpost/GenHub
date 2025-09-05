---
title: Data Type Converters
description: Converters for numeric types, nullable values, and data type transformations
---

# Data Type Converters

These converters handle numeric data types, nullable values, and type conversions for form inputs and data binding.

## `NullableDoubleConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts between nullable doubles and strings with graceful null handling
- **Features**:
  - Double to string conversion with culture support
  - String to double conversion with fallback defaults
  - Handles empty/whitespace strings safely
- **Return Type**: `string` or `double`

### Numeric Input Binding

```xml
<!-- Bind double property to TextBox -->
<TextBox Text="{Binding ScaleFactor, Converter={StaticResource NullableDoubleConverter}}" />
```

### With Default Parameter

```xml
<!-- Use ConverterParameter for default value -->
<TextBox Text="{Binding Opacity, Converter={StaticResource NullableDoubleConverter},
                       ConverterParameter='1.0'}" />
```

### Culture-Aware Formatting

```xml
<!-- Automatically formats with current culture -->
<TextBlock Text="{Binding Price, Converter={StaticResource NullableDoubleConverter}}" />
```

---

## `NullableIntConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts between nullable integers and strings with graceful null handling
- **Features**:
  - Integer to string conversion
  - String to integer conversion with fallback defaults
  - Handles empty/whitespace strings safely
- **Return Type**: `string` or `int`

### Integer Input Binding

```xml
<!-- Bind int property to TextBox -->
<TextBox Text="{Binding PortNumber, Converter={StaticResource NullableIntConverter}}" />
```

### With Default Parameter (Integer)

```xml
<!-- Use ConverterParameter for default value -->
<TextBox Text="{Binding MaxRetries, Converter={StaticResource NullableIntConverter},
                       ConverterParameter='3'}" />
```

---

## `StringToIntConverter`

- **Namespace**: `GenHub.Infrastructure.Converters`
- **Purpose**: Converts strings to integers for CommandParameter binding scenarios
- **Features**:
  - String to integer conversion
  - Integer to string conversion (ConvertBack)
  - Safe fallback to 0 for invalid inputs
- **Return Type**: `int` or `string`

### Command Parameter Binding

```xml
<!-- Convert string command parameter to int -->
<Button Command="{Binding SelectItemCommand}"
        CommandParameter="{Binding ItemId, Converter={StaticResource StringToIntConverter}}" />
```

### Numeric Input Validation

```xml
<!-- Bind string input to integer property -->
<TextBox Text="{Binding Quantity, Converter={StaticResource StringToIntConverter}}" />
```

### Real Usage Patterns

```xml
<!-- Numeric input with culture-aware formatting -->
<TextBox Text="{Binding ScaleFactor, Converter={StaticResource NullableDoubleConverter}}" />

<!-- Integer input with validation -->
<TextBox Text="{Binding PortNumber, Converter={StaticResource NullableIntConverter}}" />

<!-- Command parameter conversion -->
<Button Command="{Binding SelectItemCommand}"
        CommandParameter="{Binding ItemId, Converter={StaticResource StringToIntConverter}}" />

<!-- Form validation with defaults -->
<TextBox Text="{Binding MaxRetries, Converter={StaticResource NullableIntConverter}, ConverterParameter='3'}" />
```

### Real Usage in Configuration Scenarios

```xml
<!-- Settings with safe defaults -->
<NumericUpDown Value="{Binding TimeoutSeconds, Converter={StaticResource NullableIntConverter}, ConverterParameter='30'}" />

<!-- Percentage inputs -->
<TextBox Text="{Binding Opacity, Converter={StaticResource NullableDoubleConverter}, ConverterParameter='1.0'}" />

<!-- ID-based selections -->
<ComboBox SelectedValue="{Binding SelectedId, Converter={StaticResource StringToIntConverter}}" />
```
