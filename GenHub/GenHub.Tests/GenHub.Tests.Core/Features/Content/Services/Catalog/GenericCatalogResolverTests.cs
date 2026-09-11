using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Unit and regression tests for <see cref="GenericCatalogResolver"/>.
/// </summary>
public class GenericCatalogResolverTests
{
    private readonly Func<IContentManifestBuilder> _builderFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogResolverTests"/> class.
    /// </summary>
    public GenericCatalogResolverTests()
    {
        var manifestIdServiceMock = new Mock<IManifestIdService>();
        manifestIdServiceMock.Setup(x => x.GeneratePublisherContentId(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string p, ContentType ct, string c, int v) =>
            {
                var generated = ManifestIdGenerator.GeneratePublisherContentId(p, ct, c, v);
                return OperationResult<ManifestId>.CreateSuccess(ManifestId.Create(generated));
            });

        _builderFactory = () => new ContentManifestBuilder(
            Mock.Of<ILogger<ContentManifestBuilder>>(),
            Mock.Of<IFileHashProvider>(),
            manifestIdServiceMock.Object,
            Mock.Of<IDownloadService>(),
            Mock.Of<IConfigurationProviderService>());
    }

    /// <summary>
    /// Verifies that colliding sanitized artifact filenames are disambiguated with a numeric index.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_CollidingFilenames_DisambiguatesWithNumericIndexAsync()
    {
        // Arrange
        var resolver = new GenericCatalogResolver(Mock.Of<ILogger<GenericCatalogResolver>>(), _builderFactory);

        var release = new ContentRelease
        {
            Version = "1.0",
            Artifacts =
            [
                new ReleaseArtifact { DownloadUrl = "https://example.com/downloads/file.zip", Filename = "file.zip", IsPrimary = true },
                new ReleaseArtifact { DownloadUrl = "https://example.com/mirror1/file.zip", Filename = "file.zip" },
                new ReleaseArtifact { DownloadUrl = "https://example.com/mirror2/file.zip", Filename = "file.zip" },
            ],
        };

        var contentItem = new CatalogContentItem
        {
            Id = "test-item",
            Name = "Test Item",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var publisher = new PublisherProfile
        {
            Id = "community-outpost",
            Name = "Community Outpost",
        };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.community-outpost.mod.test-item",
        };
        searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release);
        searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher);

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        result.Success.Should().BeTrue(result.FirstError ?? string.Empty);
        result.Data.Should().NotBeNull();
        result.Data!.Files.Should().HaveCount(3);
        result.Data.Files.Select(f => f.RelativePath).Should().BeEquivalentTo([
            "file.zip",
            "file_1.zip",
            "file_2.zip",
        ]);
    }

    /// <summary>
    /// Verifies that multi-variant releases filter artifacts according to the primary artifact's variant.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_MultiVariantRelease_FiltersArtifactsToPrimaryVariantAsync()
    {
        // Arrange
        var resolver = new GenericCatalogResolver(Mock.Of<ILogger<GenericCatalogResolver>>(), _builderFactory);

        var release = new ContentRelease
        {
            Version = "1.0",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    DownloadUrl = "https://example.com/zh.zip",
                    Filename = "zh.zip",
                    VariantAxis = "game-type",
                    Variant = "Zero Hour",
                    IsPrimary = true,
                },
                new ReleaseArtifact
                {
                    DownloadUrl = "https://example.com/gen.zip",
                    Filename = "gen.zip",
                    VariantAxis = "game-type",
                    Variant = "Generals",
                },
                new ReleaseArtifact
                {
                    DownloadUrl = "https://example.com/manual.pdf",
                    Filename = "manual.pdf",
                    VariantAxis = null,
                },
            ],
        };

        var contentItem = new CatalogContentItem
        {
            Id = "dual-mod",
            Name = "Dual Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var publisher = new PublisherProfile
        {
            Id = "community-outpost",
            Name = "Community Outpost",
        };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.community-outpost.mod.dual-mod",
        };
        searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release);
        searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher);

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        result.Success.Should().BeTrue(result.FirstError ?? string.Empty);
        result.Data.Should().NotBeNull();

        // Should include Zero Hour artifact and common manual, but EXCLUDE Generals artifact
        result.Data!.Files.Select(f => f.RelativePath).Should().BeEquivalentTo([
            "zh.zip",
            "manual.pdf",
        ]);
    }

    /// <summary>
    /// Verifies that SHA256 hashes are stamped per matching relative file path.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_MultipleArtifactsWithHashes_StampsSha256ByFilenameAsync()
    {
        // Arrange
        var resolver = new GenericCatalogResolver(Mock.Of<ILogger<GenericCatalogResolver>>(), _builderFactory);

        var release = new ContentRelease
        {
            Version = "1.0",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    DownloadUrl = "https://example.com/primary.zip",
                    Filename = "primary.zip",
                    Sha256 = "1111111111111111111111111111111111111111111111111111111111111111",
                    IsPrimary = true,
                },
                new ReleaseArtifact
                {
                    DownloadUrl = "https://example.com/secondary.zip",
                    Filename = "secondary.zip",
                    Sha256 = "2222222222222222222222222222222222222222222222222222222222222222",
                },
            ],
        };

        var contentItem = new CatalogContentItem
        {
            Id = "hashed-item",
            Name = "Hashed Item",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var publisher = new PublisherProfile
        {
            Id = "community-outpost",
            Name = "Community Outpost",
        };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.community-outpost.mod.hashed-item",
        };
        searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release);
        searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher);

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        result.Success.Should().BeTrue(result.FirstError ?? string.Empty);
        result.Data.Should().NotBeNull();
        result.Data!.Files.Should().HaveCount(2);

        var primaryFile = result.Data.Files.First(f => f.RelativePath == "primary.zip");
        var secondaryFile = result.Data.Files.First(f => f.RelativePath == "secondary.zip");

        primaryFile.Hash.Should().Be("1111111111111111111111111111111111111111111111111111111111111111");
        secondaryFile.Hash.Should().Be("2222222222222222222222222222222222222222222222222222222222222222");
    }

    /// <summary>
    /// Verifies that if the primary artifact has no download URL, its hash is not stamped onto an unrelated secondary file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_PrimaryArtifactWithoutDownloadUrl_DoesNotStampHashOnSecondaryFileAsync()
    {
        // Arrange
        var resolver = new GenericCatalogResolver(
            Mock.Of<ILogger<GenericCatalogResolver>>(),
            _builderFactory);

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    IsPrimary = true,
                    Filename = "primary.zip",
                    DownloadUrl = string.Empty, // No download URL -> not registered in Files
                    Sha256 = "1111111111111111111111111111111111111111111111111111111111111111",
                },
                new ReleaseArtifact
                {
                    Filename = "secondary.zip",
                    DownloadUrl = "https://example.com/secondary.zip", // No hash
                },
            ],
        };

        var contentItem = new CatalogContentItem
        {
            Id = "item-id",
            Name = "Item Name",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var publisher = new PublisherProfile
        {
            Id = "community-outpost",
            Name = "Community Outpost",
        };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.community-outpost.mod.item-id",
        };
        searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release);
        searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher);

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        result.Success.Should().BeTrue(result.FirstError ?? string.Empty);
        result.Data.Should().NotBeNull();
        result.Data!.Files.Should().HaveCount(1);

        var file = result.Data.Files.Single();
        file.RelativePath.Should().Be("secondary.zip");
        file.Hash.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Verifies that an artifact on the same variant axis with a null or empty variant label is treated as common and registered.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_SameAxisArtifactWithNullVariant_IsRegisteredAsCommonAsync()
    {
        // Arrange
        var resolver = new GenericCatalogResolver(
            Mock.Of<ILogger<GenericCatalogResolver>>(),
            _builderFactory);

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    IsPrimary = true,
                    Filename = "zh.zip",
                    DownloadUrl = "https://example.com/zh.zip",
                    VariantAxis = "Game",
                    Variant = "ZeroHour",
                },
                new ReleaseArtifact
                {
                    Filename = "common-shared.zip",
                    DownloadUrl = "https://example.com/common-shared.zip",
                    VariantAxis = "Game",
                    Variant = null, // Null variant on the same axis should be treated as common
                },
                new ReleaseArtifact
                {
                    Filename = "generals.zip",
                    DownloadUrl = "https://example.com/generals.zip",
                    VariantAxis = "Game",
                    Variant = "Generals", // Different variant on the same axis should be excluded
                },
            ],
        };

        var contentItem = new CatalogContentItem
        {
            Id = "item-id",
            Name = "Item Name",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var publisher = new PublisherProfile
        {
            Id = "community-outpost",
            Name = "Community Outpost",
        };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.community-outpost.mod.item-id",
        };
        searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release);
        searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher);

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        result.Success.Should().BeTrue(result.FirstError ?? string.Empty);
        result.Data.Should().NotBeNull();
        result.Data!.Files.Select(f => f.RelativePath).Should().BeEquivalentTo([
            "zh.zip",
            "common-shared.zip",
        ]);
    }

    /// <summary>
    /// Verifies that base game dependencies misdeclared as Mod still resolve to GameInstallation dependencies.
    /// </summary>
    [Fact]
    public void ResolveDependencyContentType_BaseGameDependencyMisdeclaredAsMod_ResolvesToGameInstallation()
    {
        var dep = new CatalogDependency
        {
            PublisherId = "any",
            ContentId = "zerohour",
            ContentType = "Mod",
        };
        var parent = new CatalogContentItem
        {
            Id = "my-mod",
            Name = "My Mod",
            ContentType = ContentType.Mod,
        };

        var resolvedType = CatalogManifestIdentity.ResolveDependencyContentType(dep, parent);

        resolvedType.Should().Be(ContentType.GameInstallation);
    }

    /// <summary>
    /// Verifies that JsonPublisherCatalogParser fails validation when a base game dependency declares a non-GameInstallation contentType.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_BaseGameDependencyDeclaringNonGameInstallationType_FailsValidationAsync()
    {
        var parser = new JsonPublisherCatalogParser(Mock.Of<ILogger<JsonPublisherCatalogParser>>());
        var json = """
        {
            "schemaVersion": 1,
            "publisher": { "id": "test-pub", "name": "Test Publisher" },
            "content": [
                {
                    "id": "test-mod",
                    "name": "Test Mod",
                    "contentType": "Mod",
                    "releases": [
                        {
                            "version": "1.0.0",
                            "dependencies": [
                                { "publisherId": "any", "contentId": "zerohour", "contentType": "Mod" }
                            ],
                            "artifacts": [
                                { "filename": "test.zip", "downloadUrl": "https://example.com/test.zip", "isPrimary": true }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var result = await parser.ParseCatalogAsync(json);

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("cannot declare non-GameInstallation contentType");
    }

    /// <summary>
    /// Verifies that JsonPublisherCatalogParser succeeds when a base game dependency declares GameInstallation contentType with surrounding whitespace.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_BaseGameDependencyDeclaringGameInstallationType_SucceedsAsync()
    {
        var parser = new JsonPublisherCatalogParser(Mock.Of<ILogger<JsonPublisherCatalogParser>>());
        var json = """
        {
            "schemaVersion": 1,
            "publisher": { "id": "test-pub", "name": "Test Publisher" },
            "content": [
                {
                    "id": "test-mod",
                    "name": "Test Mod",
                    "contentType": "Mod",
                    "releases": [
                        {
                            "version": "1.0.0",
                            "dependencies": [
                                { "publisherId": "any", "contentId": "zerohour", "contentType": "  GameInstallation  " }
                            ],
                            "artifacts": [
                                { "filename": "test.zip", "downloadUrl": "https://example.com/test.zip", "isPrimary": true }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var result = await parser.ParseCatalogAsync(json);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Content.Should().HaveCount(1);
        var dep = result.Data.Content[0].Releases[0].Dependencies![0];
        var resolvedType = CatalogManifestIdentity.ResolveDependencyContentType(dep, result.Data.Content[0]);
        resolvedType.Should().Be(ContentType.GameInstallation);
    }

    /// <summary>
    /// Verifies that JsonPublisherCatalogParser fails validation when dependency contentType is a bare integer string.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_DependencyDeclaringIntegerContentType_FailsValidationAsync()
    {
        var parser = new JsonPublisherCatalogParser(Mock.Of<ILogger<JsonPublisherCatalogParser>>());
        var json = """
        {
            "schemaVersion": 1,
            "publisher": { "id": "test-pub", "name": "Test Publisher" },
            "content": [
                {
                    "id": "test-mod",
                    "name": "Test Mod",
                    "contentType": "Mod",
                    "releases": [
                        {
                            "version": "1.0.0",
                            "dependencies": [
                                { "publisherId": "custom", "contentId": "other", "contentType": "2" }
                            ],
                            "artifacts": [
                                { "filename": "test.zip", "downloadUrl": "https://example.com/test.zip", "isPrimary": true }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var result = await parser.ParseCatalogAsync(json);

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("specifies invalid contentType");
    }

    /// <summary>
    /// Verifies that JsonPublisherCatalogParser fails validation when dependency contentType is a signed numeric string.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_DependencyDeclaringSignedNumericContentType_FailsValidationAsync()
    {
        var parser = new JsonPublisherCatalogParser(Mock.Of<ILogger<JsonPublisherCatalogParser>>());
        var json = """
        {
            "schemaVersion": 1,
            "publisher": { "id": "test-pub", "name": "Test Publisher" },
            "content": [
                {
                    "id": "test-mod",
                    "name": "Test Mod",
                    "contentType": "Mod",
                    "releases": [
                        {
                            "version": "1.0.0",
                            "dependencies": [
                                { "publisherId": "custom", "contentId": "other", "contentType": "+2" }
                            ],
                            "artifacts": [
                                { "filename": "test.zip", "downloadUrl": "https://example.com/test.zip", "isPrimary": true }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var result = await parser.ParseCatalogAsync(json);

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("specifies invalid contentType");
    }

    /// <summary>
    /// Verifies that JsonPublisherCatalogParser fails validation when dependency contentType is an undefined named string starting with a letter.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseCatalogAsync_DependencyDeclaringUndefinedNamedContentType_FailsValidationAsync()
    {
        var parser = new JsonPublisherCatalogParser(Mock.Of<ILogger<JsonPublisherCatalogParser>>());
        var json = """
        {
            "schemaVersion": 1,
            "publisher": { "id": "test-pub", "name": "Test Publisher" },
            "content": [
                {
                    "id": "test-mod",
                    "name": "Test Mod",
                    "contentType": "Mod",
                    "releases": [
                        {
                            "version": "1.0.0",
                            "dependencies": [
                                { "publisherId": "custom", "contentId": "other", "contentType": "NonExistentType" }
                            ],
                            "artifacts": [
                                { "filename": "test.zip", "downloadUrl": "https://example.com/test.zip", "isPrimary": true }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var result = await parser.ParseCatalogAsync(json);

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("specifies invalid contentType");
    }

    /// <summary>
    /// Verifies that ResolveDependencyContentType ignores undefined or numeric enum values and falls back.
    /// </summary>
    [Fact]
    public void ResolveDependencyContentType_UndefinedNumericContentType_FallsBackToDefault()
    {
        var dep = new CatalogDependency
        {
            PublisherId = "custom",
            ContentId = "custom-addon",
            ContentType = "+3",
        };
        var parent = new CatalogContentItem
        {
            Id = "my-mod",
            Name = "My Mod",
            ContentType = ContentType.Mod,
        };

        var resolvedType = CatalogManifestIdentity.ResolveDependencyContentType(dep, parent);

        resolvedType.Should().Be(ContentType.Mod);
    }
}
