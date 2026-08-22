using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Info;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the FAQ section.
/// </summary>
public sealed partial class FaqSectionViewModel(IFaqService faqService, ILogger<FaqSectionViewModel> logger) : ObservableObject, IInfoSectionViewModel, IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private bool _disposed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private LanguageOption _selectedLanguageOption = new("English", "en", "avares://GenHub/Assets/Images/Flags/en.png");

    [ObservableProperty]
    private FaqCategoryViewModel? _selectedCategory;

    /// <inheritdoc/>
    public string Id => "faq";

    /// <inheritdoc/>
    public string Title => "Zero Hour";

    /// <summary>
    /// Gets the icon key.
    /// </summary>
    public string IconKey => "HelpCircleOutline";

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

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await LoadFaqAsync();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CancellationTokenSource? ctsToDispose = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ctsToDispose = _loadCts;
            _loadCts = null;
        }

        if (ctsToDispose != null)
        {
            ctsToDispose.Cancel();
            ctsToDispose.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        await cts.CancelAsync();
        cts.Dispose();
    }

    [RelayCommand]
    private void SelectLanguage(LanguageOption option)
    {
        if (option != null && SelectedLanguageOption != option)
        {
            SelectedLanguageOption = option;
        }
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption value)
    {
        _ = LoadFaqAsync();
    }

    [RelayCommand]
    private async Task LoadFaqAsync()
    {
        if (!TryPrepareLoad(out var token, out var currentGeneration, out var oldCts))
        {
            return;
        }

        await CancelAndDisposeAsync(oldCts);

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var result = await faqService.GetFaqAsync(SelectedLanguageOption.Code, token);
            if (token.IsCancellationRequested || !IsCurrentGeneration(currentGeneration))
            {
                return;
            }

            if (result.Success && result.Data != null)
            {
                await PopulateCategoriesAsync(result.Data, currentGeneration, token);
            }
            else
            {
                StatusMessage = result.FirstError ?? "Unknown error loading FAQ.";
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer load request preempts this one.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading FAQ");
            StatusMessage = "An unexpected error occurred.";
        }
        finally
        {
            CompleteLoad(currentGeneration);
        }
    }

    private bool TryPrepareLoad(out CancellationToken token, out int generation, out CancellationTokenSource? oldCts)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                token = CancellationToken.None;
                generation = 0;
                oldCts = null;
                return false;
            }

            oldCts = _loadCts;
            var cts = new CancellationTokenSource();
            _loadCts = cts;
            generation = ++_loadGeneration;
            token = cts.Token;
            return true;
        }
    }

    private bool IsCurrentGeneration(int generation)
    {
        lock (_gate)
        {
            return !_disposed && _loadGeneration == generation;
        }
    }

    private async Task PopulateCategoriesAsync(IReadOnlyList<FaqCategory> categories, int generation, CancellationToken token)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }

                Categories.Clear();
                foreach (var category in categories)
                {
                    Categories.Add(new FaqCategoryViewModel(category));
                }

                SelectedCategory = Categories.FirstOrDefault();
            },
            Avalonia.Threading.DispatcherPriority.Normal,
            token);
    }

    private void CompleteLoad(int generation)
    {
        lock (_gate)
        {
            if (_loadGeneration == generation)
            {
                IsLoading = false;
            }
        }
    }
}
