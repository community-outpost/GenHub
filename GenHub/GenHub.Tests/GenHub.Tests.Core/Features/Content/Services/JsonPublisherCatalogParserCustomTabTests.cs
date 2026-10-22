using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
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
    private sealed class CapturingLogger : ILogger<JsonPublisherCatalogParser>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

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
    /// A null entry in a catalog-level tab's appliesTo list does not break parsing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_NullAppliesToEntry_ParsesAsync()
    {
        const string json = """
            {
              "$schemaVersion": 1,
              "publisher": { "id": "tabs-pub", "name": "Tabs Publisher" },
              "customTabs": [ { "tabId": "shared", "header": "Shared", "appliesTo": [ null, "mod-a", "missing" ] } ],
              "content": [
                {
                  "id": "mod-a",
                  "name": "Mod A",
                  "releases": [ { "version": "1.0.0", "artifacts": [ { "filename": "a.zip", "downloadUrl": "https://example.com/a.zip" } ] } ]
                }
              ]
            }
            """;

        var result = await _parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("mod-a", Assert.Single(result.Data!.CustomTabs).AppliesTo);
    }

    /// <summary>
    /// A catalog with one bad card still parses. Only that card is dropped, and the other items and tabs survive.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_OneBadCard_DropsOnlyThatCardAsync()
    {
        const string json = """
            {
              "$schemaVersion": 1,
              "publisher": { "id": "tabs-pub", "name": "Tabs Publisher" },
              "content": [
                {
                  "id": "mod-a",
                  "name": "Mod A",
                  "releases": [ { "version": "1.0.0", "artifacts": [ { "filename": "a.zip", "downloadUrl": "https://example.com/a.zip" } ] } ],
                  "customTabs": [
                    {
                      "tabId": "gallery",
                      "header": "Gallery",
                      "cards": [
                        { "title": "Good card", "imageUrl": "https://example.com/good.png" },
                        { "title": "Bad card", "imageUrl": "http://example.com/bad.png" }
                      ]
                    },
                    { "tabId": "notes", "header": "Notes", "body": "Notes body" }
                  ]
                },
                {
                  "id": "mod-b",
                  "name": "Mod B",
                  "releases": [ { "version": "1.0.0", "artifacts": [ { "filename": "b.zip", "downloadUrl": "https://example.com/b.zip" } ] } ],
                  "customTabs": [ { "tabId": "guide", "header": "Guide" } ]
                }
              ]
            }
            """;

        var result = await _parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var modA = Assert.Single(result.Data!.Content, c => c.Id == "mod-a");
        Assert.Equal(["gallery", "notes"], modA.CustomTabs.Select(t => t.TabId));
        Assert.Equal("Good card", Assert.Single(modA.CustomTabs[0].Cards).Title);
        Assert.Equal("guide", Assert.Single(Assert.Single(result.Data.Content, c => c.Id == "mod-b").CustomTabs).TabId);
    }

    /// <summary>
    /// A dropped card logs a warning that names the item, tab and card.
    /// </summary>
    [Fact]
    public void NormalizeCatalog_DroppedCard_LogsWarning()
    {
        var logger = new CapturingLogger();
        var parser = new JsonPublisherCatalogParser(logger);
        var catalog = CreateCatalog(new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Cards = [new CatalogTabCardDefinition { Title = string.Empty }],
        });

        parser.NormalizeCatalog(catalog);

        Assert.Empty(Assert.Single(catalog.Content).CustomTabs[0].Cards);
        Assert.Contains(logger.Warnings, w => w.Contains("Content item 'mod-a' custom tab 'gallery' card 0 is missing title"));
    }

    /// <summary>
    /// Tab content issues drop the tab that cannot render, log a warning, and leave the catalog valid.
    /// </summary>
    /// <param name="issue">The tab content issue to introduce.</param>
    [Theory]
    [InlineData("missing-header")]
    [InlineData("missing-id")]
    [InlineData("long-header")]
    [InlineData("long-intro")]
    [InlineData("long-body")]
    public void NormalizeCatalog_TabThatCannotRender_DropsTabAndStaysValid(string issue)
    {
        var bad = new CatalogTabDefinition { TabId = "bad", Header = "Bad" };
        switch (issue)
        {
            case "missing-header":
                bad.Header = " ";
                break;
            case "missing-id":
                bad.TabId = string.Empty;
                break;
            case "long-header":
                bad.Header = new string('h', CustomTabConstants.MaxHeaderLength + 1);
                break;
            case "long-intro":
                bad.Intro = new string('i', CustomTabConstants.MaxIntroLength + 1);
                break;
            default:
                bad.Body = new string('b', CustomTabConstants.MaxBodyLength + 1);
                break;
        }

        var logger = new CapturingLogger();
        var parser = new JsonPublisherCatalogParser(logger);
        var catalog = CreateCatalog(new CatalogTabDefinition { TabId = "good", Header = "Good" }, bad);

        parser.NormalizeCatalog(catalog);
        var result = parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("good", Assert.Single(Assert.Single(catalog.Content).CustomTabs).TabId);
        Assert.Single(logger.Warnings, w => w.Contains("Dropped publisher tab content"));
    }

    /// <summary>
    /// Card issues drop only the offending card: bad image schemes, missing titles, over-long text,
    /// and cards past the per-tab limit.
    /// </summary>
    [Fact]
    public void NormalizeCatalog_BadCards_DropsCardsAndTrimsToLimit()
    {
        var cards = Enumerable.Range(0, CustomTabConstants.MaxCardsPerTab + 6)
            .Select(i => new CatalogTabCardDefinition { Title = $"Card {i}" })
            .ToList();
        cards[0].ImageUrl = "file:///etc/passwd";
        cards[1].Title = string.Empty;
        cards[2].Description = new string('d', CustomTabConstants.MaxCardDescriptionLength + 1);
        cards[3].Label = new string('l', CustomTabConstants.MaxCardTitleLength + 1);
        cards[4].ImageUrl = "avares://GenHub/Assets/Covers/usa-cover.jpg";
        var catalog = CreateCatalog(new CatalogTabDefinition { TabId = "gallery", Header = "Gallery", Cards = cards });

        _parser.NormalizeCatalog(catalog);
        var result = _parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var kept = Assert.Single(Assert.Single(catalog.Content).CustomTabs).Cards;
        Assert.Equal(CustomTabConstants.MaxCardsPerTab, kept.Count);
        Assert.Equal("Card 4", kept[0].Title);
        Assert.Equal($"Card {4 + CustomTabConstants.MaxCardsPerTab - 1}", kept[^1].Title);
    }

    /// <summary>
    /// Tab IDs must be unique within an item regardless of case. This stays a catalog error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_DuplicateTabIdIgnoringCase_FailsAsync()
    {
        var catalog = CreateCatalog(
            new CatalogTabDefinition { TabId = "notes", Header = "Notes" },
            new CatalogTabDefinition { TabId = "NOTES", Header = "More notes" });

        var result = await _parser.ParseCatalogAsync(System.Text.Json.JsonSerializer.Serialize(catalog));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("duplicate custom tab ID 'NOTES'"));
    }

    /// <summary>
    /// Structural checks run after invalid tabs are dropped. A duplicate whose copy cannot render, or an
    /// eleventh tab that cannot render, leaves an unambiguous scope within the limit, so the catalog is valid.
    /// </summary>
    /// <param name="overLimit">Whether to test the tab limit instead of a duplicate ID.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizeCatalog_StructuralIssueOnDroppedTab_IsAccepted(bool overLimit)
    {
        var tabs = overLimit
            ? Enumerable.Range(0, CustomTabConstants.MaxTabsPerScope)
                .Select(i => new CatalogTabDefinition { TabId = $"tab-{i}", Header = $"Tab {i}" })
                .Append(new CatalogTabDefinition { TabId = "broken", Header = string.Empty })
                .ToArray()
            : [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }, new CatalogTabDefinition { TabId = "NOTES", Header = string.Empty }];
        var catalog = CreateCatalog(tabs);

        _parser.NormalizeCatalog(catalog);
        var result = _parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(overLimit ? CustomTabConstants.MaxTabsPerScope : 1, Assert.Single(catalog.Content).CustomTabs.Count);
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
    /// More tabs than the per-scope limit stays a catalog error, for items and the catalog root.
    /// </summary>
    /// <param name="atRoot">Whether the tabs are declared at the catalog root.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseCatalogAsync_TooManyTabs_FailsAsync(bool atRoot)
    {
        var tabs = Enumerable.Range(0, CustomTabConstants.MaxTabsPerScope + 1)
            .Select(i => new CatalogTabDefinition { TabId = $"tab-{i}", Header = $"Tab {i}", AppliesTo = ["mod-a"] })
            .ToList();
        var catalog = CreateCatalog();
        if (atRoot)
        {
            catalog.CustomTabs = tabs;
        }
        else
        {
            catalog.Content[0].CustomTabs = tabs;
        }

        var result = await _parser.ParseCatalogAsync(System.Text.Json.JsonSerializer.Serialize(catalog));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains($"The maximum is {CustomTabConstants.MaxTabsPerScope}"));
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
