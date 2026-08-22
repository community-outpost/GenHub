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
public sealed partial class FaqSectionViewModel(IFaqService faqService, ILogger<FaqSectionViewModel> logger) : ObservableObject, IInfoSectionViewModel, IDisposable
{
    private CancellationTokenSource? _loadCts;

    private bool _disposed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <inheritdoc/>
    public string Id => "faq";

    /// <inheritdoc/>
    public string Title => "Zero Hour";

    /// <summary>
    /// Gets the icon key.
    /// </summary>
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
    private LanguageOption _selectedLanguageOption = new("English", "en", "avares://GenHub/Assets/Images/Flags/en.png"); // Default, updated in constructor logic if needed but simpler to just init here or OnActivated

    [ObservableProperty]
    private FaqCategoryViewModel? _selectedCategory;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await LoadFaqAsync();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var oldCts = Interlocked.Exchange(ref _loadCts, null);
        if (oldCts != null)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        GC.SuppressFinalize(this);
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
        var oldCts = Interlocked.Exchange(ref _loadCts, null);
        if (oldCts != null)
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }

        if (_disposed)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var result = await faqService.GetFaqAsync(SelectedLanguageOption.Code, token);
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
            logger.LogError(ex, "Error loading FAQ");
            StatusMessage = "An unexpected error occurred.";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }
}
