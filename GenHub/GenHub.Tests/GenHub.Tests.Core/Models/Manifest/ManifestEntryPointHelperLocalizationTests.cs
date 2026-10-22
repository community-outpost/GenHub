using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Manifest;
using Moq;
using System;
using System.IO;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Verifies entry-point baking failures resolve through <see cref="ILocalizationService"/>
/// when one is provided, since these failures surface in download UI.
/// </summary>
public sealed class ManifestEntryPointHelperLocalizationTests : IDisposable
{
    private readonly string _payload = Path.Combine(Path.GetTempPath(), "GenHub_BakeEntryLoc_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestEntryPointHelperLocalizationTests"/> class.
    /// </summary>
    public ManifestEntryPointHelperLocalizationTests()
    {
        Directory.CreateDirectory(_payload);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_payload))
        {
            Directory.Delete(_payload, recursive: true);
        }
    }

    /// <summary>
    /// A provided localization service renders the failure through the matching
    /// resource key instead of the English fallback.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_WithLocalizationService_UsesResourceKey()
    {
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.locmissing",
            ContentType = ContentType.GameClient,
            EntryPoint = "custom/launcher.sh",
        };
        var localizationMock = new Mock<ILocalizationService>();
        localizationMock
            .Setup(x => x.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => $"LOC:{key}:{args.Length}");

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload, localizationService: localizationMock.Object);

        Assert.False(result.Success);
        Assert.Equal($"LOC:{ManifestConstants.EntryPointNotFoundInPayloadKey}:2", result.FirstError);
        localizationMock.Verify(
            x => x.GetString(ManifestConstants.EntryPointNotFoundInPayloadKey, It.IsAny<object?[]>()),
            Times.Once);
    }

    /// <summary>
    /// Without a localization service the English fallback still renders, so
    /// headless and test paths keep working.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_WithoutLocalizationService_UsesEnglishFallback()
    {
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.locmissing",
            ContentType = ContentType.GameClient,
            EntryPoint = "custom/launcher.sh",
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.False(result.Success);
        Assert.Contains("was not found in its payload", result.FirstError, StringComparison.Ordinal);
    }
}
