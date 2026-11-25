using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for Community Outpost publisher.
/// Limits options to valid content types (no Mods/Maps filters if confusing, although they act as categories).
/// </summary>
public partial class CommunityOutpostFilterViewModel : ContentTypeFilterViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostFilterViewModel"/> class.
    /// </summary>
    public CommunityOutpostFilterViewModel()
    {
        ContentTypeFilters = CreateDefaultContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.CommunityOutpost;

    private static ObservableCollection<ContentTypeFilterItem> CreateDefaultContentTypeFilters()
    {
        // Only include relevant types for Community Outpost
        // Community Outpost is a curated catalog, so we only show what they have
        // Note: Filter types must match ContentType values used in GenPatcherContentRegistry
        // Tools (gent, gena) are ContentType.Addon in the registry, not Executable
        // Executable is only used for prerequisites (vc05, vc08, vc10)
        return
        [
            new ContentTypeFilterItem(ContentType.GameClient, CommunityOutpostConstants.ContentTypeGameClients),
            new ContentTypeFilterItem(ContentType.Addon, CommunityOutpostConstants.ContentTypeAddons),
            new ContentTypeFilterItem(ContentType.MapPack, CommunityOutpostConstants.ContentTypeMaps),
            new ContentTypeFilterItem(ContentType.Executable, "Prerequisites"),
        ];
    }
}
