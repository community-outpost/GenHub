using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Info;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the FAQ section.
/// </summary>
public partial class FaqSectionViewModel : ObservableObject, IInfoSectionViewModel
{
    private readonly IFaqService _faqService;
    private readonly ILogger<FaqSectionViewModel> _logger;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <inheritdoc/>
    public string Title => "Zero Hour";

    /// <inheritdoc/>
    public string IconKey => "HelpCircleOutline"; // Material Design Icon

    /// <inheritdoc/>
    public int Order => 0;

    /// <summary>
    /// Gets the list of FAQ categories.
    /// </summary>
    public ObservableCollection<FaqCategoryViewModel> Categories { get; private set; } = [];

    /// <summary>
    /// Gets the supported languages.
    /// </summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new LanguageOption("English", "en", "avares://GenHub/Assets/Images/Flags/en.png"),
        new LanguageOption("German", "de", "avares://GenHub/Assets/Images/Flags/de.png"),
        new LanguageOption("Filipino", "ph", "avares://GenHub/Assets/Images/Flags/ph.png"),
        new LanguageOption("Arabic", "ar", "avares://GenHub/Assets/Images/Flags/ar.webp"),
    ];

    [ObservableProperty]
    private LanguageOption _selectedLanguageOption;

    [ObservableProperty]
    private FaqCategoryViewModel? _selectedCategory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaqSectionViewModel"/> class.
    /// </summary>
    /// <param name="faqService">The FAQ service.</param>
    /// <param name="logger">The logger.</param>
    public FaqSectionViewModel(IFaqService faqService, ILogger<FaqSectionViewModel> logger)
    {
        _faqService = faqService;
        _logger = logger;
        _selectedLanguageOption = LanguageOptions.First(x => x.Code == InfoConstants.FaqDefaultLanguage);
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await LoadFaqAsync();
    }

    [RelayCommand]
    private void SelectLanguage(LanguageOption option)
    {
        if (option != null && SelectedLanguageOption != option)
        {
            SelectedLanguageOption = option;
        }
    }

    async partial void OnSelectedLanguageOptionChanged(LanguageOption value)
    {
        await LoadFaqAsync();
    }

    [RelayCommand]
    private async Task LoadFaqAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var result = await _faqService.GetFaqAsync(SelectedLanguageOption.Code, token);
            if (result.Success)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        Categories.Clear();
                        foreach (var category in result.Data)
                        {
                            Categories.Add(new FaqCategoryViewModel(category));
                        }

                        SelectedCategory = Categories.FirstOrDefault();
                    },
                    Avalonia.Threading.DispatcherPriority.Normal,
                    token);
            }
            else
            {
                StatusMessage = result.FirstError ?? "Unknown error loading FAQ.";
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading FAQ");
            StatusMessage = "An unexpected error occurred.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
