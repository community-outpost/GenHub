using GenHub.Core.Constants;
using GenHub.Core.Models.Providers;
using System.Text.Json;
using Xunit;

namespace GenHub.Tests.Core.Models.Providers;

/// <summary>
/// Unit tests for <see cref="PublisherDefinition"/> serialization and legacy compatibility normalization.
/// </summary>
public class PublisherDefinitionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Verifies that when legacy catalogUrl appears before an empty catalogs array in JSON,
    /// OnDeserialized restores the primary catalog entry URL.
    /// </summary>
    [Fact]
    public void Deserialize_CatalogUrlBeforeEmptyCatalogs_RestoresPrimaryCatalogUrl()
    {
        const string json = """
            {
                "$schemaVersion": 1,
                "publisher": { "id": "test-pub", "name": "Test Publisher" },
                "catalogUrl": "https://example.com/primary.json",
                "catalogs": []
            }
            """;

        var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, JsonOptions);

        Assert.NotNull(definition);
        Assert.Equal("https://example.com/primary.json", definition.CatalogUrl);
        var primary = Assert.Single(definition.Catalogs);
        Assert.Equal("https://example.com/primary.json", primary.Url);
        Assert.Equal(CatalogConstants.DefaultCatalogId, primary.Id);
        Assert.Equal(CatalogConstants.DefaultCatalogName, primary.Name);
    }

    /// <summary>
    /// Verifies that when legacy catalogUrl appears after an empty catalogs array in JSON,
    /// OnDeserialized preserves the primary catalog entry URL.
    /// </summary>
    [Fact]
    public void Deserialize_CatalogUrlAfterEmptyCatalogs_RestoresPrimaryCatalogUrl()
    {
        const string json = """
            {
                "$schemaVersion": 1,
                "publisher": { "id": "test-pub", "name": "Test Publisher" },
                "catalogs": [],
                "catalogUrl": "https://example.com/primary.json"
            }
            """;

        var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, JsonOptions);

        Assert.NotNull(definition);
        Assert.Equal("https://example.com/primary.json", definition.CatalogUrl);
        var primary = Assert.Single(definition.Catalogs);
        Assert.Equal("https://example.com/primary.json", primary.Url);
        Assert.Equal(CatalogConstants.DefaultCatalogId, primary.Id);
        Assert.Equal(CatalogConstants.DefaultCatalogName, primary.Name);
    }

    /// <summary>
    /// Verifies that when catalogs begins with a null entry, legacy catalogUrl replaces the null
    /// entry with a synthesized primary entry while preserving sibling catalogs.
    /// </summary>
    [Fact]
    public void Deserialize_CatalogsWithNullFirstEntryAndLegacyCatalogUrl_ReplacesNullWithPrimaryCatalog()
    {
        const string json = """
            {
                "$schemaVersion": 1,
                "publisher": { "id": "test-pub", "name": "Test Publisher" },
                "catalogUrl": "https://example.com/primary.json",
                "catalogs": [
                    null,
                    {
                        "id": "second",
                        "name": "Second Catalog",
                        "url": "https://example.com/second.json"
                    }
                ]
            }
            """;

        var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, JsonOptions);

        Assert.NotNull(definition);
        Assert.Equal(2, definition.Catalogs.Count);
        var primary = definition.Catalogs[0];
        Assert.NotNull(primary);
        Assert.Equal("https://example.com/primary.json", primary.Url);
        Assert.Equal(CatalogConstants.DefaultCatalogId, primary.Id);
        Assert.Equal(CatalogConstants.DefaultCatalogName, primary.Name);
        Assert.Equal("https://example.com/primary.json", definition.CatalogUrl);

        var second = definition.Catalogs[1];
        Assert.NotNull(second);
        Assert.Equal("second", second.Id);
        Assert.Equal("https://example.com/second.json", second.Url);
    }

    /// <summary>
    /// Verifies that when the primary catalog has a blank URL and a legacy catalogUrl is present,
    /// OnDeserialized populates the primary entry's URL.
    /// </summary>
    [Fact]
    public void Deserialize_CatalogsWithBlankPrimaryUrlAndLegacyCatalogUrl_PopulatesPrimaryUrl()
    {
        const string json = """
            {
                "$schemaVersion": 1,
                "publisher": { "id": "test-pub", "name": "Test Publisher" },
                "catalogUrl": "https://example.com/primary.json",
                "catalogs": [
                    {
                        "id": "main",
                        "name": "Main Catalog",
                        "url": ""
                    }
                ]
            }
            """;

        var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, JsonOptions);

        Assert.NotNull(definition);
        var primary = Assert.Single(definition.Catalogs);
        Assert.Equal("https://example.com/primary.json", primary.Url);
        Assert.Equal("main", primary.Id);
    }

    /// <summary>
    /// Verifies that legacy catalogMirrors are normalized into the primary catalog across key orderings.
    /// </summary>
    [Fact]
    public void Deserialize_LegacyCatalogMirrors_PopulatesPrimaryMirrors()
    {
        const string json = """
            {
                "$schemaVersion": 1,
                "publisher": { "id": "test-pub", "name": "Test Publisher" },
                "catalogMirrors": [
                    "https://mirror1.example.com/catalog.json",
                    "https://mirror2.example.com/catalog.json"
                ],
                "catalogs": []
            }
            """;

        var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, JsonOptions);

        Assert.NotNull(definition);
        var primary = Assert.Single(definition.Catalogs);
        Assert.Equal(2, primary.Mirrors.Count);
        Assert.Equal("https://mirror1.example.com/catalog.json", primary.Mirrors[0]);
        Assert.Equal("https://mirror2.example.com/catalog.json", primary.Mirrors[1]);
        Assert.Equal(2, definition.CatalogMirrors.Count);
    }

    /// <summary>
    /// Verifies that setting CatalogUrl programmatically on a new definition initializes
    /// Catalogs[0] using DefaultCatalogId and DefaultCatalogName.
    /// </summary>
    [Fact]
    public void CatalogUrl_SetterOnEmptyCatalogs_CreatesPrimaryCatalogWithDefaultConstants()
    {
        var definition = new PublisherDefinition();
        definition.CatalogUrl = "https://example.com/test.json";

        var primary = Assert.Single(definition.Catalogs);
        Assert.Equal("https://example.com/test.json", primary.Url);
        Assert.Equal(CatalogConstants.DefaultCatalogId, primary.Id);
        Assert.Equal(CatalogConstants.DefaultCatalogName, primary.Name);
    }

    /// <summary>
    /// Verifies that setting CatalogMirrors programmatically on a new definition initializes
    /// Catalogs[0] using DefaultCatalogId and DefaultCatalogName.
    /// </summary>
    [Fact]
    public void CatalogMirrors_SetterOnEmptyCatalogs_CreatesPrimaryCatalogWithDefaultConstants()
    {
        var definition = new PublisherDefinition();
        definition.CatalogMirrors = ["https://mirror.example.com/test.json"];

        var primary = Assert.Single(definition.Catalogs);
        Assert.Single(primary.Mirrors);
        Assert.Equal(CatalogConstants.DefaultCatalogId, primary.Id);
        Assert.Equal(CatalogConstants.DefaultCatalogName, primary.Name);
    }
}
