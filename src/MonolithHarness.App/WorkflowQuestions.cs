using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task<AgentAnswer> AskWorkflowQuestionsAsync(ConversationRun run, IReadOnlyList<AgentQuestion> questions, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<AgentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new StackPanel { Spacing = 12 };
        var heading = new Grid { ColumnSpacing = 16 };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var progress = new TextBlock { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(progress);
        var steps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Visibility = questions.Count > 1 ? Visibility.Visible : Visibility.Collapsed };
        var dots = new List<Border>();
        for (int i = 0; i < questions.Count; i++)
        {
            if (i > 0) steps.Children.Add(new Border { Width = 12, Height = 1, Background = FluentDesign.Stroke, VerticalAlignment = VerticalAlignment.Center });
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new(4), Background = FluentDesign.Stroke };
            dots.Add(dot); steps.Children.Add(dot);
        }
        Grid.SetColumn(steps, 1); heading.Children.Add(steps);
        heading.SizeChanged += (_, _) => steps.Visibility = questions.Count > 1 && heading.ActualWidth >= 360 ? Visibility.Visible : Visibility.Collapsed;
        var collapse = new Button { Content = FluentDesign.Icon("\uE70E", 12), MinWidth = 30, MinHeight = 30,
            Padding = new(6), Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0) };
        AutomationProperties.SetAutomationId(collapse, "workflow-collapse");
        AutomationProperties.SetName(collapse, WorkflowText("Réduire ou développer les questions", "Collapse or expand questions"));
        Grid.SetColumn(collapse, 2); heading.Children.Add(collapse); panel.Children.Add(heading);
        var details = new StackPanel { Spacing = 12 };
        panel.Children.Add(details);
        var accordion = new StackPanel { Spacing = 6 };
        var questionViewport = new ScrollViewer { Content = accordion, MaxHeight = 490,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        details.Children.Add(questionViewport);
        var readers = new List<Func<IReadOnlyList<string>>>();
        var customValid = new List<Func<bool>>();
        var pages = new List<StackPanel>();
        var headers = new List<Button>();
        var summaries = new List<TextBlock>();
        var chevrons = new List<FontIcon>();
        int current = 0;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"), FontSize = 12 };
        AutomationProperties.SetAutomationId(error, "workflow-error");
        Action refresh = () => { };
        for (int qi = 0; qi < questions.Count; qi++)
        {
            int index = qi; var q = questions[qi];
            var group = new StackPanel { Spacing = 10 };
            var title = new TextBlock { Text = QuestionDisplayTitle(q.Question, qi), FontSize = 15, TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary };
            var summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Secondary };
            summaries.Add(summary);
            var copy = new StackPanel { Spacing = 3 }; copy.Children.Add(title); copy.Children.Add(summary);
            var headerGrid = new Grid { ColumnSpacing = 10 };
            headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock { Text = questions.Count > 1 ? $"{qi + 1}." : "", FontSize = 14,
                Foreground = FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) });
            Grid.SetColumn(copy, 1); headerGrid.Children.Add(copy);
            var chevron = FluentDesign.Icon("\uE70D", 11); Grid.SetColumn(chevron, 2); headerGrid.Children.Add(chevron); chevrons.Add(chevron);
            var header = new Button { Content = headerGrid, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(10, 9, 10, 9),
                BorderThickness = new(0), Background = FluentDesign.Resource("TransparentBrush"), CornerRadius = new(8) };
            AutomationProperties.SetAutomationId(header, "workflow-question-" + qi); headers.Add(header); group.Children.Add(header);
            var page = new StackPanel { Spacing = 10, Margin = new(10, 0, 10, 10), Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(page, "workflow-question-page-" + qi);
            pages.Add(page); group.Children.Add(page);
            var tiles = new Grid { ColumnSpacing = 8, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
            var choices = new List<(ToggleButton Toggle, string Label)>();
            var tilesList = new List<Border>();
            string groupName = "question-" + Guid.NewGuid().ToString("N");
            Border Choice(string label, string description, string icon, ToggleButton toggle)
            {
                var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary });
                if (!string.IsNullOrWhiteSpace(description)) text.Children.Add(new TextBlock { Text = description,
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary });
                var layout = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
                layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                if (QuestionIcon(icon) is { } glyph) layout.Children.Add(FluentDesign.Icon(glyph, 24));
                Grid.SetColumn(text, 1); layout.Children.Add(text);
                // The native templates pin their bullet to the top of multiline Content.
                // Keep an empty native selector in its own centered column instead.
                toggle.Content = null; toggle.Width = 24; toggle.MinWidth = 0; toggle.MinHeight = 32;
                // Auto height keeps the native bullet centered when TextZoom scales fixed-height controls.
                toggle.Height = double.NaN;
                toggle.VerticalAlignment = VerticalAlignment.Center; toggle.HorizontalAlignment = HorizontalAlignment.Center;
                toggle.Padding = new(0); toggle.Margin = new(0); toggle.Background = FluentDesign.Resource("TransparentBrush"); toggle.BorderThickness = new(0);
                AutomationProperties.SetName(toggle, label);
                var content = new Grid { ColumnSpacing = 10 };
                content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                content.Children.Add(toggle); Grid.SetColumn(layout, 1); content.Children.Add(layout);
                var tile = new Border { Child = content, Padding = new(12), MinHeight = 80, BorderThickness = new(1), CornerRadius = new(8),
                    Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke };
                AutomationProperties.SetAutomationId(tile, "workflow-choice");
                tile.Tapped += (_, args) => {
                    // Let the native control handle its own click exactly once.
                    for (var node = args.OriginalSource as DependencyObject; node != null && node != tile; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                        if (node == toggle) return;
                    if (!toggle.IsEnabled) return;
                    toggle.IsChecked = q.Multiple ? toggle.IsChecked != true : true;
                    toggle.Focus(FocusState.Pointer); args.Handled = true;
                };
                bool hovered = false;
                void Paint() {
                    tile.Background = toggle.IsChecked == true ? FluentDesign.Resource("ControlSelectedBrush") : hovered ? FluentDesign.Resource("ControlHoverBrush") : FluentDesign.Card;
                    tile.BorderBrush = toggle.IsChecked == true ? FluentDesign.Resource("AccentFillColorDefaultBrush") : FluentDesign.Stroke;
                    refresh();
                }
                toggle.Checked += (_, _) => Paint(); toggle.Unchecked += (_, _) => Paint();
                tile.PointerEntered += (_, _) => { hovered = true; Paint(); }; tile.PointerExited += (_, _) => { hovered = false; Paint(); };
                return tile;
            }
            foreach (var option in q.Options)
            {
                ToggleButton toggle = q.Multiple ? new CheckBox() : new RadioButton { GroupName = groupName };
                choices.Add((toggle, option.Label)); tilesList.Add(Choice(option.Label, option.Description, option.Icon, toggle));
            }
            var free = new TextBox { PlaceholderText = WorkflowText("Écrivez votre réponse…", "Write your answer…"),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 92, MaxHeight = 180 };
            AutomationProperties.SetAutomationId(free, "workflow-free-" + qi);
            ToggleButton? custom = null;
            if (q.Custom && q.Options.Count > 0)
            {
                custom = q.Multiple ? new CheckBox() : new RadioButton { GroupName = groupName };
                tilesList.Add(Choice(WorkflowText("Autre réponse", "Custom answer"), WorkflowText("Écrivez votre propre réponse", "Write your own answer"), "edit", custom));
                free.Visibility = Visibility.Collapsed;
                custom.Checked += (_, _) => { free.Visibility = Visibility.Visible; free.Focus(FocusState.Programmatic); };
                custom.Unchecked += (_, _) => free.Visibility = Visibility.Collapsed;
            }
            foreach (var tile in tilesList) tiles.Children.Add(tile);
            void ArrangeTiles() {
                int cols = tiles.ActualWidth >= 850 ? 3 : tiles.ActualWidth >= 520 ? 2 : 1;
                if (tiles.ColumnDefinitions.Count == cols && tiles.RowDefinitions.Count > 0) return;
                tiles.ColumnDefinitions.Clear(); tiles.RowDefinitions.Clear();
                for (int i = 0; i < cols; i++) tiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                for (int i = 0; i < (tilesList.Count + cols - 1) / cols; i++) tiles.RowDefinitions.Add(new() { Height = GridLength.Auto });
                for (int i = 0; i < tilesList.Count; i++) { Grid.SetColumn(tilesList[i], i % cols); Grid.SetRow(tilesList[i], i / cols); }
            }
            if (tilesList.Count > 0) { ArrangeTiles(); tiles.SizeChanged += (_, _) => ArrangeTiles(); page.Children.Add(tiles); }
            if (q.Custom) page.Children.Add(free);
            free.TextChanged += (_, _) => refresh();
            bool UsesText() => q.Custom && (q.Options.Count == 0 || custom?.IsChecked == true);
            readers.Add(() => {
                var values = choices.Where(x => x.Toggle.IsChecked == true).Select(x => x.Label).ToList();
                if (UsesText() && !string.IsNullOrWhiteSpace(free.Text)) values.Add(free.Text.Trim());
                return values;
            });
            customValid.Add(() => !UsesText() || !string.IsNullOrWhiteSpace(free.Text));
            header.Click += (_, _) => {
                if (current == index && pages[index].Visibility == Visibility.Visible) pages[index].Visibility = Visibility.Collapsed;
                else Show(index);
                refresh();
            };
            accordion.Children.Add(new Border { Child = group, BorderBrush = FluentDesign.Stroke, BorderThickness = new(0, 0, 0, 1) });
        }
        details.Children.Add(error);
        var footer = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var cancel = new Button { Content = WorkflowText("Annuler", "Cancel"), HorizontalAlignment = HorizontalAlignment.Left };
        var back = new Button { Content = WorkflowText("Précédente", "Previous") };
        var send = new Button { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetAutomationId(cancel, "workflow-cancel"); AutomationProperties.SetAutomationId(back, "workflow-back"); AutomationProperties.SetAutomationId(send, "workflow-next");
        footer.Children.Add(cancel); Grid.SetColumn(back, 1); footer.Children.Add(back); Grid.SetColumn(send, 2); footer.Children.Add(send); details.Children.Add(footer);
        bool? narrowFooter = null;
        void ArrangeFooter()
        {
            double minimumWidth = current == questions.Count - 1 ? (current > 0 ? 440 : 320) : (current > 0 ? 340 : 240);
            bool narrow = footer.ActualWidth > 0 && footer.ActualWidth < minimumWidth * Math.Max(1, TextZoom.ForWindow(root) / 100d);
            if (narrowFooter == narrow) return;
            narrowFooter = narrow; footer.ColumnDefinitions.Clear(); footer.RowDefinitions.Clear();
            footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            if (!narrow) footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
            if (narrow) footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(back, 1); Grid.SetColumn(send, narrow ? 0 : 2); Grid.SetRow(send, narrow ? 1 : 0); Grid.SetColumnSpan(send, narrow ? 2 : 1);
            send.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        }
        footer.SizeChanged += (_, _) => ArrangeFooter(); footer.LayoutUpdated += (_, _) => ArrangeFooter(); ArrangeFooter();
        refresh = () => {
            progress.Text = questions.Count == 1 ? WorkflowText("Votre réponse", "Your answer") : WorkflowText("Question", "Question") + $" {current + 1} / {questions.Count}";
            for (int i = 0; i < questions.Count; i++) {
                var answers = readers[i]();
                dots[i].Background = i == current || answers.Count > 0 ? FluentDesign.Resource("AccentFillColorDefaultBrush") : FluentDesign.Stroke;
                summaries[i].Text = answers.Count > 0 ? "✓ " + string.Join(", ", answers) : questions[i].Options.Count == 0 ? WorkflowText("Réponse libre", "Free text") : questions[i].Multiple ? WorkflowText("Plusieurs réponses possibles", "Multiple answers allowed") : WorkflowText("Une seule réponse", "One answer");
                headers[i].Background = i == current ? FluentDesign.Resource("ControlHoverBrush") : FluentDesign.Resource("TransparentBrush");
                chevrons[i].Glyph = pages[i].Visibility == Visibility.Visible ? "\uE70E" : "\uE70D";
            }
            if (readers[current]().Count > 0 && customValid[current]()) error.Text = "";
            back.Visibility = current == 0 ? Visibility.Collapsed : Visibility.Visible;
            send.Content = current == questions.Count - 1 ? WorkflowText("Répondre et reprendre", "Answer and resume") : WorkflowText("Suivante", "Next");
        };
        void Show(int index)
        {
            current = index; for (int i = 0; i < pages.Count; i++) pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; refresh();
            DispatcherQueue.TryEnqueue(() => { if (headers[index].IsLoaded) headers[index].StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 }); });
        }
        Show(0);
        collapse.Click += (_, _) => { details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; collapse.Content = FluentDesign.Icon(details.Visibility == Visibility.Visible ? "\uE70E" : "\uE70D", 12); };
        back.Click += (_, _) => { error.Text = ""; Show(current - 1); };
        send.Click += (_, _) => {
            try {
                if (!customValid[current]()) throw new ArgumentException();
                WorkflowTools.ValidateAnswer([questions[current]], new(false, [readers[current]()]));
            } catch (ArgumentException) { error.Text = WorkflowText("Sélectionnez une réponse ou saisissez votre texte.", "Select an answer or enter your text."); Show(current); return; }
            error.Text = "";
            if (current < questions.Count - 1) { Show(current + 1); return; }
            for (int i = 0; i < questions.Count; i++) {
                try { if (!customValid[i]()) throw new ArgumentException(); WorkflowTools.ValidateAnswer([questions[i]], new(false, [readers[i]()])); }
                catch (ArgumentException) { Show(i); error.Text = WorkflowText("Répondez à cette question.", "Answer this question."); return; }
            }
            completion.TrySetResult(new(false, readers.Select(read => read()).ToList()));
        };
        cancel.Click += (_, _) => completion.TrySetResult(new(true, []));
        var card = new Border { Child = panel, Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), Background = FluentDesign.Resource("AssistantMessageFillBrush"), BorderBrush = FluentDesign.Stroke };
        void FitQuestionViewport()
        {
            if (scroll.ActualHeight <= 0) return;
            double overhead = card.ActualHeight > 0 ? Math.Max(128, card.ActualHeight - questionViewport.ActualHeight) : 160;
            double height = Math.Clamp(scroll.ActualHeight - overhead - 12, 96, 490);
            if (Math.Abs(questionViewport.MaxHeight - height) > 1) questionViewport.MaxHeight = height;
        }
        SizeChangedEventHandler viewportChanged = (_, _) => FitQuestionViewport();
        scroll.SizeChanged += viewportChanged; card.SizeChanged += viewportChanged;
        run.Messages.Children.Add(card); FitQuestionViewport(); ScrollRunToBottom(run); NotifyChat(run, true);
        SetRunStatus(run, WorkflowText("Réponse attendue dans la conversation", "Waiting for your answer in the conversation"), StatusKind.Notice);
        try {
            var answer = await completion.Task.WaitAsync(ct);
            string summary = string.Join("\n\n", questions.Select((q, i) => q.Question + "\n→ " + (answer.Cancelled ? WorkflowText("Annulé", "Cancelled") : string.Join(", ", answer.Answers[i]))));
            await using var store = new HarnessDb();
            store.Messages.Add(new Message { ChatId = run.Chat.Id, Role = "interaction", State = "ui", Content = summary });
            await store.SaveChangesAsync(ct); AddMessage("assistant", summary, [], run.Messages);
            SetRunStatus(run, WorkflowText("Reprise du travail…", "Resuming…")); return answer;
        } finally { scroll.SizeChanged -= viewportChanged; card.SizeChanged -= viewportChanged; run.Messages.Children.Remove(card); AcknowledgeChatNotice(run.Chat.Id, actionResolved: true); }
    }
    static string QuestionDisplayTitle(string title, int index)
    {
        var value = title.TrimStart();
        foreach (var prefix in new[] { $"{index + 1}. ", $"{index + 1}) " })
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return value[prefix.Length..].TrimStart();
        return title;
    }
    static string? QuestionIcon(string icon) => icon switch { "sun" => "\uE706", "moon" => "\uE708", "system" => "\uE7F4", "code" => "\uE943", "edit" => "\uE70F", "folder" => "\uE8B7", "check" => "\uE73E", "warning" => "\uE7BA", _ => null };
}
