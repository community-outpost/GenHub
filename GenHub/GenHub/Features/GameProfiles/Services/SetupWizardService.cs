using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using GenHub.Features.GameProfiles.Views.Wizard;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        static bool IsCpRetailClient(GameClient c) =>
            c.PublisherType == CommunityOutpostConstants.PublisherType &&
            !CommunityOutpostConstants.IsBaseGameIdentifier(c.Id) &&
            !CommunityOutpostConstants.IsBaseGameIdentifier(c.Name) &&
            !CommunityOutpostConstants.IsNonRetailIdentifier(c.Id) &&
            !CommunityOutpostConstants.IsNonRetailIdentifier(c.Name);

        static bool IsCpNonRetClient(GameClient c) =>
            c.PublisherType == CommunityOutpostConstants.PublisherType &&
            !CommunityOutpostConstants.IsBaseGameIdentifier(c.Id) &&
            !CommunityOutpostConstants.IsBaseGameIdentifier(c.Name) &&
            (CommunityOutpostConstants.IsNonRetailIdentifier(c.Id) ||
             CommunityOutpostConstants.IsNonRetailIdentifier(c.Name));

        static bool IsGeneralsOnlineClient(GameClient c) => c.PublisherType == PublisherTypeConstants.GeneralsOnline;

        static bool IsSuperHackersClient(GameClient c) => c.PublisherType == PublisherTypeConstants.TheSuperHackers;

        var cpRetailGlobal = installationsList.Select(inst => new
        {
            Inst = inst,
            Client = SelectClient(inst, IsCpRetailClient),
        }).Where(x => x.Client != null).ToList();

        var cpNonRetGlobal = installationsList.Select(inst => new
        {
            Inst = inst,
            Client = SelectClient(inst, IsCpNonRetClient),
        }).Where(x => x.Client != null).ToList();

        var goGlobal = installationsList.Select(inst => new
        {
            Inst = inst,
            Client = SelectClient(inst, IsGeneralsOnlineClient),
        }).Where(x => x.Client != null).ToList();

        var shGlobal = installationsList.Select(inst => new
        {
            Inst = inst,
            Client = SelectClient(inst, IsSuperHackersClient),
        }).Where(x => x.Client != null).ToList();

        // 3. Collection Phase: Build Wizard Items
        var wizardItems = new List<SetupWizardItemViewModel>();

        // Pre-fetch latest versions
        var (cpRetailLatestVersion, cpNonRetLatestVersion) = await GetLatestCommunityPatchVersionsAsync();
        var goLatestVersion = await GetLatestVersionAsync(PublisherTypeConstants.GeneralsOnline);
        var shLatestVersion = await GetLatestVersionAsync(PublisherTypeConstants.TheSuperHackers);

        // Initialize default actions (Decline/None)
        result.CommunityPatchAction = GameClientConstants.WizardActionTypes.Decline;
        result.CommunityPatchNonRetAction = GameClientConstants.WizardActionTypes.Decline;
        result.GeneralsOnlineAction = GameClientConstants.WizardActionTypes.Decline;
        result.SuperHackersAction = GameClientConstants.WizardActionTypes.Decline;

        // Helper to check for managed/up-to-date client for a specific global list
        async Task<(bool SkipWizard, string FinalAction, bool IsNative, SetupWizardItemViewModel? Item)> ProcessComponentAsync(WizardComponentConfig config)
        {
            var componentGlobal = config.ComponentGlobal.Cast<dynamic>().ToList();

            // A native client on macOS or Linux is profiled as it is: the publisher package is a
            // Windows build, so there is nothing to download or update for it. This runs before the
            // up-to-date checks, which look at Windows packages and would otherwise hide it.
            // Every native build matching this component counts, not only the client selected per
            // installation, so a profiled Zero Hour build does not hide an unprofiled Generals build
            // beside it.
            var nativeClients = componentGlobal
                .Select(x => x.Inst as GameInstallation)
                .OfType<GameInstallation>()
                .SelectMany(inst => inst.AvailableGameClients)
                .Where(c => config.ClientFilter(c) && PublisherProfileOrchestrator.IsHostNativeClient(c))
                .ToList();
            if (nativeClients.Count > 0)
            {
                // Only profiles for the native builds themselves count; one for a Windows build of
                // the same publisher (for example under Wine) still leaves a native build unprofiled.
                foreach (var (nativeClientId, nativeVersion) in nativeClients.Select(c => (c.Id, c.Version)))
                {
                    if (string.IsNullOrEmpty(nativeClientId) ||
                        !await gameClientProfileService.ProfileExistsForGameClientAsync(nativeClientId, cancellationToken))
                    {
                        var nativeItem = new SetupWizardItemViewModel
                        {
                            Title = config.Title,
                            Status = GameClientConstants.WizardStatuses.Detected,
                            Description = FormatCreateProfileDescription(config.Title, nativeVersion) + (config.DescriptionSuffix ?? string.Empty),
                            Version = nativeVersion == GameClientConstants.UnknownVersion ? string.Empty : nativeVersion,
                            ActionLabel = GameClientConstants.WizardActionLabels.CreateProfile,
                            ActionType = GameClientConstants.WizardActionTypes.CreateProfile,
                            IsSelected = true,
                            IconPath = config.IconPath,
                            Metadata = config.Metadata,
                        };
                        wizardItems.Add(nativeItem);
                        return (false, nativeItem.ActionType, true, nativeItem);
                    }
                }

                logger.LogInformation("[SetupWizard] Native client and profile found for {Title}, nothing to update", config.Title);
                return (true, GameClientConstants.WizardActionTypes.Decline, true, null);
            }

            // 1. Identify managed clients in the manifest pool
            var managedManifests = allPoolManifests
                .Where(m => m.ContentType == ContentType.GameClient &&
                            (string.Equals(m.Publisher?.PublisherType, config.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                             m.Id.Value.Contains($".{config.PublisherType}.", StringComparison.OrdinalIgnoreCase)) &&
                            (config.ManifestFilter == null || config.ManifestFilter(m)))
                .ToList();

            // 2. Check if any managed client in the pool is up-to-date
            var upToDateManagedManifest = managedManifests
                .FirstOrDefault(m => !string.IsNullOrEmpty(config.LatestVersion) &&
                                     string.Equals(CleanVersionString(m.Version), config.LatestVersion, StringComparison.OrdinalIgnoreCase));

            if (upToDateManagedManifest != null)
            {
                bool profileExists = await gameClientProfileService.ProfileExistsForGameClientAsync(upToDateManagedManifest.Id.Value, cancellationToken);
                if (profileExists)
                {
                    logger.LogInformation("[SetupWizard] Managed up-to-date manifest and profile found for {Title} ({Version})", config.Title, config.LatestVersion);
                    return (true, GameClientConstants.WizardActionTypes.Decline, false, null);
                }

                logger.LogInformation("[SetupWizard] Managed up-to-date manifest found for {Title} ({Version}), but profile missing. Showing in wizard to create profile.", config.Title, config.LatestVersion);
                var downloadedItem = new SetupWizardItemViewModel
                {
                    Title = config.Title,
                    Status = GameClientConstants.WizardStatuses.Downloaded,
                    Description = FormatCreateProfileDescription(config.Title, config.LatestVersion) + (config.DescriptionSuffix ?? string.Empty),
                    ActionLabel = GameClientConstants.WizardActionLabels.CreateProfile,
                    ActionType = GameClientConstants.WizardActionTypes.CreateProfile,
                    IsSelected = true,
                    IconPath = config.IconPath,
                    Metadata = config.Metadata,
                    Version = config.LatestVersion,
                };
                wizardItems.Add(downloadedItem);
                return (false, downloadedItem.ActionType, false, downloadedItem);
            }

            // Also check unmanaged clients from installations in case an unmanaged client is managed/has ID
            var managedClientsFromInstallations = componentGlobal
                .Where(x => x.Client != null && !string.IsNullOrEmpty((string)x.Client.Id))
                .ToList();

            var upToDateFromInstallations = managedClientsFromInstallations
                .FirstOrDefault(x => x.Client != null &&
                                     !string.IsNullOrEmpty(config.LatestVersion) &&
                                     string.Equals(CleanVersionString((string)x.Client.Version), config.LatestVersion, StringComparison.OrdinalIgnoreCase));

            if (upToDateFromInstallations != null)
            {
                bool profileExists = await gameClientProfileService.ProfileExistsForGameClientAsync((string)upToDateFromInstallations.Client.Id, cancellationToken);
                if (profileExists)
                {
                    logger.LogInformation("[SetupWizard] Up-to-date client and profile found in installations for {Title} ({Version})", config.Title, config.LatestVersion);
                    return (true, GameClientConstants.WizardActionTypes.Decline, false, null);
                }

                logger.LogInformation("[SetupWizard] Up-to-date client found in installations for {Title} ({Version}), profile missing. Showing in wizard to create profile.", config.Title, config.LatestVersion);
                var detectedItem = new SetupWizardItemViewModel
                {
                    Title = config.Title,
                    Status = GameClientConstants.WizardStatuses.Detected,
                    Description = FormatCreateProfileDescription(config.Title, config.LatestVersion) + (config.DescriptionSuffix ?? string.Empty),
                    ActionLabel = GameClientConstants.WizardActionLabels.CreateProfile,
                    ActionType = GameClientConstants.WizardActionTypes.CreateProfile,
                    IsSelected = true,
                    IconPath = config.IconPath,
                    Metadata = config.Metadata,
                    Version = config.LatestVersion,
                };
                wizardItems.Add(detectedItem);
                return (false, detectedItem.ActionType, false, detectedItem);
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
            var displayVersion = !string.IsNullOrEmpty(config.LatestVersion) && config.LatestVersion != GameClientConstants.UnknownVersion
                ? config.LatestVersion
                : (managedManifests.FirstOrDefault()?.Version ?? config.LatestVersion);

            // Construct Wizard Item
            var item = new SetupWizardItemViewModel
            {
                Title = config.Title,
                IsSelected = config.DefaultSelected,
                IconPath = config.IconPath,
                Metadata = config.Metadata,
                Version = displayVersion,
            };

            if (anyProfileExists)
            {
                // Profile exists, but it is not the latest managed version
                item.Status = GameClientConstants.WizardStatuses.Installed;
                item.Description = FormatUpdateProfileDescription(config.Title, displayVersion) + (config.DescriptionSuffix ?? string.Empty);
                item.ActionLabel = GameClientConstants.WizardActionLabels.UpdateReinstall;
                item.ActionType = GameClientConstants.WizardActionTypes.Update;
                item.IsSelected = false;
            }
            else if (managedManifests.Count > 0)
            {
                // Content downloaded in pool, but no profile exists
                item.Status = GameClientConstants.WizardStatuses.Downloaded;
                item.Description = FormatCreateProfileDescription(config.Title, displayVersion) + (config.DescriptionSuffix ?? string.Empty);
                item.ActionLabel = GameClientConstants.WizardActionLabels.CreateProfile;
                item.ActionType = GameClientConstants.WizardActionTypes.CreateProfile;
                item.IsSelected = true;
            }
            else if (isDetected)
            {
                // Unmanaged files detected but no profile
                item.Status = GameClientConstants.WizardStatuses.Detected;
                item.Description = FormatDetectedInstallDescription(config.Title, config.LatestVersion) + (config.DescriptionSuffix ?? string.Empty);
                item.ActionLabel = GameClientConstants.WizardActionLabels.DownloadAndInstall;
                item.ActionType = GameClientConstants.WizardActionTypes.Install;
                item.IsSelected = true;
            }
            else
            {
                // Nothing found at all
                item.Status = GameClientConstants.WizardStatuses.Missing;
                item.Description = config.MissingDescription + (config.DescriptionSuffix ?? string.Empty);
                item.ActionLabel = GameClientConstants.WizardActionLabels.DownloadAndInstall;
                item.ActionType = GameClientConstants.WizardActionTypes.Install;
                item.IsSelected = config.DefaultSelected;
            }

            wizardItems.Add(item);
            return (false, item.ActionType, false, item);
        }

        var cpRetailCleanVersion = CleanVersionString(cpRetailLatestVersion);
        var cpNonRetCleanVersion = CleanVersionString(cpNonRetLatestVersion);
        var goCleanVersion = CleanVersionString(goLatestVersion);
        var shCleanVersion = CleanVersionString(shLatestVersion);

        static bool IsCpRetailManifest(ContentManifest m) =>
            CommunityOutpostConstants.IsCommunityPatch(m) &&
            !CommunityOutpostConstants.IsNonRetailIdentifier(m.Id.Value) &&
            !CommunityOutpostConstants.IsNonRetailIdentifier(m.Name) &&
            !(m.Metadata?.Tags != null && m.Metadata.Tags.Any(CommunityOutpostConstants.IsNonRetailIdentifier));

        static bool IsCpNonRetManifest(ContentManifest m) =>
            CommunityOutpostConstants.IsCommunityPatch(m) &&
            (CommunityOutpostConstants.IsNonRetailIdentifier(m.Id.Value) ||
             CommunityOutpostConstants.IsNonRetailIdentifier(m.Name) ||
             (m.Metadata?.Tags != null && m.Metadata.Tags.Any(CommunityOutpostConstants.IsNonRetailIdentifier)));

        var cpRetailDescription = string.IsNullOrEmpty(cpRetailCleanVersion)
            ? "Download and install Community Patch (Retail)."
            : $"Download and install Community Patch (Retail) {cpRetailCleanVersion}.";

        var cpNonRetDescription = string.IsNullOrEmpty(cpNonRetCleanVersion)
            ? "Download and install Community Patch (Non-Retail)."
            : $"Download and install Community Patch (Non-Retail) {cpNonRetCleanVersion}.";

        // Process all components
        var cpRetailConfig = new WizardComponentConfig
        {
            PublisherType = CommunityOutpostConstants.PublisherType,
            ComponentGlobal = cpRetailGlobal,
            ClientFilter = IsCpRetailClient,
            LatestVersion = cpRetailCleanVersion,
            Title = "Community Patch (Retail)",
            MissingDescription = cpRetailDescription,
            IconPath = CommunityOutpostConstants.LogoSource,
            Metadata = CommunityOutpostConstants.CommunityPatchRetailCode,
            ManifestFilter = IsCpRetailManifest,
            DefaultSelected = true,
        };
        var cpRetailRes = await ProcessComponentAsync(cpRetailConfig);
        result.CommunityPatchAction = cpRetailRes.FinalAction;

        var cpNonRetConfig = new WizardComponentConfig
        {
            PublisherType = CommunityOutpostConstants.PublisherType,
            ComponentGlobal = cpNonRetGlobal,
            ClientFilter = IsCpNonRetClient,
            LatestVersion = cpNonRetCleanVersion,
            Title = "Community Patch (Non-Retail)",
            MissingDescription = cpNonRetDescription,
            IconPath = CommunityOutpostConstants.LogoSource,
            Metadata = CommunityOutpostConstants.CommunityPatchNonRetCode,
            ManifestFilter = IsCpNonRetManifest,
            DefaultSelected = false,
            DescriptionSuffix = " Not compatible with retail 1.04 zero hour.",
        };
        var cpNonRetRes = await ProcessComponentAsync(cpNonRetConfig);
        result.CommunityPatchNonRetAction = cpNonRetRes.FinalAction;

        // A native non-retail build is profiled as it is. Leave the Windows retail package unselected
        // so confirming the wizard does not also download it next to the non-retail build.
        if (cpNonRetRes.IsNative && !cpRetailRes.IsNative)
        {
            if (cpRetailRes.Item != null)
            {
                cpRetailRes.Item.IsSelected = false;
            }

            result.CommunityPatchAction = GameClientConstants.WizardActionTypes.Decline;
        }

        var goConfig = new WizardComponentConfig
        {
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            ComponentGlobal = goGlobal,
            ClientFilter = IsGeneralsOnlineClient,
            LatestVersion = goCleanVersion,
            Title = "Generals Online",
            MissingDescription = string.IsNullOrEmpty(goCleanVersion) ? "Download and install Generals Online." : $"Download and install Generals Online {goCleanVersion}.",
            IconPath = UriConstants.GeneralsOnlineLogoUri,
            Metadata = PublisherTypeConstants.GeneralsOnline,
            DefaultSelected = true,
        };
        var goRes = await ProcessComponentAsync(goConfig);
        result.GeneralsOnlineAction = goRes.FinalAction;

        var shConfig = new WizardComponentConfig
        {
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            ComponentGlobal = shGlobal,
            ClientFilter = IsSuperHackersClient,
            LatestVersion = shCleanVersion,
            Title = "TheSuperHackers",
            MissingDescription = string.IsNullOrEmpty(shCleanVersion) ? "Download and install TheSuperHackers." : $"Download and install TheSuperHackers {shCleanVersion}.",
            IconPath = UriConstants.SuperHackersLogoUri,
            Metadata = PublisherTypeConstants.TheSuperHackers,
            DefaultSelected = false,
        };
        var shRes = await ProcessComponentAsync(shConfig);
        result.SuperHackersAction = shRes.FinalAction;

        // 4. Show Wizard Dialog if there are items to review
        if (wizardItems.Count > 0)
        {
            logger.LogInformation("[SetupWizard] Showing wizard with {Count} item(s)", wizardItems.Count);
            var wizardVm = new SetupWizardViewModel(wizardItems);

            var accepted = await ShowWizardDialogAsync(wizardVm);
            result.Confirmed = accepted;
            if (accepted)
            {
                logger.LogInformation("[SetupWizard] User accepted wizard selections");

                // Read decisions back to result
                foreach (var item in wizardVm.Items)
                {
                    var finalAction = item.IsSelected ? item.ActionType : GameClientConstants.WizardActionTypes.Decline;
                    if (string.Equals(item.Metadata as string, CommunityOutpostConstants.CommunityPatchRetailCode, StringComparison.Ordinal))
                    {
                        result.CommunityPatchAction = finalAction;
                    }
                    else if (string.Equals(item.Metadata as string, CommunityOutpostConstants.CommunityPatchNonRetCode, StringComparison.Ordinal))
                    {
                        result.CommunityPatchNonRetAction = finalAction;
                    }
                    else if (string.Equals(item.Metadata as string, PublisherTypeConstants.GeneralsOnline, StringComparison.Ordinal))
                    {
                        result.GeneralsOnlineAction = finalAction;
                    }
                    else if (string.Equals(item.Metadata as string, PublisherTypeConstants.TheSuperHackers, StringComparison.Ordinal))
                    {
                        result.SuperHackersAction = finalAction;
                    }
                }
            }
            else
            {
                logger.LogInformation("[SetupWizard] User canceled or declined the wizard");
                result.CommunityPatchAction = GameClientConstants.WizardActionTypes.Decline;
                result.CommunityPatchNonRetAction = GameClientConstants.WizardActionTypes.Decline;
                result.GeneralsOnlineAction = GameClientConstants.WizardActionTypes.Decline;
                result.SuperHackersAction = GameClientConstants.WizardActionTypes.Decline;
            }
        }
        else
        {
            logger.LogInformation("[SetupWizard] No wizard items required. All detected clients are already up to date.");
            result.Confirmed = true;
        }

        return result;
    }

    /// <summary>
    /// Selects the installation's client that matches <paramref name="predicate"/>, preferring a
    /// build native to this host so a Windows build of the same publisher does not hide it.
    /// </summary>
    private static GameClient? SelectClient(GameInstallation installation, Func<GameClient, bool> predicate) =>
        installation.AvailableGameClients
            .Where(predicate)
            .OrderBy(c => PublisherProfileOrchestrator.IsHostNativeClient(c) ? 0 : 1)
            .FirstOrDefault();

    private static string FormatCreateProfileDescription(string title, string? version) =>
        string.IsNullOrEmpty(version) || version == GameClientConstants.UnknownVersion
            ? $"Create a game profile for {title}."
            : $"Create a game profile for {title} {version}.";

    private static string FormatUpdateProfileDescription(string title, string? version) =>
        string.IsNullOrEmpty(version) || version == GameClientConstants.UnknownVersion
            ? $"Update {title} to the latest version."
            : $"Update {title} to version {version}.";

    private static string FormatDetectedInstallDescription(string title, string? version) =>
        string.IsNullOrEmpty(version) || version == GameClientConstants.UnknownVersion
            ? $"Download and install managed {title} files."
            : $"Download and install managed {title} {version} files.";

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

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    private async Task<(string RetailVersion, string NonRetVersion)> GetLatestCommunityPatchVersionsAsync()
    {
        try
        {
            var result = await communityOutpostDiscoverer.DiscoverAsync(new ContentSearchQuery());
            if (result.Success && result.Data?.Items != null)
            {
                var cpItems = result.Data.Items.Where(CommunityOutpostConstants.IsCommunityPatch).ToList();

                var retail = cpItems.FirstOrDefault(i =>
                    !CommunityOutpostConstants.IsNonRetailIdentifier(i.Id) &&
                    !CommunityOutpostConstants.IsNonRetailIdentifier(i.Name))?.Version ?? string.Empty;

                var nonRet = cpItems.FirstOrDefault(i =>
                    CommunityOutpostConstants.IsNonRetailIdentifier(i.Id) ||
                    CommunityOutpostConstants.IsNonRetailIdentifier(i.Name))?.Version ?? string.Empty;

                return (retail, nonRet);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve latest Community Patch versions");
        }

        return (string.Empty, string.Empty);
    }

    private async Task<string> GetLatestVersionAsync(string publisher)
    {
        try
        {
            if (publisher == PublisherTypeConstants.GeneralsOnline)
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

    /// <summary>
    /// Shows the wizard dialog asynchronously, respecting testing hooks.
    /// </summary>
    private async Task<bool> ShowWizardDialogAsync(SetupWizardViewModel viewModel)
    {
        if (DialogShower != null)
        {
            return await DialogShower(viewModel);
        }

        var mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            logger.LogWarning("[SetupWizard] Cannot display wizard dialog: MainWindow is null");
            return false;
        }

        var dialog = new SetupWizardView
        {
            DataContext = viewModel,
        };

        await dialog.ShowDialog(mainWindow);
        return viewModel.Confirmed;
    }

    /// <summary>
    /// Configuration model for processing an individual setup wizard component.
    /// </summary>
    private sealed record WizardComponentConfig
    {
        /// <summary>Gets the publisher type.</summary>
        public required string PublisherType { get; init; }

        /// <summary>Gets the collection of globally available clients for this component.</summary>
        public required System.Collections.IEnumerable ComponentGlobal { get; init; }

        /// <summary>Gets the predicate that selects this component's clients in an installation.</summary>
        public required Func<GameClient, bool> ClientFilter { get; init; }

        /// <summary>Gets the latest discovered version string.</summary>
        public required string LatestVersion { get; init; }

        /// <summary>Gets the display title for the component.</summary>
        public required string Title { get; init; }

        /// <summary>Gets the description when the component is missing.</summary>
        public required string MissingDescription { get; init; }

        /// <summary>Gets the icon or logo URI/path.</summary>
        public required string IconPath { get; init; }

        /// <summary>Gets the metadata identifier for wizard action finalization.</summary>
        public required string Metadata { get; init; }

        /// <summary>Gets an optional filter predicate for pool manifests.</summary>
        public Func<ContentManifest, bool>? ManifestFilter { get; init; }

        /// <summary>Gets a value indicating whether the component is selected by default.</summary>
        public bool DefaultSelected { get; init; }

        /// <summary>Gets an optional description suffix (e.g., compatibility warnings).</summary>
        public string? DescriptionSuffix { get; init; }
    }
}
