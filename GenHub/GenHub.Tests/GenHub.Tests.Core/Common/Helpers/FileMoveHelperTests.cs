using GenHub.Common.Helpers;
using GenHub.Tests.Core.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// Verifies that a destination that exists when a locked file is moved is left untouched.
    /// </summary>
    [Fact]
    public void MoveWithoutResidue_WhenDestinationExistsAndSourceIsLocked_KeepsDestination()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "source");
        File.WriteAllText(destination, "other");
        ReadOnlyFolderFixtures.LockImmutable(source);

        var exception = Record.Exception(() => FileMoveHelper.MoveWithoutResidue(source, destination));

        Assert.True(exception is UnauthorizedAccessException or IOException, exception?.ToString());
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("other", File.ReadAllText(destination));
    }

    /// <summary>
    /// Verifies that when another writer creates the destination while the move runs, whichever side loses
    /// the race leaves the winner's file in place and nothing is deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task MoveWithoutResidue_WhenDestinationIsCreatedConcurrently_NeverRemovesTheOtherFileAsync()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var source = Path.Combine(_tempDirectory, $"Old{attempt}.map");
            var destination = Path.Combine(_tempDirectory, $"New{attempt}.map");
            File.WriteAllText(source, "source");
            using var start = new Barrier(2);

            var other = Task.Run(() =>
            {
                start.SignalAndWait();
                try
                {
                    using var stream = new FileStream(destination, System.IO.FileMode.CreateNew, FileAccess.Write);
                    stream.WriteByte((byte)'o');
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            });

            start.SignalAndWait();
            var moveException = Record.Exception(() => FileMoveHelper.MoveWithoutResidue(source, destination));
            var otherCreated = await other;

            if (moveException is null)
            {
                Assert.False(otherCreated);
                Assert.False(File.Exists(source));
                Assert.Equal("source", File.ReadAllText(destination));
            }
            else
            {
                Assert.True(otherCreated, moveException.ToString());
                Assert.Equal("source", File.ReadAllText(source));
                Assert.Equal("o", File.ReadAllText(destination));
            }
        }
    }

    /// <summary>
    /// Verifies that the link fallback moves an ordinary file.
    /// </summary>
    [Fact]
    public void MoveByLink_WithWritableFile_MovesFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "map");

        FileMoveHelper.MoveByLink(source, destination);

        Assert.Equal(["New.map"], Directory.GetFiles(_tempDirectory).Select(Path.GetFileName));
        Assert.Equal("map", File.ReadAllText(destination));
    }

    /// <summary>
    /// Verifies that the link fallback refuses an existing destination and leaves both files untouched.
    /// </summary>
    [Fact]
    public void MoveByLink_WhenDestinationExists_ThrowsAndKeepsBothFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "source");
        File.WriteAllText(destination, "other");

        Assert.Throws<IOException>(() => FileMoveHelper.MoveByLink(source, destination));

        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("other", File.ReadAllText(destination));
    }

    /// <summary>
    /// Verifies that the link fallback fails on a locked file without copying it, leaving only the source.
    /// </summary>
    [Fact]
    public void MoveByLink_WhenSourceIsLocked_ThrowsAndLeavesOnlySource()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var source = Path.Combine(_tempDirectory, "Old.map");
        var destination = Path.Combine(_tempDirectory, "New.map");
        File.WriteAllText(source, "map");
        ReadOnlyFolderFixtures.LockImmutable(source);

        var exception = Record.Exception(() => FileMoveHelper.MoveByLink(source, destination));

        Assert.True(exception is UnauthorizedAccessException or IOException, exception?.ToString());
        Assert.Equal(["Old.map"], Directory.GetFiles(_tempDirectory).Select(Path.GetFileName));
    }
}
