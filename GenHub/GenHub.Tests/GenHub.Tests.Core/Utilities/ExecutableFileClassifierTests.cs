using GenHub.Core.Utilities;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Table-driven tests for <see cref="ExecutableFileClassifier"/>, which replaced five
/// disagreeing implementations of "is this executable".
/// </summary>
public class ExecutableFileClassifierTests
{
    /// <summary>
    /// Verifies which files are marked as needing the Unix execute bit.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="expected">Whether the execute bit is required.</param>
    [Theory]

    // Native binaries have no extension. This is the case the old classifiers disagreed
    // on, and the one a native macOS or Linux game client depends on.
    [InlineData("generalszh", true)]
    [InlineData("GeneralsMD/Release/generalszh", true)]
    [InlineData("generals.exe", true)]
    [InlineData("run.sh", true)]
    [InlineData("Launch.command", true)]

    // Loadable code, mapped by the loader with read access. Marking these +x is
    // meaningless and, under a hard-link workspace, would mutate a shared CAS blob.
    [InlineData("libSDL3.dylib", false)]
    [InlineData("libbgfx.so", false)]
    [InlineData("d3d8.dll", false)]

    // Data, whatever the Steam layout does with it.
    [InlineData("game.dat", false)]
    [InlineData("INIZH.big", false)]
    [InlineData("Options.ini", false)]
    [InlineData("texture.tga", false)]
    [InlineData("", false)]
    public void RequiresExecutePermission_ClassifiesCorrectly(string path, bool expected)
    {
        Assert.Equal(expected, ExecutableFileClassifier.RequiresExecutePermission(path));
    }

    /// <summary>
    /// Verifies which files may serve as a launch target when no entry point is declared.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="expected">Whether the file is a plausible legacy launch target.</param>
    [Theory]
    [InlineData("generalszh", true)]
    [InlineData("generals.exe", true)]
    [InlineData("GeneralsOnlineZH_60.exe", true)]

    // A library is never launched, so it must not win a FirstOrDefault over the real
    // entry point simply by appearing earlier in the file list.
    [InlineData("libSDL3.dylib", false)]
    [InlineData("libbgfx.so", false)]
    [InlineData("d3d8.dll", false)]

    // Shell wrappers are runnable but are not what a profile launches; the engine binary is.
    [InlineData("run.sh", false)]
    [InlineData("game.dat", false)]
    [InlineData("", false)]
    public void IsLegacyLaunchCandidate_ClassifiesCorrectly(string path, bool expected)
    {
        Assert.Equal(expected, ExecutableFileClassifier.IsLegacyLaunchCandidate(path));
    }

    /// <summary>
    /// The two questions must not be assumed equivalent. A dylib needs neither, a shell
    /// wrapper needs the execute bit but is not a launch target, and a native binary
    /// needs both. Collapsing them into one boolean is what this class exists to undo.
    /// </summary>
    [Fact]
    public void TheTwoQuestionsAreIndependent()
    {
        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("run.sh"));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidate("run.sh"));

        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("generalszh"));
        Assert.True(ExecutableFileClassifier.IsLegacyLaunchCandidate("generalszh"));

        Assert.False(ExecutableFileClassifier.RequiresExecutePermission("libSDL3.dylib"));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidate("libSDL3.dylib"));
    }
}
