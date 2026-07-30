using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for <see cref="RetailArchiveClassifier"/>.
/// </summary>
/// <remarks>
/// Fixtures use the retail archive filenames a real installation holds: any
/// <c>*zh.big</c> marks Zero Hour data, any archive from the canonical Generals set
/// marks Generals data. An arbitrary <c>.big</c> proves neither.
/// </remarks>
public class RetailArchiveClassifierTests : IDisposable
{
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetailArchiveClassifierTests"/> class.
    /// </summary>
    public RetailArchiveClassifierTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("GenHub.ClassifierTests.").FullName;
    }

    /// <summary>
    /// A directory holding only Zero Hour archives classifies as Zero Hour alone.
    /// </summary>
    [Fact]
    public void ClassifyArchives_ZeroHourOnlyDirectory_IsZeroHourOnly()
    {
        var dir = CreateDirectoryWithArchives("zh-only", "INIZH.big", "AudioZH.big");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.True(classification.HasZeroHourArchives);
        Assert.False(classification.HasGeneralsArchives);
    }

    /// <summary>
    /// A directory holding only canonical Generals archives classifies as Generals alone.
    /// </summary>
    [Fact]
    public void ClassifyArchives_GeneralsOnlyDirectory_IsGeneralsOnly()
    {
        var dir = CreateDirectoryWithArchives("gen-only", "INI.big", "W3D.big", "Audio.big");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.True(classification.HasGeneralsArchives);
        Assert.False(classification.HasZeroHourArchives);
    }

    /// <summary>
    /// A combined flat directory — both games' archives side by side — classifies as both.
    /// This is the state the issue's acceptance criteria centre on: it is real, not a
    /// conflict, and must not collapse into Zero Hour alone.
    /// </summary>
    [Fact]
    public void ClassifyArchives_CombinedDirectory_IsBothGames()
    {
        var dir = CreateDirectoryWithArchives("combined", "INI.big", "INIZH.big");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.True(classification.HasGeneralsArchives);
        Assert.True(classification.HasZeroHourArchives);
        Assert.True(classification.HasAnyGame);
    }

    /// <summary>
    /// A directory without archives classifies as neither game.
    /// </summary>
    [Fact]
    public void ClassifyArchives_EmptyDirectory_IsNeitherGame()
    {
        var dir = CreateDirectoryWithArchives("empty");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.False(classification.HasGeneralsArchives);
        Assert.False(classification.HasZeroHourArchives);
        Assert.False(classification.HasAnyGame);
    }

    /// <summary>
    /// Archives that are neither Zero Hour suffixed nor in the canonical Generals set —
    /// mod content, hotkey packs, control bars — must not make a directory read as a game.
    /// An arbitrary <c>.big</c> proves nothing about retail data, which is why the
    /// launch-side any-archive sentinel cannot be reused for classification.
    /// </summary>
    [Fact]
    public void ClassifyArchives_ModArchivesOnly_IsNeitherGame()
    {
        var dir = CreateDirectoryWithArchives("mods-only", "somemod.big", "hotkeypack.big", "controlbarpro.big");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.False(classification.HasGeneralsArchives);
        Assert.False(classification.HasZeroHourArchives);
    }

    /// <summary>
    /// A nonexistent directory classifies as neither game rather than throwing.
    /// </summary>
    [Fact]
    public void ClassifyArchives_NonexistentDirectory_IsNeitherGame()
    {
        var missing = Path.Combine(_tempDir, "gone");

        Assert.False(RetailArchiveClassifier.ClassifyArchives(missing).HasAnyGame);
        Assert.False(RetailArchiveClassifier.ClassifyArchives(null).HasAnyGame);
    }

    /// <summary>
    /// Only the directory root is classified, never subdirectories: Data/INI/INIZH.big is
    /// a duplicate shipped in the English, Chinese and Korean SKUs and must not be
    /// counted twice.
    /// </summary>
    [Fact]
    public void ClassifyArchives_ArchiveInSubdirectory_IsNotCounted()
    {
        var dir = Path.Combine(_tempDir, "nested");
        var nested = Path.Combine(dir, "Data", "INI");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "INIZH.big"), "archive");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.False(classification.HasZeroHourArchives);
        Assert.False(classification.HasGeneralsArchives);
    }

    /// <summary>
    /// Upper-cased archives — retail data copied from a disc or a Windows machine — must
    /// still classify.
    /// </summary>
    /// <remarks>
    /// CAVEAT: this only exercises the case-insensitivity fix on a case-sensitive volume,
    /// in practice Linux CI. On macOS and Windows the default filesystem matching is
    /// already case-insensitive, so this test passes there whether or not
    /// <c>RetailArchiveConstants.ArchiveSearch</c> carries its <c>MatchCasing</c> setting —
    /// a local pass implies no coverage of the regression.
    /// </remarks>
    [Fact]
    public void ClassifyArchives_UpperCasedArchives_StillClassify()
    {
        var dir = CreateDirectoryWithArchives("upper", "INIZH.BIG", "TEXTURES.BIG");

        var classification = RetailArchiveClassifier.ClassifyArchives(dir);

        Assert.True(classification.HasZeroHourArchives);
        Assert.True(classification.HasGeneralsArchives);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    private string CreateDirectoryWithArchives(string name, params string[] archiveNames)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tempDir, name)).FullName;
        foreach (var archiveName in archiveNames)
        {
            File.WriteAllText(Path.Combine(dir, archiveName), "archive");
        }

        return dir;
    }
}
