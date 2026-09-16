using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.GenLauncher;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherCatalogParser"/>.
/// </summary>
public sealed class GenLauncherCatalogParserTests
{
    private const string SampleRootYaml = @"
LauncherVersion: 1.0.0
modDatas:
  - ModName: Shockwave
    ModLink: https://example.com/shockwave.yaml
    ModPatches:
      - https://example.com/shockwave_patch.yaml
    ModAddons:
      - https://example.com/shockwave_addon.yaml
";

    private const string SampleChildYaml = @"
Name: Shockwave
Version: 1.2
ModificationType: 0
SimpleDownloadLink: https://example.com/shockwave.zip
UIImageSourceLink: https://moddb.com/shockwave.png
S3HostLink: gen.insave.ovh:9000
S3BucketName: generals-mods
S3FolderName: Shockwave_1.2/
";

    /// <summary>
    /// Tests that ParseRootCatalog correctly parses the root catalog manifest.
    /// </summary>
    [Fact]
    public void ParseRootCatalog_ReturnsRootManifestWithModData()
    {
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());

        var root = parser.ParseRootCatalog(SampleRootYaml);

        Assert.NotNull(root);
        Assert.Equal("1.0.0", root.LauncherVersion);
        Assert.Single(root.ModDatas);

        var mod = root.ModDatas[0];
        Assert.Equal("Shockwave", mod.ModName);
        Assert.Equal("https://example.com/shockwave.yaml", mod.ModLink);
        Assert.Single(mod.ModPatches);
        Assert.Equal("https://example.com/shockwave_patch.yaml", mod.ModPatches[0]);
        Assert.Single(mod.ModAddons);
        Assert.Equal("https://example.com/shockwave_addon.yaml", mod.ModAddons[0]);
    }

    /// <summary>
    /// Tests that ParseVersionManifest correctly parses child version manifests.
    /// </summary>
    [Fact]
    public void ParseVersionManifest_ReturnsVersionManifestWithFields()
    {
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());

        var manifest = parser.ParseVersionManifest(SampleChildYaml);

        Assert.NotNull(manifest);
        Assert.Equal("Shockwave", manifest.Name);
        Assert.Equal("1.2", manifest.Version);
        Assert.Equal(GenLauncherModificationType.Mod, manifest.GetParsedType());
        Assert.Equal("https://example.com/shockwave.zip", manifest.SimpleDownloadLink);
        Assert.Equal("https://moddb.com/shockwave.png", manifest.UIImageSourceLink);
        Assert.Equal("gen.insave.ovh:9000", manifest.S3HostLink);
        Assert.Equal("generals-mods", manifest.S3BucketName);
        Assert.Equal("Shockwave_1.2/", manifest.S3FolderName);
    }

    /// <summary>
    /// Tests that ParseAsync maps version manifest properties to ContentSearchResult.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_MapsPropertiesCorrectly()
    {
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var provider = new ProviderDefinition
        {
            ProviderId = GenLauncherConstants.PublisherId,
            PublisherType = PublisherTypeConstants.GenLauncher,
            TargetGame = GameType.ZeroHour,
        };

        var result = await parser.ParseAsync(SampleChildYaml, provider);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var first = result.Data.First();
        Assert.Equal("Shockwave", first.Name);
        Assert.Equal("1.2", first.Version);
        Assert.Equal(GenHub.Core.Models.Enums.ContentType.Mod, first.ContentType);
        Assert.Equal(GameType.ZeroHour, first.TargetGame);
        Assert.Equal(PublisherTypeConstants.GenLauncher, first.ProviderName);
        Assert.Equal(GenLauncherConstants.PublisherId, first.ResolverId);
        Assert.True(first.RequiresResolution);
        Assert.Equal("https://moddb.com/shockwave.png", first.IconUrl);
    }
}
