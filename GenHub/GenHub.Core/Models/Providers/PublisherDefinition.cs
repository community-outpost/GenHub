using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a publisher's definition file that contains identity,
/// catalog URLs, and self-update information.
/// This is the recommended subscription endpoint for users.
/// </summary>
public class PublisherDefinition : IJsonOnDeserialized
{
    private List<CatalogEntry> _catalogs = [];
    private List<string> _previousDefinitionUrls = [];
    private List<PublisherReferral> _referrals = [];
    private List<string> _tags = [];
    private string? _legacyCatalogUrl;
    private List<string>? _legacyCatalogMirrors;

    /// <summary>
    /// Gets or sets the schema version for definition format compatibility.
    /// </summary>
    [JsonPropertyName("$schemaVersion")]
    public int SchemaVersion { get; set; } = CatalogConstants.DefinitionSchemaVersion;

    /// <summary>
    /// Gets or sets the publisher identity and branding information.
    /// </summary>
    [JsonPropertyName("publisher")]
    public PublisherProfile Publisher { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of catalogs provided by this publisher.
    /// Each catalog can contain different types of content.
    /// </summary>
    [JsonPropertyName("catalogs")]
    public List<CatalogEntry> Catalogs
    {
        get => _catalogs ??= [];
        set => _catalogs = value ?? [];
    }

    /// <summary>
    /// Gets or sets historical URLs where this definition was previously hosted.
    /// Helps clients update their subscription URL if the publisher moved.
    /// </summary>
    [JsonPropertyName("previousDefinitionUrls")]
    public List<string> PreviousDefinitionUrls
    {
        get => _previousDefinitionUrls ??= [];
        set => _previousDefinitionUrls = value ?? [];
    }

    /// <summary>
    /// Gets or sets the primary URL to the publisher's catalog JSON.
    /// Computed property for convenience - accesses Catalogs[0].
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string CatalogUrl
    {
        get => (Catalogs.Count > 0 && Catalogs[0] != null) ? (Catalogs[0].Url ?? string.Empty) : (_legacyCatalogUrl ?? string.Empty);
        set
        {
            _legacyCatalogUrl = value;
            GetOrCreatePrimaryCatalog().Url = value;
        }
    }

    /// <summary>
    /// Gets or sets alternate catalog URLs for redundancy.
    /// Computed property for convenience - accesses Catalogs[0].
    /// </summary>
    [JsonPropertyName("catalogMirrors")]
    public List<string> CatalogMirrors
    {
        get => (Catalogs.Count > 0 && Catalogs[0] != null) ? Catalogs[0].Mirrors : (_legacyCatalogMirrors ?? []);
        set
        {
            _legacyCatalogMirrors = value;
            GetOrCreatePrimaryCatalog().Mirrors = value;
        }
    }

    /// <summary>
    /// Gets or sets the URL where this definition file is hosted.
    /// Used for self-updates (e.g. if the publisher moves their catalog hosting).
    /// </summary>
    [JsonPropertyName("definitionUrl")]
    public string? DefinitionUrl { get; set; }

    /// <summary>
    /// Gets or sets when this definition was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets referrals to other publishers (cross-publisher discovery).
    /// </summary>
    [JsonPropertyName("referrals")]
    public List<PublisherReferral> Referrals
    {
        get => _referrals ??= [];
        set => _referrals = value ?? [];
    }

    /// <summary>
    /// Gets or sets tags for publisher categorization.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags ??= [];
        set => _tags = value ?? [];
    }

    /// <inheritdoc/>
    public void OnDeserialized()
    {
        if (!string.IsNullOrWhiteSpace(_legacyCatalogUrl) &&
            (Catalogs.Count == 0 || Catalogs[0] == null || string.IsNullOrWhiteSpace(Catalogs[0].Url)))
        {
            GetOrCreatePrimaryCatalog().Url = _legacyCatalogUrl;
        }

        if (_legacyCatalogMirrors != null && _legacyCatalogMirrors.Count > 0 &&
            (Catalogs.Count == 0 || Catalogs[0] == null || Catalogs[0].Mirrors.Count == 0))
        {
            GetOrCreatePrimaryCatalog().Mirrors = _legacyCatalogMirrors;
        }
    }

    private CatalogEntry GetOrCreatePrimaryCatalog()
    {
        if (Catalogs.Count == 0 || Catalogs[0] == null)
        {
            var entry = new CatalogEntry
            {
                Id = CatalogConstants.DefaultCatalogId,
                Name = CatalogConstants.DefaultCatalogName,
            };

            if (Catalogs.Count == 0)
            {
                Catalogs.Add(entry);
            }
            else
            {
                Catalogs[0] = entry;
            }

            return entry;
        }

        return Catalogs[0];
    }
}
