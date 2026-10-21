// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Shared temporary-workspace host for the WorldBuilder catalog tests: loose-file
/// fixtures, VFS mounting, and subsystem loading. No real install is touched.
/// </summary>
public sealed class CatalogTestHost : IDisposable
{
    private readonly string _tempRoot;

    private CatalogTestHost(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    /// <summary>
    /// Creates a host rooted at a fresh temporary directory.
    /// </summary>
    /// <returns>The host.</returns>
    public static CatalogTestHost Create()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_CatalogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return new CatalogTestHost(tempRoot);
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
    /// Creates a named directory under the temporary root.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <returns>The full path.</returns>
    public string NewDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Writes a loose text fixture under a root, translating engine backslashes.
    /// </summary>
    /// <param name="root">The workspace root.</param>
    /// <param name="relativePath">The engine-style relative path.</param>
    /// <param name="contents">The file text.</param>
    public void WriteLoose(string root, string relativePath, string contents)
    {
        var full = Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
    }

    /// <summary>
    /// Writes a loose binary fixture under a root, translating engine backslashes.
    /// </summary>
    /// <param name="root">The workspace root.</param>
    /// <param name="relativePath">The engine-style relative path.</param>
    /// <param name="contents">The file bytes.</param>
    public void WriteLoose(string root, string relativePath, byte[] contents)
    {
        var full = Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, contents);
    }

    /// <summary>
    /// Writes a text file directly into a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="contents">The file text.</param>
    /// <returns>The full path.</returns>
    public string WriteFile(string directory, string fileName, string contents)
    {
        var full = Path.Combine(directory, fileName);
        File.WriteAllText(full, contents);
        return full;
    }

    /// <summary>
    /// Creates an unloaded SAGE INI database.
    /// </summary>
    /// <returns>The database.</returns>
    public SageIniDatabase CreateDatabase()
    {
        return new SageIniDatabase(
            new SageIniParser(NullLogger<SageIniParser>.Instance),
            NullLogger<SageIniDatabase>.Instance);
    }

    /// <summary>
    /// Mounts a workspace root and loads the subsystem boot table from it.
    /// </summary>
    /// <param name="workspace">The workspace root.</param>
    /// <returns>The mounted file system and loaded database.</returns>
    public async Task<(GameAssetFileSystem FileSystem, SageIniDatabase Database)> CreateLoadedDatabaseAsync(string workspace)
    {
        var fileSystem = new GameAssetFileSystem(
            Mock.Of<IGameInstallationService>(),
            NullLogger<GameAssetFileSystem>.Instance);
        var mounted = await fileSystem.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace));
        if (!mounted.Success)
        {
            throw new InvalidOperationException("Fixture workspace failed to mount: " + mounted.FirstError);
        }

        var database = CreateDatabase();
        var loaded = await database.LoadSubsystemsAsync(fileSystem);
        if (!loaded.Success)
        {
            throw new InvalidOperationException("Fixture subsystems failed to load: " + loaded.FirstError);
        }

        return (fileSystem, database);
    }

    /// <summary>
    /// Mounts a workspace root without loading subsystems.
    /// </summary>
    /// <param name="workspace">The workspace root.</param>
    /// <returns>The mounted file system.</returns>
    public async Task<GameAssetFileSystem> MountAsync(string workspace)
    {
        var fileSystem = new GameAssetFileSystem(
            Mock.Of<IGameInstallationService>(),
            NullLogger<GameAssetFileSystem>.Instance);
        var mounted = await fileSystem.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace));
        if (!mounted.Success)
        {
            throw new InvalidOperationException("Fixture workspace failed to mount: " + mounted.FirstError);
        }

        return fileSystem;
    }
}
