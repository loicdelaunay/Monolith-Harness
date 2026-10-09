using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

/// <summary>Native, modeless review window. Project files are touched only by Finish.</summary>
internal sealed class ProposalReviewWindow : Window
{
    readonly IReadOnlyList<ProposalDiff> diffs;
    readonly Func<string, string, string> text;
    readonly Func<IReadOnlyCollection<ProposalDecision>, Task> save, finish;
    readonly Dictionary<(string Path, int Hunk), bool> decisions = [];
    readonly ListView fileList = new() { SelectionMode = ListViewSelectionMode.Single };
    readonly ListView diffList = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false };
    readonly TextBlock fileHeading = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock progress = new() { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Foreground = FluentDesign.Resource("DiffRemovedTextBrush") };
    readonly List<TextBlock> fileStates = [];
    readonly List<ReviewCommand> commands = [];
    readonly List<ReviewRow> headers = [];
    readonly Button finishButton;
    bool busy, closed;
    int Selected => fileList.SelectedIndex;
    public Grid ReviewRoot { get; }

    public ProposalReviewWindow(string title, ProposalBatch batch, IReadOnlyList<ProposalDiff> diffs, ElementTheme theme,
        Func<string, string, string> text, Func<IReadOnlyCollection<ProposalDecision>, Task> save,
        Func<IReadOnlyCollection<ProposalDecision>, Task> finish)
    {
        this.diffs = diffs; this.text = text; this.save = save; this.finish = finish;
        Title = text("Revue des modifications", "Change review") + " · " + title;
        foreach (var decision in batch.Decisions ?? [])
            if (diffs.Any(diff => diff.File.Path == decision.Path && diff.Hunks.Any(hunk => hunk.Id == decision.HunkId)))
                decisions[(decision.Path, decision.HunkId)] = decision.Accepted;
        ReviewRoot = new Grid { RequestedTheme = theme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Padding = new(20), RowSpacing = 16 };
        ReviewRoot.RowDefinitions.Add(new() { Height = GridLength.Auto });
        ReviewRoot.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        ReviewRoot.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Spacing = 6 };
        heading.Children.Add(new TextBlock { Text = text("Revue des modifications proposées", "Review proposed changes"), FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = text("Acceptez ou refusez chaque bloc, puis terminez pour appliquer vos choix.", "Accept or reject every block, then finish to apply your choices."),
            Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap });
        var totals = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        totals.Children.Add(new TextBlock { Text = text($"{diffs.Count} fichiers proposés", $"{diffs.Count} proposed files"), Foreground = FluentDesign.Primary });
        totals.Children.Add(new TextBlock { Text = "+" + diffs.Sum(diff => diff.Added), Foreground = FluentDesign.Resource("DiffAddedTextBrush") });
        totals.Children.Add(new TextBlock { Text = "−" + diffs.Sum(diff => diff.Removed), Foreground = FluentDesign.Resource("DiffRemovedTextBrush") });
        heading.Children.Add(totals); ReviewRoot.Children.Add(heading);

        var columns = new Grid { ColumnSpacing = 16 };
        columns.ColumnDefinitions.Add(new() { Width = new(280), MinWidth = 160 });
        columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        columns.SizeChanged += (_, e) => columns.ColumnDefinitions[0].Width = new(Math.Clamp(e.NewSize.Width * .27, 160, 310));
        Grid.SetRow(columns, 1); ReviewRoot.Children.Add(columns);
        var files = new Grid { RowSpacing = 8 };
        files.RowDefinitions.Add(new() { Height = GridLength.Auto }); files.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        files.Children.Add(new TextBlock { Text = text("Fichiers", "Files"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary });
        fileList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(fileList, text("Fichiers proposés", "Proposed files"));
        Grid.SetRow(fileList, 1); files.Children.Add(fileList); columns.Children.Add(FluentDesign.Surface(files, 12));
        foreach (var diff in diffs)
        {
            var item = new StackPanel { Spacing = 6, Padding = new(4, 6, 4, 6) };
            var name = new TextBlock { Text = diff.File.Path, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 };
            ToolTipService.SetToolTip(name, diff.File.ResolvedPath); item.Children.Add(name);
            var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            counts.Children.Add(new TextBlock { Text = "+" + diff.Added, Foreground = FluentDesign.Resource("DiffAddedTextBrush"), FontSize = 12 });
            counts.Children.Add(new TextBlock { Text = "−" + diff.Removed, Foreground = FluentDesign.Resource("DiffRemovedTextBrush"), FontSize = 12 });
            item.Children.Add(counts);
            var state = new TextBlock { FontSize = 12, Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap };
            item.Children.Add(state); fileStates.Add(state);
            var entry = new ListViewItem { Content = item, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(entry, diff.File.Path); fileList.Items.Add(entry);
        }
        var detail = new Grid { RowSpacing = 10 };
        detail.RowDefinitions.Add(new() { Height = GridLength.Auto }); detail.RowDefinitions.Add(new() { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        fileHeading.Foreground = FluentDesign.Primary; detail.Children.Add(fileHeading);
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fileActions.Children.Add(CommandButton(text("Accepter ce fichier", "Accept file"), () => DecideAsync(true, Selected), () => Selected >= 0 && diffs[Selected].Hunks.Count > 0));
        fileActions.Children.Add(CommandButton(text("Refuser ce fichier", "Reject file"), () => DecideAsync(false, Selected), () => Selected >= 0 && diffs[Selected].Hunks.Count > 0));
        Grid.SetRow(fileActions, 1); detail.Children.Add(fileActions);
        diffList.ItemTemplate = (DataTemplate)XamlReader.Load(RowTemplate);
        var rowStyle = new Style(typeof(ListViewItem));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        rowStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        rowStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        rowStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        rowStyle.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        // The default ListViewItemPresenter keeps its own touch-target minimum height,
        // even with ListViewItem.MinHeight = 0. Size each diff row from its content instead.
        rowStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}"
                    HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch"
                    Padding="0" Margin="0" MinHeight="0" />
            </ControlTemplate>
            """)));
        diffList.ItemContainerStyle = rowStyle;
        ScrollViewer.SetHorizontalScrollBarVisibility(diffList, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(diffList, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(diffList, text("Blocs de différences", "Change blocks"));
        Grid.SetRow(diffList, 2); detail.Children.Add(diffList);
        var detailSurface = FluentDesign.Surface(detail, 12); Grid.SetColumn(detailSurface, 1); columns.Children.Add(detailSurface);

        var footer = new StackPanel { Spacing = 10 };
        footer.Children.Add(progress); footer.Children.Add(error);
        var actionRow = new Grid { ColumnSpacing = 8, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < 4; i++) actionRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        actionRow.RowDefinitions.Add(new() { Height = GridLength.Auto }); actionRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var rejectAll = CommandButton(text("Refuser tout", "Reject all"), () => DecideAsync(false), () => diffs.Any(diff => diff.Hunks.Count > 0));
        var acceptAll = CommandButton(text("Accepter tout", "Accept all"), () => DecideAsync(true), () => diffs.Any(diff => diff.Hunks.Count > 0));
        var closeButton = new Button { Content = text("Fermer", "Close") }; closeButton.Click += (_, _) => Close();
        finishButton = new Button { Content = text("Terminer", "Finish"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        finishButton.Click += async (_, _) => await FinishAsync();
        var actions = new[] { rejectAll, acceptAll, closeButton, finishButton };
        for (int i = 0; i < actions.Length; i++) { Grid.SetColumn(actions[i], i); actionRow.Children.Add(actions[i]); }
        footer.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 600;
            for (int i = 0; i < actions.Length; i++) { Grid.SetColumn(actions[i], narrow ? 2 + i % 2 : i); Grid.SetRow(actions[i], narrow && i >= 2 ? 1 : 0); }
        };
        ToolTipService.SetToolTip(finishButton, text("Toutes les modifications doivent être acceptées ou refusées. Seuls les blocs acceptés seront appliqués.", "Every change must be accepted or rejected. Only accepted blocks will be applied."));
        ToolTipService.SetToolTip(closeButton, text("Fermer et conserver la revue en attente.", "Close and keep the review pending."));
        footer.Children.Add(actionRow); Grid.SetRow(footer, 2); ReviewRoot.Children.Add(footer);
        fileList.SelectionChanged += (_, _) => ShowFile();
        Content = ReviewRoot; Closed += (_, _) => closed = true;
        FluentDesign.WindowChrome(this); AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1180, Height = 820 });
        fileList.SelectedIndex = 0; RefreshDecisions();
    }

    Button CommandButton(string label, Func<Task> action, Func<bool> available)
    {
        var command = new ReviewCommand(action, () => !busy && !closed && available()); commands.Add(command);
        return new Button { Content = label, Command = command };
    }
    void ShowFile()
    {
        if (Selected < 0) return;
        var diff = diffs[Selected]; fileHeading.Text = diff.File.Path; headers.Clear();
        var rows = new List<ReviewRow>();
        foreach (var hunk in diff.Hunks)
        {
            var key = (diff.File.Path, hunk.Id);
            var header = new ReviewRow { Hunk = hunk, Path = diff.File.Path, HeaderVisibility = Visibility.Visible,
                AcceptText = text("Accepter", "Accept"), RejectText = text("Refuser", "Reject") };
            header.Accept = new ReviewCommand(() => DecideAsync(true, hunk: key), () => !busy && !closed && (!decisions.TryGetValue(key, out var accepted) || !accepted));
            header.Reject = new ReviewCommand(() => DecideAsync(false, hunk: key), () => !busy && !closed && (!decisions.TryGetValue(key, out var accepted) || accepted));
            headers.Add(header); rows.Add(header);
            if (hunk.Lines.Count == 0) rows.Add(ReviewRow.Note(text("Création d’un fichier vide", "Create empty file")));
            foreach (var line in hunk.Lines)
            {
                var added = line.Kind == ProposalLineKind.Added; var removed = line.Kind == ProposalLineKind.Removed;
                rows.Add(new ReviewRow { CodeVisibility = Visibility.Visible, Code = line.Text, BeforeLine = line.BeforeLine?.ToString() ?? "", AfterLine = line.AfterLine?.ToString() ?? "",
                    Marker = added ? "+" : removed ? "−" : "", Background = FluentDesign.Resource(added ? "DiffAddedFillBrush" : removed ? "DiffRemovedFillBrush" : "TransparentBrush"),
                    Foreground = FluentDesign.Resource(added ? "DiffAddedTextBrush" : removed ? "DiffRemovedTextBrush" : "TextFillColorPrimaryBrush") });
                if (!line.HasLineEnding) rows.Add(ReviewRow.Note(text("\\ Pas de saut de ligne en fin de fichier", "\\ No newline at end of file")));
            }
        }
        if (rows.Count == 0) rows.Add(ReviewRow.Note(text("Aucune différence textuelle", "No text differences")));
        diffList.ItemsSource = rows; RefreshDecisions();
    }
    IReadOnlyCollection<ProposalDecision> Snapshot() => decisions.Select(entry => new ProposalDecision(entry.Key.Path, entry.Key.Hunk, entry.Value)).ToArray();
    async Task DecideAsync(bool accepted, int? file = null, (string Path, int Hunk)? hunk = null)
    {
        if (busy || closed) return;
        var previous = decisions.ToDictionary(entry => entry.Key, entry => entry.Value);
        if (hunk is { } key) decisions[key] = accepted;
        else
            foreach (var diff in file is { } index ? new[] { diffs[index] } : diffs)
                foreach (var change in diff.Hunks) decisions[(diff.File.Path, change.Id)] = accepted;
        busy = true; error.Visibility = Visibility.Collapsed; RefreshDecisions();
        try { await save(Snapshot()); }
        catch (Exception ex)
        {
            decisions.Clear(); foreach (var entry in previous) decisions[entry.Key] = entry.Value;
            ReportError(ex);
        }
        finally { busy = false; if (!closed) RefreshDecisions(); }
    }
    async Task FinishAsync()
    {
        if (busy || closed || decisions.Count != diffs.Sum(diff => diff.Hunks.Count)) return;
        busy = true; error.Visibility = Visibility.Collapsed; RefreshDecisions();
        try { await finish(Snapshot()); if (!closed) Close(); }
        catch (Exception ex) { ReportError(ex); }
        finally { busy = false; if (!closed) RefreshDecisions(); }
    }
    void ReportError(Exception ex)
    {
        AppLog.Write(AppLogLevel.Error, "proposals.review_failed", ex);
        if (!closed) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
    }
    void RefreshDecisions()
    {
        var total = diffs.Sum(diff => diff.Hunks.Count); var accepted = decisions.Count(entry => entry.Value); var rejected = decisions.Count - accepted;
        progress.Text = busy ? text("Enregistrement…", "Saving…") : text($"{decisions.Count}/{total} modifications décidées · {accepted} acceptées · {rejected} refusées", $"{decisions.Count}/{total} changes decided · {accepted} accepted · {rejected} rejected");
        finishButton.IsEnabled = !busy && decisions.Count == total;
        for (int i = 0; i < diffs.Count; i++)
        {
            var diff = diffs[i]; var chosen = decisions.Count(entry => entry.Key.Path == diff.File.Path); var yes = decisions.Count(entry => entry.Key.Path == diff.File.Path && entry.Value);
            fileStates[i].Foreground = chosen == diff.Hunks.Count && diff.Hunks.Count > 0
                ? FluentDesign.Resource(yes == 0 ? "DiffRemovedTextBrush" : yes == diff.Hunks.Count ? "DiffAddedTextBrush" : "TextFillColorSecondaryBrush") : FluentDesign.Secondary;
            fileStates[i].Text = diff.Hunks.Count == 0 ? text("Identique", "Unchanged") : chosen < diff.Hunks.Count ? text($"{chosen}/{diff.Hunks.Count} blocs décidés", $"{chosen}/{diff.Hunks.Count} blocks decided")
                : yes == 0 ? text("Refusé", "Rejected") : yes == diff.Hunks.Count ? text("Accepté", "Accepted") : text("Choix partiels", "Partial selection");
        }
        foreach (var header in headers)
        {
            var hunk = header.Hunk!; var decided = decisions.TryGetValue((header.Path, hunk.Id), out var value);
            header.Header = text($"Bloc {hunk.Id + 1} · +{hunk.Added} −{hunk.Removed}", $"Block {hunk.Id + 1} · +{hunk.Added} −{hunk.Removed}") + " · "
                + (!decided ? text("À décider", "Pending") : value ? text("Accepté", "Accepted") : text("Refusé", "Rejected"));
            header.Accept!.Refresh(); header.Reject!.Refresh();
        }
        foreach (var command in commands) command.Refresh();
    }

    public sealed class ReviewCommand(Func<Task> action, Func<bool> available) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => available();
        public async void Execute(object? parameter) { if (CanExecute(parameter)) await action(); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    public sealed class ReviewRow : INotifyPropertyChanged
    {
        string header = "";
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Header { get => header; set { header = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Header))); } }
        public string Path { get; init; } = "";
        public ProposalHunk? Hunk { get; init; }
        public Visibility HeaderVisibility { get; init; } = Visibility.Collapsed;
        public Visibility CodeVisibility { get; init; } = Visibility.Collapsed;
        public string Code { get; init; } = "";
        public string BeforeLine { get; init; } = "";
        public string AfterLine { get; init; } = "";
        public string Marker { get; init; } = "";
        public Brush Background { get; init; } = FluentDesign.Resource("TransparentBrush");
        public Brush Foreground { get; init; } = FluentDesign.Primary;
        public Brush Muted => FluentDesign.Secondary;
        public string AcceptText { get; init; } = "";
        public string RejectText { get; init; } = "";
        public ReviewCommand? Accept { get; set; }
        public ReviewCommand? Reject { get; set; }
        public static ReviewRow Note(string text) => new() { CodeVisibility = Visibility.Visible, Code = text, Foreground = FluentDesign.Secondary };
    }

    const string RowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid HorizontalAlignment="Stretch">
            <Grid Visibility="{Binding HeaderVisibility}" Padding="8,12" ColumnSpacing="8">
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBlock Text="{Binding Header}" Foreground="{Binding Foreground}" TextWrapping="Wrap" VerticalAlignment="Center" FontWeight="SemiBold"/>
              <Button Grid.Column="1" Content="{Binding AcceptText}" Command="{Binding Accept}"/>
              <Button Grid.Column="2" Content="{Binding RejectText}" Command="{Binding Reject}"/>
            </Grid>
            <Grid Visibility="{Binding CodeVisibility}" Background="{Binding Background}" Padding="4,2" MinHeight="22">
              <Grid.ColumnDefinitions><ColumnDefinition Width="48"/><ColumnDefinition Width="48"/><ColumnDefinition Width="22"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBlock Text="{Binding BeforeLine}" Foreground="{Binding Muted}" HorizontalAlignment="Right" Margin="0,0,8,0" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12"/>
              <TextBlock Grid.Column="1" Text="{Binding AfterLine}" Foreground="{Binding Muted}" HorizontalAlignment="Right" Margin="0,0,8,0" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12"/>
              <TextBlock Grid.Column="2" Text="{Binding Marker}" Foreground="{Binding Foreground}" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12"/>
              <TextBlock Grid.Column="3" Text="{Binding Code}" Foreground="{Binding Foreground}" TextWrapping="NoWrap" IsTextSelectionEnabled="True" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12"/>
            </Grid>
          </Grid>
        </DataTemplate>
        """;
}
