using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for static publishers (GeneralsOnline, SuperHackers, CommunityOutpost).
/// Provides content type filtering with toggle buttons.
/// </summary>
public partial class StaticPublisherFilterViewModel : ContentTypeFilterViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StaticPublisherFilterViewModel"/> class.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    public StaticPublisherFilterViewModel(string publisherId)
    {
        PublisherId = publisherId;
        ContentTypeFilters = CreateDefaultContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId { get; }

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        baseQuery.ContentType = SelectedContentType;

        return baseQuery;
    }

    private static ObservableCollection<ContentTypeFilterItem> CreateDefaultContentTypeFilters()
    {
        return
        [
            new ContentTypeFilterItem(ContentType.GameClient, "Game clients"),
            new ContentTypeFilterItem(ContentType.Addon, "Add-ons"),
            new ContentTypeFilterItem(ContentType.MapPack, "Map packs"),
            new ContentTypeFilterItem(ContentType.Executable, "Runtime components"),
        ];
    }
}
