using GenHub.Core.Extensions.GameInstallations;
using System;
using System.IO;

namespace GenHub.Tests.Core.Extensions.GameInstallations;

/// <summary>
/// Tests for the case-insensitive file lookup in <see cref="InstallationExtensions"/>.
/// </summary>
public sealed class InstallationExtensionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"GenHub.InstallationExtensionsTests.{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationExtensionsTests"/> class.
    /// </summary>
    public InstallationExtensionsTests()
    {
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Verifies a query in different casing returns the name stored on disk.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_DifferentCasing_ReturnsOnDiskName()
    {
        File.WriteAllText(Path.Combine(_directory, "BINKW32.DLL"), "dependency");

        var found = Path.Combine(_directory, "binkw32.dll").TryGetFileCaseInsensitive(out var matchedPath);

        Assert.True(found);
        Assert.Equal(Path.Combine(_directory, "BINKW32.DLL"), matchedPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies a query in the stored casing returns that path.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_ExactCasing_ReturnsPath()
    {
        var path = Path.Combine(_directory, "game.dat");
        File.WriteAllText(path, "game");

        var found = path.TryGetFileCaseInsensitive(out var matchedPath);

        Assert.True(found);
        Assert.Equal(path, matchedPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies a known file remains accessible when its parent permits traversal but not listing.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_WithoutDirectoryListingPermission_ReturnsKnownPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "game.dat");
        File.WriteAllText(path, "game");
        var originalMode = File.GetUnixFileMode(_directory);
        try
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.True(File.Exists(path));
            try
            {
                Directory.GetFiles(_directory);

                // Elevated users and some filesystems do not enforce the requested restriction.
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // The permission restriction is effective; exercise the fallback below.
            }

            Assert.True(path.TryGetFileCaseInsensitive(out var matchedPath));
            Assert.Equal(path, matchedPath, StringComparer.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(_directory, originalMode);
        }
    }

    /// <summary>
    /// Verifies exact casing wins when the filesystem stores both case variants.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_WithDistinctCaseVariants_PrefersExactMatch()
    {
        var upperPath = Path.Combine(_directory, "GAME.DAT");
        var lowerPath = Path.Combine(_directory, "game.dat");
        File.WriteAllText(upperPath, "upper");
        File.WriteAllText(lowerPath, "lower");
        if (Directory.GetFiles(_directory).Length != 2)
        {
            // The volume is case-insensitive, regardless of the operating system.
            return;
        }

        Assert.True(upperPath.TryGetFileCaseInsensitive(out var upperMatch));
        Assert.Equal(upperPath, upperMatch, StringComparer.Ordinal);
        Assert.True(lowerPath.TryGetFileCaseInsensitive(out var lowerMatch));
        Assert.Equal(lowerPath, lowerMatch, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies hidden files are found, matching <see cref="File.Exists(string)"/>.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_HiddenFile_ReturnsOnDiskName()
    {
        var path = Path.Combine(_directory, ".Hidden.DLL");
        File.WriteAllText(path, "hidden");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);

        var found = Path.Combine(_directory, ".hidden.dll").TryGetFileCaseInsensitive(out var matchedPath);

        Assert.True(found);
        Assert.Equal(path, matchedPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies a missing file is not found.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_MissingFile_ReturnsFalse()
    {
        var found = Path.Combine(_directory, "mss32.dll").TryGetFileCaseInsensitive(out var matchedPath);

        Assert.False(found);
        Assert.Null(matchedPath);
    }

    /// <summary>
    /// Verifies a file in a missing directory is not found.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_MissingDirectory_ReturnsFalse()
    {
        var found = Path.Combine(_directory, "missing", "game.dat").TryGetFileCaseInsensitive(out var matchedPath);

        Assert.False(found);
        Assert.Null(matchedPath);
    }

    /// <summary>
    /// Verifies wildcard characters in the name are matched literally, not as a search pattern.
    /// </summary>
    /// <param name="fileName">The queried file name.</param>
    [Theory]
    [InlineData("*.dll")]
    [InlineData("binkw3?.dll")]
    public void TryGetFileCaseInsensitive_WildcardName_IsNotTreatedAsPattern(string fileName)
    {
        File.WriteAllText(Path.Combine(_directory, "binkw32.dll"), "dependency");

        var found = Path.Combine(_directory, fileName).TryGetFileCaseInsensitive(out var matchedPath);

        Assert.False(found);
        Assert.Null(matchedPath);
    }

    /// <summary>
    /// Verifies existence-only lookup accepts exact and differently cased names.
    /// </summary>
    /// <param name="fileName">The queried file name.</param>
    [Theory]
    [InlineData("BINKW32.DLL")]
    [InlineData("binkw32.dll")]
    public void FileExistsCaseInsensitive_ExistingFile_ReturnsTrue(string fileName)
    {
        File.WriteAllText(Path.Combine(_directory, "BINKW32.DLL"), "dependency");

        Assert.True(Path.Combine(_directory, fileName).FileExistsCaseInsensitive());
        Assert.False(Path.Combine(_directory, "missing.dll").FileExistsCaseInsensitive());
    }

    /// <summary>
    /// Verifies Unix filenames containing wildcard characters are found literally.
    /// </summary>
    /// <param name="fileName">The stored file name.</param>
    [Theory]
    [InlineData("*.DLL")]
    [InlineData("binkw3?.DLL")]
    public void TryGetFileCaseInsensitive_LiteralWildcardFile_ReturnsOnDiskName(string fileName)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "dependency");

        Assert.True(Path.Combine(_directory, fileName.ToLowerInvariant()).TryGetFileCaseInsensitive(out var matchedPath));
        Assert.Equal(path, matchedPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies bare filenames retain direct filesystem lookup semantics without changing the process directory.
    /// </summary>
    [Fact]
    public void TryGetFileCaseInsensitive_BareFileName_PreservesDirectLookup()
    {
        var fileName = $"GenHub.Lookup.{Guid.NewGuid():N}.DLL";
        try
        {
            File.WriteAllText(fileName, "dependency");
            Assert.True(fileName.TryGetFileCaseInsensitive(out var exactMatch));
            Assert.Equal(fileName, exactMatch, StringComparer.Ordinal);

            var differentCase = fileName.ToLowerInvariant();
            var directlyAccessible = File.Exists(differentCase);
            Assert.Equal(directlyAccessible, differentCase.TryGetFileCaseInsensitive(out var differentMatch));
            Assert.Equal(directlyAccessible ? differentCase : null, differentMatch, StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(fileName);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
