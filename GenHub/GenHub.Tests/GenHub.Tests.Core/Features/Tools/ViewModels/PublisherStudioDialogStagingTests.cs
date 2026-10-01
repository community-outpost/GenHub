using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for multi-file staging, archive previews, and accent presets
/// in <see cref="AddContentDialogViewModel"/>.
/// </summary>
public sealed class PublisherStudioDialogStagingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "GenHubStagingTests_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Staging two files must create one artifact per file and mark the initial
    /// release bundled so players install both files as one package.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromPaths_TwoFiles_BundlesArtifactsOnCreateAsync()
    {
        var first = WriteTempFile("controlbar-a.big", "first-payload");
        var second = WriteTempFile("controlbar-b.big", "second-payload");
        CatalogContentItem? created = null;
        using var vm = new AddContentDialogViewModel(item => created = item);

        vm.PopulateFromPaths([first, second]);

        Assert.Equal(2, vm.StagedFiles.Count);
        Assert.True(vm.IsMultiFileStaging);
        Assert.NotNull(vm.MultiFileBundleNote);
        Assert.Equal(first, vm.LocalFilePath);

        await WaitForComputeAsync(vm);

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        Assert.True(release.BundleArtifacts);
        Assert.Equal(2, release.Artifacts.Count);
        Assert.Equal("controlbar-a.big", release.Artifacts[0].Filename);
        Assert.Equal("controlbar-b.big", release.Artifacts[1].Filename);
        Assert.True(release.Artifacts[0].IsPrimary);
        Assert.False(release.Artifacts[1].IsPrimary);
        Assert.All(release.Artifacts, a => Assert.True(a.Size > 0));
    }

    /// <summary>
    /// A single staged file keeps the legacy one-artifact path without bundling.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromPath_SingleFile_CreatesSingleUnbundledArtifactAsync()
    {
        var file = WriteTempFile("single-mod.big", "payload");
        CatalogContentItem? created = null;
        using var vm = new AddContentDialogViewModel(item => created = item);

        vm.PopulateFromPath(file);
        await WaitForComputeAsync(vm);

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        Assert.False(release.BundleArtifacts);
        var artifact = Assert.Single(release.Artifacts);
        Assert.Equal("single-mod.big", artifact.Filename);
        Assert.True(artifact.IsPrimary);
    }

    /// <summary>
    /// Staging a zip archive must surface its entry count and the automatic
    /// extraction note so publishers know installs unpack it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromPath_ZipFile_ShowsArchiveNoteAsync()
    {
        var zipPath = Path.Combine(TempDir(), "bundle.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("a.big");
            archive.CreateEntry("b.big");
        }

        using var vm = new AddContentDialogViewModel(_ => { });
        vm.PopulateFromPath(zipPath);
        await WaitForComputeAsync(vm);

        var entry = Assert.Single(vm.StagedFiles);
        Assert.True(entry.IsArchive);
        Assert.NotNull(entry.ArchiveNoteText);
        Assert.Contains("2", entry.ArchiveNoteText);
    }

    /// <summary>
    /// Removing a staged file must drop its entry and re-sync the primary path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveStagedFile_RemovesEntryAndSyncsPrimaryAsync()
    {
        var first = WriteTempFile("one.big", "one");
        var second = WriteTempFile("two.big", "two");
        using var vm = new AddContentDialogViewModel(_ => { });
        vm.PopulateFromPaths([first, second]);
        await WaitForComputeAsync(vm);

        vm.RemoveStagedFileCommand.Execute(vm.StagedFiles[0]);

        var remaining = Assert.Single(vm.StagedFiles);
        Assert.Equal(second, remaining.LocalPath);
        Assert.Equal(second, vm.LocalFilePath);
        Assert.False(vm.IsMultiFileStaging);
    }

    /// <summary>
    /// Switching to direct-URL mode must clear staged files and primary metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ToggleToDirectUrl_ClearsStagedFilesAsync()
    {
        var file = WriteTempFile("mod.big", "payload");
        using var vm = new AddContentDialogViewModel(_ => { });
        vm.PopulateFromPath(file);
        await WaitForComputeAsync(vm);
        Assert.NotEmpty(vm.StagedFiles);

        await WaitForComputeAsync(vm);

        vm.UseDirectUrl = true;

        Assert.Empty(vm.StagedFiles);
        Assert.False(vm.HasStagedFiles);
        Assert.Null(vm.LocalFilePath);
    }

    /// <summary>
    /// Picking a preset accent color must apply its hex value.
    /// </summary>
    [Fact]
    public void SelectAccentColor_SetsAccentColor()
    {
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.SelectAccentColorCommand.Execute("#DC2626");

        Assert.Equal("#DC2626", vm.AccentColor);
    }

    /// <summary>
    /// Verifies that clearing artwork fields in edit mode does not resurrect previous metadata.
    /// </summary>
    [Fact]
    public void EditMode_ClearingArtwork_DoesNotResurrectOldMetadata()
    {
        var existingItem = new CatalogContentItem
        {
            Id = "mod-test",
            Name = "Test Mod",
            Description = "Initial description that meets length requirements",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            TargetGame = GameType.Generals,
            Metadata = new ContentRichMetadata
            {
                IconUrl = "https://example.com/old_icon.png",
                BannerUrl = "https://example.com/old_banner.png",
            },
        };

        CatalogContentItem? savedItem = null;
        using var vm = new AddContentDialogViewModel(existingItem, item => savedItem = item);

        // Clear the icon in edit mode
        vm.IconArtwork = string.Empty;

        // Save
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.Null(savedItem.Metadata?.IconUrl);
        Assert.Equal("https://example.com/old_banner.png", savedItem.Metadata?.BannerUrl);
    }

    /// <summary>
    /// Editing a ContentBundle when catalog is null must populate bundle component options
    /// from existing bundled items so existing configurations are retained upon saving.
    /// </summary>
    [Fact]
    public void EditContentBundle_WithoutCatalog_PopulatesAndRetainsBundledItems()
    {
        var existingBundle = new CatalogContentItem
        {
            Id = "competitive-pack",
            Name = "Competitive Pack",
            Description = "A complete competitive package for Zero Hour.",
            ContentType = GenHub.Core.Models.Enums.ContentType.ContentBundle,
            BundledItems =
            [
                new CatalogDependency
                {
                    ContentId = "thesuperhackers-zh",
                    DefaultVariant = "Gentool",
                    ContentType = nameof(GenHub.Core.Models.Enums.ContentType.GameClient),
                },
                new CatalogDependency
                {
                    ContentId = "zh-community-patch",
                    DefaultVariant = "1.06",
                    ContentType = nameof(GenHub.Core.Models.Enums.ContentType.Patch),
                },
            ],
        };

        CatalogContentItem? savedItem = null;
        var vm = new AddContentDialogViewModel(existingBundle, item => savedItem = item);

        Assert.Equal(2, vm.BundleComponentOptions.Count);
        Assert.All(vm.BundleComponentOptions, opt => Assert.True(opt.IsSelected));
        Assert.Contains(vm.BundleComponentOptions, opt => opt.ContentId == "thesuperhackers-zh" && opt.SelectedVariant == "Gentool");
        Assert.Contains(vm.BundleComponentOptions, opt => opt.ContentId == "zh-community-patch" && opt.SelectedVariant == "1.06");

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.Equal(2, savedItem.BundledItems.Count);
        Assert.Contains(savedItem.BundledItems, b => b.ContentId == "thesuperhackers-zh" && b.DefaultVariant == "Gentool");
        Assert.Contains(savedItem.BundledItems, b => b.ContentId == "zh-community-patch" && b.DefaultVariant == "1.06");
    }

    /// <summary>
    /// Cloned release dependencies must keep their variant defaults so editing
    /// and saving an item does not lose bundle variant configuration.
    /// </summary>
    [Fact]
    public void EditItem_ExistingReleaseDependency_PreservesVariantFields()
    {
        var existingItem = new CatalogContentItem
        {
            Id = "variant-mod",
            Name = "Variant Mod",
            Description = "Initial description that meets length requirements",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Artifacts = [new ReleaseArtifact { DownloadUrl = "https://example.com/mod.zip" }],
                    Dependencies =
                    [
                        new CatalogDependency
                        {
                            ContentId = "base-client",
                            DefaultVariant = "720p",
                            AllowedVariantAxes = ["resolution", "language"],
                        },
                    ],
                },
            ],
        };

        CatalogContentItem? savedItem = null;
        var vm = new AddContentDialogViewModel(existingItem, item => savedItem = item);
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        var savedDependency = Assert.Single(Assert.Single(savedItem.Releases).Dependencies ?? []);
        Assert.Equal("720p", savedDependency.DefaultVariant);
        Assert.Equal(["resolution", "language"], savedDependency.AllowedVariantAxes);
    }

    /// <summary>
    /// Saving an edited bundle must mirror the selected components into every release's
    /// dependencies and drop deselected ones, so manual bundles match JSON-authored ones.
    /// </summary>
    [Fact]
    public void EditContentBundle_DeselectedComponent_MirrorsSelectionIntoReleaseDependencies()
    {
        var existingBundle = new CatalogContentItem
        {
            Id = "competitive-pack",
            Name = "Competitive Pack",
            Description = "A complete competitive package for Zero Hour.",
            ContentType = GenHub.Core.Models.Enums.ContentType.ContentBundle,
            BundledItems =
            [
                new CatalogDependency { ContentId = "comp-a" },
                new CatalogDependency { ContentId = "comp-b" },
            ],
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Dependencies =
                    [
                        new CatalogDependency { ContentId = "comp-a" },
                        new CatalogDependency { ContentId = "comp-b" },
                    ],
                },
            ],
        };

        CatalogContentItem? savedItem = null;
        var vm = new AddContentDialogViewModel(existingBundle, item => savedItem = item);
        var deselected = vm.BundleComponentOptions.First(o => o.ContentId == "comp-b");
        deselected.IsSelected = false;
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.Equal("comp-a", Assert.Single(savedItem.BundledItems).ContentId);
        Assert.All(savedItem.Releases, release =>
            Assert.Equal("comp-a", Assert.Single(release.Dependencies ?? []).ContentId));
    }

    /// <summary>
    /// Converting an upstream item back to static releases must preserve its
    /// publisher identity instead of dropping it.
    /// </summary>
    [Fact]
    public void EditUpstreamItem_DisablingUpstream_PreservesPublisherType()
    {
        var existingItem = new CatalogContentItem
        {
            Id = "superhackers-client",
            Name = "SuperHackers Client",
            Description = "Initial description that meets length requirements",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            PublisherType = "thesuperhackers",
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "TheSuperHackers",
                Repository = "TheSuperHackers/GeneralsGameCode",
            },
        };

        CatalogContentItem? savedItem = null;
        var vm = new AddContentDialogViewModel(existingItem, item => savedItem = item);
        Assert.True(vm.IsUpstreamSource);

        vm.IsUpstreamSource = false;
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.Equal("thesuperhackers", savedItem.PublisherType);
        Assert.Null(savedItem.UpstreamSync);
    }

    /// <summary>
    /// Editing an upstream item with a blank stored provider must fall back to the
    /// default provider so the provider selector shows a valid selection.
    /// </summary>
    /// <param name="provider">The blank stored provider value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EditUpstreamItem_BlankProvider_FallsBackToDefault(string? provider)
    {
        var existingItem = new CatalogContentItem
        {
            Id = "upstream-client",
            Name = "Upstream Client",
            Description = "Initial description that meets length requirements",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = provider!,
                Repository = "TheSuperHackers/GeneralsGameCode",
            },
        };

        var vm = new AddContentDialogViewModel(existingItem, _ => { });

        Assert.True(vm.IsUpstreamSource);
        Assert.Equal(CatalogConstants.UpstreamProviders.TheSuperHackers, vm.SelectedUpstreamProvider);
    }

    /// <summary>
    /// When catalog sibling has releases without variant annotations but defines UpstreamSync.AssetRules,
    /// RefreshBundleComponentOptions must inspect AssetRules and populate available variants.
    /// </summary>
    [Fact]
    public void EditContentBundle_WithCatalogSiblingHavingReleasesAndAssetRules_PopulatesAssetRulesVariants()
    {
        var siblingItem = new CatalogContentItem
        {
            Id = "thesuperhackers-zh",
            Name = "TheSuperHackers ZH",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Artifacts =
                    [
                        new ReleaseArtifact
                        {
                            Filename = "release.zip",
                        },
                    ],
                },
            ],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "GitHub",
                Repository = "TheSuperHackers/GeneralsGamePatch",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule
                    {
                        Pattern = ".*Gentool.*",
                        Variant = "Gentool",
                    },
                ],
            },
        };

        var catalog = new PublisherCatalog
        {
            Content = [siblingItem],
        };

        var existingBundle = new CatalogContentItem
        {
            Id = "competitive-pack",
            Name = "Competitive Pack",
            Description = "A complete competitive package for Zero Hour.",
            ContentType = GenHub.Core.Models.Enums.ContentType.ContentBundle,
            BundledItems =
            [
                new CatalogDependency
                {
                    ContentId = "thesuperhackers-zh",
                    DefaultVariant = "Gentool",
                    ContentType = nameof(GenHub.Core.Models.Enums.ContentType.GameClient),
                },
            ],
        };

        CatalogContentItem? savedItem = null;
        var vm = new AddContentDialogViewModel(existingBundle, item => savedItem = item, catalog: catalog);

        Assert.Single(vm.BundleComponentOptions);
        var opt = vm.BundleComponentOptions[0];
        Assert.True(opt.IsSelected);
        Assert.Contains("Gentool", opt.AvailableVariants);
        Assert.Equal("Gentool", opt.SelectedVariant);
    }

    /// <summary>
    /// Verifies that AddContentDialogViewModel preserves multiple dependencies with the same ContentId
    /// when they originate from distinct publishers, without cross-overwriting metadata.
    /// </summary>
    [Fact]
    public void AddContentDialogViewModel_ContentBundle_PreservesMultiplePublishersForSameContentId()
    {
        var existingBundle = new CatalogContentItem
        {
            Id = "multi-pack",
            Name = "Multi Pack",
            Description = "A bundle referencing distinct publisher versions of the same content ID.",
            ContentType = GenHub.Core.Models.Enums.ContentType.ContentBundle,
            BundledItems =
            [
                new CatalogDependency
                {
                    PublisherId = "pub-alpha",
                    ContentId = "shared-mod",
                    VersionConstraint = ">= 1.0.0",
                    CatalogUrl = "https://alpha.example.com/catalog.json",
                    ContentType = nameof(GenHub.Core.Models.Enums.ContentType.Mod),
                },
                new CatalogDependency
                {
                    PublisherId = "pub-beta",
                    ContentId = "shared-mod",
                    VersionConstraint = ">= 2.0.0",
                    CatalogUrl = "https://beta.example.com/catalog.json",
                    ContentType = nameof(GenHub.Core.Models.Enums.ContentType.Mod),
                },
            ],
        };

        CatalogContentItem? savedItem = null;
        using var vm = new AddContentDialogViewModel(existingBundle, item => savedItem = item);

        Assert.Equal(2, vm.BundleComponentOptions.Count);
        Assert.All(vm.BundleComponentOptions, opt => Assert.True(opt.IsSelected));

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.Equal(2, savedItem.BundledItems.Count);
        Assert.Contains(
            savedItem.BundledItems,
            b => b.PublisherId == "pub-alpha" &&
                 b.ContentId == "shared-mod" &&
                 b.VersionConstraint == ">= 1.0.0" &&
                 b.CatalogUrl == "https://alpha.example.com/catalog.json");
        Assert.Contains(
            savedItem.BundledItems,
            b => b.PublisherId == "pub-beta" &&
                 b.ContentId == "shared-mod" &&
                 b.VersionConstraint == ">= 2.0.0" &&
                 b.CatalogUrl == "https://beta.example.com/catalog.json");
    }

    /// <summary>
    /// Editing an upstream item must preserve its JSON-loaded asset rules, since the
    /// dialog has no rule editor and dropping them would break variant mapping.
    /// </summary>
    [Fact]
    public void EditUpstreamItem_PreservesAssetRules()
    {
        var existingItem = new CatalogContentItem
        {
            Id = "variant-client",
            Name = "Variant Client",
            Description = "Initial description that meets length requirements",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
                Repository = "Example/Repo",
                VariantAxis = "game-type",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule
                    {
                        Pattern = "generalszh-.*\\.zip$",
                        Variant = "Zero Hour",
                        IsDefault = true,
                        TargetGame = GameType.ZeroHour,
                    },
                ],
            },
        };

        CatalogContentItem? savedItem = null;
        using var vm = new AddContentDialogViewModel(existingItem, item => savedItem = item);
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.NotNull(savedItem.UpstreamSync);
        var rule = Assert.Single(savedItem.UpstreamSync.AssetRules);
        Assert.Equal("generalszh-.*\\.zip$", rule.Pattern);
        Assert.Equal("Zero Hour", rule.Variant);
        Assert.True(rule.IsDefault);
        Assert.Equal(GameType.ZeroHour, rule.TargetGame);
    }

    /// <summary>
    /// Editing an upstream item with a provider alias must normalize to the canonical
    /// provider so the selector and ingestion agree.
    /// </summary>
    [Fact]
    public void EditUpstreamItem_AliasProvider_NormalizesToCanonical()
    {
        var existingItem = new CatalogContentItem
        {
            Id = "alias-client",
            Name = "Alias Client",
            Description = "Initial description that meets length requirements",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GitHubReleasesAlias,
                Repository = "Example/Repo",
            },
        };

        using var vm = new AddContentDialogViewModel(existingItem, _ => { });

        Assert.Equal(CatalogConstants.UpstreamProviders.GitHubReleases, vm.SelectedUpstreamProvider);
    }

    /// <summary>
    /// Checking featured without an accent color must default to gold.
    /// </summary>
    [Fact]
    public void CheckingFeatured_WithoutAccentColor_DefaultsToGold()
    {
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.IsFeatured = true;

        Assert.Equal(CatalogConstants.FeaturedDefaultColor, vm.AccentColor);
    }

    /// <summary>
    /// Checking featured must keep an explicitly chosen accent color.
    /// </summary>
    [Fact]
    public void CheckingFeatured_WithAccentColor_KeepsCustomColor()
    {
        using var vm = new AddContentDialogViewModel(_ => { });
        vm.AccentColor = "#FF0000";

        vm.IsFeatured = true;

        Assert.Equal("#FF0000", vm.AccentColor);
    }

    /// <summary>
    /// GitHub-backed providers show repository and channel fields but no content code.
    /// </summary>
    /// <param name="provider">The GitHub-family provider identifier.</param>
    [Theory]
    [InlineData("GitHubReleases")]
    [InlineData("TheSuperHackers")]
    public void UpstreamProvider_GitHubFamily_ShowsRepositoryAndChannel(string provider)
    {
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.SelectedUpstreamProvider = provider;

        Assert.True(vm.ShowUpstreamRepository);
        Assert.True(vm.ShowUpstreamChannel);
        Assert.False(vm.ShowUpstreamContentCode);
    }

    /// <summary>
    /// Catalog-backed providers show the content code field but no repository or channel.
    /// </summary>
    /// <param name="provider">The catalog-backed provider identifier.</param>
    [Theory]
    [InlineData("GeneralsOnline")]
    [InlineData("CommunityOutpost")]
    public void UpstreamProvider_CatalogBacked_ShowsContentCodeOnly(string provider)
    {
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.SelectedUpstreamProvider = provider;

        Assert.False(vm.ShowUpstreamRepository);
        Assert.False(vm.ShowUpstreamChannel);
        Assert.True(vm.ShowUpstreamContentCode);
    }

    /// <summary>
    /// Saving a bundle without releases must synthesize one release mirroring the
    /// selected components, so catalogs without releases display like complete ones.
    /// </summary>
    [Fact]
    public void EditContentBundle_WithoutReleases_SynthesizesMirroredRelease()
    {
        var existingBundle = new CatalogContentItem
        {
            Id = "releaseless-pack",
            Name = "Releaseless Pack",
            Description = "A complete competitive package for Zero Hour.",
            ContentType = GenHub.Core.Models.Enums.ContentType.ContentBundle,
            BundledItems =
            [
                new CatalogDependency { ContentId = "comp-a" },
            ],
        };

        CatalogContentItem? savedItem = null;
        using var vm = new AddContentDialogViewModel(existingBundle, item => savedItem = item);
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        var release = Assert.Single(savedItem.Releases);
        Assert.Equal("comp-a", Assert.Single(release.Dependencies ?? []).ContentId);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        for (var i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
                break;
            }
            catch (IOException)
            {
                if (i == 4)
                {
                    break;
                }

                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                if (i == 4)
                {
                    break;
                }

                Thread.Sleep(50);
            }
        }
    }

    private string TempDir()
    {
        Directory.CreateDirectory(_tempDir);
        return _tempDir;
    }

    private string WriteTempFile(string fileName, string content)
    {
        var path = Path.Combine(TempDir(), fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task WaitForComputeAsync(AddContentDialogViewModel vm)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while ((vm.IsComputingHash || string.IsNullOrEmpty(vm.Sha256Hash)) && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(50);
        }

        if (vm.IsComputingHash || string.IsNullOrEmpty(vm.Sha256Hash))
        {
            throw new TimeoutException($"Hash computation did not complete within {timeout.TotalSeconds} seconds.");
        }
    }
}
