using GenHub.Core.Helpers;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests for <see cref="LocalManifestResolver"/>.
/// </summary>
public class LocalManifestResolverTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly LocalManifestResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalManifestResolverTests"/> class.
    /// </summary>
    public LocalManifestResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        _resolver = new LocalManifestResolver(new Mock<ILogger<LocalManifestResolver>>().Object);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that a manifest with a valid id but no files is rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithNoFiles_ReturnsFailureAsync()
    {
        var manifestPath = Path.Combine(_tempRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{\"Id\":\"1.104.ea.gameclient.generals\",\"Files\":[]}");

        var result = await _resolver.ResolveAsync(new ContentSearchResult { SourceUrl = manifestPath });

        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that a manifest whose files are all in variants is accepted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithFilesOnlyInVariants_SucceedsAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [new ManifestFile { RelativePath = "generalszh-host" }],
            [new ManifestFile { RelativePath = "generalszh-foreign.exe" }]);
        var manifestPath = Path.Combine(_tempRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, ManifestJsonOptions.Default));

        var result = await _resolver.ResolveAsync(new ContentSearchResult { SourceUrl = manifestPath });

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(2, result.Data!.Variants.Count);
    }

    /// <summary>
    /// Tests that a manifest with an invalid id returns a failure result instead of throwing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithInvalidId_ReturnsFailureAsync()
    {
        var manifestPath = Path.Combine(_tempRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{\"Id\":null,\"Files\":[]}");

        var result = await _resolver.ResolveAsync(new ContentSearchResult { SourceUrl = manifestPath });

        Assert.False(result.Success);
    }
}
