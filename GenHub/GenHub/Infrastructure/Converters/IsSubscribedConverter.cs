using Avalonia.Data.Converters;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter to check if a PR, Branch, or Custom Build is currently subscribed.
/// Expects values: [Item, UpdateNotificationViewModel.SubscribedPr, UpdateNotificationViewModel.SubscribedBranch, (optional) UpdateNotificationViewModel.SubscribedCustomBuildContentId].
/// </summary>
public class IsSubscribedConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 1)
        {
            return false;
        }

        var item = values[0];
        if (item == null || item == Avalonia.AvaloniaProperty.UnsetValue)
        {
            return false;
        }

        var subscribedPr = values.Count > 1 ? values[1] as PullRequestInfo : null;
        var subscribedBranch = values.Count > 2 ? values[2] as string : null;
        var subscribedCustomBuild = values.Count > 3 ? values[3] as string : null;
        var subscribedPublisherId = values.Count > 4 ? values[4] as string : null;

        if (item is PullRequestInfo pr)
        {
            return subscribedPr?.Number == pr.Number;
        }

        if (item is string branchName)
        {
            return string.Equals(subscribedBranch, branchName, StringComparison.Ordinal);
        }

        if (item is CustomBuildSubscriptionItem customBuild)
        {
            var contentMatches = string.Equals(subscribedCustomBuild, customBuild.ContentId, StringComparison.OrdinalIgnoreCase);
            if (!contentMatches)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(subscribedPublisherId) && !string.IsNullOrWhiteSpace(customBuild.PublisherId))
            {
                return string.Equals(subscribedPublisherId, customBuild.PublisherId, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a binding target value to the source binding values.
    /// </summary>
    /// <param name="value">The value that the binding target produces.</param>
    /// <param name="targetTypes">The types to convert to.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>An array of values that have been converted from the target value back to the source values.</returns>
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return Array.Empty<object?>();
    }
}
