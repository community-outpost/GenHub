using GenHub.Core.Constants;
using GenHub.Features.Content.Services.GeneralsOnline;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineClientIdentifier"/> directory entry resolution and
/// companion detection: one directory of shipped binaries resolves to one client.
/// </summary>
public class GeneralsOnlineClientIdentifierTests : IDisposable
{
    private readonly GeneralsOnlineClientIdentifier _identifier = new();
    private readonly string _packageDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineClientIdentifierTests"/> class.
    /// </summary>
    public GeneralsOnlineClientIdentifierTests()
    {
        _packageDirectory = Path.Combine(Path.GetTempPath(), $"genhub-identifier-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_packageDirectory);
    }

    /// <summary>
    /// The bootstrapper wins whenever present; otherwise the 60Hz binary, then Unix.
    /// </summary>
    /// <param name="fileNames">The directory listing.</param>
    /// <param name="expected">The expected entry name.</param>
    [Theory]
    [InlineData(
        new[] { "generalsonlinezh_60.exe", "EAC_LaunchGeneralsOnline.exe", "libcurl.dll" },
        "EAC_LaunchGeneralsOnline.exe")]
    [InlineData(
        new[] { "generalsonlinezh_60.exe", "libcurl.dll" },
        "generalsonlinezh_60.exe")]
    [InlineData(
        new[] { "GeneralsOnlineZH", "libcurl.dylib" },
        "GeneralsOnlineZH")]
    [InlineData(
        new[] { "libcurl.dll", "GeneralsOnlineZH.exe" },
        null)]
    public void ResolveDirectoryEntryPoint_Precedence_ReturnsSingleEntry(string[] fileNames, string? expected)
    {
        Assert.Equal(expected, _identifier.ResolveDirectoryEntryPoint(fileNames));
    }

    /// <summary>
    /// Entry matching keeps the package's own casing for case-sensitive volumes.
    /// </summary>
    [Fact]
    public void ResolveDirectoryEntryPoint_MixedCase_PreservesDiskCasing()
    {
        var entry = _identifier.ResolveDirectoryEntryPoint(["GeneralsOnlineZH_60.exe"]);

        Assert.Equal("GeneralsOnlineZH_60.exe", entry);
    }

    /// <summary>
    /// Wrapped binaries beside a bootstrapper are companions, never scan candidates.
    /// </summary>
    [Fact]
    public void IsCompanionFile_BootstrapperDirectory_MarksWrappedBinaries()
    {
        var bootstrapper = WriteFile(GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        var sixtyHertz = WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        var unwrapped = WriteFile(GameClientConstants.GeneralsOnlineDefaultExecutable);

        Assert.False(_identifier.IsCompanionFile(bootstrapper));
        Assert.True(_identifier.IsCompanionFile(sixtyHertz));
        Assert.True(_identifier.IsCompanionFile(unwrapped));
    }

    /// <summary>
    /// A renamed wrapped binary is still a companion when the bootstrapper settings
    /// name it: renames need no hardcoded names to stay invisible to scans.
    /// </summary>
    [Fact]
    public void IsCompanionFile_SettingsNamedBinary_MarksCompanion()
    {
        WriteFile(GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        var renamed = WriteFile("GeneralsOnlineZH_TestEnvironment.exe");
        WriteSettingsJson("GeneralsOnlineZH_TestEnvironment.exe");

        Assert.True(_identifier.IsCompanionFile(renamed));
    }

    /// <summary>
    /// Without a bootstrapper beside it, a 60Hz binary is the entry itself, not content.
    /// </summary>
    [Fact]
    public void IsCompanionFile_PreEacDirectory_MarksNothing()
    {
        var sixtyHertz = WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);

        Assert.False(_identifier.IsCompanionFile(sixtyHertz));
    }

    /// <summary>
    /// Unknown files the settings do not name are left alone for generic inspection.
    /// </summary>
    [Fact]
    public void IsCompanionFile_UnrelatedBinary_MarksNothing()
    {
        WriteFile(GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        WriteSettingsJson(GameClientConstants.GeneralsOnline60HzExecutable);
        var unrelated = WriteFile("someone-elses-tool.exe");

        Assert.False(_identifier.IsCompanionFile(unrelated));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    private string WriteFile(string fileName)
    {
        var fullPath = Path.Combine(_packageDirectory, fileName);
        File.WriteAllText(fullPath, fileName);
        return fullPath;
    }

    private void WriteSettingsJson(string executable)
    {
        var settingsPath = Path.Combine(_packageDirectory, "EasyAntiCheat", "Settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{\"executable\": \"" + executable + "\"}");
    }
}
