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
        if (values == null || values.Count == 0)
        {
            return false;
        }

        var item = values[0];
        if (item == null || item == Avalonia.AvaloniaProperty.UnsetValue)
        {
            return false;
        }

        var subscribedPr = GetValue<PullRequestInfo>(values, 1);
        var subscribedBranch = GetValue<string>(values, 2);
        var subscribedCustomBuild = GetValue<string>(values, 3);
        var subscribedPublisherId = GetValue<string>(values, 4);

        return item switch
        {
            PullRequestInfo pr => subscribedPr?.Number == pr.Number,
            string branchName => string.Equals(subscribedBranch, branchName, StringComparison.Ordinal),
            CustomBuildSubscriptionItem customBuild => IsCustomBuildSubscribed(customBuild, subscribedCustomBuild, subscribedPublisherId),
            _ => false,
        };
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
        return [];
    }

    private static T? GetValue<T>(IList<object?> values, int index)
        where T : class
    {
        return values.Count > index ? values[index] as T : null;
    }

    private static bool IsCustomBuildSubscribed(
        CustomBuildSubscriptionItem customBuild,
        string? subscribedCustomBuild,
        string? subscribedPublisherId)
    {
        if (!string.Equals(subscribedCustomBuild, customBuild.ContentId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrEmpty(subscribedPublisherId) ||
               string.Equals(subscribedPublisherId, customBuild.PublisherId, StringComparison.OrdinalIgnoreCase);
    }
}
