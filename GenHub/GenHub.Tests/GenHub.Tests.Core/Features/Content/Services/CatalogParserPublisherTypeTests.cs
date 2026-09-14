using System.Collections.Generic;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests verifying catalog validation and publisherType enforcement in <see cref="JsonPublisherCatalogParser"/>.
/// </summary>
public sealed class CatalogParserPublisherTypeTests
{
    private readonly JsonPublisherCatalogParser _parser = new(NullLogger<JsonPublisherCatalogParser>.Instance);

    /// <summary>
    /// Verifies that a catalog item missing a publisherType defaults to generic-catalog and succeeds validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_MissingPublisherType_DefaultsToGenericCatalog_Succeeds()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-publisher", Name = "Test Publisher" },
            Content = new List<CatalogContentItem>
            {
                new()
                {
                    Id = "item-1",
                    Name = "Item 1",
                    PublisherType = null,
                    Releases = new List<ContentRelease>
                    {
                        new()
                        {
                            Version = "1.0",
                            Artifacts = new List<ReleaseArtifact>
                            {
                                new() { DownloadUrl = "https://example.com/file.zip" },
                            },
                        },
                    },
                },
            },
        };

        var result = _parser.ValidateCatalog(catalog);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that a catalog item with an unknown/un-allowlisted publisherType fails validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_InvalidPublisherType_FailsValidation()
    {
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-publisher", Name = "Test Publisher" },
            Content = new List<CatalogContentItem>
            {
                new()
                {
                    Id = "item-1",
                    Name = "Item 1",
                    PublisherType = "invalid-publisher-type-xxx",
                    Releases = new List<ContentRelease>
                    {
                        new()
                        {
                            Version = "1.0",
                            Artifacts = new List<ReleaseArtifact>
                            {
                                new() { DownloadUrl = "https://example.com/file.zip" },
                            },
                        },
                    },
                },
            },
        };

        var result = _parser.ValidateCatalog(catalog);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("unknown publisherType"));
    }

    /// <summary>
    /// Verifies that a bundle dependency with a mismatched publisherId compared to the sibling fails validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_MismatchedSiblingDependencyPublisherId_FailsValidation()
    {
        var sibling = new CatalogContentItem
        {
            Id = "sibling-client",
            Name = "Sibling Client",
            PublisherType = "thesuperhackers",
            Releases = new List<ContentRelease>
            {
                new()
                {
                    Version = "1.0",
                    Artifacts = new List<ReleaseArtifact>
                    {
                        new() { DownloadUrl = "https://example.com/client.zip" },
                    },
                },
            },
        };

        var bundle = new CatalogContentItem
        {
            Id = "bundle-1",
            Name = "Bundle 1",
            PublisherType = "generic-catalog",
            ContentType = ContentType.ContentBundle,
            Releases = new List<ContentRelease>
            {
                new()
                {
                    Version = "1.0",
                    Dependencies = new List<CatalogDependency>
                    {
                        new()
                        {
                            ContentId = "sibling-client",
                            PublisherId = "communityoutpost", // Mismatched! Expected thesuperhackers
                            VersionConstraint = ">=1.0",
                        },
                    },
                },
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-publisher", Name = "Test Publisher" },
            Content = new List<CatalogContentItem> { sibling, bundle },
        };

        var result = _parser.ValidateCatalog(catalog);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("does not match sibling's declared publisherType"));
    }

    /// <summary>
    /// Verifies that valid publisherTypes and matching sibling dependencies pass validation.
    /// </summary>
    [Fact]
    public void ValidateCatalog_ValidPublisherTypesAndSiblings_PassesValidation()
    {
        var sibling = new CatalogContentItem
        {
            Id = "sibling-client",
            Name = "Sibling Client",
            PublisherType = "thesuperhackers",
            Releases = new List<ContentRelease>
            {
                new()
                {
                    Version = "1.0",
                    Artifacts = new List<ReleaseArtifact>
                    {
                        new() { DownloadUrl = "https://example.com/client.zip" },
                    },
                },
            },
        };

        var bundle = new CatalogContentItem
        {
            Id = "bundle-1",
            Name = "Bundle 1",
            PublisherType = "generic-catalog",
            ContentType = ContentType.ContentBundle,
            Releases = new List<ContentRelease>
            {
                new()
                {
                    Version = "1.0",
                    Dependencies = new List<CatalogDependency>
                    {
                        new()
                        {
                            ContentId = "sibling-client",
                            PublisherId = "thesuperhackers", // Matched!
                            VersionConstraint = ">=1.0",
                        },
                    },
                },
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-publisher", Name = "Test Publisher" },
            Content = new List<CatalogContentItem> { sibling, bundle },
        };

        var result = _parser.ValidateCatalog(catalog);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that a base game dependency (publisherId "ea", contentId "zerohour") is ignored when validating sibling publisher types.
    /// </summary>
    [Fact]
    public void ValidateCatalog_BaseGameDependency_IgnoresSiblingPublisherMismatch()
    {
        var gameCodeSibling = new CatalogContentItem
        {
            Id = "zerohour",
            Name = "Zero Hour Game Code",
            PublisherType = "thesuperhackers",
            Releases = new List<ContentRelease>
            {
                new()
                {
                    Version = "1.0",
                    Artifacts = new List<ReleaseArtifact>
                    {
                        new() { DownloadUrl = "https://example.com/client.zip" },
                    },
                    Dependencies = new List<CatalogDependency>
                    {
                        new()
                        {
                            ContentId = "zerohour",
                            PublisherId = "ea",
                            ContentType = "GameInstallation",
                        },
                    },
                },
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-publisher", Name = "Test Publisher" },
            Content = new List<CatalogContentItem> { gameCodeSibling },
        };

        var result = _parser.ValidateCatalog(catalog);
        Assert.True(result.Success);
    }
}
