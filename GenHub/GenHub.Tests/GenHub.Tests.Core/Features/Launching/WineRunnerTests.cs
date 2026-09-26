using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"), [$"missing-wine-{Guid.NewGuid():N}"]);
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
    /// Verifies an explicit runner binary override wins over PATH discovery.
    /// </summary>
    /// <remarks>
    /// Power users point GenHub at alternate runtimes (Proton builds, custom Wine)
    /// through the override without changing the default discovery.
    /// </remarks>
    [Fact]
    public void ResolveCommand_WithWineBinaryOverride_UsesOverride()
    {
        // Arrange
        var binDirectory = CreateDirectory("override-bin");
        var overridePath = Path.Combine(binDirectory, "custom-wine");
        File.WriteAllText(overridePath, "fake wine");
        var previous = Environment.GetEnvironmentVariable(WineConstants.WineBinaryOverrideEnvVar);
        Environment.SetEnvironmentVariable(WineConstants.WineBinaryOverrideEnvVar, overridePath);
        try
        {
            var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"), [$"missing-wine-{Guid.NewGuid():N}"]);
            var configuration = new GameLaunchConfiguration
            {
                ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            };

            // Act
            var result = runner.ResolveCommand(configuration);

            // Assert
            Assert.True(result.Success, result.AllErrors);
            Assert.NotNull(result.Data);
            Assert.Equal(overridePath, result.Data.FileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WineConstants.WineBinaryOverrideEnvVar, previous);
        }
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
    /// Verifies the native Options.ini is mirrored into both prefix user documents folders.
    /// Plain Wine reads "My Documents" while Proton reads "Documents", and a fresh managed
    /// prefix has neither, so guessing one would silently drop the profile's settings.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithNativeOptionsIni_MirrorsItIntoBothPrefixUserDocuments()
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
        Assert.Equal(2, mirrored.Length);
        Assert.All(mirrored, mirroredPath => Assert.Equal("native-settings", File.ReadAllText(mirroredPath)));
        Assert.Contains(mirrored, mirroredPath => mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName));
        Assert.Contains(
            mirrored,
            mirroredPath => !mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName) && mirroredPath.Contains(WineConstants.DocumentsDirectoryName));
    }

    /// <summary>
    /// Verifies that custom maps and user data folders in the native user data directory are
    /// bridged into the Wine prefix user documents folders.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithCustomMapsInNativeUserData_BridgesMapsIntoWinePrefix()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-custom-maps");
        var runner = CreateRunner([binDirectory], prefixPath);

        var nativeUserData = CreateDirectory("userdata-maps");
        var nativeOptionsPath = Path.Combine(nativeUserData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "resolution = 1920 1080");

        var nativeMapDir = Path.Combine(nativeUserData, GameSettingsConstants.FolderNames.Maps, "Tournament Desert");
        Directory.CreateDirectory(nativeMapDir);
        var mapFilePath = Path.Combine(nativeMapDir, "Tournament Desert.map");
        File.WriteAllText(mapFilePath, "map-data-content");

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

        var docsMapPath = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            GameSettingsConstants.FolderNames.Maps,
            "Tournament Desert",
            "Tournament Desert.map");

        var myDocsMapPath = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.MyDocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            GameSettingsConstants.FolderNames.Maps,
            "Tournament Desert",
            "Tournament Desert.map");

        Assert.True(File.Exists(docsMapPath), $"Expected map file at {docsMapPath}");
        Assert.True(File.Exists(myDocsMapPath), $"Expected map file at {myDocsMapPath}");
        Assert.Equal("map-data-content", File.ReadAllText(docsMapPath));
        Assert.Equal("map-data-content", File.ReadAllText(myDocsMapPath));
    }

    /// <summary>
    /// Verifies that user data bridging maintains strict separation between Generals and Zero Hour directories.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithUserDataDirectories_SeparatesGeneralsAndZeroHour()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-separation");
        var runner = CreateRunner([binDirectory], prefixPath);

        var nativeGeneralsData = CreateDirectory("userdata-generals");
        var generalsOptionsPath = Path.Combine(nativeGeneralsData, "Options.ini");
        File.WriteAllText(generalsOptionsPath, "generals-settings");

        var generalsMapDir = Path.Combine(nativeGeneralsData, GameSettingsConstants.FolderNames.Maps, "GeneralsMap");
        Directory.CreateDirectory(generalsMapDir);
        File.WriteAllText(Path.Combine(generalsMapDir, "GeneralsMap.map"), "generals-map");

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.Generals,
            NativeOptionsIniPath = generalsOptionsPath,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);

        var generalsPrefixDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.GeneralsDataDirectoryName);

        var zeroHourPrefixDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName);

        Assert.True(Directory.Exists(generalsPrefixDir), "Generals data directory should exist");
        Assert.False(Directory.Exists(zeroHourPrefixDir), "Zero Hour data directory should NOT exist when launching Generals");
        Assert.True(File.Exists(Path.Combine(generalsPrefixDir, GameSettingsConstants.FolderNames.Maps, "GeneralsMap", "GeneralsMap.map")));
    }

    /// <summary>
    /// Verifies that when only the standard Documents folder exists in the prefix, the missing
    /// My Documents folder is created and Options.ini is mirrored into both.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithOnlyDocumentsDirectoryExisting_MirrorsIntoBothDocumentsFolders()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-docs-only");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-docs"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "docs-only-settings");

        var userDocs = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName);
        Directory.CreateDirectory(userDocs);

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
        Assert.Equal(2, mirrored.Length);
        Assert.All(mirrored, mirroredPath => Assert.Equal("docs-only-settings", File.ReadAllText(mirroredPath)));
        Assert.Contains(mirrored, mirroredPath => mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName));
        Assert.Contains(
            mirrored,
            mirroredPath => !mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName) && mirroredPath.Contains(WineConstants.DocumentsDirectoryName));
    }

    /// <summary>
    /// Verifies that when only the legacy My Documents folder exists in the prefix, the missing
    /// Documents folder is created and Options.ini is mirrored into both.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithOnlyLegacyMyDocumentsExisting_MirrorsIntoBothDocumentsFolders()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-legacy-docs");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-legacy"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "legacy-settings");

        var userDocs = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.MyDocumentsDirectoryName);
        Directory.CreateDirectory(userDocs);

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
        Assert.Equal(2, mirrored.Length);
        Assert.All(mirrored, mirroredPath => Assert.Equal("legacy-settings", File.ReadAllText(mirroredPath)));
        Assert.Contains(mirrored, mirroredPath => mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName));
        Assert.Contains(
            mirrored,
            mirroredPath => !mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName) && mirroredPath.Contains(WineConstants.DocumentsDirectoryName));
    }

    /// <summary>
    /// Verifies that when both Documents and My Documents exist, Options.ini is mirrored into both.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithBothDocumentsAndMyDocumentsExisting_MirrorsIntoBoth()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-both-docs");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-both"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "both-settings");

        var userDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName);
        Directory.CreateDirectory(Path.Combine(userDir, WineConstants.DocumentsDirectoryName));
        Directory.CreateDirectory(Path.Combine(userDir, WineConstants.MyDocumentsDirectoryName));

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
        Assert.Equal(2, mirrored.Length);
        Assert.All(mirrored, mirroredPath => Assert.Equal("both-settings", File.ReadAllText(mirroredPath)));
        Assert.Contains(mirrored, mirroredPath => mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName));
        Assert.Contains(
            mirrored,
            mirroredPath => !mirroredPath.Contains(WineConstants.MyDocumentsDirectoryName) && mirroredPath.Contains(WineConstants.DocumentsDirectoryName));
    }

    /// <summary>
    /// Verifies a newer prefix Options.ini (for example edited in-game) is never overwritten,
    /// in either documents folder.
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
        var mirroredPaths = Directory.GetFiles(prefixPath, "Options.ini", SearchOption.AllDirectories);
        Assert.Equal(2, mirroredPaths.Length);
        foreach (var mirroredPath in mirroredPaths)
        {
            File.WriteAllText(mirroredPath, "in-game-settings");
            File.SetLastWriteTimeUtc(mirroredPath, DateTime.UtcNow.AddHours(1));
        }

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.All(mirroredPaths, mirroredPath => Assert.Equal("in-game-settings", File.ReadAllText(mirroredPath)));
    }

    /// <summary>
    /// Verifies that in a Proton prefix (e.g. Steam compatdata), Options.ini is mirrored
    /// into steamuser's Documents and My Documents folders.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithProtonPrefix_MirrorsIntoSteamUserDocumentsFolders()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "steamapps", "compatdata", "1273270", "pfx");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-proton"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "proton-settings");
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
        var steamUserDocs = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            WineConstants.ProtonUserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            "Options.ini");
        var steamUserMyDocs = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            WineConstants.ProtonUserName,
            WineConstants.MyDocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            "Options.ini");

        Assert.True(File.Exists(steamUserDocs));
        Assert.True(File.Exists(steamUserMyDocs));
        Assert.Equal("proton-settings", File.ReadAllText(steamUserDocs));
        Assert.Equal("proton-settings", File.ReadAllText(steamUserMyDocs));
    }

    /// <summary>
    /// Verifies that Proton's tracked-files marker is probed in the compatdata root next to
    /// the prefix directory: a prefix path without "compatdata" still mirrors into
    /// steamuser's folders when the marker sits in the parent directory.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithTrackedFilesNextToPrefix_MirrorsIntoSteamUserDocumentsFolders()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var compatRoot = CreateDirectory("compat-root");
        File.WriteAllText(Path.Combine(compatRoot, WineConstants.ProtonTrackedFilesMarker), "tracked");
        var prefixPath = Path.Combine(compatRoot, "pfx");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-tracked"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "tracked-settings");
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
        var steamUserDocs = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            WineConstants.ProtonUserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            "Options.ini");

        Assert.True(File.Exists(steamUserDocs));
        Assert.Equal("tracked-settings", File.ReadAllText(steamUserDocs));
    }

    /// <summary>
    /// Verifies that if the native Options.ini does not exist on disk, a baseline Options.ini
    /// is bootstrapped using the configured resolution arguments and mirrored into the prefix.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithMissingNativeOptionsIni_BootstrapsBaselineOptionsIni()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-bootstrap");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-bootstrap"), "Options.ini");
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
            Arguments = new Dictionary<string, string>
            {
                ["-xres"] = "1920",
                ["-yres"] = "1080",
            },
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.True(File.Exists(nativeOptionsPath));
        var content = File.ReadAllText(nativeOptionsPath);
        Assert.Contains("Resolution = 1920 1080", content);

        var mirrored = Directory.GetFiles(prefixPath, "Options.ini", SearchOption.AllDirectories);
        Assert.NotEmpty(mirrored);
        Assert.All(mirrored, path => Assert.Contains("Resolution = 1920 1080", File.ReadAllText(path)));
    }

    /// <summary>
    /// Verifies that configuration environment variables are forwarded to the resolved Wine command.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithCustomEnvironmentVariables_PreservesThem()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-env");
        var runner = CreateRunner([binDirectory], prefixPath);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["CNC_GENERALS_INSTALLPATH"] = @"C:\Generals",
                ["CUSTOM_LAUNCH_FLAG"] = "1",
            },
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(@"C:\Generals", result.Data.EnvironmentVariables["CNC_GENERALS_INSTALLPATH"]);
        Assert.Equal("1", result.Data.EnvironmentVariables["CUSTOM_LAUNCH_FLAG"]);
        Assert.Equal(prefixPath, result.Data.EnvironmentVariables[WineConstants.PrefixEnvironmentVariable]);
    }

    /// <summary>
    /// Verifies that when d3d8.dll exists in the working directory, WINEDLLOVERRIDES is set to d3d8=n,b.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithDirect3DWrapper_ConfiguresWineDllOverrides()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var gameDir = CreateDirectory("game-d3d8");
        File.WriteAllText(Path.Combine(gameDir, GameClientConstants.Direct3D8WrapperDll), "fake d3d8 wrapper");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-d3d8");
        var runner = CreateRunner([binDirectory], prefixPath);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(gameDir, "generals.exe"),
            WorkingDirectory = gameDir,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(WineConstants.Direct3D8DllOverride, result.Data.EnvironmentVariables[WineConstants.DllOverridesEnvironmentVariable]);
    }

    /// <summary>
    /// Verifies that existing DLL overrides are preserved and d3d8=n,b is appended when d3d8.dll is present.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithExistingDllOverridesAndDirect3DWrapper_AppendsOverride()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var gameDir = CreateDirectory("game-d3d8-existing");
        File.WriteAllText(Path.Combine(gameDir, GameClientConstants.Direct3D8WrapperDll), "fake d3d8 wrapper");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-d3d8-append");
        var runner = CreateRunner([binDirectory], prefixPath);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(gameDir, "generals.exe"),
            WorkingDirectory = gameDir,
            EnvironmentVariables = new Dictionary<string, string>
            {
                [WineConstants.DllOverridesEnvironmentVariable] = "mshtml=d",
            },
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal($"mshtml=d;{WineConstants.Direct3D8DllOverride}", result.Data.EnvironmentVariables[WineConstants.DllOverridesEnvironmentVariable]);
    }

    /// <summary>
    /// Verifies that an override for a different DLL whose name merely contains d3d8 does not
    /// suppress the wrapper override: d3d8 is matched as an exact DLL name, not a substring.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithD3D8SubstringOverrideAndDirect3DWrapper_AppendsOverride()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var gameDir = CreateDirectory("game-d3d8-proxy");
        File.WriteAllText(Path.Combine(gameDir, GameClientConstants.Direct3D8WrapperDll), "fake d3d8 wrapper");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-d3d8-proxy");
        var runner = CreateRunner([binDirectory], prefixPath);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(gameDir, "generals.exe"),
            WorkingDirectory = gameDir,
            EnvironmentVariables = new Dictionary<string, string>
            {
                [WineConstants.DllOverridesEnvironmentVariable] = "d3d8proxy=n",
            },
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal($"d3d8proxy=n;{WineConstants.Direct3D8DllOverride}", result.Data.EnvironmentVariables[WineConstants.DllOverridesEnvironmentVariable]);
    }

    /// <summary>
    /// Verifies that a user override naming d3d8 inside a comma-separated DLL group is respected
    /// and the wrapper override is not appended a second time.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithGroupedD3D8OverrideAndDirect3DWrapper_PreservesOverride()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var gameDir = CreateDirectory("game-d3d8-grouped");
        File.WriteAllText(Path.Combine(gameDir, GameClientConstants.Direct3D8WrapperDll), "fake d3d8 wrapper");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-d3d8-grouped");
        var runner = CreateRunner([binDirectory], prefixPath);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(gameDir, "generals.exe"),
            WorkingDirectory = gameDir,
            EnvironmentVariables = new Dictionary<string, string>
            {
                [WineConstants.DllOverridesEnvironmentVariable] = "mshtml=d;D3D8,ddraw=n",
            },
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal("mshtml=d;D3D8,ddraw=n", result.Data.EnvironmentVariables[WineConstants.DllOverridesEnvironmentVariable]);
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
        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"), [$"missing-wine-{Guid.NewGuid():N}"]);

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

    /// <summary>
    /// Verifies that WineRunner respects the priority order of BinaryNames across all search directories.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithMultipleBinaryNames_PrefersPrimaryBinaryNameAcrossSearchDirectories()
    {
        // Arrange
        var dir1 = CreateDirectory("dir1");
        var dir2 = CreateDirectory("dir2");
        File.WriteAllText(Path.Combine(dir1, "wine64"), "fake wine64");
        File.WriteAllText(Path.Combine(dir2, "wine"), "fake wine");

        var options = new WineRunnerOptions(
            BinaryNames: ["wine", "wine64"],
            AbsoluteBinaryPaths: [],
            ExtraSearchDirectories: [dir1, dir2],
            PrefixPath: Path.Combine(_tempDirectory, "prefix"));
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
        Assert.Equal(Path.Combine(dir2, "wine"), result.Data.FileName);
    }

    /// <summary>
    /// Verifies that QuoteArgument properly escapes embedded quotes in the executable path.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithQuotedExecutablePath_EscapesEmbeddedQuotes()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");

        var runner = CreateRunner([binDirectory], Path.Combine(_tempDirectory, "prefix"));
        var weirdExePath = Path.Combine(_tempDirectory, "game", "mod \"v1\"", "game.exe");
        var configuration = new GameLaunchConfiguration { ExecutablePath = weirdExePath };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        var expectedEscapedPath = $"\"{weirdExePath.Replace("\"", "\\\"")}\"";
        Assert.Equal(expectedEscapedPath, result.Data.ArgumentPrefix);
    }

    /// <summary>
    /// Verifies that flatpak bundles fail with guidance: they need install-then-run
    /// outside the pipeline, so no direct launch command is constructed.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithFlatpakBundle_FailsWithGuidance()
    {
        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));
        var flatpakPath = Path.Combine(_tempDirectory, "game.flatpak");
        var configuration = new GameLaunchConfiguration { ExecutablePath = flatpakPath };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert: every host fails via the Flatpak guidance branch before cross-OS validation.
        Assert.False(result.Success);
        Assert.Contains("launch", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a Flatpak bundle with an embedded ref fails with guidance naming
    /// the real application ID and the exact install-then-run commands.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithFlatpakBundleContainingRef_NamesAppId()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));
        var flatpakPath = Path.Combine(_tempDirectory, "game.flatpak");
        var header = new byte[512];
        Encoding.ASCII.GetBytes("flatpak").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("app/com.example.GameClient/x86_64/stable").CopyTo(header, 64);
        File.WriteAllBytes(flatpakPath, header);
        var configuration = new GameLaunchConfiguration { ExecutablePath = flatpakPath };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("com.example.GameClient", result.FirstError);
        Assert.Contains("flatpak run", result.FirstError);
    }

    /// <summary>
    /// Verifies that macOS bundles fail on Linux.
    /// </summary>
    [Fact]
    public void ResolveCommand_MacOsBundleOnLinux_Fails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));
        var appPath = Path.Combine(_tempDirectory, "ZeroHour.app");
        var configuration = new GameLaunchConfiguration { ExecutablePath = appPath };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("macOS", result.FirstError);
    }

    /// <summary>
    /// Verifies the native Network.ini (LAN nickname) is mirrored into both
    /// prefix user documents folders, so the game reads the synced player name.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithNativeNetworkIni_MirrorsItIntoBothPrefixUserDocuments()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-network");
        var runner = CreateRunner([binDirectory], prefixPath);
        var userData = CreateDirectory("userdata-network");
        var nativeOptionsPath = Path.Combine(userData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "Resolution=1920 1080");
        var nativeNetworkPath = Path.Combine(userData, "Network.ini");
        File.WriteAllText(nativeNetworkPath, "UserName = TestPlayer");
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
            NativeNetworkIniPath = nativeNetworkPath,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        var mirrored = Directory.GetFiles(prefixPath, "Network.ini", SearchOption.AllDirectories);
        Assert.Equal(2, mirrored.Length);
        Assert.All(mirrored, mirroredPath => Assert.Equal("UserName = TestPlayer", File.ReadAllText(mirroredPath)));
    }

    /// <summary>
    /// Verifies no Network.ini is created in the prefix when no native
    /// Network.ini exists (no nickname was ever synced).
    /// </summary>
    [Fact]
    public void ResolveCommand_WithoutNativeNetworkIni_CreatesNoNetworkIni()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-no-network");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-no-network"), "Options.ini");
        File.WriteAllText(nativeOptionsPath, "Resolution=1920 1080");
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
        Assert.Empty(Directory.GetFiles(prefixPath, "Network.ini", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Verifies a prefix Network.ini newer than the native one is kept, so a
    /// name the game itself just wrote is never clobbered by a stale sync.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithNewerPrefixNetworkIni_KeepsPrefixContents()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-network-newer");
        var runner = CreateRunner([binDirectory], prefixPath);
        var userData = CreateDirectory("userdata-network-newer");
        var nativeOptionsPath = Path.Combine(userData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "Resolution=1920 1080");
        var nativeNetworkPath = Path.Combine(userData, "Network.ini");
        File.WriteAllText(nativeNetworkPath, "UserName = StaleName");
        File.SetLastWriteTimeUtc(nativeNetworkPath, DateTime.UtcNow - TimeSpan.FromHours(1));
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
            NativeNetworkIniPath = nativeNetworkPath,
        };

        var first = runner.ResolveCommand(configuration);
        Assert.True(first.Success, first.AllErrors);
        var mirrored = Directory.GetFiles(prefixPath, "Network.ini", SearchOption.AllDirectories);
        Assert.Equal(2, mirrored.Length);
        foreach (var mirroredPath in mirrored)
        {
            File.WriteAllText(mirroredPath, "UserName = InGameName");
        }

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.All(
            Directory.GetFiles(prefixPath, "Network.ini", SearchOption.AllDirectories),
            mirroredPath => Assert.Equal("UserName = InGameName", File.ReadAllText(mirroredPath)));
    }

    /// <summary>
    /// Verifies the overlay IP preselected by Online Play (both network keys)
    /// survives the prefix mirror verbatim, so the launched game binds the
    /// lobby IP without any manual adapter choice.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithOverlayIpInNativeOptionsIni_MirrorsBothIpKeysVerbatim()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-overlay-ip");
        var runner = CreateRunner([binDirectory], prefixPath);
        var nativeOptionsPath = Path.Combine(CreateDirectory("userdata-overlay-ip"), "Options.ini");
        File.WriteAllText(
            nativeOptionsPath,
            "SFXVolume=70\nResolution=1920 1080\nGameSpyIPAddress=10.42.0.7\nIPAddress=10.42.0.7\n");
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
        Assert.Equal(2, mirrored.Length);
        Assert.All(mirrored, mirroredPath =>
        {
            var text = File.ReadAllText(mirroredPath);
            Assert.Contains("GameSpyIPAddress=10.42.0.7", text);
            Assert.Contains("IPAddress=10.42.0.7", text);
        });
    }

    /// <summary>
    /// Verifies that when a prefix user-data subdirectory already exists as a non-empty directory,
    /// its contents are migrated to the native side and bridged.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithPreExistingNonEmptyPrefixDirectory_MigratesFilesAndBridges()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-migration");
        var runner = CreateRunner([binDirectory], prefixPath);

        var nativeZeroHourData = CreateDirectory("userdata-migration-zh");
        var nativeOptionsPath = Path.Combine(nativeZeroHourData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "zh-settings");

        var nativeMapDir = Path.Combine(nativeZeroHourData, GameSettingsConstants.FolderNames.Maps, "NativeMap");
        Directory.CreateDirectory(nativeMapDir);
        File.WriteAllText(Path.Combine(nativeMapDir, "NativeMap.map"), "native-map-content");

        // Pre-create non-empty prefix Maps directory
        var prefixMapsDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            GameSettingsConstants.FolderNames.Maps);
        var oldPrefixMapDir = Path.Combine(prefixMapsDir, "OldPrefixMap");
        Directory.CreateDirectory(oldPrefixMapDir);
        File.WriteAllText(Path.Combine(oldPrefixMapDir, "OldPrefixMap.map"), "prefix-map-content");

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

        // Native side must now have the migrated map from prefix
        var migratedNativeMapPath = Path.Combine(nativeZeroHourData, GameSettingsConstants.FolderNames.Maps, "OldPrefixMap", "OldPrefixMap.map");
        Assert.True(File.Exists(migratedNativeMapPath), "OldPrefixMap should have migrated to native directory");
        Assert.Equal("prefix-map-content", File.ReadAllText(migratedNativeMapPath));

        // Native map should still exist
        var nativeMapPath = Path.Combine(nativeMapDir, "NativeMap.map");
        Assert.True(File.Exists(nativeMapPath), "NativeMap should still exist in native directory");

        // Both maps must exist in the prefix (either through symlink or sync)
        Assert.True(File.Exists(Path.Combine(prefixMapsDir, "NativeMap", "NativeMap.map")));
        Assert.True(File.Exists(Path.Combine(prefixMapsDir, "OldPrefixMap", "OldPrefixMap.map")));
    }

    /// <summary>
    /// Verifies that deletions on native or prefix side propagate and are not resurrected on subsequent launches.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithFileDeletions_PropagatesDeletionsWithoutResurrection()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-sync-deletions");
        var runner = CreateRunner([binDirectory], prefixPath);

        var nativeZeroHourData = CreateDirectory("userdata-deletions-zh");
        var nativeOptionsPath = Path.Combine(nativeZeroHourData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "zh-settings");

        var mapADir = Path.Combine(nativeZeroHourData, GameSettingsConstants.FolderNames.Maps, "MapA");
        var mapBDir = Path.Combine(nativeZeroHourData, GameSettingsConstants.FolderNames.Maps, "MapB");
        Directory.CreateDirectory(mapADir);
        Directory.CreateDirectory(mapBDir);
        File.WriteAllText(Path.Combine(mapADir, "MapA.map"), "map-a-content");
        File.WriteAllText(Path.Combine(mapBDir, "MapB.map"), "map-b-content");

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
        };

        // Act 1: Initial launch synchronizes both maps
        var result1 = runner.ResolveCommand(configuration);
        Assert.True(result1.Success, result1.AllErrors);

        var prefixMapsDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            GameSettingsConstants.FolderNames.Maps);

        var prefixMapAPath = Path.Combine(prefixMapsDir, "MapA", "MapA.map");
        var prefixMapBPath = Path.Combine(prefixMapsDir, "MapB", "MapB.map");
        var nativeMapAPath = Path.Combine(mapADir, "MapA.map");
        var nativeMapBPath = Path.Combine(mapBDir, "MapB.map");

        Assert.True(File.Exists(prefixMapAPath));
        Assert.True(File.Exists(prefixMapBPath));

        // Act 2: User deletes MapA from native side
        Directory.Delete(mapADir, recursive: true);
        Assert.False(File.Exists(nativeMapAPath));

        var result2 = runner.ResolveCommand(configuration);
        Assert.True(result2.Success, result2.AllErrors);

        // Assert 2: MapA must not be resurrected on native side, and must be deleted from prefix
        Assert.False(File.Exists(nativeMapAPath), "MapA should NOT be resurrected on native side");
        Assert.False(File.Exists(prefixMapAPath), "MapA should be removed from prefix side");
        Assert.True(File.Exists(nativeMapBPath), "MapB should remain on native side");
        Assert.True(File.Exists(prefixMapBPath), "MapB should remain on prefix side");

        // Act 3: User deletes MapB from prefix side (in all mirrored shell folders)
        var prefixMyDocsMapsDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.MyDocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            GameSettingsConstants.FolderNames.Maps);

        var prefixMapBDir = Path.Combine(prefixMapsDir, "MapB");
        if (Directory.Exists(prefixMapBDir))
        {
            Directory.Delete(prefixMapBDir, recursive: true);
        }

        var prefixMyDocsMapBDir = Path.Combine(prefixMyDocsMapsDir, "MapB");
        if (Directory.Exists(prefixMyDocsMapBDir))
        {
            Directory.Delete(prefixMyDocsMapBDir, recursive: true);
        }

        var result3 = runner.ResolveCommand(configuration);
        Assert.True(result3.Success, result3.AllErrors);

        // Assert 3: MapB must not be resurrected on prefix side, and must be deleted from native
        Assert.False(File.Exists(prefixMapBPath), "MapB should NOT be resurrected on prefix side");
        Assert.False(File.Exists(nativeMapBPath), "MapB should be removed from native side");
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

    /// <summary>
    /// A Windows binary without the <c>.exe</c> extension (Steam launches <c>game.dat</c>,
    /// which is a PE) routes through Wine by content sniffing instead of passing through
    /// to a native exec that can only fail with <c>ENOEXEC</c>.
    /// </summary>
    [Fact]
    public void ResolveCommand_NonExeWindowsBinary_UsesWine()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        var winePath = Path.Combine(binDirectory, "wine");
        File.WriteAllText(winePath, "fake wine");
        var runner = CreateRunner([binDirectory], Path.Combine(_tempDirectory, "prefix"));
        var dataDirectory = CreateDirectory("game");
        var datPath = Path.Combine(dataDirectory, "game.dat");
        File.WriteAllBytes(datPath, [(byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = datPath,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(winePath, result.Data.FileName);
        Assert.Contains("game.dat", result.Data.ArgumentPrefix);
    }

    /// <summary>
    /// A native Linux binary without the <c>.exe</c> extension keeps passing through
    /// directly, so content sniffing cannot reroute a native launch into Wine.
    /// </summary>
    [Fact]
    public void ResolveCommand_NativeLinuxBinary_PassesThrough()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var runner = CreateRunner([], Path.Combine(_tempDirectory, "prefix"));
        var binDirectory = CreateDirectory("native");
        var nativePath = Path.Combine(binDirectory, "generalszh");
        File.WriteAllBytes(nativePath, [0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00]);
        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = nativePath,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(nativePath, result.Data.FileName);
    }

    /// <summary>
    /// Verifies that WineRunner fallback directory synchronization ignores reparse points / directory symlinks
    /// without entering an infinite loop.
    /// </summary>
    [Fact]
    public void ResolveCommand_WithReparsePointInUserData_SkipsReparsePointDirectoryDuringSync()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-reparse-skip");
        var runner = CreateRunner([binDirectory], prefixPath);

        var nativeUserData = CreateDirectory("userdata-reparse");
        var nativeOptionsPath = Path.Combine(nativeUserData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "resolution = 1920 1080");

        var regularDir = Path.Combine(nativeUserData, GameSettingsConstants.FolderNames.Maps, "RegularMap");
        Directory.CreateDirectory(regularDir);
        File.WriteAllText(Path.Combine(regularDir, "RegularMap.map"), "map-content");

        // Create a directory symlink that points to its parent if supported in test environment
        var loopLink = Path.Combine(regularDir, "loop");
        try
        {
            Directory.CreateSymbolicLink(loopLink, regularDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // If symlinks cannot be created, skip symlink assertion
        }

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
        };

        // Create a non-empty real prefix Maps directory to force fallback synchronization via MirrorDirectoryContent
        var prefixMapsDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName,
            GameSettingsConstants.FolderNames.Maps);
        Directory.CreateDirectory(prefixMapsDir);
        File.WriteAllText(Path.Combine(prefixMapsDir, "existing.txt"), "pre-existing");

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        var synchronizedMapFile = Path.Combine(prefixMapsDir, "RegularMap", "RegularMap.map");
        Assert.True(File.Exists(synchronizedMapFile));
        if (Directory.Exists(loopLink))
        {
            var prefixMapsInfo = new DirectoryInfo(prefixMapsDir);
            var isSymlink = prefixMapsInfo.LinkTarget != null || (prefixMapsInfo.Attributes & FileAttributes.ReparsePoint) != 0;
            if (!isSymlink)
            {
                var loopDirInPrefix = Path.Combine(prefixMapsDir, "RegularMap", "loop");
                Assert.False(Directory.Exists(loopDirInPrefix));
            }
        }
    }

    /// <summary>
    /// Verifies that when a prefix user-data subdirectory target is an existing regular file,
    /// WineRunner preserves the file untouched and skips bridging rather than deleting it.
    /// </summary>
    [Fact]
    public void ResolveCommand_WhenPrefixSubdirectoryIsRegularFile_LeavesFileUntouched()
    {
        // Arrange
        var binDirectory = CreateDirectory("bin");
        File.WriteAllText(Path.Combine(binDirectory, "wine"), "fake wine");
        var prefixPath = Path.Combine(_tempDirectory, "prefix-file-preserve");
        var runner = CreateRunner([binDirectory], prefixPath);

        var nativeUserData = CreateDirectory("userdata-file-preserve");
        var nativeOptionsPath = Path.Combine(nativeUserData, "Options.ini");
        File.WriteAllText(nativeOptionsPath, "resolution = 1920 1080");

        var nativeMapDir = Path.Combine(nativeUserData, GameSettingsConstants.FolderNames.Maps, "TestMap");
        Directory.CreateDirectory(nativeMapDir);
        File.WriteAllText(Path.Combine(nativeMapDir, "TestMap.map"), "map-content");

        // Create a regular file in the prefix where Maps directory would normally be bridged
        var prefixDocsDir = Path.Combine(
            prefixPath,
            WineConstants.DriveCDirectoryName,
            WineConstants.PrefixUsersDirectoryName,
            Environment.UserName,
            WineConstants.DocumentsDirectoryName,
            MapManagerConstants.ZeroHourDataDirectoryName);
        Directory.CreateDirectory(prefixDocsDir);
        var conflictingFilePath = Path.Combine(prefixDocsDir, GameSettingsConstants.FolderNames.Maps);
        File.WriteAllText(conflictingFilePath, "important non-directory file content");

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_tempDirectory, "game", "generals.exe"),
            GameType = GameType.ZeroHour,
            NativeOptionsIniPath = nativeOptionsPath,
        };

        // Act
        var result = runner.ResolveCommand(configuration);

        // Assert: launch succeeds and the file is never overwritten or deleted
        Assert.True(result.Success, result.AllErrors);
        Assert.True(File.Exists(conflictingFilePath));
        Assert.Equal("important non-directory file content", File.ReadAllText(conflictingFilePath));
    }

    private WineRunner CreateRunner(
        string[] extraSearchDirectories,
        string prefixPath,
        string[]? binaryNames = null)
    {
        var options = new WineRunnerOptions(
            binaryNames ?? [WineConstants.WineBinaryName, WineConstants.Wine64BinaryName],
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
