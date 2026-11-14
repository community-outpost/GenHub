using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.Tools.Views.Dialogs;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>Checks rendered field states and the actual authoring-form consumers.</summary>
public class FormFieldStyleTests
{
    /// <summary>The actual Fluent text editor retains selection and validation while using shared state colors.</summary>
    [AvaloniaFact]
    public void TextField_EditingFocusValidationAndDisabledStatesRemainDistinct()
    {
        var field = new TextBox { Text = "Community content", Width = 300 };
        field.Classes.Add("form-field");
        var window = new Window { Width = 380, Height = 180, Content = field };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var border = field.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_BorderElement");
            border.Transitions = null;
            Assert.Equal(ResourceColor("FormFieldFill"), BrushColor(border.Background));
            Assert.Equal(new CornerRadius(8), field.CornerRadius);
            Assert.Equal(36, field.MinHeight);
            Assert.Equal(13, field.FontSize);
            window.MouseMove(field.TranslatePoint(new Point(150, field.Bounds.Height / 2), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("FormFieldHoverBorder"), BrushColor(border.BorderBrush));
            Assert.Equal(ResourceColor("FormFieldHoverFill"), BrushColor(border.Background));
            field.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("AccentBrush"), BrushColor(border.BorderBrush));
            field.SelectionStart = 0;
            field.SelectionEnd = 9;
            field.SelectedText = "Publisher";
            Assert.Equal("Publisher content", field.Text);
            DataValidationErrors.SetErrors(field, ["A required value is missing"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("StatusErrorBrush"), BrushColor(border.BorderBrush));
            field.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("TextMuted"), BrushColor(field.Foreground));
            Assert.Equal(ResourceColor("FormFieldBorder"), BrushColor(border.BorderBrush));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Two real Studio dialogs must share field geometry despite different form content.</summary>
    [AvaloniaFact]
    public void StudioForms_NameFieldsShareGeometryAndReadableSurfaces()
    {
        var create = new AddContentDialogView();
        var rename = new RenameCatalogDialogView();
        var window = new Window { Width = 900, Height = 700, Content = new StackPanel { Children = { create, rename } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var createField = create.GetVisualDescendants().OfType<TextBox>().First();
            var renameField = rename.GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal(createField.CornerRadius, renameField.CornerRadius);
            Assert.Equal(createField.Padding, renameField.Padding);
            Assert.Equal(createField.MinHeight, renameField.MinHeight);
            Assert.Equal(createField.FontSize, renameField.FontSize);
            Assert.Equal(ResourceColor("FormFieldFill"), BrushColor(createField.Background));
            Assert.Equal(BrushColor(createField.Background), BrushColor(renameField.Background));
            Assert.Equal(ResourceColor("TextPrimary"), BrushColor(createField.Foreground));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A compact dropdown still exposes selected content and keyboard focus without clipping its glyph.</summary>
    [AvaloniaFact]
    public void CompactDropdown_SelectionFocusAndDisabledInkRemainAvailable()
    {
        var field = new ComboBox { ItemsSource = new[] { "Generals", "Zero Hour" }, SelectedIndex = 0, Width = 230 };
        field.Classes.Add("form-compact");
        var window = new Window { Width = 320, Height = 180, Content = field };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var border = field.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "Background");
            var glyph = field.GetVisualDescendants().OfType<PathIcon>().Single(item => item.Name == "DropDownGlyph");

            // Inspect final state colors without the glyph's interpolation clock.
            field.Transitions = null;
            border.Transitions = null;
            glyph.Transitions = null;
            Assert.Equal(32, field.MinHeight);
            Assert.Equal(new CornerRadius(8), field.CornerRadius);
            Assert.Equal("Generals", field.SelectedItem);
            field.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("AccentBrush"), BrushColor(border.BorderBrush));
            field.SelectedIndex = 1;
            Assert.Equal("Zero Hour", field.SelectedItem);
            field.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("TextMuted"), BrushColor(field.Foreground));
            Assert.Equal(ResourceColor("TextMuted"), BrushColor(glyph.Foreground));
            Assert.True(field.Bounds.Height >= glyph.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Ordinary read-only fields reject typing while embedded numeric editors retain scrubbing geometry.</summary>
    [AvaloniaFact]
    public void ReadOnlyField_RejectsTypingWithoutRestylingEmbeddedNumericEditor()
    {
        var readOnly = new TextBox { Text = "https://example.invalid/catalog.json", IsReadOnly = true };
        readOnly.Classes.Add("form-field");
        var number = new NumericUpDown { Value = 5 };
        var window = new Window { Width = 420, Height = 220, Content = new StackPanel { Children = { readOnly, number } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            readOnly.Focus(NavigationMethod.Tab);
            window.KeyTextInput("changed");
            Assert.Equal("https://example.invalid/catalog.json", readOnly.Text);
            Assert.Equal(ResourceColor("TextSecondary"), BrushColor(readOnly.Foreground));
            var embedded = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            Assert.DoesNotContain("form-field", embedded.Classes);
            Assert.NotEqual(36, embedded.MinHeight);
            Assert.Equal(28, number.MinHeight);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Single-line text and watermarks stay centered when a form allocates extra height.</summary>
    /// <param name="compact">Whether compact field metrics apply.</param>
    /// <param name="empty">Whether to inspect the watermark instead of entered text.</param>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SingleLineField_CentersItsRenderedLineWhenHeightGrows(bool compact, bool empty)
    {
        var field = new TextBox { Text = empty ? string.Empty : "Catalog gjpq", Watermark = "Enter catalog URL", Height = 56 };
        field.Classes.Add("form-field");
        if (compact)
        {
            field.Classes.Add("form-compact");
        }

        var window = new Window { Width = 400, Height = 140, Content = new StackPanel { Children = { field } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Control line = empty
                ? field.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name == "PART_Watermark")
                : field.GetVisualDescendants().OfType<TextPresenter>().Single();
            var inputBorder = field.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_BorderElement");
            var lineHeight = line is TextPresenter textPresenter ? textPresenter.TextLayout.Height : ((TextBlock)line).TextLayout.Height;
            var origin = line.TranslatePoint(default, inputBorder)!.Value;
            Assert.InRange(Math.Abs(origin.Y + (lineHeight / 2) - (inputBorder.Bounds.Height / 2)), 0, 0.5);
            field.Focus(NavigationMethod.Tab);
            DataValidationErrors.SetErrors(field, ["Required"]);
            Dispatcher.UIThread.RunJobs();
            var errorCaption = field.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Text == "Required");
            Assert.Equal(ResourceColor("StatusErrorBrush"), BrushColor(errorCaption.Foreground));
            origin = line.TranslatePoint(default, inputBorder)!.Value;
            Assert.InRange(Math.Abs(origin.Y + (lineHeight / 2) - (inputBorder.Bounds.Height / 2)), 0, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Centering single-line fields must not move the first line of a multiline editor.</summary>
    [AvaloniaFact]
    public void MultilineField_KeepsItsFirstLineAtTheTopInset()
    {
        var field = new TextBox { Text = "First line\nSecond line", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120 };
        field.Classes.Add("form-field");
        var window = new Window { Width = 400, Height = 180, Content = new StackPanel { Children = { field } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var presenter = field.GetVisualDescendants().OfType<TextPresenter>().Single();
            Assert.Equal(VerticalAlignment.Top, field.VerticalContentAlignment);
            Assert.Equal(field.Padding.Top + field.BorderThickness.Top, presenter.TranslatePoint(default, field)!.Value.Y);
        }
        finally
        {
            window.Close();
        }
    }

    private static Color ResourceColor(string key) => BrushColor((IBrush)Avalonia.Application.Current!.FindResource(key)!);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
