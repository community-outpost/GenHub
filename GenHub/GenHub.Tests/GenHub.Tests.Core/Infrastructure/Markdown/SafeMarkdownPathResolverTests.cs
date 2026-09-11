using System.IO;
using System.Threading.Tasks;
using GenHub.Infrastructure.Markdown;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Markdown;

/// <summary>
/// Unit tests for <see cref="SafeMarkdownPathResolver"/>.
/// </summary>
public sealed class SafeMarkdownPathResolverTests
{
    private readonly SafeMarkdownPathResolver _resolver = new();

    /// <summary>
    /// Verifies that non-HTTP/HTTPS paths (file, avares, relative, UNC) return null without throwing.
    /// </summary>
    /// <param name="path">The unsafe or non-HTTP image path.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("avares://GenHub/Assets/logo.png")]
    [InlineData("\\\\192.168.1.1\\share\\pic.png")]
    [InlineData("relative/image.png")]
    [InlineData("../parent/image.png")]
    [InlineData("")]
    public async Task ResolveImageResource_NonHttpPaths_ReturnsNullAsync(string path)
    {
        var resultTask = _resolver.ResolveImageResource(path);
        Assert.NotNull(resultTask);

        var stream = await resultTask!;
        Assert.Null(stream);
    }
}
