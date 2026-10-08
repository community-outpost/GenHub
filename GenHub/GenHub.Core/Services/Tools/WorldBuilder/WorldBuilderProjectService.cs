using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Manages WorldBuilder project folders with a JSON manifest.
/// </summary>
public sealed class WorldBuilderProjectService(ILogger<WorldBuilderProjectService> logger) : IWorldBuilderProjectService
{
    private sealed record ProjectManifest(string Name, string? GameInstallationId, DateTime CreatedUtc, List<string>? Maps);

    private static readonly string[] ImportExtensions =
    [
        WorldBuilderConstants.FileExtensions.Map,
        WorldBuilderConstants.FileExtensions.MapIni,
        WorldBuilderConstants.FileExtensions.WaveTracks,
        WorldBuilderConstants.FileExtensions.PreviewImage,
    ];

    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

    /// <inheritdoc />
    public async Task<OperationResult<WorldBuilderProject>> CreateAsync(
        string folderPath,
        string name,
        string? gameInstallationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);
        ArgumentException.ThrowIfNullOrEmpty(name);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folderPath);
            var project = new WorldBuilderProject
            {
                Name = name,
                FolderPath = folderPath,
                GameInstallationId = gameInstallationId,
                CreatedUtc = DateTime.UtcNow,
            };
            await WriteManifestAsync(project, cancellationToken).ConfigureAwait(false);
            return OperationResult<WorldBuilderProject>.CreateSuccess(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to create project at {FolderPath}.", folderPath);
            return OperationResult<WorldBuilderProject>.CreateFailure($"Failed to create project: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorldBuilderProject>> OpenAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(folderPath, WorldBuilderConstants.FileNames.Manifest);
            if (!File.Exists(manifestPath))
            {
                return OperationResult<WorldBuilderProject>.CreateFailure("Not a WorldBuilder project folder.");
            }

            var project = await ReadManifestAsync(folderPath, manifestPath, cancellationToken).ConfigureAwait(false);
            if (project == null)
            {
                return OperationResult<WorldBuilderProject>.CreateFailure("Project manifest is corrupt.");
            }

            RefreshMaps(project);
            return OperationResult<WorldBuilderProject>.CreateSuccess(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to open project at {FolderPath}.", folderPath);
            return OperationResult<WorldBuilderProject>.CreateFailure($"Failed to open project: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> ImportFromFolderAsync(
        WorldBuilderProject project,
        string sourceFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(sourceFolder);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(sourceFolder))
            {
                return OperationResult<IReadOnlyList<string>>.CreateFailure("Source folder not found.");
            }

            var imported = new List<string>();
            foreach (var file in Directory.GetFiles(sourceFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ImportExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Path.Combine(project.FolderPath, Path.GetFileName(file));
                await CopyFileAsync(file, target, cancellationToken).ConfigureAwait(false);
                imported.Add(Path.GetFileName(file));
            }

            imported.Sort(StringComparer.OrdinalIgnoreCase);
            RefreshMaps(project);
            await WriteManifestAsync(project, cancellationToken).ConfigureAwait(false);
            return OperationResult<IReadOnlyList<string>>.CreateSuccess(imported);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to import maps from {SourceFolder}.", sourceFolder);
            return OperationResult<IReadOnlyList<string>>.CreateFailure($"Failed to import maps: {ex.Message}");
        }
    }

    private static async Task CopyFileAsync(string source, string target, CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(source);
        using var output = File.Create(target);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void RefreshMaps(WorldBuilderProject project)
    {
        project.MapFiles.Clear();
        if (!Directory.Exists(project.FolderPath))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(project.FolderPath, $"*{WorldBuilderConstants.FileExtensions.Map}"))
        {
            project.MapFiles.Add(Path.GetFileName(file));
        }

        project.MapFiles.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task WriteManifestAsync(WorldBuilderProject project, CancellationToken cancellationToken)
    {
        var manifest = new ProjectManifest(
            project.Name,
            project.GameInstallationId,
            project.CreatedUtc,
            [.. project.MapFiles]);
        var json = JsonSerializer.Serialize(manifest, ManifestOptions);
        await AtomicFile.WriteTextAsync(Path.Combine(project.FolderPath, WorldBuilderConstants.FileNames.Manifest), json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorldBuilderProject?> ReadManifestAsync(string folderPath, string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ProjectManifest>(json, ManifestOptions);
            if (manifest == null)
            {
                return null;
            }

            var project = new WorldBuilderProject
            {
                Name = manifest.Name,
                FolderPath = folderPath,
                GameInstallationId = manifest.GameInstallationId,
                CreatedUtc = manifest.CreatedUtc,
            };
            if (manifest.Maps != null)
            {
                project.MapFiles.AddRange(manifest.Maps);
            }

            return project;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
