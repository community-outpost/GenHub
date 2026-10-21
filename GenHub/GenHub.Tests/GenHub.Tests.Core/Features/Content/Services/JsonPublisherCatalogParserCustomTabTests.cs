using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests parsing and validation of publisher-defined custom tabs in <see cref="JsonPublisherCatalogParser"/>.
/// </summary>
public sealed class JsonPublisherCatalogParserCustomTabTests
{
    private readonly JsonPublisherCatalogParser _parser = new(NullLogger<JsonPublisherCatalogParser>.Instance);

    /// <summary>
    /// Per-item tabs parse with their Markdown body and cards, and stay on their own item.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_ItemCustomTabs_ParsesBodyAndCardsOnTheirItemAsync()
    {
        const string json = """
            {
              "$schemaVersion": 1,
              "publisher": { "id": "tabs-pub", "name": "Tabs Publisher" },
              "content": [
                {
                  "id": "mod-a",
                  "name": "Mod A",
                  "contentType": "Mod",
                  "targetGame": "ZeroHour",
                  "releases": [ { "version": "1.0.0", "artifacts": [ { "filename": "a.zip", "downloadUrl": "https://example.com/a.zip" } ] } ],
                  "customTabs": [
                    {
                      "tabId": "install-guide",
                      "header": "Install guide",
                      "order": 5,
                      "intro": "Read this first.",
                      "body": "## Steps\n\n1. Install the base game.",
                      "cards": [ { "title": "Step one", "imageUrl": "https://example.com/step.png" } ]
                    }
                  ]
                },
                {
                  "id": "mod-b",
                  "name": "Mod B",
                  "contentType": "Mod",
                  "targetGame": "ZeroHour",
                  "releases": [ { "version": "1.0.0", "artifacts": [ { "filename": "b.zip", "downloadUrl": "https://example.com/b.zip" } ] } ]
                }
              ]
            }
            """;

        var result = await _parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var modA = Assert.Single(result.Data!.Content, c => c.Id == "mod-a");
        var tab = Assert.Single(modA.CustomTabs);
        Assert.Equal("install-guide", tab.TabId);
        Assert.Equal("Install guide", tab.Header);
        Assert.Equal(5, tab.Order);
        Assert.Equal("## Steps\n\n1. Install the base game.", tab.Body);
        Assert.Equal("Step one", Assert.Single(tab.Cards).Title);
        Assert.Empty(Assert.Single(result.Data.Content, c => c.Id == "mod-b").CustomTabs);
        Assert.Empty(result.Data.CustomTabs);
    }

    /// <summary>
    /// Null tab collections and null entries normalize away instead of failing the catalog.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_NullTabCollections_NormalizeToEmptyAsync()
    {
        const string json = """
            {
              "$schemaVersion": 1,
              "publisher": { "id": "tabs-pub", "name": "Tabs Publisher" },
              "customTabs": null,
              "content": [
                {
                  "id": "mod-a",
                  "name": "Mod A",
                  "releases": [ { "version": "1.0.0", "artifacts": [ { "filename": "a.zip", "downloadUrl": "https://example.com/a.zip" } ] } ],
                  "customTabs": [ null, { "tabId": "notes", "header": "Notes", "cards": null, "appliesTo": null } ]
                }
              ]
            }
            """;

        var result = await _parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(result.Data!.CustomTabs);
        var tab = Assert.Single(Assert.Single(result.Data.Content).CustomTabs);
        Assert.Empty(tab.Cards);
        Assert.Empty(tab.AppliesTo);
    }

    /// <summary>
    /// A tab without a header fails validation with an error naming the content item and tab.
    /// </summary>
    [Fact]
    public void ValidateCatalog_ItemTabMissingHeader_FailsWithItemAndTabContext()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition { TabId = "notes", Header = " " });

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("'mod-a'") && e.Contains("'notes'") && e.Contains("missing header"));
    }

    /// <summary>
    /// A tab without an ID fails validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_ItemTabMissingTabId_Fails()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition { TabId = string.Empty, Header = "Notes" });

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("'mod-a'") && e.Contains("missing tabId"));
    }

    /// <summary>
    /// Tab IDs must be unique within an item regardless of case.
    /// </summary>
    [Fact]
    public void ValidateCatalog_DuplicateTabIdIgnoringCase_Fails()
    {
        var catalog = CreateCatalog(
            new CatalogTabDefinition { TabId = "notes", Header = "Notes" },
            new CatalogTabDefinition { TabId = "NOTES", Header = "More notes" });

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("duplicate custom tab ID 'NOTES'"));
    }

    /// <summary>
    /// The same tab ID on two different items is allowed.
    /// </summary>
    [Fact]
    public void ValidateCatalog_SameTabIdOnDifferentItems_Succeeds()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition { TabId = "notes", Header = "Notes" });
        var second = CreateItem("mod-b");
        second.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }];
        catalog.Content.Add(second);

        var result = _parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Card images must use HTTPS or a bundled avares resource.
    /// </summary>
    /// <param name="imageUrl">The card image URL.</param>
    /// <param name="expectedValid">Whether the URL is allowed.</param>
    [Theory]
    [InlineData("https://example.com/card.png", true)]
    [InlineData("avares://GenHub/Assets/Covers/usa-cover.jpg", true)]
    [InlineData("http://example.com/card.png", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("relative/card.png", false)]
    public void ValidateCatalog_CardImageUrl_AllowsOnlyHttpsAndAvares(string imageUrl, bool expectedValid)
    {
        var catalog = CreateCatalog(new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Cards = [new CatalogTabCardDefinition { Title = "Card", ImageUrl = imageUrl }],
        });

        var result = _parser.ValidateCatalog(catalog);

        Assert.Equal(expectedValid, result.Success);
        if (!expectedValid)
        {
            Assert.Contains(result.Errors, e => e.Contains("invalid image URL"));
        }
    }

    /// <summary>
    /// Cards must have a title.
    /// </summary>
    [Fact]
    public void ValidateCatalog_CardMissingTitle_Fails()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Cards = [new CatalogTabCardDefinition { Title = string.Empty }],
        });

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("card 0 is missing title"));
    }

    /// <summary>
    /// Card text longer than the limits fails validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_CardTextTooLong_Fails()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Cards =
            [
                new CatalogTabCardDefinition { Title = new string('t', CustomTabConstants.MaxCardTitleLength + 1) },
                new CatalogTabCardDefinition { Title = "Card", Description = new string('d', CustomTabConstants.MaxCardDescriptionLength + 1) },
            ],
        });

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("card 0 has a title or label longer than"));
        Assert.Contains(result.Errors, e => e.Contains("card 1 has a description longer than"));
    }

    /// <summary>
    /// An item that declares more tabs than the limit fails validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_TooManyTabs_Fails()
    {
        var tabs = Enumerable.Range(0, CustomTabConstants.MaxTabsPerScope + 1)
            .Select(i => new CatalogTabDefinition { TabId = $"tab-{i}", Header = $"Tab {i}" })
            .ToArray();
        var catalog = CreateCatalog(tabs);

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains($"The maximum is {CustomTabConstants.MaxTabsPerScope}"));
    }

    /// <summary>
    /// A body longer than the limit fails validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_BodyTooLong_Fails()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition
        {
            TabId = "notes",
            Header = "Notes",
            Body = new string('x', CustomTabConstants.MaxBodyLength + 1),
        });

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("body longer than"));
    }

    /// <summary>
    /// Catalog-level tabs are validated with the same rules as item tabs.
    /// </summary>
    [Fact]
    public void ValidateCatalog_InvalidRootTab_Fails()
    {
        var catalog = CreateCatalog();
        catalog.CustomTabs = [new CatalogTabDefinition { TabId = "shared", Header = string.Empty, AppliesTo = ["mod-a"] }];

        var result = _parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.StartsWith("Catalog custom tab 'shared'") && e.Contains("missing header"));
    }

    /// <summary>
    /// Soft issues such as an unsupported content type hint, an unknown appliesTo ID, or an untargeted
    /// catalog-level tab do not fail an otherwise valid catalog.
    /// </summary>
    [Fact]
    public void ValidateCatalog_SoftTabIssues_StillSucceeds()
    {
        var catalog = CreateCatalog(new CatalogTabDefinition { TabId = "notes", Header = "Notes", ContentType = "hologram" });
        catalog.CustomTabs =
        [
            new CatalogTabDefinition { TabId = "untargeted", Header = "Untargeted" },
            new CatalogTabDefinition { TabId = "unknown-target", Header = "Unknown", AppliesTo = ["missing-item"] },
        ];

        var result = _parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    private static PublisherCatalog CreateCatalog(params CatalogTabDefinition[] itemTabs)
    {
        var item = CreateItem("mod-a");
        item.CustomTabs = [.. itemTabs];
        return new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "tabs-pub", Name = "Tabs Publisher" },
            Content = [item],
        };
    }

    private static CatalogContentItem CreateItem(string id)
    {
        return new CatalogContentItem
        {
            Id = id,
            Name = id,
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Artifacts = [new ReleaseArtifact { Filename = $"{id}.zip", DownloadUrl = $"https://example.com/{id}.zip" }],
                },
            ],
        };
    }
}
