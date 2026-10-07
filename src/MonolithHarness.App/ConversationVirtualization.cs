using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using Windows.Foundation;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly ConditionalWeakTable<StackPanel, VirtualConversation> virtualConversations = new();
    readonly DispatcherTimer messageVirtualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    bool messageVirtualizationQueued, messageVirtualizationInitialized, selectingChatText;

    void QueueMessageVirtualization()
    {
        if (!messageVirtualizationInitialized)
        {
            messageVirtualizationInitialized = true;
            messageVirtualizationTimer.Tick += async (_, _) =>
            {
                messageVirtualizationTimer.Stop(); messageVirtualizationQueued = false;
                if (conversationLoading || scroll.Content is not StackPanel host || !virtualConversations.TryGetValue(host, out var view)) return;
                try { await view.UpdateAsync(); }
                catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "chat.virtualization", ex); }
            };
            Closed += (_, _) => messageVirtualizationTimer.Stop();
        }
        if (messageVirtualizationQueued) return;
        messageVirtualizationQueued = true; messageVirtualizationTimer.Start();
    }
    VirtualConversation Virtualize(StackPanel host) => virtualConversations.GetValue(host, panel => new(this, panel));
    StackPanel MessageHost(StackPanel host) => virtualConversations.TryGetValue(host, out var view) && view.Live?.Content is { } live ? live : host;
    void BeginVirtualTurn(StackPanel host)
    {
        if (ReferenceEquals(host, messages) || conversationRuns.Values.Any(x => ReferenceEquals(x.Messages, host))) Virtualize(host).BeginLive();
    }
    void ClearMessagePanel(StackPanel host)
    {
        if (virtualConversations.TryGetValue(host, out var view)) view.Reset();
        host.Children.Clear();
    }
    void SealVirtualTurn(ConversationRun run)
    {
        if (virtualConversations.TryGetValue(run.Messages, out var view)) view.SealLive(run.Db.Messages.Local.OrderBy(x => x.Id).ToList(), run.Project);
        QueueMessageVirtualization();
    }
    static bool ContainsVisual(DependencyObject parent, DependencyObject? child)
    {
        if (child == null) return false;
        for (var node = child; node != null; node = node is FrameworkElement element && element.Parent != null ? element.Parent : VisualTreeHelper.GetParent(node)) if (ReferenceEquals(parent, node)) return true;
        return false;
    }
    static bool HasSelectedText(DependencyObject element)
    {
        if (element is TextBlock text && !string.IsNullOrEmpty(text.SelectedText) || element is AdvancedMarkdownView { HasSelection: true }) return true;
#if WINDOWS
        if (element is RichTextBlock rich && !string.IsNullOrEmpty(rich.SelectedText)) return true;
#endif
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) if (HasSelectedText(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }
    static IEnumerable<Expander> Expanders(DependencyObject element)
    {
        if (element is Expander expander) yield return expander;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in Expanders(VisualTreeHelper.GetChild(element, i))) yield return child;
    }

    // Lightweight slots preserve the scroll extent. Only nearby turns own rich controls,
    // decoded images and WebViews; live turns and selections remain attached.
    sealed class VirtualConversation
    {
        readonly MainWindow owner;
        readonly StackPanel host;
        readonly List<VirtualTurn> turns = [];
        readonly Dictionary<int, VirtualTurn> index = [];
        bool updating, correctionQueued;
        double pendingCorrection;
        public VirtualTurn? Live { get; private set; }
        public VirtualConversation(MainWindow owner, StackPanel host)
        {
            this.owner = owner; this.host = host;
            host.Loaded += (_, _) => owner.QueueMessageVirtualization();
            host.Unloaded += (_, _) => { foreach (var turn in turns) if (!ReferenceEquals(turn, Live)) turn.Release(force: true); };
            host.SizeChanged += (_, _) => owner.QueueMessageVirtualization();
        }
        public void Reset()
        {
            foreach (var old in turns) { old.IsLive = false; old.Release(force: true); host.Children.Remove(old.Frame); }
            turns.Clear(); index.Clear(); Live = null;
        }
        public async Task InitializeAsync(List<Message> history, Project? project, CancellationToken ct)
        {
            Reset();
            var items = history.Where(x => x.Role != "tasks").ToList();
            var start = 0;
            for (var i = 1; i <= items.Count; i++)
                if (i == items.Count || items[i].Role == "user")
                {
                    ct.ThrowIfCancellationRequested();
                    Add(items.GetRange(start, i - start), project); start = i;
                    if (turns.Count % 64 == 0) await Task.Delay(1, ct);
                }
            // Measure the recent edge before jumping to the bottom. Older slots load on demand.
            var initial = FeatureSettings.Read(owner.state.FeaturesJson).VirtualizeChat ? turns.TakeLast(2) : turns;
            var mounted = 0;
            foreach (var turn in initial) { turn.Wanted = true; await turn.MountAsync(ct); if (++mounted % 4 == 0) await Task.Delay(1, ct); }
        }
        VirtualTurn Add(List<Message> records, Project? project, bool live = false)
        {
            var turn = new VirtualTurn(this, records, project, live);
            turns.Add(turn); host.Children.Add(turn.Frame);
            foreach (var message in records) index[message.Id] = turn;
            return turn;
        }
        public void BeginLive()
        {
            // A new send only starts after its predecessor has been sealed.
            if (Live != null) return;
            Live = Add([], null, true);
        }
        public void TrackUser(Message message, Border card)
        {
            if (Live?.Content is { } body && ContainsVisual(body, card)) { Live.FirstMessageId = message.Id; index[message.Id] = Live; }
        }
        public void SealLive(List<Message> history, Project project)
        {
            if (Live is not { } live) return;
            var first = history.FindIndex(x => x.Id == live.FirstMessageId && x.Role == "user");
            if (first < 0) return; // Keep an unsaved/failed draft's visual content intact.
            live.Records = history.Skip(first).Where(x => x.Role != "tasks").ToList(); live.Project = project;
            foreach (var record in live.Records) index[record.Id] = live;
            live.IsLive = false; Live = null;
        }
        void CorrectReadingOffset(double delta)
        {
            pendingCorrection += delta;
            if (correctionQueued) return;
            correctionQueued = true;
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                correctionQueued = false; var correction = pendingCorrection; pendingCorrection = 0;
                if (ReferenceEquals(owner.scroll.Content, host) && !owner.followChatTail)
                    owner.scroll.ChangeView(null, Math.Max(0, owner.scroll.VerticalOffset + correction), null, true);
            });
        }
        public async Task RevealAsync(int messageId)
        {
            if (!index.TryGetValue(messageId, out var turn)) return;
            turn.KeepUntil = Environment.TickCount64 + 5000; turn.Wanted = true;
            turn.Frame.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = .4, AnimationDesired = false });
            await turn.MountAsync(CancellationToken.None);
            if (turn.Content == null && ReferenceEquals(owner.scroll.Content, host)) await turn.MountAsync(CancellationToken.None);
            if (!ReferenceEquals(owner.scroll.Content, host)) return;
            if (turn.Content is { } body && owner.activityGroups.TryGetValue(body, out var activity)) activity.SetExpanded(true);
        }
        public async Task UpdateAsync()
        {
            if (updating) { owner.QueueMessageVirtualization(); return; }
            updating = true;
            try
            {
                var viewport = Math.Max(240, owner.scroll.ViewportHeight);
                var margin = Math.Max(800, viewport * 2);
                var enabled = FeatureSettings.Read(owner.state.FeaturesJson).VirtualizeChat;
                var wanted = new List<VirtualTurn>();
                foreach (var turn in turns)
                {
                    if (!ReferenceEquals(turn.Frame.Parent, host)) continue;
                    var y = turn.Frame.TransformToVisual(owner.scroll).TransformPoint(new Point()).Y;
                    turn.Wanted = !enabled || turn.IsLive || turn.KeepUntil > Environment.TickCount64 || y <= viewport + margin && y + turn.Frame.ActualHeight >= -margin;
                    if (turn.Wanted) { if (turn.Content == null) wanted.Add(turn); }
                    else turn.Release();
                }
                // Realize a small batch per dispatcher turn to keep wheel/keyboard input responsive.
                foreach (var turn in wanted.Take(2)) await turn.MountAsync(CancellationToken.None);
                if (wanted.Count > 2) owner.QueueMessageVirtualization();
            }
            finally { updating = false; }
        }
        public sealed class VirtualTurn
        {
            readonly VirtualConversation view;
            public readonly Border Frame;
            public StackPanel? Content => Frame.Child as StackPanel;
            public List<Message> Records;
            public Project? Project;
            public int FirstMessageId;
            public bool IsLive, Wanted;
            public long KeepUntil;
            Task? loading;
            CancellationTokenSource? cancellation;
            double height;
            bool? activityExpanded, reasoningPreference;
            bool[] expanderState = [];
            public VirtualTurn(VirtualConversation view, List<Message> records, Project? project, bool live)
            {
                this.view = view; Records = records; Project = project; IsLive = live;
                var width = Math.Max(320, view.host.ActualWidth > 0 ? view.host.ActualWidth : 1050);
                var columns = Math.Max(20, width / (ChatDensity.FontSize * .53));
                height = Math.Max(80, records.Sum(x => x.Role == "tool" ? 96 : 76 + ChatDensity.LineHeight *
                    Math.Min(2000, Math.Max(x.Content.Take(16384).Count(c => c == '\n') + 1, Math.Ceiling(x.Content.Length / columns))) + 220 * x.Attachments.Count));
                Frame = new Border { Height = live ? double.NaN : height, HorizontalAlignment = HorizontalAlignment.Stretch };
                if (live) Frame.Child = NewContent();
                Frame.SizeChanged += (_, e) =>
                {
                    if (e.NewSize.Height <= 0) return;
                    var delta = e.NewSize.Height - height; var previous = height; height = e.NewSize.Height;
                    if (Math.Abs(delta) > 1 && ReferenceEquals(view.owner.scroll.Content, view.host) && !view.owner.followChatTail)
                    {
                        var top = Frame.TransformToVisual(view.owner.scroll).TransformPoint(new Point()).Y - view.pendingCorrection;
                        if (top + previous <= 0) view.CorrectReadingOffset(delta);
                    }
                    view.owner.QueueMessageVirtualization();
                };
            }
            StackPanel NewContent() => ChatDensity.Track(new StackPanel(), "virtual-turn");
            public Task MountAsync(CancellationToken ct)
            {
                if (Content != null || IsLive) return Task.CompletedTask;
                return loading is { IsCompleted: false } ? loading : loading = LoadAsync(ct);
            }
            async Task LoadAsync(CancellationToken ct)
            {
                using var source = CancellationTokenSource.CreateLinkedTokenSource(ct); cancellation = source;
                try
                {
                    await LoadHistoryImagesAsync(Records, 0, Records.Count, source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    if (!Wanted || !ReferenceEquals(view.owner.scroll.Content, view.host)) return;
                    var body = NewContent(); view.owner.RenderHistory(Records, body, Project);
                    if (activityExpanded.HasValue && reasoningPreference == view.owner.state.ShowReasoningDetails && view.owner.activityGroups.TryGetValue(body, out var activity))
                        activity.SetExpanded(activityExpanded.Value);
                    var i = 0; foreach (var expander in Expanders(body)) { if (i < expanderState.Length) expander.IsExpanded = expanderState[i]; i++; }
                    Frame.Child = body; Frame.Height = double.NaN;
                    view.owner.ApplyChatTextHighlights(body);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Write(AppLogLevel.Warning, "chat.virtualization.load", ex);
                    if (Wanted && ReferenceEquals(view.owner.scroll.Content, view.host))
                    {
                        var body = NewContent();
                        body.Children.Add(new TextBlock { Text = UiText.Resolve("Affichage simplifié · ", "Simplified display · ") + ex.Message, TextWrapping = TextWrapping.Wrap });
                        foreach (var record in Records)
                        {
                            var text = new TextBlock { Text = record.Content, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                            var card = new Border { Child = text }; body.Children.Add(card); view.owner.chatSearchAnchors[record.Id] = new(card);
                        }
                        Frame.Child = body; Frame.Height = double.NaN;
                        view.owner.ApplyChatTextHighlights(body);
                    }
                }
                finally { cancellation = null; if (Content == null) { ReleaseImages(); if (Wanted) view.owner.QueueMessageVirtualization(); } }
            }
            void ReleaseImages()
            {
                foreach (var message in Records) foreach (var image in message.Attachments) if (image.Id > 0) image.Data = [];
            }
            public void Release(bool force = false)
            {
                if (IsLive) return;
                if (!force && (view.owner.selectingChatText || KeepUntil > Environment.TickCount64)) return;
                if (Content is not { } body) { cancellation?.Cancel(); return; }
                if (!force)
                {
                    if (HasSelectedText(body) || ContainsVisual(body, view.owner.highlightedSearchCard)) return;
                    var focus = view.owner.root.XamlRoot == null ? null : FocusManager.GetFocusedElement(view.owner.root.XamlRoot) as DependencyObject;
                    if (ContainsVisual(body, focus)) return;
                }
                if (view.owner.activityGroups.TryGetValue(body, out var activity)) { activityExpanded = activity.Expanded; reasoningPreference = view.owner.state.ShowReasoningDetails; }
                expanderState = Expanders(body).Select(x => x.IsExpanded).ToArray();
                if (Frame.ActualHeight > 0) height = Frame.ActualHeight; Frame.Height = height; Frame.Child = null;
                ReleaseImages();
            }
        }
    }
}
