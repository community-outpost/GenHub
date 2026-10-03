using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for TheSuperHackers publisher (Game Client only).
/// </summary>
public partial class SuperHackersFilterViewModel : ContentTypeFilterViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersFilterViewModel"/> class.
    /// </summary>
    public SuperHackersFilterViewModel()
    {
        ContentTypeFilters = CreateDefaultContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.TheSuperHackers;

    private static ObservableCollection<ContentTypeFilterItem> CreateDefaultContentTypeFilters()
    {
        // TheSuperHackers only releases Game Clients / Patches
        return
        [
            new ContentTypeFilterItem(ContentType.GameClient, "Game Client"),
        ];
    }
}
