using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests for <see cref="JsonPublisherCatalogParser"/> validation rules.
/// </summary>
public sealed class JsonPublisherCatalogParserTests
{
    /// <summary>
    /// ContentBundle releases with dependencies but no artifacts must validate successfully.
    /// </summary>
    [Fact]
    public void ValidateCatalog_DependencyOnlyBundle_Succeeds()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "bundle-a",
                    Name = "Bundle A",
                    ContentType = ContentType.ContentBundle,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "1.0.0",
                            Artifacts = [],
                            Dependencies =
                            [
                                new CatalogDependency
                                {
                                    PublisherId = "test-pub",
                                    ContentId = "client-a",
                                    VersionConstraint = ">=1.0",
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// A release with neither artifacts nor dependencies must still fail validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_EmptyArtifactsAndDependencies_Fails()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "empty-release",
                    Name = "Empty",
                    ContentType = ContentType.Mod,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "1.0.0",
                            Artifacts = [],
                            Dependencies = [],
                        },
                    ],
                },
            ],
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("no artifacts or dependencies", StringComparison.Ordinal));
    }

    /// <summary>
    /// Dynamic releases for SuperHackers or specified as 'latest' without pre-populated artifacts must pass validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_DynamicSuperHackersRelease_Succeeds()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "zerohour",
                    Name = "TheSuperHackers Zero Hour",
                    ContentType = ContentType.GameClient,
                    PublisherType = "thesuperhackers",
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "latest",
                            Artifacts = [],
                            Dependencies = [],
                        },
                    ],
                },
            ],
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = parser.ValidateCatalog(catalog);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// The checked-in sample catalog must parse and validate end-to-end.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_SampleTestCatalog_SucceedsAsync()
    {
        var path = FindSampleCatalogPath();
        Assert.True(File.Exists(path), $"Sample catalog not found at {path}");

        var json = await File.ReadAllTextAsync(path);
        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = await parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("genhub-test-publishers", result.Data!.Publisher.Id);
        Assert.Contains(result.Data.Content, c => c.ContentType == ContentType.ContentBundle);
        var stack = Assert.Single(result.Data.Content, c => c.Id == "bundle-thesuperhackers-latest-stack");
        Assert.Contains(
            stack.Releases[0].Dependencies,
            d => d.ContentId == "lemon-controlbar" && d.ContentType == "Addon");
        Assert.Contains(result.Data.Content, c => c.Id == "bundle-community-outpost-stack");
        Assert.Contains(result.Data.Content, c => c.Id == "bundle-generalsonline-complete-pack");
        Assert.False(result.Data.Content.First(c => c.Id == "lemon-controlbar").IsStandalone);
        Assert.True(stack.IsStandalone);
    }

    /// <summary>
    /// Third-party catalogs using string enums, formatVersion, and catalog-level metadata
    /// (e.g. community mappack catalogs) must parse and validate end-to-end.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_StringEnumsAndCatalogMetadata_SucceedsAsync()
    {
        const string json = """
            {
              "$schema": "https://genhub.net/schemas/publisher-catalog.json",
              "formatVersion": "1.0.0",
              "publisher": { "id": "dominator", "name": "Dominator Mappacks" },
              "metadata": { "title": "Dominator Mappacks", "source": "https://example.com/maps" },
              "content": [
                {
                  "id": "gla-campaign-by-tklyo",
                  "name": "GLA Campaign by TKlyo",
                  "description": "Custom singleplayer GLA campaign missions.",
                  "contentType": "MapPack",
                  "isStandalone": false,
                  "targetGame": "ZeroHour",
                  "tags": ["maps", "campaign"],
                  "metadata": { "author": "Dominator", "category": "Maps" },
                  "releases": [
                    {
                      "version": "1.0.0",
                      "isLatest": true,
                      "artifacts": [
                        { "filename": "gla-campaign.rar", "downloadUrl": "https://example.com/gla-campaign.rar", "isPrimary": true }
                      ],
                      "dependencies": [
                        { "publisherId": "ea", "contentId": "zerohour", "versionConstraint": "1.04", "contentType": "GameInstallation" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = await parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("dominator", result.Data!.Publisher.Id);
        var item = Assert.Single(result.Data.Content);
        Assert.Equal(ContentType.MapPack, item.ContentType);
        Assert.Equal(GameType.ZeroHour, item.TargetGame);
    }

    /// <summary>
    /// Duplicate content IDs (case-insensitive) must be rejected with validation errors.
    /// </summary>
    [Fact]
    public void ValidateCatalog_DuplicateContentId_Fails()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "item-1",
                    Name = "Item 1",
                    ContentType = ContentType.Mod,
                    Releases = [new ContentRelease { Version = "1.0.0", Artifacts = [new ReleaseArtifact { DownloadUrl = "https://example.com/file.zip" }] }],
                },
                new CatalogContentItem
                {
                    Id = "ITEM-1",
                    Name = "Item 1 Duplicate",
                    ContentType = ContentType.Mod,
                    Releases = [new ContentRelease { Version = "1.0.0", Artifacts = [new ReleaseArtifact { DownloadUrl = "https://example.com/file.zip" }] }],
                },
            ],
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate content ID"));
    }

    /// <summary>
    /// Null dependencies or artifacts must fail validation safely without throwing NullReferenceException.
    /// </summary>
    [Fact]
    public void ValidateCatalog_NullDependencyOrArtifact_HandledGracefully()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "item-1",
                    Name = "Item 1",
                    ContentType = ContentType.Mod,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "1.0.0",
                            Artifacts = [null!],
                            Dependencies = [null!],
                        },
                    ],
                },
            ],
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
    }

    /// <summary>
    /// VerifySignature returns true and warns when a signature is present but unconfigured.
    /// </summary>
    [Fact]
    public void VerifySignature_WithSignature_AcceptsWithWarning()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Signature = "dummy-base64-signature",
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var verified = parser.VerifySignature("{}", catalog);

        Assert.True(verified);
    }

    /// <summary>
    /// VerifySignature returns true when no signature is declared.
    /// </summary>
    [Fact]
    public void VerifySignature_WithoutSignature_ReturnsTrue()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Signature = null,
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var verified = parser.VerifySignature("{}", catalog);

        Assert.True(verified);
    }

    /// <summary>
    /// The community competitive ecosystem catalog must parse and validate end-to-end.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_CommunityCompetitiveEcosystemCatalog_SucceedsAsync()
    {
        var path = FindCommunityCompetitiveCatalogPath();
        Assert.True(File.Exists(path), $"Sample catalog not found at {path}");

        var json = await File.ReadAllTextAsync(path);
        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = await parser.ParseCatalogAsync(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("community-competitive-hub", result.Data!.Publisher.Id);
        Assert.Contains(result.Data.Content, c => c.ContentType == ContentType.ContentBundle);
        var bundle = Assert.Single(result.Data.Content, c => c.Id == "thesuperhackers-competitive-bundle");
        Assert.Contains(bundle.BundledItems, d => d.ContentId == "thesuperhackers-client");
        Assert.Contains(bundle.BundledItems, d => d.ContentId == "l3m-controlbar");
        Assert.Contains(bundle.BundledItems, d => d.ContentId == "leikeze-hotkeys");
        Assert.Contains(bundle.BundledItems, d => d.ContentId == "eliorata-improved-menus");
    }

    /// <summary>
    /// Upstream GitHub items with whitespace in owner or repository identifier must fail validation.
    /// </summary>
    /// <param name="invalidRepo">The invalid repository string with whitespace.</param>
    [Theory]
    [InlineData("owner/ repo")]
    [InlineData("owner/repo\n")]
    [InlineData(" owner/repo")]
    [InlineData("owner /repo")]
    [InlineData("owner/")]
    [InlineData("/repo")]
    public void ValidateCatalog_GitHubUpstreamRepository_WhitespaceRejected(string invalidRepo)
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "item-gh",
                    Name = "GitHub Item",                    ContentType = ContentType.GameClient,
                    UpstreamSync = new CatalogUpstreamSync
                    {
                        Provider = "github-releases",
                        Repository = invalidRepo,
                    },
                },
            ],
        };

        var parser = new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance);
        var result = parser.ValidateCatalog(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("must declare a valid repository in 'owner/repo' format", StringComparison.Ordinal));
    }

    private static string FindCommunityCompetitiveCatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "GenHub", "GenHub", "SampleCatalogs", "community-competitive-ecosystem.catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(dir.FullName, "GenHub", "SampleCatalogs", "community-competitive-ecosystem.catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "GenHub",
            "SampleCatalogs",
            "community-competitive-ecosystem.catalog.json"));
    }

    private static string FindSampleCatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "GenHub", "GenHub", "SampleCatalogs", "genhub-test-catalog.catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(dir.FullName, "GenHub", "SampleCatalogs", "genhub-test-catalog.catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "GenHub",
            "SampleCatalogs",
            "genhub-test-catalog.catalog.json"));
    }
}
