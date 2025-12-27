using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.Content.ViewModels;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter to create a tuple from ContentItem and Profile for AddToProfileCommand.
/// The first value is the ContentItemViewModel (from parent DataContext),
/// the second value is the GameProfile (from ItemsControl binding).
/// </summary>
public class ProfileSelectionConverter : IMultiValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static ProfileSelectionConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[1] is GameProfile profile)
        {
            // Support both ContentItemViewModel and ContentManifest
            if (values[0] is ContentItemViewModel contentItem)
            {
                return new object[] { contentItem, profile };
            }
            else if (values[0] is Core.Models.Manifest.ContentManifest manifest)
            {
                return new object[] { manifest, profile };
            }
        }

        return null;
    }
}
