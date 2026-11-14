using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Notifications.Views;
using GenHub.Tests.Core.Collections;
using GenHub.Tests.Core.Features.GameProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Notifications;

/// <summary>Exercises shared action roles in the mounted notification feed consumer.</summary>
[Collection(LocalizationCultureCollection.Name)]
public class NotificationActionStyleViewTests
{
    /// <summary>Action semantics, inherited ink and callbacks survive the shared-style migration.</summary>
    /// <param name="style">The notification action style.</param>
    /// <param name="role">The expected shared action role.</param>
    /// <param name="ink">The semantic foreground resource.</param>
    [AvaloniaTheory]
    [InlineData(NotificationActionStyle.Primary, "action-primary", "ActionOnAccentBrush")]
    [InlineData(NotificationActionStyle.Success, "action-primary", "ActionOnAccentBrush")]
    [InlineData(NotificationActionStyle.Secondary, "action-secondary", "ActionSecondaryInk")]
    [InlineData(NotificationActionStyle.Danger, "action-danger", "ActionDangerInk")]
    public void FeedAction_UsesSemanticRoleAndExecutesOnce(NotificationActionStyle style, string role, string ink)
    {
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        var executed = 0;
        var message = new NotificationMessage(
            NotificationType.Info,
            "Download ready",
            "The local test item is available.",
            autoDismissMilliseconds: null,
            actions: [new NotificationAction("Open item", () => executed++, style, dismissOnExecute: false)]);
        using var viewModel = new NotificationFeedItemViewModel(message, _ => { }, _ => { }, NullLogger<NotificationFeedItemViewModel>.Instance);
        var window = new Window { Width = 420, Height = 260 };
        try
        {
            resources[LocalizationConstants.ResourceServiceKey] = new MarkerLocalizationService();
            var view = new NotificationFeedItemView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => ReferenceEquals(button.Command, viewModel.Actions[0].ExecuteCommand));
            Assert.Contains(role, action.Classes);
            Assert.Equal(32, action.MinHeight);
            var caption = Assert.IsType<TextBlock>(action.Content);
            var presenter = action.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter");
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Application.Current!.FindResource(ink));
            Assert.Equal(expected.Color, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
            Assert.Equal(expected.Color, Assert.IsAssignableFrom<ISolidColorBrush>(caption.Foreground).Color);
            action.Command!.Execute(action.CommandParameter);
            Assert.Equal(1, executed);

            var dismiss = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("icon-action"));
            Assert.True(dismiss.Bounds.Width >= 32);
            Assert.True(dismiss.Bounds.Height >= 32);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(dismiss)));
            var glyph = Assert.IsType<PathIcon>(dismiss.Content);
            Assert.Equal(16, glyph.Width);
            Assert.Equal(16, glyph.Height);
        }
        finally
        {
            window.Close();
            if (hadPrevious)
            {
                resources[LocalizationConstants.ResourceServiceKey] = previous;
            }
            else
            {
                resources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }

    /// <summary>Several translated actions wrap into rows instead of extending outside the feed.</summary>
    [AvaloniaFact]
    public void FeedActions_LongCaptionsWrapWithinNarrowFeed()
    {
        var message = new NotificationMessage(
            NotificationType.Info,
            "Offline preview",
            "The actions are available locally.",
            autoDismissMilliseconds: null,
            actions:
            [
                new NotificationAction("Открыть папку содержимого", () => { }),
                new NotificationAction("Повторить загрузку элемента", () => { }, NotificationActionStyle.Secondary),
                new NotificationAction("Удалить загруженный элемент", () => { }, NotificationActionStyle.Danger),
            ]);
        using var viewModel = new NotificationFeedItemViewModel(message, _ => { }, _ => { }, NullLogger<NotificationFeedItemViewModel>.Instance);
        var view = new NotificationFeedItemView { DataContext = viewModel };
        var window = new Window { Width = 400, Height = 400, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var actions = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("action-compact")).ToArray();
            Assert.Equal(3, actions.Length);
            var bounds = actions.Select(button => new Rect(button.TranslatePoint(default, view)!.Value, button.Bounds.Size)).ToArray();
            Assert.True(bounds.Select(rect => rect.Y).Distinct().Count() > 1);
            Assert.All(bounds, rect => Assert.True(rect.Right <= view.Bounds.Width));
        }
        finally
        {
            window.Close();
        }
    }
}
