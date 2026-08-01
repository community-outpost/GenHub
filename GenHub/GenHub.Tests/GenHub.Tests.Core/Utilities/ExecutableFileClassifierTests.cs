using System;
using System.IO;
using System.Text;
using GenHub.Core.Utilities;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Table-driven tests for <see cref="ExecutableFileClassifier"/>, which replaced five
/// disagreeing implementations of "is this executable".
/// </summary>
public class ExecutableFileClassifierTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("classifier-tests").FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

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
        Assert.Equal(expected, ExecutableFileClassifier.RequiresExecutePermissionFromName(path));
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
        Assert.Equal(expected, ExecutableFileClassifier.IsLegacyLaunchCandidateFromName(path));
    }

    /// <summary>
    /// The two questions must not be assumed equivalent. A dylib needs neither, a shell
    /// wrapper needs the execute bit but is not a launch target, and a native binary
    /// needs both. Collapsing them into one boolean is what this class exists to undo.
    /// </summary>
    [Fact]
    public void TheTwoQuestionsAreIndependent()
    {
        Assert.True(ExecutableFileClassifier.RequiresExecutePermissionFromName("run.sh"));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidateFromName("run.sh"));

        Assert.True(ExecutableFileClassifier.RequiresExecutePermissionFromName("generalszh"));
        Assert.True(ExecutableFileClassifier.IsLegacyLaunchCandidateFromName("generalszh"));

        Assert.False(ExecutableFileClassifier.RequiresExecutePermissionFromName("libSDL3.dylib"));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidateFromName("libSDL3.dylib"));
    }

    /// <summary>
    /// Verifies magic-byte recognition of every supported executable format, and
    /// rejection of everything else.
    /// </summary>
    /// <param name="header">The first bytes of a file.</param>
    /// <param name="expected">Whether the header denotes a native executable.</param>
    [Theory]

    // MZ (Windows PE), ELF (Linux).
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, true)]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00 }, true)]

    // Mach-O thin, 32- and 64-bit, both byte orders on disk.
    [InlineData(new byte[] { 0xFE, 0xED, 0xFA, 0xCE }, true)]
    [InlineData(new byte[] { 0xFE, 0xED, 0xFA, 0xCF }, true)]
    [InlineData(new byte[] { 0xCE, 0xFA, 0xED, 0xFE }, true)]
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE }, true)]

    // Mach-O universal (fat), 32- and 64-bit headers: second word is the architecture
    // count, byte-swapped alongside the swapped magics.
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x02 }, true)]
    [InlineData(new byte[] { 0xBE, 0xBA, 0xFE, 0xCA, 0x02, 0x00, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBF, 0x00, 0x00, 0x00, 0x02 }, true)]
    [InlineData(new byte[] { 0xBF, 0xBA, 0xFE, 0xCA, 0x02, 0x00, 0x00, 0x00 }, true)]

    // A Java class file shares the fat magic, but its second word is the class-file
    // version (>= 45); 0x34 is Java 8.
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x34 }, false)]

    // A fat magic with no second word cannot be confirmed as a fat binary.
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, false)]
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBF }, false)]

    // Text, shebang scripts, truncated headers, and nothing at all.
    [InlineData(new byte[] { 0x54, 0x68, 0x69, 0x73, 0x20, 0x69, 0x73 }, false)]
    [InlineData(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E, 0x2F }, false)]
    [InlineData(new byte[] { 0x4D, 0x5A }, false)]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C }, false)]
    [InlineData(new byte[] { }, false)]
    public void HasExecutableMagicBytes_ClassifiesHeaders(byte[] header, bool expected)
    {
        Assert.Equal(expected, ExecutableFileClassifier.HasExecutableMagicBytes(header));
    }

    /// <summary>
    /// An extensionless file whose content is text is exactly the false positive the
    /// name-only heuristic produced: a README is not a native binary.
    /// </summary>
    [Fact]
    public void ExtensionlessTextFile_IsNotClassifiedExecutable()
    {
        var readme = Path.Combine(_tempDirectory, "README");
        File.WriteAllText(readme, "This project is a mod for Zero Hour.\n");

        Assert.False(ExecutableFileClassifier.RequiresExecutePermission("README", readme));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidate("README", readme));
        Assert.False(ExecutableFileClassifier.HasExecutableMagicBytes(readme));
    }

    /// <summary>
    /// An extensionless shebang script needs the Unix execute bit, but is not native
    /// executable code and must not become a legacy inferred game entry point.
    /// </summary>
    [Fact]
    public void ExtensionlessShebangScript_RequiresPermissionButIsNotLaunchCandidate()
    {
        var script = Path.Combine(_tempDirectory, "launch");
        File.WriteAllText(script, "#!/bin/sh\nexec ./generalszh\n", Encoding.ASCII);

        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("launch", script));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidate("launch", script));
        Assert.False(ExecutableFileClassifier.HasExecutableMagicBytes(script));
    }

    /// <summary>
    /// Files shorter than any magic number must be rejected without throwing.
    /// </summary>
    /// <param name="content">The whole file content.</param>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x4D })]
    [InlineData(new byte[] { 0x4D, 0x5A })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C })]
    public void TinyFiles_AreRejectedWithoutThrowing(byte[] content)
    {
        var path = Path.Combine(_tempDirectory, $"tiny-{content.Length}");
        File.WriteAllBytes(path, content);

        Assert.False(ExecutableFileClassifier.RequiresExecutePermission(Path.GetFileName(path), path));
        Assert.False(ExecutableFileClassifier.HasExecutableMagicBytes(path));
    }

    /// <summary>
    /// An extensionless file that really is a native binary keeps both classifications,
    /// whichever platform's format it carries.
    /// </summary>
    /// <param name="header">The magic bytes to write.</param>
    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00 })]
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x02 })]
    [InlineData(new byte[] { 0xCA, 0xFE, 0xBA, 0xBF, 0x00, 0x00, 0x00, 0x02 })]
    public void ExtensionlessNativeBinary_IsClassifiedExecutable(byte[] header)
    {
        var path = Path.Combine(_tempDirectory, "generalszh");
        File.WriteAllBytes(path, header);

        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("generalszh", path));
        Assert.True(ExecutableFileClassifier.IsLegacyLaunchCandidate("GeneralsMD/Release/generalszh", path));
    }

    /// <summary>
    /// A missing or unreadable file cannot be confirmed as a binary and must not throw.
    /// </summary>
    [Fact]
    public void MissingFile_IsNotClassifiedExecutable()
    {
        var path = Path.Combine(_tempDirectory, "does-not-exist");

        Assert.False(ExecutableFileClassifier.HasExecutableMagicBytes(path));
        Assert.False(ExecutableFileClassifier.RequiresExecutePermission("does-not-exist", path));
    }

    /// <summary>
    /// Extension-based classification does not consult content: a library stays
    /// non-executable even though it is a real native image, and known runnable
    /// extensions do not require one.
    /// </summary>
    [Fact]
    public void ExtensionRules_AreUnchangedByContent()
    {
        var dylib = Path.Combine(_tempDirectory, "libSDL3.dylib");
        File.WriteAllBytes(dylib, [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01]);

        var script = Path.Combine(_tempDirectory, "run.sh");
        File.WriteAllText(script, "#!/bin/sh\nexec ./generalszh\n", Encoding.ASCII);

        Assert.False(ExecutableFileClassifier.RequiresExecutePermission("libSDL3.dylib", dylib));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidate("libSDL3.dylib", dylib));
        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("run.sh", script));
    }

    /// <summary>
    /// Callers that hold only a name (manifest entries, remote release assets) keep the
    /// legacy behaviour: extensionless means native binary.
    /// </summary>
    [Fact]
    public void NameOnlyClassification_KeepsLegacyExtensionlessBehaviour()
    {
        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("generalszh", null));
        Assert.True(ExecutableFileClassifier.IsLegacyLaunchCandidate("generalszh", null));
    }
}
