using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Results;

namespace GenHub.Features.Content.ViewModels;

/// <summary>
/// ViewModel for a single content item in the discovery browser.
/// </summary>
public partial class ContentItemViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentItemViewModel"/> class.
    /// </summary>
    /// <param name="model">The underlying content search result model.</param>
    public ContentItemViewModel(ContentSearchResult model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
    }

    /// <summary>
    /// Gets the underlying data model for the content item.
    /// </summary>
    public ContentSearchResult Model { get; }

    /// <summary>
    /// Gets the name of the content.
    /// </summary>
    public string Name => Model.Name ?? string.Empty;

    /// <summary>
    /// Gets the description of the content.
    /// </summary>
    public string Description => Model.Description ?? string.Empty;

    /// <summary>
    /// Gets the name of the content's author.
    /// </summary>
    public string AuthorName => Model.AuthorName ?? string.Empty;

    /// <summary>
    /// Gets the version of the content.
    /// </summary>
    public string Version => Model.Version ?? string.Empty;

    /// <summary>
    /// Gets the URL for the content's icon.
    /// </summary>
    public string IconUrl => Model.IconUrl ?? string.Empty;
}