using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using System.Threading.Tasks;

namespace GenHub.Features.GameReplays.ViewModels;

/// <summary>
/// ViewModel for a single tournament item.
/// </summary>
public partial class TournamentItemViewModel : ObservableObject
{
    private Tournament _tournament;
    private bool _isSelected;

    /// <summary>Initializes a new instance of the <see cref="TournamentItemViewModel"/> class.</summary>
    public TournamentItemViewModel()
    {
        _tournament = new Tournament();
        _isSelected = false;
    }

    /// <summary>Initializes a new instance of the <see cref="TournamentItemViewModel"/> class with a tournament model.</summary>
    /// <param name="tournament">The tournament model.</param>
    public TournamentItemViewModel(Tournament tournament)
    {
        _tournament = tournament;
        _isSelected = false;
    }

    /// <summary>
    /// Gets or sets the tournament model.
    /// </summary>
    public Tournament Tournament
    {
        get => _tournament;
        set => SetProperty(ref _tournament, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this tournament is selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Gets the display name for the tournament.
    /// </summary>
    public string DisplayName => Tournament.Name;

    /// <summary>
    /// Gets the status display text.
    /// </summary>
    public string StatusDisplay => Tournament.Status switch
    {
        TournamentStatus.SignupsOpen => "Signups Open",
        TournamentStatus.Upcoming => "Upcoming",
        TournamentStatus.Active => "Active",
        TournamentStatus.Finished => "Finished",
        _ => "Unknown",
    };

    /// <summary>
    /// Gets the status color for display.
    /// </summary>
    public string StatusColor => Tournament.Status switch
    {
        TournamentStatus.SignupsOpen => GameReplaysConstants.Colors.SignupsOpen,
        TournamentStatus.Upcoming => GameReplaysConstants.Colors.Upcoming,
        TournamentStatus.Active => GameReplaysConstants.Colors.Active,
        TournamentStatus.Finished => GameReplaysConstants.Colors.Finished,
        _ => GameReplaysConstants.Colors.Default,
    };

    /// <summary>
    /// Gets the formatted date string for display.
    /// </summary>
    public string DateDisplay
    {
        get
        {
            if (Tournament.SignupsCloseDate.HasValue)
            {
                return $"Signups close: {Tournament.SignupsCloseDate:MMM dd, yyyy}";
            }

            if (Tournament.StartDate.HasValue)
            {
                return $"Started: {Tournament.StartDate:MMM dd, yyyy}";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the host display text.
    /// </summary>
    public string HostDisplay => string.IsNullOrEmpty(Tournament.Host)
        ? string.Empty
        : $"Hosted by {Tournament.Host}";

    /// <summary>
    /// Gets the URL for opening the tournament in browser.
    /// </summary>
    public string TournamentUrl => Tournament.Url;

    /// <summary>Gets a value indicating whether the tournament has a signups close date.</summary>
    public bool HasSignupsCloseDate => Tournament.SignupsCloseDate.HasValue;

    /// <summary>Gets a value indicating whether the tournament has a start date.</summary>
    public bool HasStartDate => Tournament.StartDate.HasValue;

    /// <summary>Gets a value indicating whether the tournament is currently active.</summary>
    public bool IsActive => Tournament.Status == TournamentStatus.Active;

    /// <summary>Gets a value indicating whether signups are currently open.</summary>
    public bool IsSignupsOpen => Tournament.Status == TournamentStatus.SignupsOpen;

    /// <summary>Gets a value indicating whether the tournament is finished.</summary>
    public bool IsFinished => Tournament.Status == TournamentStatus.Finished;

    /// <summary>Gets a value indicating whether the tournament is upcoming.</summary>
    public bool IsUpcoming => Tournament.Status == TournamentStatus.Upcoming;

    /// <summary>
    /// Gets the topic ID for the tournament.
    /// </summary>
    public string TopicId => Tournament.TopicId;

    /// <summary>
    /// Gets the description for the tournament.
    /// </summary>
    public string Description => Tournament.Description ?? string.Empty;

    /// <summary>
    /// Gets the comment count for the tournament.
    /// </summary>
    public int CommentCount => Tournament.CommentCount;

    /// <summary>
    /// Command to open the tournament in browser.
    /// </summary>
    [RelayCommand]
    public void OpenTournament()
    {
        if (!string.IsNullOrEmpty(TournamentUrl))
        {
            // Placeholder for URL opening
        }
    }

    /// <summary>
    /// Command to copy the tournament URL to clipboard.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CopyTournamentUrl()
    {
        if (!string.IsNullOrEmpty(TournamentUrl))
        {
            // Placeholder for clipboard copy
            await Task.CompletedTask;
        }
    }
}
