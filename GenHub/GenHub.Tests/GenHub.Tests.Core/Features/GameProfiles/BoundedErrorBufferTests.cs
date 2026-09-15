using System.Reflection;
using GenHub.Features.GameProfiles.Infrastructure;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Tests for the bounded stderr capture used to explain a failed launch.
/// </summary>
/// <remarks>
/// The buffer is a private nested type; it is exercised through reflection rather than
/// being made public, because it is an implementation detail of process management and
/// its behaviour only matters through the diagnostics it produces.
/// </remarks>
public class BoundedErrorBufferTests
{
    private static readonly Type BufferType =
        typeof(GameProcessManager).GetNestedType("BoundedErrorBuffer", BindingFlags.NonPublic)!;

    /// <summary>
    /// The startup context is where the cause usually is, so the first lines must survive
    /// even when far more output follows than the buffer retains.
    /// </summary>
    [Fact]
    public void Append_RetainsBothTheHeadAndTheTail()
    {
        var buffer = CreateBuffer();

        Append(buffer, "dyld: library not loaded");
        for (var i = 0; i < 200; i++)
        {
            Append(buffer, $"noise line {i}");
        }

        Append(buffer, "Abort trap: 6");

        var text = buffer.ToString()!;

        Assert.Contains("dyld: library not loaded", text);
        Assert.Contains("Abort trap: 6", text);
        Assert.Contains("omitted", text);
    }

    /// <summary>
    /// A single pathological line must not be retained in full.
    /// </summary>
    [Fact]
    public void Append_TruncatesAnOverlongLine()
    {
        var buffer = CreateBuffer();

        Append(buffer, new string('x', 10_000));

        var text = buffer.ToString()!;

        Assert.Contains("line truncated", text);
        Assert.True(text.Length < 10_000, "The overlong line was retained in full.");
    }

    /// <summary>
    /// A null line is the framework's end-of-stream signal, not content.
    /// </summary>
    [Fact]
    public void Append_TreatsNullAsEndOfStreamRatherThanContent()
    {
        var buffer = CreateBuffer();

        Assert.False(EndOfStreamReached(buffer));

        Append(buffer, "something failed");
        Append(buffer, null);

        Assert.True(EndOfStreamReached(buffer));
        Assert.Equal("something failed", buffer.ToString());
    }

    /// <summary>
    /// Total retained output stays bounded regardless of how much arrives.
    /// </summary>
    [Fact]
    public void Append_BoundsTotalRetainedOutput()
    {
        var buffer = CreateBuffer();

        for (var i = 0; i < 5_000; i++)
        {
            Append(buffer, new string('y', 500));
        }

        Assert.True(
            buffer.ToString()!.Length < 128 * 1024,
            "Retained output grew beyond the cap.");
    }

    private static object CreateBuffer() => Activator.CreateInstance(BufferType, nonPublic: true)!;

    private static void Append(object buffer, string? line) =>
        BufferType.GetMethod("Append", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(buffer, [line]);

    private static bool EndOfStreamReached(object buffer) =>
        (bool)BufferType.GetProperty("EndOfStreamReached", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(buffer)!;
}
