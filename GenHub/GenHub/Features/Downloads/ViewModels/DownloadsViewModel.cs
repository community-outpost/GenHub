using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the Downloads tab.
/// </summary>
public partial class DownloadsViewModel(
    IServiceProvider serviceProvider,
    ILogger<DownloadsViewModel> logger,
    INotificationService notificationService,
    GitHubTopicsDiscoverer gitHubTopicsDiscoverer) : ViewModelBase, IRecipient<GenHub.Core.Messages.OpenPublisherDetailsMessage>, IRecipient<GenHub.Core.Messages.ClosePublisherDetailsMessage>
{
    private bool _isPublisherContentPopulated;

    [ObservableProperty]
    private string _title = "Downloads";

    [ObservableProperty]
    private string _description = "Manage your downloads and installations";

    [ObservableProperty]
    private bool _isInstallingGeneralsOnline;

    [ObservableProperty]
    private string _installationStatus = string.Empty;

    [ObservableProperty]
    private double _installationProgress;

    [ObservableProperty]
    private string _generalsOnlineVersion = "Loading...";

    [ObservableProperty]
    private string _weeklyReleaseVersion = "Loading...";

    [ObservableProperty]
    private string _communityPatchVersion = "Loading...";

    [ObservableProperty]
    private string _communityPatchStatus = string.Empty;

    [ObservableProperty]
    private double _communityPatchProgress;

    [ObservableProperty]
    private ObservableCollection<PublisherCardViewModel> _publisherCards = [];

    /// <summary>
    /// Performs asynchronous initialization for the Downloads tab.
    /// Fetches latest version information from all publishers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        try
        {
            // Initialize publisher cards
            InitializePublisherCards();

            // Fetch version information from all publishers concurrently
            var generalsOnlineTask = FetchGeneralsOnlineVersionAsync();
            var weeklyReleaseTask = FetchWeeklyReleaseVersionAsync();
            var communityPatchTask = FetchCommunityPatchVersionAsync();

            await Task.WhenAll(generalsOnlineTask, weeklyReleaseTask, communityPatchTask);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Downloads view");
        }
    }

    /// <summary>
    /// Called when the tab is activated/navigated to.
    /// Refreshes installation status for all publisher cards.
    /// </summary>
    /// <summary>
    /// Handle activation of the Downloads tab: register message recipients if needed, lazily populate publisher cards, and refresh installation status for any expanded publisher cards.
    /// </summary>
    /// <returns>A <see cref="Task"/> that completes when the activation processing and any required status refreshes have finished.</returns>
    public async Task OnTabActivatedAsync()
    {
        // Ensure we are registered for messages
        if (!WeakReferenceMessenger.Default.IsRegistered<Core.Messages.OpenPublisherDetailsMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<Core.Messages.OpenPublisherDetailsMessage>(this);
        }

        if (!WeakReferenceMessenger.Default.IsRegistered<Core.Messages.ClosePublisherDetailsMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<Core.Messages.ClosePublisherDetailsMessage>(this);
        }

        // Lazy-load publisher card content if not already populated
        if (!_isPublisherContentPopulated)
        {
            _isPublisherContentPopulated = true;
            _ = PopulatePublisherCardsAsync();
        }

        try
        {
            foreach (var card in PublisherCards)
            {
                if (card.IsExpanded)
                {
                    await card.RefreshInstallationStatusAsync();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh installation status on tab activation");
        }
    }

    private static string GetContentTypeDisplayName(ContentType type)
    {
        return type switch
        {
            ContentType.GameClient => UiConstants.GameClientDisplayName,
            ContentType.MapPack => UiConstants.MapPackDisplayName,
            ContentType.Patch => UiConstants.PatchDisplayName,
            ContentType.Addon => UiConstants.AddonDisplayName,
            ContentType.Mod => UiConstants.ModDisplayName,
            ContentType.Mission => UiConstants.MissionDisplayName,
            ContentType.Map => UiConstants.MapDisplayName,
            ContentType.LanguagePack => UiConstants.LanguagePackDisplayName,
            ContentType.ContentBundle => UiConstants.ContentBundleDisplayName,
            _ => type.ToString(),
        };
    }

    /// <summary>
    /// Create and initialize the collection of publisher card view models used by the Downloads tab.
    /// </summary>
    /// <remarks>
    /// Instantiates publisher cards from the service provider, sets identifying metadata (PublisherId, DisplayName, LogoSource, ReleaseNotes),
    /// marks them as loading, and assigns the resulting list to <see cref="PublisherCards"/>. Cards populated include Generals Online,
    /// TheSuperHackers, Community Outpost, GitHub topics, CNC Labs, and ModDB.
    /// </remarks>
    private void InitializePublisherCards()
    {
        var publishers = new System.Collections.Generic.List<PublisherCardViewModel>();

        // Create Generals Online publisher card (Feature 2)
        if (serviceProvider.GetService(typeof(PublisherCardViewModel)) is PublisherCardViewModel generalsOnlineCard)
        {
            generalsOnlineCard.PublisherId = GeneralsOnlineConstants.PublisherType;
            generalsOnlineCard.DisplayName = GeneralsOnlineConstants.ContentName;
            generalsOnlineCard.LogoSource = GeneralsOnlineConstants.LogoSource;
            generalsOnlineCard.ReleaseNotes = GeneralsOnlineConstants.ShortDescription;
            generalsOnlineCard.IsLoading = true;
            publishers.Add(generalsOnlineCard);
        }

        // Create TheSuperHackers publisher card
        if (serviceProvider.GetService(typeof(PublisherCardViewModel)) is PublisherCardViewModel superHackersCard)
        {
            superHackersCard.PublisherId = PublisherTypeConstants.TheSuperHackers;
            superHackersCard.DisplayName = SuperHackersConstants.PublisherName;
            superHackersCard.LogoSource = SuperHackersConstants.LogoSource;
            superHackersCard.ReleaseNotes = SuperHackersConstants.ProviderDescription;
            superHackersCard.IsLoading = true;
            publishers.Add(superHackersCard);
        }

        // Create Community Outpost publisher card
        if (serviceProvider.GetService(typeof(PublisherCardViewModel)) is PublisherCardViewModel communityOutpostCard)
        {
            communityOutpostCard.PublisherId = CommunityOutpostConstants.PublisherType;
            communityOutpostCard.DisplayName = CommunityOutpostConstants.PublisherName;
            communityOutpostCard.LogoSource = CommunityOutpostConstants.LogoSource;
            communityOutpostCard.ReleaseNotes = CommunityOutpostConstants.ProviderDescription;
            communityOutpostCard.IsLoading = true;
            publishers.Add(communityOutpostCard);
        }

        // Create GitHub publisher card (topic-based discovery)
        if (serviceProvider.GetService(typeof(PublisherCardViewModel)) is PublisherCardViewModel githubCard)
        {
            githubCard.PublisherId = GitHubTopicsConstants.PublisherType;
            githubCard.DisplayName = GitHubTopicsConstants.PublisherName;
            githubCard.LogoSource = GitHubTopicsConstants.LogoSource;
            githubCard.ReleaseNotes = GitHubTopicsConstants.ProviderDescription;
            githubCard.IsLoading = true;
            publishers.Add(githubCard);
        }

        // Create CNC Labs publisher card
        if (serviceProvider.GetService(typeof(PublisherCardViewModel)) is PublisherCardViewModel cncLabsCard)
        {
            cncLabsCard.PublisherId = CNCLabsConstants.PublisherType;
            cncLabsCard.DisplayName = CNCLabsConstants.PublisherName;
            cncLabsCard.LogoSource = CNCLabsConstants.LogoSource;
            cncLabsCard.ReleaseNotes = CNCLabsConstants.ShortDescription;
            cncLabsCard.IsLoading = true;
            publishers.Add(cncLabsCard);
        }

        // Create ModDB publisher card
        if (serviceProvider.GetService(typeof(PublisherCardViewModel)) is PublisherCardViewModel modDBCard)
        {
            modDBCard.PublisherId = ModDBConstants.PublisherType;
            modDBCard.DisplayName = ModDBConstants.PublisherDisplayName;
            modDBCard.LogoSource = ModDBConstants.LogoSource;
            modDBCard.ReleaseNotes = ModDBConstants.ShortDescription;
            modDBCard.IsLoading = true;
            publishers.Add(modDBCard);
        }

        PublisherCards = new ObservableCollection<PublisherCardViewModel>(publishers);
    }

    /// <summary>
    /// Populate all publisher cards.
    /// </summary>
    /// <returns>Completion of all publisher card population operations.</returns>
    private async Task PopulatePublisherCardsAsync()
    {
        await Task.WhenAll(
            PopulateGeneralsOnlineCardAsync(),
            PopulateSuperHackersCardAsync(),
            PopulateCommunityOutpostCardAsync(),
            PopulateGithubCardAsync(),
            PopulateCNCLabsCardAsync(),
            PopulateModDBCardAsync());
    }

    /// <summary>
    /// Populates the Generals Online publisher card with discovered release items and metadata.
    /// </summary>
    /// <remarks>
    /// Locates the Generals Online card, discovers releases via the GeneralsOnlineDiscoverer (if available),
    /// groups discovered releases by content type into ContentTypeGroup entries, and updates the card's
    /// LatestVersion, DownloadSize, and ReleaseDate from the most recent release. On failure the card is
    /// marked with an error message. The card's IsLoading flag is cleared when processing completes.
    /// </remarks>
    private async Task PopulateGeneralsOnlineCardAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == GeneralsOnlineConstants.PublisherType);
            if (card == null) return;

            if (serviceProvider.GetService(typeof(GeneralsOnlineDiscoverer)) is not GeneralsOnlineDiscoverer discoverer) return;

            var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
            if (result.Success && result.Data?.Items?.Any() == true)
            {
                var releases = result.Data.Items.ToList();

                // Group by content type
                var groupedContent = releases.GroupBy(r => r.ContentType).ToList();

                foreach (var group in groupedContent)
                {
                    var contentGroup = new ContentTypeGroup
                    {
                        Type = group.Key,
                        DisplayName = GetContentTypeDisplayName(group.Key),
                        Count = group.Count(),
                        Items = new ObservableCollection<ContentItemViewModel>(
                            group.Select(item => new ContentItemViewModel(item))),
                    };
                    card.ContentTypes.Add(contentGroup);
                }

                // Set card metadata from first release
                var latest = releases.FirstOrDefault();
                if (latest != null)
                {
                    card.LatestVersion = latest.Version;
                    card.DownloadSize = latest.DownloadSize;
                    card.ReleaseDate = latest.LastUpdated;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate Generals Online card");
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == GeneralsOnlineConstants.PublisherType);
            if (card != null)
            {
                card.HasError = true;
                card.ErrorMessage = "Failed to load content";
            }
        }
        finally
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == GeneralsOnlineConstants.PublisherType);
            if (card != null) card.IsLoading = false;
        }
    }

    /// <summary>
    /// Populates the "TheSuperHackers" publisher card with GitHub release data and related metadata.
    /// </summary>
    /// <remarks>
    /// Queries the GitHubReleasesDiscoverer for releases, groups results by content type into ContentTypeGroup entries,
    /// and updates the card's LatestVersion, DownloadSize, and ReleaseDate from the most recent release.
    /// If no releases are found, sets a user-facing "No releases" summary and release notes. On discovery failure or exception,
    /// marks the card with an error and sets an error message. Always clears the card's loading state when complete.
    /// </remarks>
    private async Task PopulateSuperHackersCardAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == PublisherTypeConstants.TheSuperHackers);
            if (card == null) return;

            // Query for all configured GitHub releases
            var query = new ContentSearchQuery();
            if (serviceProvider.GetService(typeof(GitHubReleasesDiscoverer)) is not GitHubReleasesDiscoverer gitHubDiscoverer)
            {
                logger.LogWarning("GitHubReleasesDiscoverer not available for SuperHackers card");
                return;
            }

            // Query for all configured GitHub releases
            var searchQuery = new ContentSearchQuery();

            var result = await gitHubDiscoverer.DiscoverAsync(searchQuery);
            if (result.Success && result.Data?.Items?.Any() == true)
            {
                // Filter for SuperHackers content if the discoverer returns more (though config should limit it)
                // And patch the ProviderName to ensure we use the SuperHackersProvider
                var releases = result.Data.Items.Select(r =>
                {
                    r.ProviderName = GenHub.Core.Constants.PublisherTypeConstants.TheSuperHackers;
                    return r;
                }).ToList();

                // Group by content type (Patch, GameClient, Tools)
                var groupedContent = releases.GroupBy(r => r.ContentType).ToList();

                foreach (var group in groupedContent)
                {
                    var contentGroup = new ContentTypeGroup
                    {
                        Type = group.Key,
                        DisplayName = GetContentTypeDisplayName(group.Key),
                        Count = group.Count(),
                        Items = new ObservableCollection<ContentItemViewModel>(
                            group.Select(item => new ContentItemViewModel(item))),
                    };
                    card.ContentTypes.Add(contentGroup);
                }

                // Set card metadata from latest release
                var latest = releases.OrderByDescending(r => r.LastUpdated).FirstOrDefault();
                if (latest != null)
                {
                    card.LatestVersion = latest.Version;
                    card.DownloadSize = latest.DownloadSize;
                    card.ReleaseDate = latest.LastUpdated;
                }

                logger.LogInformation("Populated SuperHackers card with {Count} releases", releases.Count);
            }
            else
            {
                logger.LogWarning("No releases found for SuperHackers");
                card.LatestVersion = "No releases";
                card.ReleaseNotes = "Check GitHub for updates";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate SuperHackers card");
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == PublisherTypeConstants.TheSuperHackers);
            if (card != null)
            {
                card.HasError = true;
                card.ErrorMessage = "Failed to load GitHub releases";
            }
        }
        finally
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == PublisherTypeConstants.TheSuperHackers);
            if (card != null) card.IsLoading = false;
        }
    }

    /// <summary>
    /// Populates the Community Outpost publisher card with discovered releases grouped by content type.
    /// </summary>
    /// <remarks>
    /// If discovery succeeds, the card's ContentTypes are filled and LatestVersion, DownloadSize, and ReleaseDate
    /// are set from the newest release. On failure the card's HasError and ErrorMessage are set. The card's
    /// IsLoading flag is cleared when the operation completes.
    /// </remarks>
    /// <returns>Completion of the population operation.</returns>
    private async Task PopulateCommunityOutpostCardAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CommunityOutpostConstants.PublisherType);
            if (card == null)
            {
                return;
            }

            if (serviceProvider.GetService(typeof(GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostDiscoverer)) is not GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostDiscoverer discoverer)
            {
                return;
            }

            var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

            if (result.Success && result.Data?.Items?.Any() == true)
            {
                var releases = result.Data.Items.ToList();

                // Group by content type
                var groupedContent = releases.GroupBy(r => r.ContentType).ToList();

                foreach (var group in groupedContent)
                {
                    var contentGroup = new ContentTypeGroup
                    {
                        Type = group.Key,
                        DisplayName = GetContentTypeDisplayName(group.Key),
                        Count = group.Count(),
                        Items = new ObservableCollection<ContentItemViewModel>(
                            group.Select(item => new ContentItemViewModel(item))),
                    };
                    card.ContentTypes.Add(contentGroup);
                }

                // Set card metadata from first release
                var latest = releases.FirstOrDefault();
                if (latest != null)
                {
                    card.LatestVersion = latest.Version;
                    card.DownloadSize = latest.DownloadSize;
                    card.ReleaseDate = latest.LastUpdated;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate Community Outpost card");
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CommunityOutpostConstants.PublisherType);
            if (card != null)
            {
                card.HasError = true;
                card.ErrorMessage = "Failed to load content from legi.cc";
            }
        }
        finally
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CommunityOutpostConstants.PublisherType);
            if (card != null) card.IsLoading = false;
        }
    }

    /// <summary>
    /// Populates the GitHub topics publisher card with discovered repositories grouped by content type.
    /// </summary>
    /// <returns>A task that completes when the GitHub card has been populated and its loading state updated.</returns>
    private async Task PopulateGithubCardAsync()
    {
        var card = PublisherCards.FirstOrDefault(c => c.PublisherId == GitHubTopicsConstants.PublisherType);
        if (card == null) return;

        try
        {
            var result = await gitHubTopicsDiscoverer.DiscoverAsync(new ContentSearchQuery());
            if (result.Success && result.Data?.Items?.Any() == true)
            {
                var repositories = result.Data.Items.ToList();

                // Group by content type
                var groupedContent = repositories.GroupBy(r => r.ContentType).ToList();

                foreach (var group in groupedContent)
                {
                    var contentGroup = new ContentTypeGroup
                    {
                        Type = group.Key,
                        DisplayName = GetContentTypeDisplayName(group.Key),
                        Count = group.Count(),
                        Items = new ObservableCollection<ContentItemViewModel>(
                            group.Select(item => new ContentItemViewModel(item))),
                    };
                    card.ContentTypes.Add(contentGroup);
                }

                // Set card metadata - this is an aggregate card showing multiple repos
                // LatestVersion here represents the count of discovered repositories
                // TODO: Consider adding a separate Summary property for aggregate cards
                if (repositories.Count > 0)
                {
                    card.LatestVersion = $"{repositories.Count} repos";
                }

                logger.LogInformation("Populated GitHub card with {Count} repositories", repositories.Count);
            }
            else
            {
                logger.LogInformation("No GitHub repositories found with GenHub topics");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate GitHub card");
            card.HasError = true;
            card.ErrorMessage = "Failed to discover GitHub repositories";

            notificationService.ShowError(
                "GitHub Discovery Failed",
                $"Failed to discover GitHub repositories: {ex.Message}");
        }
        finally
        {
            card.IsLoading = false;
        }
    }

    /// <summary>
    /// Populate the CNC Labs publisher card with discovered map releases and update its metadata and state.
    /// </summary>
    /// <remarks>
    /// Locates the CNC Labs publisher card, discovers available maps, groups results by content type into the card's <c>ContentTypes</c>,
    /// and updates the card's <c>LatestVersion</c>, <c>DownloadSize</c>, and <c>ReleaseDate</c> from the discovered data.
    /// If the CNCLabsMapDiscoverer is not available, sets <c>LatestVersion</c> to "Unavailable". If discovery returns no items, sets <c>LatestVersion</c> to "Ready".
    /// On discovery failure sets the card's <c>HasError</c> to true and writes a user-facing <c>ErrorMessage</c>. The card's <c>IsLoading</c> is cleared when the operation completes.
    /// </remarks>
    private async Task PopulateCNCLabsCardAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CNCLabsConstants.PublisherType);
            if (card == null) return;

            if (serviceProvider.GetService(typeof(GenHub.Features.Content.Services.ContentDiscoverers.CNCLabsMapDiscoverer)) is not GenHub.Features.Content.Services.ContentDiscoverers.CNCLabsMapDiscoverer discoverer)
            {
                logger.LogWarning("CNCLabsMapDiscoverer not available for CNCLabs card");
                card.LatestVersion = "Unavailable";
                return;
            }

            var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
            if (result.Success && result.Data?.Items?.Any() == true)
            {
                var releases = result.Data.Items.ToList();

                // Group by content type
                var groupedContent = releases.GroupBy(r => r.ContentType).ToList();

                foreach (var group in groupedContent)
                {
                    var contentGroup = new ContentTypeGroup
                    {
                        Type = group.Key,
                        DisplayName = GetContentTypeDisplayName(group.Key),
                        Count = group.Count(),
                        Items = new ObservableCollection<ContentItemViewModel>(
                            group.Select(item => new ContentItemViewModel(item))),
                    };
                    card.ContentTypes.Add(contentGroup);
                }

                // Set card metadata from first release
                var latest = releases.FirstOrDefault();
                if (latest != null)
                {
                    card.LatestVersion = $"{releases.Count} maps";
                    card.DownloadSize = latest.DownloadSize;
                    card.ReleaseDate = latest.LastUpdated;
                }
            }
            else
            {
                card.LatestVersion = "Ready";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate CNC Labs card");
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CNCLabsConstants.PublisherType);
            if (card != null)
            {
                card.HasError = true;
                card.ErrorMessage = "Failed to load content from CNC Labs";
            }
        }
        finally
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CNCLabsConstants.PublisherType);
            if (card != null) card.IsLoading = false;
        }
    }

    /// <summary>
    /// Populates the ModDB publisher card with discovered content and metadata.
    /// </summary>
    /// <remarks>
    /// If discovery succeeds, fills the card's ContentTypes grouped by content type and updates LatestVersion, DownloadSize, and ReleaseDate. If no content is found, sets LatestVersion to "Ready". On error, marks the card HasError and sets ErrorMessage. Always clears the card's IsLoading flag when finished.
    /// </remarks>
    private async Task PopulateModDBCardAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == ModDBConstants.PublisherType);
            if (card == null) return;

            if (serviceProvider.GetService(typeof(GenHub.Features.Content.Services.ContentDiscoverers.ModDBDiscoverer)) is not GenHub.Features.Content.Services.ContentDiscoverers.ModDBDiscoverer discoverer)
            {
                logger.LogWarning("ModDBDiscoverer not available for ModDB card");
                card.LatestVersion = "Unavailable";
                return;
            }

            var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
            if (result.Success && result.Data?.Items?.Any() == true)
            {
                var releases = result.Data.Items.ToList();

                // Group by content type
                var groupedContent = releases.GroupBy(r => r.ContentType).ToList();

                foreach (var group in groupedContent)
                {
                    var contentGroup = new ContentTypeGroup
                    {
                        Type = group.Key,
                        DisplayName = GetContentTypeDisplayName(group.Key),
                        Count = group.Count(),
                        Items = new ObservableCollection<ContentItemViewModel>(
                            group.Select(item => new ContentItemViewModel(item))),
                    };
                    card.ContentTypes.Add(contentGroup);
                }

                // Set card metadata from first release
                var latest = releases.OrderByDescending(r => r.LastUpdated).FirstOrDefault();
                if (latest != null)
                {
                    card.LatestVersion = $"{releases.Count} items";
                    card.DownloadSize = latest.DownloadSize;
                    card.ReleaseDate = latest.LastUpdated;
                }
            }
            else
            {
                card.LatestVersion = "Ready";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate ModDB card");
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == ModDBConstants.PublisherType);
            if (card != null)
            {
                card.HasError = true;
                card.ErrorMessage = "Failed to load content from ModDB";
            }
        }
        finally
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == ModDBConstants.PublisherType);
            if (card != null) card.IsLoading = false;
        }
    }

    /// <summary>
    /// Retrieves the latest Generals Online release version and updates the GeneralsOnlineVersion property.
    /// </summary>
    /// <remarks>
    /// If the version cannot be retrieved, sets <c>GeneralsOnlineVersion</c> to "Unavailable" and logs a warning.
    /// </remarks>
    private async Task FetchGeneralsOnlineVersionAsync()
    {
        try
        {
            if (serviceProvider.GetService(typeof(GeneralsOnlineDiscoverer)) is GeneralsOnlineDiscoverer discoverer)
            {
                var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
                if (result.Success && result.Data?.Items?.Any() == true)
                {
                    var firstResult = result.Data.Items.First();
                    GeneralsOnlineVersion = $"v{firstResult.Version}";
                    logger.LogInformation("Fetched GeneralsOnline version: {Version}", firstResult.Version);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch GeneralsOnline version");
            GeneralsOnlineVersion = "Unavailable";
        }
    }

    /// <summary>
    /// Fetches the latest weekly release from GitHub releases and updates the WeeklyReleaseVersion property.
    /// </summary>
    /// <remarks>
    /// If a latest release is found, WeeklyReleaseVersion is set to that release's version and an informational log is written.
    /// If retrieval fails or no releases are available, WeeklyReleaseVersion is set to "Unavailable" and a warning is logged.
    /// </remarks>
    private async Task FetchWeeklyReleaseVersionAsync()
    {
        try
        {
            if (serviceProvider.GetService(typeof(GitHubReleasesDiscoverer)) is GitHubReleasesDiscoverer discoverer)
            {
                var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
                if (result.Success && result.Data?.Items?.Any() == true)
                {
                    // Filter for SuperHackers content if needed, similar to PopulateSuperHackersCardAsync
                    // For now, assuming the discoverer returns relevant releases based on config
                    var latest = result.Data.Items.OrderByDescending(r => r.LastUpdated).FirstOrDefault();
                    if (latest != null)
                    {
                        WeeklyReleaseVersion = latest.Version;
                        logger.LogInformation("Fetched Weekly Release version: {Version}", latest.Version);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch weekly release version");
            WeeklyReleaseVersion = "Unavailable";
        }
    }

    /// <summary>
    /// Fetches the latest Community Patch version and updates the CommunityPatchVersion property.
    /// </summary>
    /// <remarks>
    /// If a CommunityOutpostDiscoverer is available, queries for content and sets CommunityPatchVersion to the first discovered item's Version.
    /// On failure or when no content is found, CommunityPatchVersion is set to "Unavailable".
    /// </remarks>
    private async Task FetchCommunityPatchVersionAsync()
    {
        try
        {
            if (serviceProvider.GetService(typeof(CommunityOutpostDiscoverer)) is CommunityOutpostDiscoverer discoverer)
            {
                var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
                if (result.Success && result.Data?.Items?.Any() == true)
                {
                    var firstResult = result.Data.Items.First();
                    CommunityPatchVersion = firstResult.Version;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch community patch version");
            CommunityPatchVersion = "Unavailable";
        }
    }

    [RelayCommand]
    private async Task InstallGeneralsOnlineAsync()
    {
        IsInstallingGeneralsOnline = true;
        InstallationStatus = "Starting...";
        InstallationProgress = 0;

        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == GeneralsOnlineConstants.PublisherType);
            if (card != null)
            {
                await card.InstallLatestCommand.ExecuteAsync(null);

                // Mirror the card's status if possible, or just reset after a delay since we can't easily bind to the card's internal progress from here without more complex binding
                // For now, we'll assume the card handles the actual installation UI feedback
                InstallationStatus = "Installation started via card";
            }
            else
            {
                logger.LogWarning("Generals Online card not found for installation");
                InstallationStatus = "Card not found";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Generals Online installation");
            InstallationStatus = "Error";
        }
        finally
        {
            await Task.Delay(2000); // Show status for a bit
            IsInstallingGeneralsOnline = false;
            InstallationStatus = string.Empty;
        }
    }

    [RelayCommand]
    private async Task GetWeeklyReleaseAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == PublisherTypeConstants.TheSuperHackers);
            if (card != null)
            {
                await card.InstallLatestCommand.ExecuteAsync(null);
            }
            else
            {
                logger.LogWarning("SuperHackers card not found for installation");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Weekly Release installation");
        }
    }

    [RelayCommand]
    private async Task GetCommunityPatchAsync()
    {
        try
        {
            var card = PublisherCards.FirstOrDefault(c => c.PublisherId == CommunityOutpostConstants.PublisherType);
            if (card != null)
            {
                await card.InstallLatestCommand.ExecuteAsync(null);
            }
            else
            {
                logger.LogWarning("Community Outpost card not found for installation");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Community Patch installation");
        }
    }

    /// <summary>
    /// Show an informational notification announcing upcoming GitHub Manager features.
    /// </summary>
    [RelayCommand]
    private void OpenGitHubBuilds()
    {
        notificationService.ShowInfo(
            "Coming Soon",
            "GitHub Manager will allow you to browse and manage GitHub repositories, releases, and artifacts.");
    }

    /// <summary>
    /// Gets or sets the browser view model.
    /// </summary>
    [ObservableProperty]
    private DownloadsBrowserViewModel? _browserViewModel;

    [ObservableProperty]
    private bool _isBrowserVisible;

    /// <summary>
    /// Receives message to open publisher details/browser.
    /// </summary>
    /// <summary>
    /// Opens the publisher details browser for the publisher identified by the message.
    /// </summary>
    /// <remarks>
    /// If the browser view model is not yet created, it is resolved lazily. If a matching publisher is found,
    /// it becomes the selected publisher, the browser is shown, and the view title is set to "Browser".
    /// </remarks>
    /// <param name="message">Message containing the publisher identifier to open.</param>
    public void Receive(Core.Messages.OpenPublisherDetailsMessage message)
    {
        BrowserViewModel ??= serviceProvider.GetService(typeof(DownloadsBrowserViewModel)) as DownloadsBrowserViewModel;

        if (BrowserViewModel != null)
        {
            var publisher = BrowserViewModel.Publishers.FirstOrDefault(p => p.PublisherId == message.Value);
            if (publisher != null)
            {
                BrowserViewModel.SelectedPublisher = publisher;
                IsBrowserVisible = true;
                Title = "Browser"; // Temporarily change title or keep context?
            }
        }
    }

    /// <summary>
    /// Receives message to close publisher details/browser.
    /// </summary>
    /// <summary>
    /// Closes the publisher details view and navigates back to the Downloads dashboard.
    /// </summary>
    /// <param name="message">The message indicating the publisher details view should be closed.</param>
    public void Receive(Core.Messages.ClosePublisherDetailsMessage message)
    {
        GoToDashboard();
    }

    /// <summary>
    /// Navigates back to the downloads dashboard.
    /// </summary>
    /// <remarks>
    /// Hides the publisher browser and resets the view title to "Downloads".
    /// </remarks>
    [RelayCommand]
    private void GoToDashboard()
    {
        IsBrowserVisible = false;
        Title = "Downloads";
    }
}