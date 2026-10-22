using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GeneralsOnline.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Root view model for the Online tab, managing the sidebar and child sections.
/// </summary>
public sealed partial class OnlineViewModel : ViewModelBase, IDisposable
{
    private readonly ILocalizationService? _localizationService;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<OnlineSectionItem> _sections = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralsOnlineSelected))]
    private OnlineSectionItem? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = OnlineConstants.SidebarOpenPaneLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnlineViewModel"/> class.
    /// </summary>
    /// <param name="generalsOnlineLobbies">The Generals Online view model.</param>
    /// <param name="localizationService">Optional localization service.</param>
    public OnlineViewModel(
        GeneralsOnlineLobbiesViewModel generalsOnlineLobbies,
        ILocalizationService? localizationService = null)
    {
        GeneralsOnlineLobbies = generalsOnlineLobbies ?? throw new ArgumentNullException(nameof(generalsOnlineLobbies));
        _localizationService = localizationService;

        BuildSections();

        if (_localizationService is not null)
        {
            _localizationService.PropertyChanged += OnCultureChanged;
        }
    }

    /// <summary>
    /// Gets the Generals Online lobbies and matchmaking view model.
    /// </summary>
    public GeneralsOnlineLobbiesViewModel GeneralsOnlineLobbies { get; }

    /// <summary>
    /// Gets a value indicating whether Generals Online is currently selected in the sidebar.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Depends on generated instance property SelectedSection.")]
    public bool IsGeneralsOnlineSelected =>
        SelectedSection?.Id == OnlineConstants.SectionGeneralsOnline;

    /// <summary>
    /// Performs one-time view model initialization when navigated to.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Initializes instance state and triggers instance refresh command.")]
    public void Initialize()
    {
        _ = RefreshAsync(CancellationToken.None);
    }

    /// <summary>
    /// Refreshes the currently selected online section.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the refresh operation.</returns>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (GeneralsOnlineLobbies is not null)
        {
            await GeneralsOnlineLobbies.RefreshAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_localizationService is not null)
        {
            _localizationService.PropertyChanged -= OnCultureChanged;
        }

        GeneralsOnlineLobbies?.Dispose();
    }

    private void BuildSections()
    {
        var title = _localizationService?.GetString("Online.Sections.GeneralsOnline.Title")
            ?? "Generals Online";
        var description = _localizationService?.GetString("Online.Sections.GeneralsOnline.Description")
            ?? "Community matchmaking and multiplayer";

        var goSection = new OnlineSectionItem(
            OnlineConstants.SectionGeneralsOnline,
            title,
            description,
            string.Empty,
            GeneralsOnlineConstants.LogoSource);

        Sections = [goSection];
        SelectedSection = goSection;
    }

    private void OnCultureChanged(object? sender, PropertyChangedEventArgs e)
    {
        BuildSections();
    }
}
