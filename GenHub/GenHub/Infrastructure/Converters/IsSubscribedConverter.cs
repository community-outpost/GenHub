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
        if (values.Count < 3) return false;

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
    /// <inheritdoc/>
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return Array.Empty<object?>();
    }
}
