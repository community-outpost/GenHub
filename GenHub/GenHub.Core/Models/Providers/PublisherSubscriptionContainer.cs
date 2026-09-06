using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Root model for <c>subscriptions.json</c> — the user's list of followed creator catalogs
/// (and later provider definitions) under application data.
/// </summary>
public class PublisherSubscriptionContainer
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
