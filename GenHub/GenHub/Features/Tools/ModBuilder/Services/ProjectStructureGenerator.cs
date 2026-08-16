using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Generates complete project structure with folders, config files, and README files.
/// </summary>
public sealed class ProjectStructureGenerator(
    ILogger<ProjectStructureGenerator> logger) : IProjectStructureGenerator
{
    /// <inheritdoc/>
    public async Task GenerateProjectStructureAsync(string projectPath, CancellationToken cancellationToken)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            throw new ArgumentException("Invalid project path", nameof(projectPath));
        }

        logger.LogInformation("Generating project structure at {ProjectDir}", projectDir);

        await CreateFolderStructureAsync(projectDir, cancellationToken).ConfigureAwait(false);
        await CreateConfigFilesAsync(projectDir, cancellationToken).ConfigureAwait(false);
        await CreateReadmeFilesAsync(projectDir, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Project structure generated successfully");
    }

    private static async Task CreateFolderStructureAsync(string projectDir, CancellationToken cancellationToken)
    {
        var folders = new[]
        {
            ModBuilderConstants.GameFilesEditedDir,
            $"{ModBuilderConstants.GameFilesEditedDir}/Data",
            $"{ModBuilderConstants.GameFilesEditedDir}/Data/INI",
            $"{ModBuilderConstants.GameFilesEditedDir}/Data/Audio",
            $"{ModBuilderConstants.GameFilesEditedDir}/Data/Scripts",
            $"{ModBuilderConstants.GameFilesEditedDir}/Art",
            $"{ModBuilderConstants.GameFilesEditedDir}/Art/Textures",
            $"{ModBuilderConstants.GameFilesEditedDir}/Art/W3D",
            ModBuilderConstants.DefaultBuildDir,
            ModBuilderConstants.DefaultReleaseDir,
            ModBuilderConstants.ReleaseFilesDir,
            ModBuilderConstants.ResourcesDir,
            $"{ModBuilderConstants.ResourcesDir}/{ModBuilderConstants.FileHashRegistrySubdir}",
            ModBuilderConstants.ConfigDir
        };

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderPath = Path.Combine(projectDir, folder);
            Directory.CreateDirectory(folderPath);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async Task CreateConfigFilesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var configDir = Path.Combine(projectDir, ModBuilderConstants.ConfigDir);

        // create bundle items configuration file
        var bundleItemsConfig = new
        {
            BundleItems = new object[]
            {
                new
                {
                    Name = "MyTextures",
                    Type = "Texture",
                    SourceFiles = new[] { $"{ModBuilderConstants.GameFilesEditedDir}/Art/Textures/**/*.tga" },
                    OutputFormat = "DDS",
                    Compression = "DXT5"
                },
                new
                {
                    Name = "MyINI",
                    Type = "INI",
                    SourceFiles = new[] { $"{ModBuilderConstants.GameFilesEditedDir}/Data/INI/**/*.ini" },
                    OutputFormat = "INI"
                }
            }
        };

        var bundleItemsPath = Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);
        await WriteJsonFileAsync(bundleItemsPath, bundleItemsConfig, cancellationToken).ConfigureAwait(false);

        // create bundle packs configuration file
        var bundlePacksConfig = new
        {
            BundlePacks = new[]
            {
                new
                {
                    Name = "MyMod",
                    Items = new[] { "MyTextures", "MyINI" },
                    ItemNames = new[] { "MyTextures", "MyINI" },
                    AllowBuild = true,
                    AllowInstall = true,
                    OutputFile = $"{ModBuilderConstants.DefaultReleaseDir}/MyMod.big"
                }
            }
        };

        var bundlePacksPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);
        await WriteJsonFileAsync(bundlePacksPath, bundlePacksConfig, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateReadmeFilesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var readmeFiles = new[]
        {
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "INI", "README.txt"),
                "Place your INI files here.\n\nThese files will be processed and included in your mod.\nSupported formats: .ini"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Audio", "README.txt"),
                "Place your audio files here.\n\nSupported formats: .mp3, .wav"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Scripts", "README.txt"),
                "Place your script files here.\n\nSupported formats: .scb, .txt"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Art", "Textures", "README.txt"),
                "Place your texture files here.\n\nSupported formats:\n- .tga (Targa)\n- .psd (Photoshop)\n- .dds (DirectDraw Surface)\n\nTextures will be automatically converted to DDS format during build."
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Art", "W3D", "README.txt"),
                "Place your W3D model files here.\n\nSupported formats: .w3d"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.ReleaseFilesDir, "README.txt"),
                "Files placed here are copied as-is to the release folder.\n\nUse this for:\n- Documentation files\n- Installation instructions\n- License files\n- Any other static files that don't need processing"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.ConfigDir, "README.txt"),
                "Configuration Files\n\n" +
                $"{ModBuilderConstants.BundleItemsConfigFileName} - Defines individual bundle items (textures, INI files, etc.)\n" +
                $"{ModBuilderConstants.BundlePacksConfigFileName} - Defines bundle packs that combine multiple items\n\n" +
                "Edit these files to configure your mod's build process.\n" +
                "See documentation for detailed configuration options."
            )
        };

        foreach (var (path, content) in readmeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonFileAsync<T>(string path, T data, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            IoConstants.DefaultFileBufferSize,
            useAsync: true);

        await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken).ConfigureAwait(false);
    }
}
