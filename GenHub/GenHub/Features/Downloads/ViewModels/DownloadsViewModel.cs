using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the Downloads tab.
/// </summary>
public partial class DownloadsViewModel : ViewModelBase
{
    private readonly IContentOrchestrator? _contentOrchestrator;
    private readonly IContentManifestPool? _manifestPool;
    private readonly IContentUpdateService? _updateService;
    private readonly ILogger<DownloadsViewModel>? _logger;

    [ObservableProperty]
    private string _title = "Downloads";

    [ObservableProperty]
    private string _description = "Manage your downloads and installations";

    [ObservableProperty]
    private string _generalsOnlineVersion = "Check for updates...";

    [ObservableProperty]
    private bool _isInstallingGeneralsOnline;

    [ObservableProperty]
    private string _installationStatus = string.Empty;

    [ObservableProperty]
    private double _installationProgress;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsViewModel"/> class.
    /// </summary>
    /// <param name="contentOrchestrator">The content orchestrator for managing content acquisition.</param>
    /// <param name="manifestPool">The manifest pool for managing content manifests.</param>
    /// <param name="updateService">The update service for checking content updates.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    public DownloadsViewModel(
        IContentOrchestrator? contentOrchestrator = null,
        IContentManifestPool? manifestPool = null,
        IContentUpdateService? updateService = null,
        ILogger<DownloadsViewModel>? logger = null)
    {
        _contentOrchestrator = contentOrchestrator;
        _manifestPool = manifestPool;
        _updateService = updateService;
        _logger = logger;
    }

    /// <summary>
    /// Performs asynchronous initialization for the Downloads tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        await CheckGeneralsOnlineVersionAsync();
    }

    [RelayCommand]
    private void OpenGitHubBuilds()
    {
        // TODO: Implement navigation to GitHub builds page
    }

    /// <summary>
    /// Flow: Discovery → Resolution → Delivery → CAS Storage → Manifest Pool.
    /// </summary>
    [RelayCommand]
    private async Task InstallGeneralsOnlineAsync()
    {
        if (_contentOrchestrator == null || IsInstallingGeneralsOnline)
        {
            return;
        }

        try
        {
            IsInstallingGeneralsOnline = true;
            InstallationProgress = 0;
            InstallationStatus = "Discovering Generals Online...";

            _logger?.LogInformation("Starting Generals Online installation via Downloads view");

            // Phase 1: Discovery (Content Search)
            var searchQuery = new ContentSearchQuery
            {
                SearchTerm = "Generals Online",
            };

            var searchResult = await _contentOrchestrator.SearchAsync(searchQuery);

            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                InstallationStatus = "Failed to discover Generals Online";
                _logger?.LogError("Generals Online discovery failed");
                return;
            }

            var generalsOnlineResult = searchResult.Data.FirstOrDefault(r =>
                r.Name?.Contains("Generals Online", StringComparison.OrdinalIgnoreCase) == true ||
                r.Name?.Contains("GeneralsOnline", StringComparison.OrdinalIgnoreCase) == true ||
                r.ProviderName?.Contains("GeneralsOnline", StringComparison.OrdinalIgnoreCase) == true);

            if (generalsOnlineResult == null)
            {
                InstallationStatus = "Generals Online not found in search results";
                _logger?.LogError("Generals Online not found in discovery results. Found {Count} results from search.", searchResult.Data?.Count() ?? 0);
                return;
            }

            InstallationProgress = 10;
            var version = generalsOnlineResult.Metadata.TryGetValue("Version", out var ver) ? ver : "Unknown";
            InstallationStatus = $"Found Generals Online {version}";

            _logger?.LogInformation(
                "Discovered Generals Online: {Version}",
                version);

            // Phase 2: Acquisition (Resolution → Delivery → CAS)
            InstallationStatus = "Downloading and installing...";

            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                InstallationProgress = 10 + (p.ProgressPercentage * 0.9); // 10-100%
                InstallationStatus = p.Phase switch
                {
                    ContentAcquisitionPhase.Downloading => $"Downloading: {p.ProgressPercentage:F0}%",
                    ContentAcquisitionPhase.Extracting => $"Extracting: {p.ProgressPercentage:F0}%",
                    ContentAcquisitionPhase.Copying => $"Storing in CAS: {p.ProgressPercentage:F0}%",
                    ContentAcquisitionPhase.ValidatingManifest => $"Validating manifest: {p.ProgressPercentage:F0}%",
                    ContentAcquisitionPhase.ValidatingFiles => $"Validating files: {p.ProgressPercentage:F0}%",
                    ContentAcquisitionPhase.Completed => "Installation complete!",
                    _ => $"Processing: {p.ProgressPercentage:F0}%"
                };
            });

            var acquisitionResult = await _contentOrchestrator.AcquireContentAsync(
                generalsOnlineResult,
                progress);

            if (!acquisitionResult.Success)
            {
                InstallationStatus = $"Installation failed: {acquisitionResult.FirstError ?? "Unknown error"}";
                _logger?.LogError(
                    "Generals Online acquisition failed: {Error}",
                    acquisitionResult.FirstError);
                return;
            }

            InstallationProgress = 100;
            InstallationStatus = "Generals Online installed successfully!";

            _logger?.LogInformation(
                "Generals Online installed successfully: {ManifestId}",
                acquisitionResult.Data?.Id);

            // Update version display
            GeneralsOnlineVersion = acquisitionResult.Data?.Version ?? "Installed";

            // Wait a bit to show success message
            await Task.Delay(2000);
            InstallationStatus = string.Empty;
        }
        catch (Exception ex)
        {
            InstallationStatus = $"Error: {ex.Message}";
            _logger?.LogError(ex, "Failed to install Generals Online");
        }
        finally
        {
            IsInstallingGeneralsOnline = false;
        }
    }

    /// <summary>
    /// Checks for the installed Generals Online version and available updates.
    /// Uses IContentUpdateService to check for updates.
    /// </summary>
    private async Task CheckGeneralsOnlineVersionAsync()
    {
        try
        {
            if (_updateService != null)
            {
                var (updateAvailable, latestVersion, currentVersion) =
                    await _updateService.CheckForUpdatesAsync();

                if (currentVersion != null)
                {
                    GeneralsOnlineVersion = updateAvailable
                        ? $"{currentVersion} (Update: {latestVersion})"
                        : currentVersion;
                }
                else
                {
                    GeneralsOnlineVersion = latestVersion != null
                        ? $"Install {latestVersion}"
                        : "Install Latest";
                }

                _logger?.LogInformation(
                    "Generals Online version check: Current={Current}, Latest={Latest}, UpdateAvailable={UpdateAvailable}",
                    currentVersion,
                    latestVersion,
                    updateAvailable);
            }
            else if (_manifestPool != null)
            {
                // Fallback: Check manifest pool directly
                var manifestsResult = await _manifestPool.GetAllManifestsAsync();
                if (manifestsResult.Success && manifestsResult.Data != null)
                {
                    var goManifest = manifestsResult.Data.FirstOrDefault(m =>
                        m.Id.Value?.Contains("generalsonline", StringComparison.OrdinalIgnoreCase) == true);

                    GeneralsOnlineVersion = goManifest != null
                        ? goManifest.Version ?? "Installed"
                        : "Not Installed";
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to check Generals Online version");
            GeneralsOnlineVersion = "Check failed";
        }
    }
}
