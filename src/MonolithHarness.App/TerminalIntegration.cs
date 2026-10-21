using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly TerminalHub terminals = new();
    readonly TabView terminalTabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.SizeToContent };
    sealed record TerminalUi(int ChatId, TabViewItem Tab, TextBlock Info, TextBox Command, TextBox Output, Button Run, Button Stop)
    { public ContentTailFollower? Follower { get; set; } }
    readonly Dictionary<string, TerminalUi> terminalViews = [];
    readonly DispatcherTimer terminalRefresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    int? terminalVisibleChat;
    int terminalChangeQueued, terminalDirty = 1, terminalPresentationChat;
    volatile bool terminalPresentationActive;
    bool TerminalVisualsAvailable => VisualUpdatesAllowed && browserVisible && toolTabs.SelectedIndex == 1 && terminalTabs.IsLoaded;
    void TerminalChanged(int changedChat)
    {
        Interlocked.Exchange(ref terminalDirty, 1);
        if (idleClosed || !terminalPresentationActive || changedChat != Volatile.Read(ref terminalPresentationChat) || Interlocked.Exchange(ref terminalChangeQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() => { if (TerminalVisualsAvailable) { if (!terminalRefresh.IsEnabled) terminalRefresh.Start(); } else Interlocked.Exchange(ref terminalChangeQueued, 0); }))
            Interlocked.Exchange(ref terminalChangeQueued, 0);
    }
    void RefreshTerminalPresentation()
    {
        terminalPresentationActive = TerminalVisualsAvailable; terminalPresentationChat = chat?.Id ?? 0;
        terminalRefresh.Stop(); Interlocked.Exchange(ref terminalChangeQueued, 0);
        if (TerminalVisualsAvailable && (Volatile.Read(ref terminalDirty) != 0 || terminalVisibleChat != chat?.Id)) RefreshTerminals();
    }

    FrameworkElement BuildTerminals()
    {
        CompactToolTabs(terminalTabs);
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
        terminals.Changed += TerminalChanged;
        terminalRefresh.Tick += (_, _) => { terminalRefresh.Stop(); Interlocked.Exchange(ref terminalChangeQueued, 0); if (Volatile.Read(ref terminalDirty) != 0) RefreshTerminals(); };
        terminalTabs.Loaded += (_, _) => { Interlocked.Exchange(ref terminalDirty, 1); RefreshTerminalPresentation(); };
        terminalTabs.Unloaded += (_, _) => { terminalPresentationActive = false; terminalRefresh.Stop(); Interlocked.Exchange(ref terminalChangeQueued, 0); };
        Closed += (_, _) => { terminals.Changed -= TerminalChanged; terminalRefresh.Stop(); };
        return terminalTabs;
    }
    void RefreshTerminals()
    {
        if (!TerminalVisualsAvailable) { Interlocked.Exchange(ref terminalDirty, 1); terminalRefresh.Stop(); return; }
        terminalPresentationActive = true; terminalPresentationChat = chat?.Id ?? 0;
        terminalRefresh.Stop(); Interlocked.Exchange(ref terminalChangeQueued, 0);
        Interlocked.Exchange(ref terminalDirty, 0); RecordUiWork("terminal");
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
                var output = OutputBox(); output.CornerRadius = new(6); output.Padding = new(12);
                output.TextWrapping = TextWrapping.NoWrap;
                ScrollViewer.SetHorizontalScrollBarVisibility(output, ScrollBarVisibility.Auto);
                var command = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 140, PlaceholderText = row.Shell + "…" };
                var start = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0) };
                var stop = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0) };
                StyleChromeAction(start); StyleChromeAction(stop);
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
            var header = row.Name + (row.Status == "running" ? " ●" : "") + (row.Sandbox ? " · Sandbox" : "");
            if (!Equals(ui.Tab.Header, header)) ui.Tab.Header = header;
            SetTextIfChanged(ui.Info, row.Shell + " · " + row.Status + " · " + row.Directory);
            SetTooltipIfChanged(ui.Info, ui.Info.Text);
            var text = (row.Command.Length == 0 ? "" : "> " + row.Command + "\n") + row.Output;
            if (ui.Output.Text != text)
            {
                if (ui.Follower == null && FindTerminalScroll(ui.Output) is { } scroller) ui.Follower = new ContentTailFollower(scroller);
                ui.Follower?.BeforeTextChange();
                var selection = ui.Output.SelectionStart; var length = ui.Output.SelectionLength;
                ui.Output.Text = text;
                if (length > 0) ui.Output.Select(Math.Min(selection, text.Length), Math.Min(length, Math.Max(0, text.Length - selection)));
                ui.Follower?.AfterTextChange();
                if (ui.Follower == null) ui.Output.DispatcherQueue.TryEnqueue(() => {
                    if (FindTerminalScroll(ui.Output) is { } view) { ui.Follower ??= new ContentTailFollower(view); ui.Follower.Queue(); }
                });
            }
            SetVisualValue(ui.Run, Control.IsEnabledProperty, row.Status != "running" && !row.Sandbox);
            SetVisualValue(ui.Stop, Control.IsEnabledProperty, row.Status == "running");
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
            !ReadOnlyFeatures.AutoFocusTool) return;
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
