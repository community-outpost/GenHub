using GenHub.Tests.Core.Features.GameProfiles;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Tests for the shared native-client fixture helper.
/// </summary>
/// <remarks>
/// The integration tests that use it all skip without a local engine install, so the
/// library predicate would otherwise never be exercised on CI — including the versioned
/// Linux forms, which a plain ".so" suffix test silently misses.
/// </remarks>
public class NativeClientFixtureTests
{
    /// <summary>
    /// Recognises macOS and both Linux shapes, unversioned and versioned.
    /// </summary>
    /// <param name="fileName">The candidate filename.</param>
    [Theory]
    [InlineData("libSDL3.dylib")]
    [InlineData("libSDL3.so")]
    [InlineData("libSDL3.so.0")]
    [InlineData("libSDL3.so.0.1.0")]
    [InlineData("libstdc++.so.6")]
    [InlineData("libavcodec.58.so.4")]
    [InlineData("mylib.sound.so.1")]
    public void IsDynamicLibrary_WithLibraryNames_ReturnsTrue(string fileName)
        => Assert.True(NativeClientFixture.IsDynamicLibrary(fileName));

    /// <summary>
    /// Rejects the engine binary, data files, and names that merely contain ".so".
    /// </summary>
    /// <param name="fileName">The candidate filename.</param>
    [Theory]
    [InlineData("generalszh")]
    [InlineData("INIZH.big")]
    [InlineData("readme.txt")]
    [InlineData("resources.sound")]
    [InlineData("libfoo.sox")]
    public void IsDynamicLibrary_WithNonLibraryNames_ReturnsFalse(string fileName)
        => Assert.False(NativeClientFixture.IsDynamicLibrary(fileName));
}
