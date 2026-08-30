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
    private const string ReadmeFileName = "README.txt";

    /// <inheritdoc/>
    public async Task GenerateProjectStructureAsync(string projectPath, CancellationToken cancellationToken)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            throw new ArgumentException("Invalid project path", nameof(projectPath));
        }

        await CreateFolderStructureAsync(projectDir, cancellationToken).ConfigureAwait(false);
        await CreateConfigFilesAsync(projectDir, cancellationToken).ConfigureAwait(false);
        await CreateSampleAssetsAsync(projectDir, cancellationToken).ConfigureAwait(false);
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
                    Name = "CoreINIPatch",
                    Type = "INI",
                    SourceFiles = new[] { $"{ModBuilderConstants.GameFilesEditedDir}/Data/INI/**/*.ini" },
                    OutputFormat = "INI",
                    Description = "Game balance and unit attribute INI files"
                },
                new
                {
                    Name = "CoreTextures",
                    Type = "Texture",
                    SourceFiles = new[] { $"{ModBuilderConstants.GameFilesEditedDir}/Art/Textures/**/*.tga" },
                    OutputFormat = "DDS",
                    Compression = "DXT5",
                    GenerateMipmaps = true,
                    Description = "Faction and vehicle textures converted to DDS"
                },
                new
                {
                    Name = "CoreAudio",
                    Type = "Audio",
                    SourceFiles = new[] { $"{ModBuilderConstants.GameFilesEditedDir}/Data/Audio/**/*.wav" },
                    OutputFormat = "WAV",
                    Description = "Unit sound effects and combat audio"
                },
                new
                {
                    Name = "GameScripts",
                    Type = "Script",
                    SourceFiles = new[] { $"{ModBuilderConstants.GameFilesEditedDir}/Data/Scripts/**/*.txt" },
                    OutputFormat = "TXT",
                    Description = "AI and gameplay script overrides"
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
                    Name = "CommunityDataPatch",
                    Items = new[] { "CoreINIPatch", "CoreTextures", "CoreAudio", "GameScripts" },
                    ItemNames = new[] { "CoreINIPatch", "CoreTextures", "CoreAudio", "GameScripts" },
                    AllowBuild = true,
                    AllowInstall = true,
                    OutputFile = $"{ModBuilderConstants.DefaultReleaseDir}/CommunityDataPatch.zip",
                    Description = "Full Community Patch distribution package containing all INI, texture, audio, and script fixes"
                },
                new
                {
                    Name = "CoreINIOnly",
                    Items = new[] { "CoreINIPatch" },
                    ItemNames = new[] { "CoreINIPatch" },
                    AllowBuild = true,
                    AllowInstall = true,
                    OutputFile = $"{ModBuilderConstants.DefaultReleaseDir}/CoreINIOnly.zip",
                    Description = "Lightweight INI-only data patch package"
                }
            }
        };

        var bundlePacksPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);
        await WriteJsonFileAsync(bundlePacksPath, bundlePacksConfig, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSampleAssetsAsync(string projectDir, CancellationToken cancellationToken)
    {
        var iniDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "INI");
        var textureDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Art", "Textures");
        var audioDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Audio");
        var scriptsDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Scripts");

        Directory.CreateDirectory(iniDir);
        Directory.CreateDirectory(textureDir);
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(scriptsDir);

        var sampleIniPath = Path.Combine(iniDir, "AmericaVehicleCrusaderTank.ini");
        if (!File.Exists(sampleIniPath))
        {
            const string sampleIni = "; =========================================================================\n" +
                                     "; Command & Conquer: Generals / Zero Hour - Sample Modded Object Definition\n" +
                                     "; =========================================================================\n\n" +
                                     "Object AmericaVehicleCrusaderTank\n" +
                                     "  Side = America\n" +
                                     "  EditorSorting = VEHICLE\n" +
                                     "  BuildCost = 900\n" +
                                     "  BuildTime = 10.0\n" +
                                     "  MaxHealth = 600.0\n" +
                                     "  InitialHealth = 600.0\n" +
                                     "  VisionRange = 150.0\n" +
                                     "  ShroudClearingRange = 300.0\n" +
                                     "End\n";
            await File.WriteAllTextAsync(sampleIniPath, sampleIni, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        var sampleAiDataPath = Path.Combine(iniDir, "AIData.ini");
        if (!File.Exists(sampleAiDataPath))
        {
            const string aiDataIni = "; AIData.ini - Community Patch Configuration\n" +
                                     "AIData\n" +
                                     "  StructureSeconds = 14.0\n" +
                                     "  TeamSeconds = 30.0\n" +
                                     "  Side = America\n" +
                                     "End\n";
            await File.WriteAllTextAsync(sampleAiDataPath, aiDataIni, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        var sampleTgaPath = Path.Combine(textureDir, "CrusaderTank.tga");
        if (!File.Exists(sampleTgaPath))
        {
            var tgaBytes = CreateSampleTgaBytes(32, 32);
            await File.WriteAllBytesAsync(sampleTgaPath, tgaBytes, cancellationToken).ConfigureAwait(false);
        }

        var sampleAudioPath = Path.Combine(audioDir, "TankMove.wav");
        if (!File.Exists(sampleAudioPath))
        {
            var wavBytes = CreateSampleWavBytes();
            await File.WriteAllBytesAsync(sampleAudioPath, wavBytes, cancellationToken).ConfigureAwait(false);
        }

        var sampleScriptPath = Path.Combine(scriptsDir, "CommunityFixes.txt");
        if (!File.Exists(sampleScriptPath))
        {
            const string scriptContent = "// Community Patch Script Fixes\n// Fix: Correct pathfinding obstruction handling\n";
            await File.WriteAllTextAsync(sampleScriptPath, scriptContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] CreateSampleTgaBytes(short width, short height)
    {
        var header = new byte[18];
        header[2] = 2; // uncompressed true-color image
        header[12] = (byte)(width & 0xFF);
        header[13] = (byte)((width >> 8) & 0xFF);
        header[14] = (byte)(height & 0xFF);
        header[15] = (byte)((height >> 8) & 0xFF);
        header[16] = 32; // 32 bits per pixel (BGRA)
        header[17] = 8;  // 8 bits alpha

        var pixelDataLength = width * height * 4;
        var totalBytes = new byte[18 + pixelDataLength];
        Buffer.BlockCopy(header, 0, totalBytes, 0, 18);

        // Fill with colored test pattern (cyan/blue gradient)
        for (var i = 18; i < totalBytes.Length; i += 4)
        {
            totalBytes[i] = 200;     // B
            totalBytes[i + 1] = 120; // G
            totalBytes[i + 2] = 40;  // R
            totalBytes[i + 3] = 255; // A (opaque)
        }

        return totalBytes;
    }

    private static byte[] CreateSampleWavBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        const int sampleRate = 22050;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int samplesCount = sampleRate / 4; // 0.25s audio
        const int dataSize = samplesCount * channels * (bitsPerSample / 8);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        // simple sound wave
        for (var i = 0; i < samplesCount; i++)
        {
            var t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * t) * 8000);
            writer.Write(sample);
        }

        return ms.ToArray();
    }

    private static async Task CreateReadmeFilesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var readmeFiles = new[]
        {
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "INI", ReadmeFileName),
                "Place your INI files here.\n\nThese files will be processed and included in your mod.\nSupported formats: .ini"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Audio", ReadmeFileName),
                "Place your audio files here.\n\nSupported formats: .mp3, .wav"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Scripts", ReadmeFileName),
                "Place your script files here.\n\nSupported formats: .scb, .txt"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Art", "Textures", ReadmeFileName),
                "Place your texture files here.\n\nSupported formats:\n- .tga (Targa)\n- .psd (Photoshop)\n- .dds (DirectDraw Surface)\n\nTextures will be automatically converted to DDS format during build."
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Art", "W3D", ReadmeFileName),
                "Place your W3D model files here.\n\nSupported formats: .w3d"
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.ReleaseFilesDir, ReadmeFileName),
                "Place additional release files here (e.g., custom READMEs, installers, documentation).\n\nThese files will be copied directly to the release directory."
            ),
            (
                Path.Combine(projectDir, ModBuilderConstants.ConfigDir, ReadmeFileName),
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
