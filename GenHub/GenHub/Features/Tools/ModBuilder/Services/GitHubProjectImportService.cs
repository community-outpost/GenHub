using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Imports a GitHub repository branch as a ModBuilder project by downloading the branch
/// source archive. Repositories that already contain a project file are linked as-is;
/// other repositories get a generated project that builds their contents.
/// </summary>
public class GitHubProjectImportService(
    ILogger<GitHubProjectImportService> logger,
    IDownloadService downloadService,
    IProjectConfigService projectConfigService) : IGitHubProjectImportService
{
    /// <inheritdoc />
    public async Task<OperationResult<string>> ImportRepositoryAsync(
        GitHubRepositoryReference reference,
        string targetDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return OperationResult<string>.CreateFailure("Target directory cannot be empty.");
        }

        if (Directory.Exists(targetDirectory))
        {
            var alreadyExisting = FindProjectFile(targetDirectory);
            if (alreadyExisting != null)
            {
                logger.LogInformation("Target directory {Target} already contains project {Project}", targetDirectory, alreadyExisting);
                return OperationResult<string>.CreateSuccess(alreadyExisting);
            }

            if (Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            {
                return OperationResult<string>.CreateFailure("Target directory is not empty and does not contain a project file.");
            }
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), $"genhub_github_{Guid.NewGuid():N}");
        var archivePath = Path.Combine(Path.GetTempPath(), $"genhub_github_{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report($"Downloading {reference.FullName}@{reference.Branch} from GitHub...");
            var downloadUrl = ApiConstants.GetGitHubBranchZipUrl(reference.Owner, reference.Repo, reference.Branch);
            var downloadResult = await downloadService.DownloadFileAsync(
                new Uri(downloadUrl),
                archivePath,
                progress: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!downloadResult.Success || !File.Exists(archivePath))
            {
                var error = downloadResult.FirstError ?? "Download produced no file.";
                return OperationResult<string>.CreateFailure(
                    $"Failed to download {reference.FullName}@{reference.Branch} from GitHub: {error}");
            }

            progress?.Report($"Extracting {reference.FullName}...");
            Directory.CreateDirectory(stagingDir);
            await ModBuilderArchiveExtractor.ExtractArchiveFileAsync(archivePath, stagingDir, cancellationToken).ConfigureAwait(false);

            var contentRoot = UnwrapSingleDirectory(stagingDir);
            var stagedProject = FindProjectFile(contentRoot);

            if (stagedProject != null)
            {
                var targetExisted = Directory.Exists(targetDirectory);
                try
                {
                    Directory.CreateDirectory(targetDirectory);
                    CopyDirectoryContents(contentRoot, targetDirectory, cancellationToken);

                    var projectRelativePath = Path.GetRelativePath(contentRoot, stagedProject);
                    var existingProject = Path.Combine(targetDirectory, projectRelativePath);
                    logger.LogInformation("Linked GitHub repository {Repo} as existing project {Project}", reference.FullName, existingProject);
                    return OperationResult<string>.CreateSuccess(existingProject);
                }
                catch
                {
                    if (!targetExisted && Directory.Exists(targetDirectory))
                    {
                        DeleteDirectoryQuietly(targetDirectory);
                    }

                    throw;
                }
            }

            progress?.Report($"Creating ModBuilder project for {reference.FullName}...");
            var projectPath = Path.Combine(targetDirectory, $"{reference.Repo}{ModBuilderConstants.ProjectFileExtension}");
            var createProgress = progress == null ? null : new Progress<double>(p => progress.Report($"Creating ModBuilder project for {reference.FullName} ({p:P0})..."));

            var targetDirectoryExisted = Directory.Exists(targetDirectory);
            var createResult = await projectConfigService.CreateProjectFromDirectoryAsync(
                projectPath,
                reference.Repo,
                contentRoot,
                progress: createProgress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!createResult.Success)
            {
                if (!targetDirectoryExisted && Directory.Exists(targetDirectory) && !Directory.EnumerateFileSystemEntries(targetDirectory).Any())
                {
                    DeleteDirectoryQuietly(targetDirectory);
                }

                return OperationResult<string>.CreateFailure(
                    createResult.FirstError ?? $"Failed to create project for {reference.FullName}.");
            }

            logger.LogInformation("Imported GitHub repository {Repo} as new project {Project}", reference.FullName, projectPath);
            return OperationResult<string>.CreateSuccess(projectPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import GitHub repository {Repo}", reference.FullName);
            return OperationResult<string>.CreateFailure($"Failed to import {reference.FullName}: {ex.Message}");
        }
        finally
        {
            DeleteQuietly(archivePath);
            DeleteDirectoryQuietly(stagingDir);
        }
    }

    internal static string? FindProjectFile(string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
        {
            return null;
        }

        var rootMatch = Directory.GetFiles(targetDirectory, ModBuilderConstants.ProjectFilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
        if (rootMatch != null)
        {
            return rootMatch;
        }

        return Directory.GetFiles(targetDirectory, ModBuilderConstants.ProjectFilePattern, SearchOption.AllDirectories)
            .OrderBy(f => f.Length)
            .ThenBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string UnwrapSingleDirectory(string stagingDir)
    {
        if (Directory.GetFiles(stagingDir, ModBuilderConstants.ProjectFilePattern).Length > 0)
        {
            return stagingDir;
        }

        var subDirs = Directory.GetDirectories(stagingDir);
        var files = Directory.GetFiles(stagingDir);
        if (subDirs.Length == 1 && files.Length == 0)
        {
            return subDirs[0];
        }

        return stagingDir;
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetFile = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete temporary import file {Path}", path);
        }
    }

    private void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete temporary import directory {Path}", path);
        }
    }
}
