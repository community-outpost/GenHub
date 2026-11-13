using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Common.Views.Dialogs;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Common.Views.Dialogs;

/// <summary>
/// Verifies that optional diagnostics do not crowd the message or reveal IDs by default.
/// </summary>
public class GenericMessageDetailsTests
{
    /// <summary>
    /// Diagnostics start collapsed and remain available on explicit expansion.
    /// </summary>
    [AvaloniaFact]
    public void Details_StartCollapsedAndCanBeExpanded()
    {
        var vm = new GenericMessageViewModel
        {
            Title = "One required item is missing",
            Content = "Control Bar Pro needs GenTool.",
            DetailsHeader = "Technical details",
            DetailsContent = "1.1.communityoutpost.addon.gent",
        };
        var window = new GenericMessageWindow { DataContext = vm };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var expander = Assert.Single(window.GetVisualDescendants().OfType<Expander>());
            Assert.True(expander.IsVisible);
            Assert.False(expander.IsExpanded);
            Assert.DoesNotContain(vm.DetailsContent, vm.Content);

            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == vm.DetailsContent);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Existing messages without diagnostics retain their ordinary layout.
    /// </summary>
    [AvaloniaFact]
    public void MessageWithoutDetails_HidesExpander()
    {
        var window = new GenericMessageWindow { DataContext = new GenericMessageViewModel { Title = "Message", Content = "Body" } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(Assert.Single(window.GetVisualDescendants().OfType<Expander>()).IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
