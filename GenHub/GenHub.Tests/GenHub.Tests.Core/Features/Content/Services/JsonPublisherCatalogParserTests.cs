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
