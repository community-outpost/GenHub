using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts WorkspaceStrategy enum values to descriptive tooltip text.
/// </summary>
public class WorkspaceStrategyTooltipConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="WorkspaceStrategyTooltipConverter"/>.
    /// </summary>
    public static readonly WorkspaceStrategyTooltipConverter Instance = new();

    /// <summary>
    /// Converts a WorkspaceStrategy value to a descriptive tooltip string.
    /// </summary>
    /// <param name="value">The WorkspaceStrategy value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A descriptive tooltip string for the workspace strategy.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is WorkspaceStrategy strategy)
        {
            return strategy switch
            {
                WorkspaceStrategy.SymlinkOnly => "Creates symbolic links to all files. Minimal disk usage, requires admin rights. (Legacy)",
                WorkspaceStrategy.FullCopy => "Copies all files to workspace. Maximum compatibility and isolation, highest disk usage.",
                WorkspaceStrategy.HybridCopySymlink => "Copies essential files, symlinks others. Balanced disk usage and compatibility. (Legacy)",
                WorkspaceStrategy.HardLink => "Creates hard links where possible, copies otherwise. Space-efficient, requires same volume. (Default)",
                _ => strategy.ToString(),
            };
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Not supported for one-way conversion.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
