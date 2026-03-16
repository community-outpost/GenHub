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
using GenHub.Core.Models.Content;
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
    ILogger<SetupWizardService> logger) : ISetupWizardService
{
    /// <inheritdoc/>
    public async Task<SetupWizardResult> RunSetupWizardAsync(IEnumerable<GameInstallation> installations, CancellationToken cancellationToken = default)
    {
        var installationsList = installations.ToList();
        var result = new SetupWizardResult();

        // 1. Determine Scenarios for each component across all installations
        var cpGlobal = installationsList.Select(inst => new { Inst = inst, Client = inst.AvailableGameClients.FirstOrDefault(c => c.PublisherType == CommunityOutpostConstants.PublisherType) }).Where(x => x.Client != null).ToList();
        var goGlobal = installationsList.Select(inst => new { Inst = inst, Client = inst.AvailableGameClients.FirstOrDefault(c => c.PublisherType == PublisherTypeConstants.GeneralsOnline) }).Where(x => x.Client != null).ToList();
        var shGlobal = installationsList.Select(inst => new { Inst = inst, Client = inst.AvailableGameClients.FirstOrDefault(c => c.PublisherType == PublisherTypeConstants.TheSuperHackers) }).Where(x => x.Client != null).ToList();

        // 2. Collection Phase: Build Wizard Items
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
            System.Collections.IEnumerable componentGlobalEnu,
            string latestVersion,
            string title,
            string missingDescription,
            string iconPath,
            string metadata)
        {
            var componentGlobal = componentGlobalEnu.Cast<dynamic>().ToList();

            // 1. Identify managed clients (have valid manifest IDs)
            // Detected publisher clients have empty IDs and are excluded
            var managedClients = componentGlobal
                .Where(x => x.Client != null &&
                    !string.IsNullOrEmpty((string)x.Client.Id))
                .ToList();

            // 2. Look for an up-to-date managed client
            var upToDateManaged = managedClients
                .FirstOrDefault(x => x.Client != null && string.Equals((string)x.Client.Version, latestVersion, StringComparison.OrdinalIgnoreCase));

            if (upToDateManaged != null)
            {
                // Managed and up-to-date exists!
                bool profileExists = await gameClientProfileService.ProfileExistsForGameClientAsync((string)upToDateManaged.Client.Id, cancellationToken);

                if (profileExists)
                {
                    // Everything is perfect. Skip wizard, no action.
                    return (true, GameClientConstants.WizardActionTypes.Decline);
                }
                else
                {
                    // Content is there, just needs a profile. Skip wizard, auto-accept.
                    return (true, GameClientConstants.WizardActionTypes.CreateProfile);
                }
            }

            // If we reach here, we don't have a managed up-to-date client.
            // Check if any profiles exist for this component (managed or unmanaged)
            bool anyProfileExists = false;
            foreach (var x in componentGlobal)
            {
                if (x.Client != null && await gameClientProfileService.ProfileExistsForGameClientAsync((string)x.Client.Id, cancellationToken))
                {
                    anyProfileExists = true;
                }
            }

            var detectedVersion = componentGlobal.Select(x => x.Client?.Version).FirstOrDefault() ?? GameClientConstants.UnknownVersion;
            var isDetected = componentGlobal.Count > 0;

            // Construct Wizard Item
            var item = new SetupWizardItemViewModel
            {
                Title = title,
                IsSelected = true,
                IconPath = iconPath,
                Metadata = metadata,
                Version = latestVersion,
            };

            if (anyProfileExists)
            {
                // Profile exists but it is not the latest managed version
                item.Status = "Installed";
                item.Description = $"Update existing {title} profiles to v{latestVersion}.";
                item.ActionLabel = "Update / Reinstall";
                item.ActionType = GameClientConstants.WizardActionTypes.Update;
            }
            else if (isDetected)
            {
                // Unmanaged files detected but no profile
                item.Status = "Detected";
                item.Description = $"Detected installed {title}. Install managed v{latestVersion} and create profiles?";
                item.ActionLabel = "Create Profile";
                item.ActionType = GameClientConstants.WizardActionTypes.CreateProfile;
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

        // Process all components
        var cpRes = await ProcessComponentAsync(
            cpGlobal,
            cpLatestVersion,
            "Community Patch",
            $"Download and install Community Patch v{cpLatestVersion} (Highly Recommended). Includes fixes, maps, and GenTool.",
            CommunityOutpostConstants.LogoSource,
            CommunityOutpostConstants.PublisherType);
        result.CommunityPatchAction = cpRes.FinalAction;

        var goRes = await ProcessComponentAsync(
            goGlobal,
            goLatestVersion,
            "Generals Online",
            $"Download and install Generals Online v{goLatestVersion} for multiplayer support.",
            UriConstants.GeneralsOnlineLogoUri,
            PublisherTypeConstants.GeneralsOnline);
        result.GeneralsOnlineAction = goRes.FinalAction;

        var shRes = await ProcessComponentAsync(
            shGlobal,
            shLatestVersion,
            "The Super Hackers",
            "Install The Super Hackers for advanced modding and features.",
            UriConstants.SuperHackersLogoUri,
            PublisherTypeConstants.TheSuperHackers);
        result.SuperHackersAction = shRes.FinalAction;

        // 3. Presentation Phase: Show Wizard
        if (wizardItems.Count > 0)
        {
            var wizardVm = new SetupWizardViewModel(wizardItems);
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
        else
        {
             // If we didn't show the wizard, it means we either had nothing to do or only auto-accept actions.
             result.Confirmed = true;
        }

        // 4. Final decisions: If item was in wizard, override with user selection
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
