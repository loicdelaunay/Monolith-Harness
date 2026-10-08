using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly TerminalHub terminals = new();
    readonly TabView terminalTabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.SizeToContent };
    sealed record TerminalUi(int ChatId, TabViewItem Tab, TextBlock Info, TextBox Command, TextBox Output, Button Run, Button Stop);
    readonly Dictionary<string, TerminalUi> terminalViews = [];
    readonly DispatcherTimer terminalRefresh = new() { Interval = TimeSpan.FromMilliseconds(400) };
    int? terminalVisibleChat;

    FrameworkElement BuildTerminals()
    {
        terminalTabs.AddTabButtonClick += async (_, _) => await Guard(() => {
            if (chat == null) throw new InvalidOperationException("Sélectionnez une conversation.");
            var created = terminals.Create(chat.Id, false, "", RequireDirectory());
            RefreshTerminals(); terminalTabs.SelectedItem = terminalViews[created.Id].Tab;
            return Task.CompletedTask;
        });
        terminalTabs.TabCloseRequested += async (_, e) => await Guard(async () => {
            var id = (string)e.Tab.Tag;
            var view = terminalViews[id];
            await terminals.DeleteAsync(view.ChatId, null, id);
            RefreshTerminals();
        });
        terminalRefresh.Tick += (_, _) => RefreshTerminals();
        terminalTabs.Loaded += (_, _) => { RefreshTerminals(); terminalRefresh.Start(); };
        terminalTabs.Unloaded += (_, _) => terminalRefresh.Stop();
        return terminalTabs;
    }
    void RefreshTerminals()
    {
        var rows = chat == null ? [] : terminals.List(chat.Id);
        if (terminalVisibleChat != chat?.Id) { terminalTabs.TabItems.Clear(); terminalVisibleChat = chat?.Id; }
        foreach (var item in terminalTabs.TabItems.OfType<TabViewItem>().ToList())
            if (!rows.Any(x => x.Id == (string)item.Tag)) { terminalTabs.TabItems.Remove(item); terminalViews.Remove((string)item.Tag); }
        foreach (var row in rows)
        {
            if (!terminalViews.TryGetValue(row.Id, out var ui))
            {
                var panel = new Grid { RowSpacing = 8, Padding = new Thickness(4) };
                foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) panel.RowDefinitions.Add(new() { Height = height });
                var info = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Foreground = FluentDesign.Secondary };
                var output = OutputBox();
                var command = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 140, PlaceholderText = row.Shell + "…" };
                var start = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0) };
                var stop = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0) };
                FluentDesign.IconButton(start, "\uE768", WorkflowText("Exécuter", "Run"), false);
                FluentDesign.IconButton(stop, "\uE71A", WorkflowText("Arrêter", "Stop"), false);
                start.Click += async (_, _) => await Guard(() => {
                    var sourceProject = db.Projects.Local.FirstOrDefault(p => p.Id == db.Chats.Local.FirstOrDefault(c => c.Id == row.ChatId)?.ProjectId);
                    if (sourceProject == null || !sourceProject.GetSourceFolders().Any(path => PlatformSupport.PathComparer.Equals(Path.GetFullPath(path), row.Directory))) throw new UnauthorizedAccessException("Dossier détaché du projet.");
                    terminals.Start(row.ChatId, false, row.Id, command.Text, (text, append, ct) => WorkspaceTools.ShellAsync(text, row.Directory, ct, append), CancellationToken.None);
                    RefreshTerminals(); return Task.CompletedTask;
                });
                stop.Click += async (_, _) => await Guard(() => { terminals.Stop(row.ChatId, null, row.Id); return Task.CompletedTask; });
                panel.Children.Add(ToolToolbar(info, start, stop)); Grid.SetRow(output, 1); panel.Children.Add(output); Grid.SetRow(command, 2); panel.Children.Add(command);
                var tab = new TabViewItem { Tag = row.Id, Content = panel };
                ui = new(row.ChatId, tab, info, command, output, start, stop); terminalViews[row.Id] = ui;
            }
            if (!terminalTabs.TabItems.Contains(ui.Tab)) terminalTabs.TabItems.Add(ui.Tab);
            ui.Tab.Header = row.Name + (row.Status == "running" ? " ●" : "") + (row.Sandbox ? " · Sandbox" : "");
            ui.Info.Text = row.Shell + " · " + row.Status + " · " + row.Directory;
            ToolTipService.SetToolTip(ui.Info, ui.Info.Text);
            var text = (row.Command.Length == 0 ? "" : "> " + row.Command + "\n") + row.Output;
            if (ui.Output.Text != text)
            {
                var scroller = FindTerminalScroll(ui.Output);
                var offset = scroller?.VerticalOffset ?? 0;
                var follow = scroller == null || scroller.ScrollableHeight - offset < 24;
                var selection = ui.Output.SelectionStart; var length = ui.Output.SelectionLength;
                ui.Output.Text = text;
                if (length > 0) ui.Output.Select(Math.Min(selection, text.Length), Math.Min(length, Math.Max(0, text.Length - selection)));
                ui.Output.DispatcherQueue.TryEnqueue(() => {
                    if (ui.Output.Text != text) return;
                    ui.Output.UpdateLayout();
                    var view = FindTerminalScroll(ui.Output);
                    view?.ChangeView(null, follow ? view.ScrollableHeight : offset, null, true);
                });
            }
            ui.Run.IsEnabled = row.Status != "running" && !row.Sandbox;
            ui.Stop.IsEnabled = row.Status == "running";
        }
    }
    static ScrollViewer? FindTerminalScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindTerminalScroll(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    void SelectTerminalTab(ConversationRun run, string? terminalId, bool legacyTerminal = false)
    {
        if (!IsVisible(run) || !browserVisible || toolTabs.SelectedIndex != 1 ||
            !FeatureSettings.Read(state.FeaturesJson).AutoFocusTool) return;
        RefreshTerminals();
        if (legacyTerminal)
        {
            var directory = run.Project.GetSourceFolders().FirstOrDefault(Directory.Exists);
            terminalId = terminals.List(run.Chat.Id, run.Chat.SandboxEnabled)
                .FirstOrDefault(item => item.Name == "Terminal" && item.Status != "running" &&
                    directory != null && PlatformSupport.PathComparer.Equals(item.Directory, directory))?.Id;
        }
        if (terminalId != null && terminalViews.TryGetValue(terminalId, out var view) &&
            view.ChatId == run.Chat.Id && terminalTabs.TabItems.Contains(view.Tab))
            terminalTabs.SelectedItem = view.Tab;
    }
}
