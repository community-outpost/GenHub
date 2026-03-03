using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.AppUpdate;
using GenHub.Features.AppUpdate.ViewModels;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter to check if a PR or Branch is currently subscribed.
/// Expects values: [Item, UpdateNotificationViewModel.SubscribedPr, UpdateNotificationViewModel.SubscribedBranch].
/// </summary>
public class IsSubscribedConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3)
        {
            return false;
        }

        var item = values[0];
        var subscribedPr = values[1] as PullRequestInfo;
        var subscribedBranch = values[2] as string;

        if (item is PullRequestInfo pr)
        {
            return subscribedPr?.Number == pr.Number;
        }
        else if (item is string branchName)
        {
            return string.Equals(subscribedBranch, branchName, StringComparison.OrdinalIgnoreCase);
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
