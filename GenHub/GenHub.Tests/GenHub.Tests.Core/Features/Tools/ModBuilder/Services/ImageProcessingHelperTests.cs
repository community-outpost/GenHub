using FluentAssertions;
using GenHub.Features.Tools.ModBuilder.Services;
using ImageMagick;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="ImageProcessingHelper"/>.
/// </summary>
public sealed class ImageProcessingHelperTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageProcessingHelperTests"/> class.
    /// </summary>
    public ImageProcessingHelperTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// A cancelled save throws and writes nothing, whatever the target format.
    /// </summary>
    /// <param name="targetExt">The target extension.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(".tga")]
    [InlineData(".bmp")]
    [InlineData(".png")]
    public async Task SaveImageToTargetAsync_WhenCancelled_ThrowsAndWritesNothing(string targetExt)
    {
        var targetPath = Path.Combine(_tempDirectory, "output" + targetExt);
        using var image = new MagickImage(MagickColors.Red, 2, 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ImageProcessingHelper.SaveImageToTargetAsync(image, targetPath, targetExt, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(targetPath).Should().BeFalse();
    }

    /// <summary>
    /// Releases the temporary directory used by these tests.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
