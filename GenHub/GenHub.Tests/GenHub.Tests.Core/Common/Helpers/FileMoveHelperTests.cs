using GenHub.Common.Helpers;
using GenHub.Tests.Core.Infrastructure;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Common.Helpers;

/// <summary>
/// Unit tests for <see cref="FileMoveHelper"/>.
/// </summary>
public sealed class FileMoveHelperTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("file-move-tests").FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        ReadOnlyFolderFixtures.RestoreWritable(_tempDirectory);
        Directory.Delete(_tempDirectory, true);
    }

    /// <summary>
    /// Verifies that an ordinary move renames the file.
    /// </summary>
    [Fact]
    public void MoveWithoutResidue_WithWritableFile_MovesFile()
    {
        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "map");

        FileMoveHelper.MoveWithoutResidue(source, destination);

        Assert.False(File.Exists(source));
        Assert.Equal("map", File.ReadAllText(destination));
    }

    /// <summary>
    /// Verifies that an existing destination is refused and left untouched.
    /// </summary>
    [Fact]
    public void MoveWithoutResidue_WhenDestinationExists_ThrowsAndKeepsBothFiles()
    {
        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "source");
        File.WriteAllText(destination, "destination");

        Assert.Throws<IOException>(() => FileMoveHelper.MoveWithoutResidue(source, destination));

        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("destination", File.ReadAllText(destination));
    }

    /// <summary>
    /// Verifies that moving a locked file fails without leaving the copy the runtime made.
    /// </summary>
    [Fact]
    public void MoveWithoutResidue_WhenSourceIsLocked_ThrowsAndLeavesOnlySource()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "map");
        ReadOnlyFolderFixtures.LockImmutable(source);

        var exception = Record.Exception(() => FileMoveHelper.MoveWithoutResidue(source, destination));

        Assert.True(exception is UnauthorizedAccessException or IOException, exception?.ToString());
        Assert.Equal(["Old.map"], Directory.GetFiles(_tempDirectory).Select(Path.GetFileName));
    }
}
