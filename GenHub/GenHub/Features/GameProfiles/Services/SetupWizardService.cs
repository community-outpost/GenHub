using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using GenHub.Features.GameProfiles.Views.Wizard;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for running the Setup Wizard to handle detected game content.
/// </summary>
public class SetupWizardService(
    IGameClientProfileService gameClientProfileService,
    CommunityOutpostDiscoverer communityOutpostDiscoverer,
    GeneralsOnlineDiscoverer generalsOnlineDiscoverer,
    SuperHackersProvider superHackersProvider,
    IContentManifestPool manifestPool,
    ILogger<SetupWizardService> logger) : ISetupWizardService
{
    /// <summary>
    /// Gets or sets an optional hook for showing the wizard dialog, primarily used in unit tests to simulate user interaction.
    /// </summary>
    internal Func<SetupWizardViewModel, Task<bool>>? DialogShower { get; set; }

    /// <inheritdoc/>
    public async Task<SetupWizardResult> RunSetupWizardAsync(IEnumerable<GameInstallation> installations, CancellationToken cancellationToken = default)
    {
        var installationsList = installations.ToList();
        var result = new SetupWizardResult();

        // 1. Pre-fetch all manifests from pool once to avoid redundant pool scans
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        var allPoolManifests = allManifestsResult.Success && allManifestsResult.Data != null
            ? allManifestsResult.Data.ToList()
            : [];

        // 2. Determine Scenarios for each component across all installations
        var cpGlobal = installationsList.Select(inst => new { Inst = inst, Client = inst.AvailableGameClients.FirstOrDefault(c => c.PublisherType == CommunityOutpostConstants.PublisherType) }).Where(x => x.Client != null).ToList();
        var goGlobal = installationsList.Select(inst => new { Inst = inst, Client = inst.AvailableGameClients.FirstOrDefault(c => c.PublisherType == PublisherTypeConstants.GeneralsOnline) }).Where(x => x.Client != null).ToList();
        var shGlobal = installationsList.Select(inst => new { Inst = inst, Client = inst.AvailableGameClients.FirstOrDefault(c => c.PublisherType == PublisherTypeConstants.TheSuperHackers) }).Where(x => x.Client != null).ToList();

        // 3. Collection Phase: Build Wizard Items
        var wizardItems = new List<SetupWizardItemViewModel>();

        // Pre-fetch latest versions
        var cpLatestVersion = await GetLatestVersionAsync(CommunityOutpostConstants.PublisherType);
        var goLatestVersion = await GetLatestVersionAsync(PublisherTypeConstants.GeneralsOnline);
        var shLatestVersion = await GetLatestVersionAsync(PublisherTypeConstants.TheSuperHackers);

        // Initialize default actions (Decline/None)
        result.CommunityPatchAction = GameClientConstants.WizardActionTypes.Decline;
        result.GeneralsOnlineAction = GameClientConstants.WizardActionTypes.Decline;
        result.SuperHackersAction = GameClientConstants.WizardActionTypes.Decline;

        // Helper to check for managed/up-to-date client for a specific global list
        async Task<(bool SkipWizard, string FinalAction)> ProcessComponentAsync(
            string publisherType,
            System.Collections.IEnumerable componentGlobalEnu,
            string latestVersion,
            string title,
            string missingDescription,
            string iconPath,
            string metadata)
        {
            var componentGlobal = componentGlobalEnu.Cast<dynamic>().ToList();

            // 1. Identify managed clients in the manifest pool
            var managedManifests = allPoolManifests
                .Where(m => m.ContentType == ContentType.GameClient &&
                            (string.Equals(m.Publisher?.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase) ||
                             m.Id.Value.Contains($".{publisherType}.", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // 2. Check if any managed client in the pool is up-to-date
            var upToDateManagedManifest = managedManifests
                .FirstOrDefault(m => string.Equals(CleanVersionString(m.Version), latestVersion, StringComparison.OrdinalIgnoreCase));

            if (upToDateManagedManifest != null)
            {
                bool profileExists = await gameClientProfileService.ProfileExistsForGameClientAsync(upToDateManagedManifest.Id.Value, cancellationToken);
                if (profileExists)
                {
                    logger.LogInformation("[SetupWizard] Managed up-to-date manifest and profile found for {Title} ({Version})", title, latestVersion);
                    return (true, GameClientConstants.WizardActionTypes.Decline);
                }

                logger.LogInformation("[SetupWizard] Managed up-to-date manifest found for {Title} ({Version}), but profile missing. Showing in wizard to create profile.", title, latestVersion);
                var downloadedItem = new SetupWizardItemViewModel
                {
                    Title = title,
                    Status = "Downloaded",
                    Description = $"Create game profile for {title} {latestVersion}.",
                    ActionLabel = "Create Profile",
                    ActionType = GameClientConstants.WizardActionTypes.CreateProfile,
                    IsSelected = true,
                    IconPath = iconPath,
                    Metadata = metadata,
                    Version = latestVersion,
                };
                wizardItems.Add(downloadedItem);
                return (false, downloadedItem.ActionType);
            }

            // Also check unmanaged clients from installations in case an unmanaged client is managed/has ID
            var managedClientsFromInstallations = componentGlobal
                .Where(x => x.Client != null && !string.IsNullOrEmpty((string)x.Client.Id))
                .ToList();

            var upToDateFromInstallations = managedClientsFromInstallations
                .FirstOrDefault(x => x.Client != null && string.Equals(CleanVersionString((string)x.Client.Version), latestVersion, StringComparison.OrdinalIgnoreCase));

            if (upToDateFromInstallations != null)
            {
                bool profileExists = await gameClientProfileService.ProfileExistsForGameClientAsync((string)upToDateFromInstallations.Client.Id, cancellationToken);
                if (profileExists)
                {
                    logger.LogInformation("[SetupWizard] Up-to-date client and profile found in installations for {Title} ({Version})", title, latestVersion);
                    return (true, GameClientConstants.WizardActionTypes.Decline);
                }

                logger.LogInformation("[SetupWizard] Up-to-date client found in installations for {Title} ({Version}), profile missing. Showing in wizard to create profile.", title, latestVersion);
                var detectedItem = new SetupWizardItemViewModel
                {
                    Title = title,
                    Status = "Detected",
                    Description = $"Create game profile for {title} {latestVersion}.",
                    ActionLabel = "Create Profile",
                    ActionType = GameClientConstants.WizardActionTypes.CreateProfile,
                    IsSelected = true,
                    IconPath = iconPath,
                    Metadata = metadata,
                    Version = latestVersion,
                };
                wizardItems.Add(detectedItem);
                return (false, detectedItem.ActionType);
            }

            // 3. Check if any profiles exist for this component (managed or unmanaged)
            bool anyProfileExists = false;
            foreach (var manifest in managedManifests)
            {
                if (await gameClientProfileService.ProfileExistsForGameClientAsync(manifest.Id.Value, cancellationToken))
                {
                    anyProfileExists = true;
                    break;
                }
            }

            if (!anyProfileExists)
            {
                foreach (var x in componentGlobal)
                {
                    if (x.Client != null && !string.IsNullOrEmpty((string)x.Client.Id) &&
                        await gameClientProfileService.ProfileExistsForGameClientAsync((string)x.Client.Id, cancellationToken))
                    {
                        anyProfileExists = true;
                        break;
                    }
                }
            }

            var isDetected = componentGlobal.Count > 0;
            var displayVersion = !string.IsNullOrEmpty(latestVersion) && latestVersion != GameClientConstants.UnknownVersion
                ? latestVersion
                : (managedManifests.FirstOrDefault()?.Version ?? latestVersion);

            // Construct Wizard Item
            var item = new SetupWizardItemViewModel
            {
                Title = title,
                IsSelected = true,
                IconPath = iconPath,
                Metadata = metadata,
                Version = displayVersion,
            };

            if (anyProfileExists)
            {
                // Profile exists, but it is not the latest managed version
                item.Status = "Installed";
                item.Description = $"Update existing {title} profiles to {displayVersion}.";
                item.ActionLabel = "Update / Reinstall";
                item.ActionType = GameClientConstants.WizardActionTypes.Update;
                item.IsSelected = false;
            }
            else if (managedManifests.Count > 0)
            {
                // Content downloaded in pool, but no profile exists
                item.Status = "Downloaded";
                item.Description = $"Create game profile for {title} {displayVersion}.";
                item.ActionLabel = "Create Profile";
                item.ActionType = GameClientConstants.WizardActionTypes.CreateProfile;
                item.IsSelected = true;
            }
            else if (isDetected)
            {
                // Unmanaged files detected but no profile
                item.Status = "Detected";
                item.Description = $"Detected installed {title}. Create game profile?";
                item.ActionLabel = "Create Profile";
                item.ActionType = GameClientConstants.WizardActionTypes.CreateProfile;
                item.IsSelected = true;
            }
            else
            {
                // Nothing found at all
                item.Status = "Missing";
                item.Description = missingDescription;
                item.ActionLabel = "Download & Install";
                item.ActionType = GameClientConstants.WizardActionTypes.Install;
                item.IsSelected = title == "Community Patch"; // Defaults
            }

            wizardItems.Add(item);
            return (false, item.ActionType);
        }

        var cpCleanVersion = CleanVersionString(cpLatestVersion);
        var goCleanVersion = CleanVersionString(goLatestVersion);
        var shCleanVersion = CleanVersionString(shLatestVersion);

        // Process all components
        var cpRes = await ProcessComponentAsync(
            CommunityOutpostConstants.PublisherType,
            cpGlobal,
            cpCleanVersion,
            "Community Patch",
            $"Download and install Community Patch {cpCleanVersion}.",
            CommunityOutpostConstants.LogoSource,
            CommunityOutpostConstants.PublisherType);
        result.CommunityPatchAction = cpRes.FinalAction;

        var goRes = await ProcessComponentAsync(
            PublisherTypeConstants.GeneralsOnline,
            goGlobal,
            goCleanVersion,
            "Generals Online",
            $"Download and install Generals Online {goCleanVersion} for multiplayer support.",
            UriConstants.GeneralsOnlineLogoUri,
            PublisherTypeConstants.GeneralsOnline);
        result.GeneralsOnlineAction = goRes.FinalAction;

        var shRes = await ProcessComponentAsync(
            PublisherTypeConstants.TheSuperHackers,
            shGlobal,
            shCleanVersion,
            "The Super Hackers",
            "Install The Super Hackers for advanced modding and features.",
            UriConstants.SuperHackersLogoUri,
            PublisherTypeConstants.TheSuperHackers);
        result.SuperHackersAction = shRes.FinalAction;

        // 4. Presentation Phase: Show Wizard
        if (wizardItems.Count > 0)
        {
            var wizardVm = new SetupWizardViewModel(wizardItems);
            if (DialogShower != null)
            {
                result.Confirmed = await DialogShower(wizardVm);
            }
            else
            {
                var mainWindow = GetMainWindow();
                if (mainWindow != null)
                {
                    var wizardView = new SetupWizardView
                    {
                        DataContext = wizardVm,
                    };

                    await wizardView.ShowDialog(mainWindow);

                    result.Confirmed = wizardVm.Confirmed;
                }
                else
                {
                    logger.LogWarning("Could not resolve MainWindow for Setup Wizard.");
                    result.Confirmed = false;
                }
            }
        }
        else
        {
             // If we didn't show the wizard, it means we either had nothing to do or only auto-accept actions.
             result.Confirmed = true;
        }

        // 5. Final decisions: If item was in wizard, override with user selection
        string FinalizeAction(string metadata, string currentAction)
        {
            var item = wizardItems.FirstOrDefault(x => x.Metadata as string == metadata);
            if (item != null)
            {
                return (result.Confirmed && item.IsSelected) ? item.ActionType : GameClientConstants.WizardActionTypes.Decline;
            }

            return currentAction;
        }

        result.CommunityPatchAction = FinalizeAction(CommunityOutpostConstants.PublisherType, result.CommunityPatchAction);
        result.GeneralsOnlineAction = FinalizeAction(PublisherTypeConstants.GeneralsOnline, result.GeneralsOnlineAction);
        result.SuperHackersAction = FinalizeAction(PublisherTypeConstants.TheSuperHackers, result.SuperHackersAction);

        return result;
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    private static string CleanVersionString(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            return trimmed[1..];
        }

        return trimmed;
    }

    private async Task<string> GetLatestVersionAsync(string publisher)
    {
        try
        {
            if (publisher == CommunityOutpostConstants.PublisherType)
            {
                var result = await communityOutpostDiscoverer.DiscoverAsync(new ContentSearchQuery());
                if (result.Success && result.Data != null)
                {
                    var version = result.Data.Items.FirstOrDefault()?.Version;
                    if (!string.IsNullOrEmpty(version)) return version;
                }
            }
            else if (publisher == PublisherTypeConstants.GeneralsOnline)
            {
                var result = await generalsOnlineDiscoverer.DiscoverAsync(new ContentSearchQuery());
                if (result.Success && result.Data != null)
                {
                    var version = result.Data.Items.FirstOrDefault()?.Version;
                    if (!string.IsNullOrEmpty(version)) return version;
                }
            }
            else if (publisher == PublisherTypeConstants.TheSuperHackers)
            {
                var query = new ContentSearchQuery
                {
                    AuthorName = SuperHackersConstants.GeneralsGameCodeOwner,
                    SearchTerm = SuperHackersConstants.GeneralsGameCodeRepo,
                };

                var result = await superHackersProvider.SearchAsync(query);
                if (result.Success && result.Data != null)
                {
                    var version = result.Data
                        .OrderByDescending(x => x.Version)
                        .FirstOrDefault()?.Version;
                    if (!string.IsNullOrEmpty(version)) return version;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch latest version for {Publisher}", publisher);
        }

        return GameClientConstants.UnknownVersion;
    }
}
