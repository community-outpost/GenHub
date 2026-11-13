using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="SageVirtualFileSystem"/> base allow-list filtering.
/// </summary>
public sealed class SageVirtualFileSystemFilteringTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="SageVirtualFileSystemFilteringTests"/> class.
    /// </summary>
    public SageVirtualFileSystemFilteringTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_VfsFilterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that base archives missing from the allow-list are not mounted.
    /// </summary>
    [Fact]
    public void Constructor_WithAllowList_SkipsForeignBaseArchives()
    {
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Retail.big"),
            ("Data\\INI\\GameData.ini", "GameData retail"));
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Foreign.big"),
            ("Data\\INI\\Object\\ModUnit.ini", "Object mod"));

        var vfs = new SageVirtualFileSystem(
            _tempRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            allowedBaseRelativePaths: ["Retail.big"]);

        Assert.NotNull(vfs.Read("Data\\INI\\GameData.ini"));
        Assert.Null(vfs.Read("Data\\INI\\Object\\ModUnit.ini"));
        Assert.DoesNotContain(
            vfs.GetMountedArchivesInOrder(),
            a => a.EndsWith("Foreign.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a null allow-list preserves the legacy mount-everything behavior.
    /// </summary>
    [Fact]
    public void Constructor_WithoutAllowList_MountsAllBaseArchives()
    {
        BigArchiveFixture.Write(
            Path.Combine(_tempRoot, "Anything.big"),
            ("Data\\INI\\GameData.ini", "GameData retail"));

        var vfs = new SageVirtualFileSystem(_tempRoot, isZeroHour: true, logger: Mock.Of<ILogger>());

        Assert.NotNull(vfs.Read("Data\\INI\\GameData.ini"));
    }

    /// <summary>
    /// Verifies that allow-list entries may use forward slashes for nested archives.
    /// </summary>
    [Fact]
    public void Constructor_WithForwardSlashAllowList_MountsNestedArchive()
    {
        var nestedDir = Path.Combine(_tempRoot, "Sub", "Dir");
        Directory.CreateDirectory(nestedDir);
        BigArchiveFixture.Write(
            Path.Combine(nestedDir, "Nested.big"),
            ("Data\\INI\\GameData.ini", "GameData retail"));

        var vfs = new SageVirtualFileSystem(
            _tempRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            allowedBaseRelativePaths: ["Sub/Dir/Nested.big"]);

        Assert.NotNull(vfs.Read("Data\\INI\\GameData.ini"));
    }

    /// <summary>
    /// Verifies that loose base files missing from the allow-list are hidden from reads and enumeration.
    /// </summary>
    [Fact]
    public void Read_WithAllowList_SkipsForeignLooseFiles()
    {
        var iniDir = Path.Combine(_tempRoot, "Data", "INI");
        Directory.CreateDirectory(iniDir);
        File.WriteAllText(Path.Combine(iniDir, "GameData.ini"), "GameData retail");
        File.WriteAllText(Path.Combine(iniDir, "Foreign.ini"), "Foreign drop-in");

        var vfs = new SageVirtualFileSystem(
            _tempRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            allowedBaseRelativePaths: ["Data\\INI\\GameData.ini"]);

        Assert.NotNull(vfs.Read("Data\\INI\\GameData.ini"));
        Assert.Null(vfs.Read("Data\\INI\\Foreign.ini"));
        Assert.DoesNotContain(
            vfs.FilesUnder("Data\\INI"),
            f => f.EndsWith("Foreign.ini", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that explicitly mounted sideloads bypass the base allow-list.
    /// </summary>
    [Fact]
    public void AddSideload_WithAllowList_MountsRegardlessOfSet()
    {
        var sidecar = Path.Combine(_tempRoot, "Sidecar.big");
        BigArchiveFixture.Write(sidecar, ("Data\\INI\\Object\\SideUnit.ini", "Object sidecar"));

        var vfs = new SageVirtualFileSystem(
            _tempRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            allowedBaseRelativePaths: []);
        vfs.AddSideload(sidecar);

        Assert.NotNull(vfs.Read("Data\\INI\\Object\\SideUnit.ini"));
    }

    /// <summary>
    /// Verifies that extensionless mod archives (e.g. CAS blobs) mount through AddModArchive.
    /// </summary>
    [Fact]
    public void AddModArchive_WithExtensionlessBlob_MountsArchive()
    {
        var blob = Path.Combine(_tempRoot, "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3");
        BigArchiveFixture.Write(blob, ("Data\\INI\\Object\\BlobUnit.ini", "Object blob"));

        var vfs = new SageVirtualFileSystem(
            _tempRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            allowedBaseRelativePaths: []);
        vfs.AddModArchive(blob);

        Assert.NotNull(vfs.Read("Data\\INI\\Object\\BlobUnit.ini"));
    }
}
