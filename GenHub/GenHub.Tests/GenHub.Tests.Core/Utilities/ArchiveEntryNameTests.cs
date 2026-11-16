using GenHub.Core.Utilities;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Tests the screening applied to archive entry names before they become filesystem paths.
/// </summary>
public class ArchiveEntryNameTests
{
    /// <summary>
    /// Accepts the ordinary relative names archives are made of, including the traversal segments
    /// that the containment check rather than this screen is responsible for.
    /// </summary>
    /// <param name="entryName">The entry name under test.</param>
    [Theory]
    [InlineData("readme.txt")]
    [InlineData("patch/readme.txt")]
    [InlineData("patch\\readme.txt")]
    [InlineData("Bob's Map/bob.map")]
    [InlineData("patch/../readme.txt")]
    [InlineData("../escaped.big")]
    public void IsExtractable_AcceptsNamesThatCanNameAFile(string entryName)
    {
        Assert.True(ArchiveEntryName.IsExtractable(entryName));
    }

    /// <summary>
    /// Refuses names that cannot name a file. These are the dangerous ones: combined with the
    /// extraction directory they resolve to that directory itself, so the write would land on the
    /// directory rather than inside it, and the containment check sees nothing wrong.
    /// </summary>
    /// <param name="entryName">The entry name under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("patch/")]
    [InlineData("patch\\")]
    [InlineData("patch/ /readme.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("patch/.")]
    [InlineData("patch/..")]
    public void IsExtractable_RefusesNamesThatCannotNameAFile(string? entryName)
    {
        Assert.False(ArchiveEntryName.IsExtractable(entryName));
    }

    /// <summary>
    /// Refuses names the strictest supported host cannot represent, so an archive is extracted the
    /// same way everywhere. The colon matters most: on NTFS it names an alternate data stream, which
    /// writes content that ordinary directory listings never show.
    /// </summary>
    /// <param name="entryName">The entry name under test.</param>
    [Theory]
    [InlineData("readme.txt:stream")]
    [InlineData("patch/readme.txt:stream")]
    [InlineData("bad|name.dat")]
    [InlineData("bad<name.dat")]
    [InlineData("bad>name.dat")]
    [InlineData("bad?name.dat")]
    [InlineData("bad*name.dat")]
    [InlineData("bad\"name.dat")]
    [InlineData("bad\u0001name.dat")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("patch/LPT1.dat")]
    public void IsExtractable_RefusesNamesTheStrictestHostCannotRepresent(string entryName)
    {
        Assert.False(ArchiveEntryName.IsExtractable(entryName));
    }
}
