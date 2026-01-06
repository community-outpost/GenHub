using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Content.ViewModels;

/// <summary>
/// ViewModel for browsing and searching content from multiple providers.
/// </summary>
public partial class ContentBrowserViewModel(IContentOrchestrator contentOrchestrator) : ObservableObject
{
    private readonly IContentOrchestrator _contentOrchestrator = contentOrchestrator;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ContentSortField _selectedSortOrder = ContentSortField.Relevance;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets the collection of search results to be displayed in the content browser.
    /// </summary>
    public ObservableCollection<ContentItemViewModel> SearchResults { get; } = [];

    /// <summary>
    /// Searches for content asynchronously based on the current search parameters.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task SearchAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        SearchResults.Clear();
        try
        {
            var query = new ContentSearchQuery
            {
                SearchTerm = SearchTerm,
                ContentType = SelectedContentType,
                SortOrder = SelectedSortOrder,
                Take = 50,
            };
            var result = await _contentOrchestrator.SearchAsync(query);
            if (result.Success && result.Data != null)
            {
                foreach (var item in result.Data)
                {
                    SearchResults.Add(new ContentItemViewModel(item));
                }
            }
            else
            {
                ErrorMessage = result.FirstError ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An unexpected error occurred: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}