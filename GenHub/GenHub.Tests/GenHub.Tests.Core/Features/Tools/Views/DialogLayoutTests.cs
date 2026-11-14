using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Common.Views.Dialogs;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels.Dialogs;
using GenHub.Features.Tools.Views.Dialogs;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Views;

/// <summary>Exercises dialog footer layout with constrained forms and long captions.</summary>
public class DialogLayoutTests
{
    /// <summary>A translated title can grow without pushing the confirmation action outside its window.</summary>
    [AvaloniaFact]
    public void Confirmation_LongTitleGrowsWhileKeepingActionsVisible()
    {
        var viewModel = new ConfirmationDialogViewModel
        {
            Title = "Подтвердите применение изменений ко всем выбранным профилям — تأكيد تطبيق التغييرات على جميع الملفات المحددة — "
                + "Проверьте настройки содержимого перед сохранением — تحقق من إعدادات المحتوى قبل الحفظ",
            Message = "The selected profile settings will be updated.",
            ConfirmButtonText = "Apply",
            CancelButtonText = "Cancel",
        };
        var window = new ConfirmationDialogWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var title = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Classes.Contains("dialog-title"));
            Assert.Equal(20, title.FontSize);
            Assert.True(title.Bounds.Height > 40);
            var confirm = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("action-primary"));
            var point = confirm.TranslatePoint(default, window)!.Value;
            Assert.True(point.Y + confirm.Bounds.Height <= window.ClientSize.Height);
            Assert.True(window.ClientSize.Height > 280);
            Assert.Same(viewModel.ConfirmCommand, confirm.Command);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Validation and actions remain outside the scrolling body at both normal and narrow widths.</summary>
    /// <param name="release">Whether to exercise the release form.</param>
    /// <param name="width">The available dialog width.</param>
    [AvaloniaTheory]
    [InlineData(false, 360)]
    [InlineData(false, 740)]
    [InlineData(true, 360)]
    [InlineData(true, 740)]
    public void AuthoringFooter_LongCaptionRemainsVisibleBelowScrollingBody(bool release, double width)
    {
        var validation = "Проверьте обязательные поля. تحقق من الحقول المطلوبة قبل الحفظ.";
        object viewModel;
        UserControl view;
        if (release)
        {
            var releaseViewModel = new AddReleaseDialogViewModel(
                new CatalogContentItem { Id = "sample", Name = "Sample" },
                new PublisherCatalog(),
                _ => { },
                Mock.Of<IPublisherStudioDialogService>())
            {
                ValidationError = validation,
            };
            viewModel = releaseViewModel;
            view = new AddReleaseDialogView { DataContext = releaseViewModel };
        }
        else
        {
            var contentViewModel = new AddContentDialogViewModel(_ => { }) { ValidationError = validation };
            viewModel = contentViewModel;
            view = new AddContentDialogView { DataContext = contentViewModel };
        }

        var window = new Window { Width = width, Height = 540, Content = view };
        window.Show();
        try
        {
            var root = view.FindControl<Grid>("DialogLayoutRoot")!;
            var body = view.FindControl<ScrollViewer>("DialogBodyScroll")!;
            var footer = view.FindControl<Grid>("DialogFooter")!;
            var submit = Assert.Single(footer.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("action-primary"));
            var originalCommand = submit.Command;
            var caption = Assert.IsType<TextBlock>(submit.Content);
            caption.Text = "Сохранить изменения содержимого — حفظ جميع التغييرات المطلوبة";
            Dispatcher.UIThread.RunJobs();
            var footerPosition = footer.TranslatePoint(default, root)!.Value;
            var submitPosition = submit.TranslatePoint(default, root)!.Value;
            var bodyBottom = body.Bounds.Bottom;
            Assert.True(body.Bounds.Height > 0);
            Assert.True(bodyBottom <= footerPosition.Y);
            Assert.True(submitPosition.Y >= bodyBottom);
            Assert.True(submitPosition.Y + submit.Bounds.Height <= root.Bounds.Height + 0.1);
            Assert.True(submitPosition.X >= 0);
            Assert.True(submitPosition.X + submit.Bounds.Width <= root.Bounds.Width + 0.1);
            Assert.True(caption.Bounds.Width <= submit.Bounds.Width - submit.Padding.Left - submit.Padding.Right);
            Assert.Same(originalCommand, submit.Command);
            Assert.NotNull(originalCommand);
            var footerY = footerPosition.Y;
            body.Offset = new Vector(0, double.MaxValue);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(footerY, footer.TranslatePoint(default, root)!.Value.Y);
        }
        finally
        {
            window.Close();
            (viewModel as IDisposable)?.Dispose();
        }
    }

    /// <summary>Overlay scrollbar tracks must not overlap field borders in a constrained artifact form.</summary>
    /// <param name="width">The available dialog width.</param>
    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(700)]
    public void ArtifactForm_ReservesARealGutterBesideTheInputs(double width)
    {
        var view = new AddArtifactDialogView();
        var window = new Window { Width = width, Height = 460, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(item => item.Classes.Contains("dialog-scroll"));
            var scrollbar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(item => ReferenceEquals(item.TemplatedParent, scroll) && item.Orientation == Orientation.Vertical);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var trackLeft = scrollbar.TranslatePoint(default, scroll)!.Value.X;
            var fieldRight = scroll.GetVisualDescendants().OfType<TextBox>()
                .Where(field => field.IsEffectivelyVisible && field.Bounds.Width > 0)
                .Max(field => field.TranslatePoint(default, scroll)!.Value.X + field.Bounds.Width);
            Assert.True(fieldRight + 4 <= trackLeft, $"Field right {fieldRight}; scrollbar left {trackLeft}");
        }
        finally
        {
            window.Close();
        }
    }
}
