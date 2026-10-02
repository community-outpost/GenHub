using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.Services;

/// <summary>
/// Unit tests for <see cref="RmlImageResolver"/>.
/// </summary>
public sealed class RmlImageResolverTests : IDisposable
{
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private readonly string _root;
    private readonly string _documentDirectory;
    private readonly RmlImageResolver _resolver;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlImageResolverTests"/> class.
    /// </summary>
    public RmlImageResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _documentDirectory = Directory.CreateDirectory(Path.Combine(_root, "docscreen")).FullName;
        var bitmaps = new TextureBitmapService(new Mock<ISageTextureCodec>().Object, new Mock<ILogger<TextureBitmapService>>().Object);
        _resolver = new RmlImageResolver(bitmaps, new Mock<ILogger<RmlImageResolver>>().Object);
    }

    /// <summary>
    /// Tests that parent traversal outside the document folder is blocked.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ParentTraversal_BlockedAsync()
    {
        var secret = Path.Combine(_root, "secret.png");
        await File.WriteAllBytesAsync(secret, MinimalPng);

        var result = await _resolver.ResolveAsync("../secret.png", _documentDirectory);

        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that absolute paths outside the search folders are blocked.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_AbsoluteOutsideRoots_BlockedAsync()
    {
        var secret = Path.Combine(_root, "secret.png");
        await File.WriteAllBytesAsync(secret, MinimalPng);

        var result = await _resolver.ResolveAsync(secret, _documentDirectory);

        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that images inside the document folder still resolve.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ResolveAsync_InsideDocument_ResolvesAsync()
    {
        await File.WriteAllBytesAsync(Path.Combine(_documentDirectory, "ok.png"), MinimalPng);

        var result = await _resolver.ResolveAsync("ok.png", _documentDirectory);

        result.Should().NotBeNull();
    }

    /// <summary>
    /// Releases test resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resolver.ClearCache();
        Directory.Delete(_root, true);
    }
}
