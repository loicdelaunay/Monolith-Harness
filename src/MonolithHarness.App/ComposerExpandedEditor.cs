using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Button expandComposer = new() { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 6, 8, 0), Visibility = Visibility.Collapsed };
    readonly DispatcherTimer composerLineCheck = new() { Interval = TimeSpan.FromMilliseconds(160) };
    Border? expandedComposer;

    UIElement BuildComposerExpandButton()
    {
        FluentDesign.IconButton(expandComposer, "\uE740", WorkflowText("Agrandir la zone de saisie", "Expand message editor"), false);
        expandComposer.Click += (_, _) => ShowExpandedComposer();
        composerLineCheck.Tick += (_, _) => { composerLineCheck.Stop(); RefreshComposerExpandButton(); };
        composer.TextChanged += (_, _) => { composerLineCheck.Stop(); composerLineCheck.Start(); };
        composer.Loaded += (_, _) => RefreshComposerExpandButton();
        composer.SizeChanged += (_, _) => RefreshComposerExpandButton();
        return expandComposer;
    }
    static bool HasMoreThanTenLines(string text)
    {
        int lines = 1, position = 0;
        while (position < text.Length)
        {
            var next = text.AsSpan(position).IndexOfAny('\r', '\n');
            if (next < 0) return false;
            position += next;
            if (++lines > 10) return true;
            if (text[position++] == '\r' && position < text.Length && text[position] == '\n') position++;
        }
        return false;
    }
    void RefreshComposerExpandButton()
    {
        var text = composer.Text ?? "";
        var viewer = FindTerminalScroll(composer);
        // Logical lines are exact; the text viewport also detects long wrapped drafts.
        bool manyLines = HasMoreThanTenLines(text) || viewer is { ExtentHeight: > 0 } &&
            viewer.ExtentHeight > Math.Max(1, composer.FontSize * 1.45) * 10 + 10;
        expandComposer.Visibility = manyLines ? Visibility.Visible : Visibility.Collapsed;
        composer.Padding = new(14, 12, manyLines ? 44 : 14, 58);
    }
    void ShowExpandedComposer()
    {
        if (expandedComposer != null) return;
        var editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsSpellCheckEnabled = false,
            FontSize = composer.FontSize, FontFamily = composer.FontFamily, Text = composer.Text,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Padding = new(16) };
#if !HAS_UNO
        editor.IsTextPredictionEnabled = false;
#endif
        AppTypography.SetScope(editor, FontArea.User);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        var panel = new Grid { RowSpacing = 16, Padding = new(24) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var close = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0) };
        FluentDesign.IconButton(close, "\uE73F", WorkflowText("Réduire la zone de saisie", "Collapse message editor"), false);
        var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(Label(WorkflowText("Rédiger le message", "Compose message"), 22)); Grid.SetColumn(close, 1); heading.Children.Add(close);
        panel.Children.Add(heading); Grid.SetRow(editor, 1); panel.Children.Add(editor);
        var done = new Button { Content = WorkflowText("Revenir au chat", "Back to chat"), Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(done, 2); panel.Children.Add(done);
        var overlay = expandedComposer = new Border { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Child = panel, RequestedTheme = root.RequestedTheme };
        void Collapse()
        {
            if (!ReferenceEquals(expandedComposer, overlay)) return;
            var start = editor.SelectionStart; var length = editor.SelectionLength;
            composer.Text = editor.Text;
            root.Children.Remove(overlay); expandedComposer = null;
            composer.Focus(FocusState.Programmatic);
            composer.Select(Math.Min(start, composer.Text.Length), Math.Min(length, Math.Max(0, composer.Text.Length - start)));
            RefreshComposerExpandButton();
        }
        close.Click += (_, _) => Collapse(); done.Click += (_, _) => Collapse();
        editor.PreviewKeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { args.Handled = true; Collapse(); } };
        root.Children.Add(overlay);
        var selectionStart = composer.SelectionStart; var selectionLength = composer.SelectionLength;
        editor.Loaded += (_, _) => { editor.Focus(FocusState.Programmatic); editor.Select(selectionStart, selectionLength); };
        AppTypography.Apply(panel);
    }
}