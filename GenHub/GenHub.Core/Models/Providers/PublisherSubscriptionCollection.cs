using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Root model for the subscriptions.json file stored in user's app data.
/// </summary>
public class PublisherSubscriptionCollection
{
    /// <summary>
    /// Gets or sets the format version for subscription file compatibility.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the list of publisher subscriptions.
    /// </summary>
    [JsonPropertyName("subscriptions")]
    public List<PublisherSubscription> Subscriptions { get; set; } = [];
}
