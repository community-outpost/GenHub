using Avalonia.Controls;
using System.ComponentModel;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Scrolls a shared page scroller back to the top when the view model switches the page it shows.
/// </summary>
/// <param name="scrollViewer">The scroller shared by the pages.</param>
/// <param name="pagePropertyName">The view model property that selects the page.</param>
internal sealed class PageScrollReset(ScrollViewer scrollViewer, string pagePropertyName)
{
    private INotifyPropertyChanged? _source;

    /// <summary>
    /// Follows page changes of the given data context, replacing any previous one.
    /// </summary>
    /// <param name="dataContext">The view's data context.</param>
    public void Track(object? dataContext)
    {
        if (_source != null)
        {
            _source.PropertyChanged -= OnSourcePropertyChanged;
        }

        _source = dataContext as INotifyPropertyChanged;
        if (_source != null)
        {
            _source.PropertyChanged += OnSourcePropertyChanged;
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == pagePropertyName)
        {
            scrollViewer.Offset = default;
        }
    }
}
