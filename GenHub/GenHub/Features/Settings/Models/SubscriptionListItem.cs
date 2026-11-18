using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Settings.Models;

/// <summary>
/// A catalog subscription row in Settings, paired with its automatic update preference.
/// </summary>
public partial class SubscriptionListItem : ObservableObject
{
    [ObservableProperty]
    private bool _isAutoUpdateEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionListItem"/> class.
    /// </summary>
    /// <param name="subscription">The catalog subscription.</param>
    /// <param name="isAutoUpdateEnabled">Whether updates from this catalog are applied without prompting.</param>
    public SubscriptionListItem(PublisherSubscription subscription, bool isAutoUpdateEnabled)
    {
        Subscription = subscription;
        _isAutoUpdateEnabled = isAutoUpdateEnabled;
        SavedAutoUpdateEnabled = isAutoUpdateEnabled;
    }

    /// <summary>
    /// Gets the catalog subscription.
    /// </summary>
    public PublisherSubscription Subscription { get; }

    /// <summary>
    /// Gets how many times the automatic update preference has changed since the row was created.
    /// </summary>
    public int AutoUpdateRevision { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last successfully saved preference enables automatic updates.
    /// </summary>
    public bool SavedAutoUpdateEnabled { get; set; }

    partial void OnIsAutoUpdateEnabledChanged(bool value) => AutoUpdateRevision++;
}
