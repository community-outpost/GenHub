using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Sidebar entry for a content source in Downloads.
/// </summary>
/// <remarks>
/// <paramref name="publisherType"/> is <c>static</c> / <c>dynamic</c> for built-in providers,
/// or <see cref="GenHub.Core.Constants.CatalogConstants.SubscribedPublisherCategory"/> for
/// user-subscribed catalogs from <c>subscriptions.json</c>.
/// </remarks>
public partial class PublisherItemViewModel(
    string publisherId,
    string displayName,
    string? logoSource = null,
    string? publisherType = null) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _contentCount;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Gets the publisher ID.
    /// </summary>
    public string PublisherId { get; } = publisherId;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// Gets the logo source path or URL.
    /// </summary>
    public string? LogoSource { get; } = logoSource;

    /// <summary>
    /// Gets the publisher type (static for official publishers, dynamic for community).
    /// </summary>
    public string PublisherType { get; } = publisherType ?? "static";

    /// <summary>
    /// Gets a value indicating whether this is an official/static publisher.
    /// </summary>
    public bool IsStaticPublisher =>
        PublisherType.Equals("static", StringComparison.OrdinalIgnoreCase);
}
