using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="WineRunner"/>.
/// </summary>
public sealed class WineRunnerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"GenHub-WineRunnerTests-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes a new instance of the <see cref="WineRunnerTests"/> class.
    /// </summary>
    public WineRunnerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Verifies resolving a Windows executable wraps it with the discovered Wine binary.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithWineInSearchDirectories_ReturnsWineCommand()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        var winePath = Path.Combine(binDirectory, "wine");
        File.WriteAllText(winePath, "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix");
        var runner = CreateRunner([binDirectory], prefixPath);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(winePath, result.Data.FileName);
        Assert.Contains("generals.exe", result.Data.ArgumentPrefix);
        Assert.Equal(prefixPath, result.Data.EnvironmentVariables[WineConstants.PrefixEnvironmentVariable]);
        Assert.True(Directory.Exists(prefixPath));
    }

    /// <summary>
    /// Verifies resolution fails when no Wine binary is discoverable.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithoutWine_ReturnsFailure()
    {
        // Arrange: a machine with Wine installed cannot observe the missing-runner path.
        if (WineBinaryExistsOnPath())
        {
            return;
        }

        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Wine", result.FirstError);
    }

    /// <summary>
    /// Verifies non-Windows executables pass through without requiring Wine.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithNonWindowsExecutable_PassesThroughDirectly()
    {
        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));
        var scriptPath = Path.Combine(_tempDirectory, "tools", "setup.sh");
        var configuration = new GameLaunchConfiguration { ExecutablePath = scriptPath };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(scriptPath, result.Data.FileName);
        Assert.Equal(string.Empty, result.Data.ArgumentPrefix);
        Assert.Empty(result.Data.EnvironmentVariables);
    }

    /// <summary>
    /// Verifies executable paths with spaces are quoted for the Wine command line.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithSpacedExecutablePath_QuotesArgumentPrefix()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var runner = CreateRunner([binDirectory], Path.Combine(_tempDirectory, "prefix"));
        var executablePath = Path.Combine(_tempDirectory, "my game", "generals.exe");
        var configuration = new GameLaunchConfiguration { ExecutablePath = executablePath };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal($"\"{executablePath}\"", result.Data.ArgumentPrefix);
    }

    /// <summary>
    /// Verifies absolute binary paths win over search-directory lookup.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithAbsoluteBinaryPath_PrefersItOverSearchDirectories()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "path wine");
        var absoluteWine = Path.Combine(CreateDirectory("custom"), "wine");
        File.WriteAllText(absoluteWine, "absolute wine");
        var options = new WineRunnerOptions(
            [WineConstants.WineBinaryName, WineConstants.Wine64BinaryName],
            [absoluteWine],
            [binDirectory],
            Path.Combine(_tempDirectory, "prefix"));
        var runner = new WineRunner(options, NullLogger<WineRunner>.Instance);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(absoluteWine, result.Data.FileName);
    }

    /// <summary>
    /// Verifies the native Options.ini is mirrored into the prefix user documents.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithNativeOptionsIni_MirrorsItIntoPrefixUserDocuments()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "native-settings");
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        var mirrored = Directory.GetFiles(prefixPath, "Options.ini", SearchOption.AllDirectories);
        var mirroredPath = Assert.Single(mirrored);
        Assert.Equal("native-settings", File.ReadAllText(mirroredPath));
        Assert.Contains(WineConstants.DriveCDirectoryName, mirroredPath);
        Assert.Contains(MapManagerConstants.ZeroHourDataDirectoryName, mirroredPath);
    }

    /// <summary>
    /// Verifies a newer prefix Options.ini (for example edited in-game) is never overwritten.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithNewerPrefixOptionsIni_PreservesIt()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "native-settings");
        File.SetLastWriteTimeUtc(nativeOptionsPath, DateTime.UtcNow.AddDays(-1));
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.Generals,
            NativeOptionsIniPath = nativeOptionsPath,
        };

        Assert.True(runner.ResolveCommand(configuration).Success);
        var mirroredPath = Assert.Single(Directory.GetFiles(prefixPath, "Options.ini", SearchOption.AllDirectories));
        File.WriteAllText(mirroredPath, "in-game-settings");
        File.SetLastWriteTimeUtc(mirroredPath, DateTime.UtcNow.AddHours(1));

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("in-game-settings", File.ReadAllText(mirroredPath));
    }

    /// <summary>
    /// Verifies availability reflects Wine presence.
    /// </summary>
    [Fact]
    public void CanLaunchWindowsExecutables_WithWine_ReturnsTrue()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine64"), "fake wine64");
        var runner = CreateRunner([binDirectory], Path.Combine(_tempDirectory, "prefix"));

        // Act and Assert
        Assert.True(runner.CanLaunchWindowsExecutables());
    }

    /// <summary>
    /// Verifies availability is false without Wine.
    /// </summary>
    [Fact]
    public void CanLaunchWindowsExecutables_WithoutWine_ReturnsFalse()
    {
        // Arrange: a machine with Wine installed cannot observe the missing-runner path.
        if (WineBinaryExistsOnPath())
        {
            return;
        }

        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));

        // Act and Assert
        Assert.False(runner.CanLaunchWindowsExecutables());
    }

    /// <summary>
    /// Verifies the Linux options carry the expected conventions.
    /// </summary>
    [Fact]
    public void WineRunnerOptions_Linux_UsesWineBinariesAndManagedPrefix()
    {
        // Act
        var options = WineRunnerOptions.Linux(_tempDirectory);

        // Assert
        Assert.Equal(
            new[] { WineConstants.WineBinaryName, WineConstants.Wine64BinaryName },
            options.BinaryNames);
        Assert.Empty(options.AbsoluteBinaryPaths);
        Assert.Equal(
            Path.Combine(_tempDirectory, WineConstants.ManagedPrefixDirectoryName),
            options.PrefixPath);
    }

    /// <summary>
    /// Verifies the macOS options add the CrossOver binary.
    /// </summary>
    [Fact]
    public void WineRunnerOptions_MacOS_IncludesCrossOverBinary()
    {
        // Act
        var options = WineRunnerOptions.MacOS(_tempDirectory);

        // Assert
        Assert.Equal(
            new[] { WineConstants.WineBinaryName, WineConstants.Wine64BinaryName },
            options.BinaryNames);
        Assert.Equal(new[] { WineConstants.CrossOverWineBinaryPath }, options.AbsoluteBinaryPaths);
        Assert.Equal(
            Path.Combine(_tempDirectory, WineConstants.ManagedPrefixDirectoryName),
            options.PrefixPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    private static bool WineBinaryExistsOnPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        if (string.IsNullOrEmpty(pathVariable))
        {
            return false;
        }

        return pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .SelectMany(directory => new[]
            {
                Path.Combine(directory, WineConstants.WineBinaryName),
                Path.Combine(directory, WineConstants.Wine64BinaryName),
            })
            .Any(File.Exists);
    }

    private WineRunner CreateRunner(string[] extraSearchDirectories, string prefixPath)
    {
        var options = new WineRunnerOptions(
            [WineConstants.WineBinaryName, WineConstants.Wine64BinaryName],
            [],
            extraSearchDirectories,
            prefixPath);
        return new WineRunner(options, NullLogger<WineRunner>.Instance);
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_tempDirectory, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
