using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Features.GitHub.ViewModels.Items;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// View model for handling GitHub artifact/release installation through the content pipeline.
/// </summary>
public partial class InstallationViewModel(
    IContentStorageService contentStorageService,
    IContentOrchestrator contentOrchestrator,
    IDependencyResolver dependencyResolver,
    ILogger<InstallationViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedItemCommand))]
    private bool isInstalling;

    [ObservableProperty]
    private double installationProgress;

    [ObservableProperty]
    private string statusMessage = GitHubConstants.ReadyToInstallMessage;

    [ObservableProperty]
    private object? currentItem;

    [ObservableProperty]
    private GitHubDisplayItemViewModel? selectedItem;

    [ObservableProperty]
    private ContentType selectedContentType = ContentType.GameClient;

    /// <summary>
    /// Gets the available content types for selection.
    /// </summary>
    public ContentType[] AvailableContentTypes { get; } = new[]
    {
        ContentType.GameClient,
        ContentType.Mod,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.MapPack,
        ContentType.LanguagePack,
        ContentType.Mission,
        ContentType.Map,
    };

    /// <summary>
    /// Gets the installation log entries.
    /// </summary>
    public ObservableCollection<string> InstallationLog { get; } = new();

    /// <summary>
    /// Gets a value indicating whether an item can be installed.
    /// </summary>
    public bool CanInstallItem => SelectedItem != null && !IsInstalling && SelectedItem.CanInstall;

    /// <summary>
    /// Gets a value indicating whether an operation is active.
    /// </summary>
    public bool IsOperationActive => IsInstalling;

    /// <summary>
    /// Installs the selected item through the content pipeline.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanInstallItem))]
    public async Task InstallSelectedItemAsync()
    {
        if (SelectedItem == null) return;

        IsInstalling = true;
        InstallationProgress = 0;
        InstallationLog.Clear();
        StatusMessage = "Starting installation...";

        try
        {
            StatusMessage = "Preparing content for installation...";
            InstallationLog.Add($"Preparing {SelectedItem.DisplayName} for installation...");

            var searchResult = CreateContentSearchResultFromSelectedItem();
            if (searchResult == null)
            {
                var itemType = SelectedItem.GetType().Name;
                var errorMsg = itemType.Contains("Artifact")
                    ? "Artifact installation is not yet supported. Please install releases instead."
                    : $"Unsupported item type: {itemType}";

                StatusMessage = errorMsg;
                InstallationLog.Add($"❌ {errorMsg}");
                return;
            }

            InstallationLog.Add("Acquiring content...");
            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                // Update properties that trigger UI binding updates
                InstallationProgress = p.ProgressPercentage;
                StatusMessage = p.CurrentOperation;

                // Add log entry for significant progress updates
                if (p.ProgressPercentage % 10 == 0 || p.Phase != ContentAcquisitionPhase.Downloading)
                {
                    InstallationLog.Add($"{p.CurrentOperation} ({p.ProgressPercentage}%)");
                }
            });

            var result = await contentOrchestrator.AcquireContentAsync(
                searchResult,
                progress,
                CancellationToken.None);

            if (!result.Success)
            {
                throw new InvalidOperationException($"Content acquisition failed: {result.FirstError}");
            }

            var manifest = result.Data!;
            InstallationLog.Add($"✅ Content resolved with ID: {manifest.Id}");

            InstallationLog.Add("Resolving dependencies...");
            var depsResult = await dependencyResolver.ResolveDependenciesWithManifestsAsync(
                new[] { manifest.Id.Value }, CancellationToken.None);

            if (!depsResult.Success || depsResult.HasErrors)
            {
                var errors = string.Join("\n", depsResult.Errors);
                StatusMessage = $"Dependency resolution failed:\n{errors}";
                InstallationLog.Add($"❌ Dependency errors: {errors}");
                return;
            }

            if (depsResult.MissingContentIds.Any())
            {
                var missing = string.Join(", ", depsResult.MissingContentIds);
                StatusMessage = $"Missing required dependencies: {missing}";
                InstallationLog.Add($"❌ Missing dependencies: {missing}");
                return;
            }

            if (depsResult.Warnings.Any())
            {
                var conflicts = string.Join("\n", depsResult.Warnings);
                StatusMessage = $"Dependency conflicts detected:\n{conflicts}";
                InstallationLog.Add($"⚠️ Conflicts: {conflicts}");
                InstallationLog.Add("Proceeding despite conflicts...");
            }

            InstallationLog.Add("✅ Dependencies resolved successfully");
            StatusMessage = $"Successfully installed {SelectedItem.DisplayName}";
            InstallationLog.Add("Installation completed successfully!");
            InstallationLog.Add($"Content type: {SelectedContentType}");
            InstallationLog.Add($"Manifest ID: {manifest.Id}");

            var verifyResult = await contentStorageService.IsContentStoredAsync(manifest.Id);
            if (!verifyResult.Success || !verifyResult.Data)
            {
                throw new InvalidOperationException("Content verification failed after installation");
            }

            InstallationLog.Add("✅ Content verification passed");

            if (SelectedItem is GitHubArtifactDisplayItemViewModel artifact)
            {
                artifact.IsInstalled = true;
            }

            logger.LogInformation(
                "Successfully installed {ItemName} with manifest {ManifestId}",
                SelectedItem.DisplayName,
                manifest.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install selected item: {ItemName}", SelectedItem.DisplayName);
            StatusMessage = $"Installation failed: {ex.Message}";
            InstallationLog.Add($"ERROR: {ex.Message}");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Cancels the current installation.
    /// </summary>
    [RelayCommand]
    public void CancelInstallation()
    {
        StatusMessage = GitHubConstants.InstallationCancelledMessage;
        IsInstalling = false;
        InstallationProgress = 0;
        InstallationLog.Add(GitHubConstants.InstallationCancelledMessage);
    }

    /// <summary>
    /// Sets the selected item for installation.
    /// </summary>
    /// <param name="item">The selected item.</param>
    public void SetSelectedItem(GitHubDisplayItemViewModel? item)
    {
        SelectedItem = item;

        if (item != null)
        {
            StatusMessage = $"Ready to install: {item.DisplayName}";
            SelectedContentType = InferContentType(item.DisplayName, item.Description);

            logger.LogDebug(
                "Selected item: {DisplayName}, CanInstall: {CanInstall}, Type: {Type}",
                item.DisplayName,
                item.CanInstall,
                item.GetType().Name);
        }
        else
        {
            StatusMessage = "Select an item to install";
        }

        InstallSelectedItemCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInstallItem));
    }

    /// <summary>
    /// Clears the selected item.
    /// </summary>
    public void ClearSelection()
    {
        SelectedItem = null;
        StatusMessage = GitHubConstants.ReadyToInstallMessage;

        if (IsOperationActive)
        {
            CancelInstallationCommand.Execute(null);
        }
    }

    /// <summary>
    /// Creates a content search result from the currently selected item.
    /// </summary>
    /// <returns>The content search result, or null if unsupported.</returns>
    private ContentSearchResult? CreateContentSearchResultFromSelectedItem()
    {
        if (SelectedItem == null) return null;

        var searchResult = new ContentSearchResult
        {
            Name = SelectedItem.DisplayName,
            Description = SelectedItem.Description,
            ContentType = SelectedContentType,
            TargetGame = GameType.ZeroHour,
            ProviderName = "GitHub",
            IsInferred = true,
            RequiresResolution = true,
            ResolverId = GitHubConstants.GitHubReleaseResolverId,
        };

        if (SelectedItem is GitHubReleaseDisplayItemViewModel releaseVm)
        {
            searchResult.Id = $"{releaseVm.Owner}/{releaseVm.Repository}/releases/{releaseVm.Release.Id}";
            searchResult.Version = releaseVm.Release.TagName ?? "latest";
            searchResult.SourceUrl = releaseVm.Release.HtmlUrl;
            searchResult.LastUpdated = releaseVm.Release.PublishedAt?.DateTime ?? DateTime.Now;
            searchResult.Data = releaseVm.Release;

            if (releaseVm.Release.Assets != null)
            {
                searchResult.DownloadSize = releaseVm.Release.Assets.Sum(a => a.Size);
            }

            searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = releaseVm.Owner;
            searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = releaseVm.Repository;
            searchResult.ResolverMetadata[GitHubConstants.TagMetadataKey] = releaseVm.Release.TagName ?? string.Empty;
        }
        else if (SelectedItem is GitHubArtifactDisplayItemViewModel artifactVm)
        {
            if (artifactVm.Artifact.IsRelease)
            {
                searchResult.ResolverId = GitHubConstants.GitHubReleaseResolverId;
                searchResult.Id = $"{artifactVm.Owner}/{artifactVm.Repository}/releases/{artifactVm.Artifact.Id}";
                searchResult.Version = artifactVm.Artifact.CommitSha;
                searchResult.LastUpdated = artifactVm.Artifact.CreatedAt;
                searchResult.DownloadSize = artifactVm.Artifact.SizeInBytes;
                searchResult.Data = artifactVm.Artifact;

                searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = artifactVm.Owner;
                searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = artifactVm.Repository;
                searchResult.ResolverMetadata[GitHubConstants.TagMetadataKey] = artifactVm.Artifact.CommitSha;
                searchResult.ResolverMetadata["assetName"] = artifactVm.Artifact.Name;
            }
            else
            {
                searchResult.ResolverId = GitHubConstants.GitHubArtifactResolverId;
                searchResult.Id = $"{artifactVm.Owner}/{artifactVm.Repository}/artifacts/{artifactVm.Artifact.Id}";
                searchResult.Version = $"run{artifactVm.Artifact.WorkflowNumber}";
                searchResult.LastUpdated = artifactVm.Artifact.CreatedAt;
                searchResult.DownloadSize = artifactVm.Artifact.SizeInBytes;
                searchResult.Data = artifactVm.Artifact;

                searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = artifactVm.Owner;
                searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = artifactVm.Repository;
                searchResult.ResolverMetadata["runId"] = artifactVm.Artifact.RunId.ToString();
            }
        }
        else
        {
            return null;
        }

        return searchResult;
    }

    /// <summary>
    /// Infers the content type based on the item name and description.
    /// </summary>
    /// <param name="name">The item name.</param>
    /// <param name="description">The item description.</param>
    /// <returns>The inferred content type.</returns>
    private ContentType InferContentType(string name, string? description)
    {
        var text = $"{name} {description}".ToLowerInvariant();

        if (text.Contains(GameClientConstants.GeneralsExecutable.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)) ||
            text.Contains(GameClientConstants.ZeroHourExecutable.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)) ||
            text.Contains(GameClientConstants.SuperHackersGeneralsExecutable.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)) ||
            text.Contains(GameClientConstants.SuperHackersZeroHourExecutable.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)) ||
            text.Contains("generals") || text.Contains("zerohour") || text.Contains("zero hour"))
        {
            return ContentType.GameClient;
        }

        if (text.Contains("map") && text.Contains("pack")) return ContentType.MapPack;
        if (text.Contains("map")) return ContentType.Map;
        if (text.Contains("mission")) return ContentType.Mission;
        if (text.Contains("patch") || text.Contains("fix") || text.Contains("update")) return ContentType.Patch;
        if (text.Contains("addon") || text.Contains("utility") || text.Contains("tool")) return ContentType.Addon;
        if (text.Contains("language") || text.Contains("translation")) return ContentType.LanguagePack;
        if (text.Contains("bundle") || text.Contains("collection")) return ContentType.ContentBundle;

        return ContentType.GameClient;
    }
}
