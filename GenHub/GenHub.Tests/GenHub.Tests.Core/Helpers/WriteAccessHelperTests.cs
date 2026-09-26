using GenHub.Core.Helpers;
using GenHub.Tests.Core.Infrastructure;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="WriteAccessHelper"/>.
/// </summary>
public sealed class WriteAccessHelperTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("write-access-tests").FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        ReadOnlyFolderFixtures.RestoreWritable(_tempDirectory);
        Directory.Delete(_tempDirectory, true);
    }

    /// <summary>
    /// Verifies that a read-only folder, its files and a nested read-only folder all become writable.
    /// </summary>
    [Fact]
    public void EnsureDirectoryWritable_WithReadOnlyTree_MakesEveryEntryWritable()
    {
        var folder = Path.Combine(_tempDirectory, "Map");
        var nested = Path.Combine(folder, "Nested");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(folder, "Map.map");
        var nestedFile = Path.Combine(nested, "Map.tga");
        File.WriteAllText(file, "map");
        File.WriteAllText(nestedFile, "tga");
        ReadOnlyFolderFixtures.MakeReadOnly(nested);
        ReadOnlyFolderFixtures.MakeReadOnly(folder);

        WriteAccessHelper.EnsureDirectoryWritable(folder);

        Assert.False(ReadOnlyFolderFixtures.IsReadOnly(folder));
        Assert.False(ReadOnlyFolderFixtures.IsReadOnly(file));
        Assert.False(ReadOnlyFolderFixtures.IsReadOnly(nested));
        Assert.False(ReadOnlyFolderFixtures.IsReadOnly(nestedFile));
    }

    /// <summary>
    /// Verifies that a symbolic link inside the folder is not followed, so its read-only target is left alone.
    /// </summary>
    [Fact]
    public void EnsureDirectoryWritable_WithSymbolicLinkToOutsideFolder_LeavesTargetReadOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outside = Path.Combine(_tempDirectory, "Outside");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "shared.ini");
        File.WriteAllText(outsideFile, "ini");
        ReadOnlyFolderFixtures.MakeReadOnly(outside);

        var folder = Path.Combine(_tempDirectory, "Map");
        Directory.CreateDirectory(folder);
        Directory.CreateSymbolicLink(Path.Combine(folder, "linked"), outside);
        File.CreateSymbolicLink(Path.Combine(folder, "shared.ini"), outsideFile);
        ReadOnlyFolderFixtures.MakeReadOnly(folder);

        WriteAccessHelper.EnsureDirectoryWritable(folder);

        Assert.False(ReadOnlyFolderFixtures.IsReadOnly(folder));
        Assert.True(ReadOnlyFolderFixtures.IsReadOnly(outside));
        Assert.True(ReadOnlyFolderFixtures.IsReadOnly(outsideFile));
    }

    /// <summary>
    /// Verifies that a missing folder is ignored.
    /// </summary>
    [Fact]
    public void EnsureDirectoryWritable_WithMissingFolder_DoesNothing()
    {
        WriteAccessHelper.EnsureDirectoryWritable(Path.Combine(_tempDirectory, "Missing"));

        Assert.False(Directory.Exists(Path.Combine(_tempDirectory, "Missing")));
    }

    /// <summary>
    /// Verifies that a folder whose permissions the owner cannot change reports the failure.
    /// </summary>
    [Fact]
    public void EnsureDirectoryWritable_WhenPermissionsAreLocked_Throws()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var folder = Path.Combine(_tempDirectory, "Locked");
        Directory.CreateDirectory(folder);
        ReadOnlyFolderFixtures.MakeReadOnly(folder);
        ReadOnlyFolderFixtures.LockImmutable(folder);

        var exception = Record.Exception(() => WriteAccessHelper.EnsureDirectoryWritable(folder));

        Assert.True(exception is UnauthorizedAccessException or IOException, exception?.ToString());
        Assert.True(ReadOnlyFolderFixtures.IsReadOnly(folder));
    }
}
