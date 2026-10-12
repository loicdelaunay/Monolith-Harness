using System.Text.Json;

namespace MonolithHarness.Core.Remote;

public enum SyncLevel { Info, Warning, Error }

/// <summary>The state of the link as the user sees it.</summary>
public enum LinkState { NotConfigured, SignedOut, Connecting, Online, Offline, SessionLost }

/// <summary>A problem that is still there: it disappears by itself when the next synchronization succeeds.</summary>
public sealed record SyncIssue(string Key, SyncLevel Level, string Area, string Message, DateTime SinceUtc, DateTime LastUtc, int Count);

public sealed record SyncLogEntry(DateTime TimeUtc, SyncLevel Level, string Area, string Message);

public sealed record SyncSnapshot(LinkState State, bool Syncing, bool Realtime, DateTime? LastSyncUtc, DateTime? NextSyncUtc, int SyncMinutes, string ServerName, string ServerVersion,
    string Url, string Email, int Providers, int Projects, int SharedWithMe, int PublicShares, IReadOnlyList<SyncIssue> Issues, IReadOnlyList<SyncLogEntry> Log)
{
    /// <summary>The most serious problem, or null when everything is fine (drives the badge next to the connection chip).</summary>
    public SyncLevel? Worst => Issues.Count == 0 ? null : Issues.Max(x => x.Level);
}

/// <summary>
/// What a request sent from elsewhere (another member, another device) is doing in a conversation this device mirrors: its state, and the
/// message being written with its reasoning, refreshed about twice a second. <see cref="Finished"/> ends the follow.
/// </summary>
public sealed record RemoteProgress(int LocalChatId, Guid RunId, string Requester, string State, int? MessageId, string Text, string Reasoning, bool Finished);

[Flags]
public enum SyncScope { None = 0, Providers = 1, Projects = 2, Shares = 4, Chats = 8, Full = 15 }

/// <summary>What a synchronization changed in the local database, so that a host refreshes only what it shows.</summary>
public sealed record SyncChanges(bool Providers, IReadOnlyList<int> Projects, IReadOnlyList<int> RemovedProjects, IReadOnlyList<int> Chats, IReadOnlyList<int> RemovedChats,
    IReadOnlyList<int> Messages, bool Shares)
{
    public static readonly SyncChanges None = new(false, [], [], [], [], [], false);
    public bool Any => Providers || Projects.Count > 0 || RemovedProjects.Count > 0 || Chats.Count > 0 || RemovedChats.Count > 0 || Messages.Count > 0 || Shares;
}

/// <summary>
/// Keeps the client's mirror of the server up to date by itself: shared providers, cloud projects and their conversations, and the
/// conversations shared with the account. Changes arrive live (the server sends a small signal, the client re-reads what changed) and a
/// complete resynchronization runs on a schedule the user chooses (never, every minute, 5 minutes, hour or day). It also tells the host
/// how the link is doing and what went wrong, in plain terms.
/// </summary>
public sealed class ServerSyncService : IAsyncDisposable
{
    public static readonly int[] IntervalChoices = [0, 1, 5, 60, 1440];
    public const int DefaultMinutes = 5;
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan OfflineRetry = TimeSpan.FromSeconds(30);
    const int LogSize = 60;

    readonly ServerConnection connection;
    readonly Func<HarnessDb> openDb;
    readonly object gate = new();
    readonly SemaphoreSlim running = new(1, 1);
    readonly Dictionary<string, SyncIssue> issues = [];
    readonly List<SyncLogEntry> log = [];
    readonly HashSet<Guid> pendingProjects = [];
    readonly HashSet<int> pendingChats = [];
    readonly Dictionary<Guid, IDisposable> projectWatches = [];
    readonly Dictionary<Guid, Follow> follows = [];
    const int MaxFollows = 6;
    static readonly TimeSpan StreamEvery = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan PullEvery = TimeSpan.FromMilliseconds(800);

    /// <summary>A request being followed: its stream is relayed to the host and its messages are pulled as they appear.</summary>
    sealed class Follow(Guid runId, int localChat, string requester)
    {
        public Guid RunId { get; } = runId;
        public int LocalChat { get; } = localChat;
        public string Requester { get; } = requester;
        public IDisposable? Watch { get; set; }
        public DateTime LastEvent { get; set; } = DateTime.UtcNow;
        public string State { get; set; } = "running";
        public (int? Id, string Text, string Reasoning)? Pending { get; set; }
        public int StreamScheduled, PullScheduled;
    }
    CancellationTokenSource? lifetime;
    Task? loop;
    bool subscribed, forceFull = true, syncing;
    SyncScope pendingScope;
    DateTime pendingDue;
    DateTime? lastSync, nextFull, nextRetry;
    LinkState state = LinkState.Connecting;
    ServerStatus? status;
    List<ServerShare> sharedWithMe = [], publicShares = [];
    string sharesSignature = "";
    readonly HashSet<string> skipNotes = [];

    public ServerSyncService(ServerConnection connection, Func<HarnessDb> openDb)
    {
        this.connection = connection;
        this.openDb = openDb;
        connection.RealtimeConnected += () => { lock (gate) forceFull = true; return Task.CompletedTask; };
        connection.SessionLost += () => { lock (gate) state = LinkState.SessionLost; };
    }

    /// <summary>Conversations the host is working on (a run in progress): they are not rewritten under its feet.</summary>
    public Func<int, bool>? IsChatBusy { get; set; }

    public event Action<SyncSnapshot>? StateChanged;

    /// <summary>
    /// A request of a cloud project changed state (queued, running, finished), whoever sent it: the host follows it live if it shows that
    /// conversation. Raised off the UI thread, for every project the account is a member of.
    /// </summary>
    public event Func<ServerRun, Task>? RemoteRun;

    /// <summary>Live progress of requests started elsewhere in the conversations this device mirrors (see <see cref="RemoteProgress"/>).</summary>
    public event Func<RemoteProgress, Task>? RemoteRunProgress;

    /// <summary>Raised, off the UI thread, after a synchronization changed local data.</summary>
    public event Func<SyncChanges, Task>? Changed;

    public IReadOnlyList<ServerShare> SharedWithMe { get { lock (gate) return [.. sharedWithMe]; } }
    public IReadOnlyList<ServerShare> PublicShares { get { lock (gate) return [.. publicShares]; } }

    // ---- Lifecycle ----

    public void Start()
    {
        lock (gate)
        {
            if (loop != null) return;
            lifetime = new CancellationTokenSource();
            loop = Task.Run(() => LoopAsync(lifetime.Token));
        }
    }

    /// <summary>To call after the account, the address or the settings of the link changed (sign-in, sign-out, forget): starts over.</summary>
    public void LinkChanged()
    {
        lock (gate)
        {
            forceFull = true; state = LinkState.Connecting; nextRetry = null; subscribed = false;
            issues.Clear(); pendingScope = SyncScope.None; pendingProjects.Clear(); pendingChats.Clear();
            foreach (var watch in projectWatches.Values) watch.Dispose();
            projectWatches.Clear();
            foreach (var follow in follows.Values) follow.Watch?.Dispose();
            follows.Clear();
            sharedWithMe = []; publicShares = []; sharesSignature = ""; lastSync = null; nextFull = null;
        }
        Publish();
    }

    /// <summary>A conversation was renamed (or otherwise changed) on this device: send it to the server soon.</summary>
    public void RequestPush(int localChatId)
    {
        var link = connection.Link;
        lock (gate)
        {
            pendingScope |= SyncScope.Projects;
            if (link.Chats.TryGetValue(localChatId, out var entry) && Guid.TryParse(entry.ServerProjectId, out var project)) pendingProjects.Add(project);
            pendingDue = DateTime.UtcNow;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? source;
        Task? current;
        lock (gate) { source = lifetime; current = loop; lifetime = null; loop = null; }
        if (source == null) return;
        source.Cancel();
        try { if (current != null) await current.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        source.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ---- Settings ----

    public int Minutes => Math.Clamp(connection.Link.SyncMinutes, 0, 10080);

    public void SetMinutes(int minutes)
    {
        minutes = IntervalChoices.Contains(minutes) ? minutes : DefaultMinutes;
        connection.Store.Update(l => l.SyncMinutes = minutes);
        lock (gate) nextFull = minutes == 0 ? null : (lastSync ?? DateTime.UtcNow).AddMinutes(minutes);
        Publish();
    }

    // ---- State ----

    public SyncSnapshot Snapshot
    {
        get
        {
            var link = connection.Link;
            lock (gate)
            {
                var current = state;
                if (!link.Configured) current = LinkState.NotConfigured;
                else if (!link.SignedIn && current != LinkState.SessionLost) current = LinkState.SignedOut;
                return new SyncSnapshot(current, syncing, connection.IsRealtimeConnected, lastSync, link.SyncMinutes == 0 ? null : nextFull, link.SyncMinutes,
                    status?.ServerName is { Length: > 0 } name ? name : link.ServerName, status?.Version is { Length: > 0 } version ? version : link.ServerVersion, link.Url, link.Email,
                    link.Providers.Count, link.Projects.Count, sharedWithMe.Count, publicShares.Count,
                    [.. issues.Values.OrderByDescending(x => x.Level).ThenByDescending(x => x.LastUtc)], [.. log.AsEnumerable().Reverse()]);
            }
        }
    }

    void Publish() => StateChanged?.Invoke(Snapshot);

    // ---- Problems reported by the hosts ----

    public void ReportIssue(string key, SyncLevel level, string area, string message)
    {
        AddIssue(key, level, area, message);
        Publish();
    }

    public void ClearIssue(string key)
    {
        bool removed;
        lock (gate) removed = issues.Remove(key);
        if (removed) Publish();
    }

    /// <summary>The user dismisses what is listed; whatever is still wrong comes back at the next synchronization.</summary>
    public void ClearIssues()
    {
        lock (gate) issues.Clear();
        Publish();
    }

    void AddIssue(string key, SyncLevel level, string area, string message)
    {
        var now = DateTime.UtcNow;
        lock (gate)
        {
            issues[key] = issues.TryGetValue(key, out var previous) && previous.Message == message
                ? previous with { LastUtc = now, Count = previous.Count + 1 }
                : new SyncIssue(key, level, area, message, previous?.SinceUtc ?? now, now, 1);
            Log(level, area, message, previous?.Message == message);
        }
    }

    void Log(SyncLevel level, string area, string message, bool repeated = false)
    {
        if (repeated) return;
        log.Add(new SyncLogEntry(DateTime.UtcNow, level, area, message));
        if (log.Count > LogSize) log.RemoveRange(0, log.Count - LogSize);
    }

    // ---- Loop ----

    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AddIssue("internal", SyncLevel.Error, "Synchronisation", "Erreur inattendue : " + ex.Message);
                AppLog.Write(AppLogLevel.Warning, "server.sync_failed", ex);
                Publish();
            }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    async Task TickAsync(CancellationToken ct)
    {
        var link = connection.Link;
        if (!link.Configured || !link.SignedIn)
        {
            bool changed;
            lock (gate)
            {
                var next = link.Configured ? LinkState.SignedOut : LinkState.NotConfigured;
                changed = state != next || issues.Count > 0;
                state = next; subscribed = false; issues.Clear(); forceFull = true; sharedWithMe = []; publicShares = [];
            }
            if (changed) Publish();
            return;
        }
        await EnsureSubscribedAsync();
        var now = DateTime.UtcNow;
        // A run whose end was never heard of (connection lost) stops being followed after a while; the next full sync catches up.
        List<Follow> stale;
        lock (gate) stale = follows.Values.Where(x => now - x.LastEvent > TimeSpan.FromMinutes(15)).ToList();
        foreach (var follow in stale) await FinishFollowAsync(follow, "cancelled");
        SyncScope scope = SyncScope.None;
        HashSet<Guid>? projects = null;
        HashSet<int>? chats = null;
        lock (gate)
        {
            if (state == LinkState.SessionLost) return;
            if (forceFull) { scope = SyncScope.Full; forceFull = false; }
            else if (state == LinkState.Offline && nextRetry is { } retry && now >= retry) scope = SyncScope.Full;
            else if (state == LinkState.Offline) return;
            else if (pendingScope != SyncScope.None && now >= pendingDue)
            {
                scope = pendingScope; projects = pendingProjects.Count > 0 ? [.. pendingProjects] : null; chats = pendingChats.Count > 0 ? [.. pendingChats] : null;
                pendingScope = SyncScope.None; pendingProjects.Clear(); pendingChats.Clear();
            }
            else if (link.SyncMinutes > 0 && nextFull is { } due && now >= due) scope = SyncScope.Full;
        }
        if (scope != SyncScope.None) await SyncAsync(scope, projects, chats, ct);
    }

    async Task EnsureSubscribedAsync()
    {
        lock (gate) { if (subscribed) return; subscribed = true; }
        try
        {
            // The account topic and the server topic are implicit on the server: nothing to subscribe, only handlers to attach.
            await connection.WatchAsync("user", OnEventAsync, subscribe: false);
            await connection.WatchAsync("server", OnEventAsync, subscribe: false);
        }
        catch (ServerException) { lock (gate) subscribed = false; }
    }

    Task OnEventAsync(ServerEvent evt)
    {
        if (evt.Name == "feature.changed") { lock (gate) forceFull = true; return Task.CompletedTask; }
        if (evt.Name != "sync") return Task.CompletedTask;
        var area = evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("area", out var a) ? a.GetString() : null;
        Guid? project = evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("projectId", out var p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out var id) ? id : null;
        int? serverChat = evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("chatId", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var chatId) ? chatId : null;
        lock (gate)
        {
            switch (area)
            {
                case "providers": pendingScope |= SyncScope.Providers; break;
                case "shares": pendingScope |= SyncScope.Shares; break;
                case "projects":
                    // A project was added, renamed, removed or its members changed: re-list them all (cheap) and look inside the one concerned.
                    pendingScope |= SyncScope.Projects | SyncScope.Chats;
                    if (project is { } changedProject) pendingProjects.Add(changedProject);
                    break;
                case "chats":
                    pendingScope |= SyncScope.Projects | SyncScope.Chats;
                    if (project is { } chatProject) pendingProjects.Add(chatProject);
                    // The conversation that changed is pulled even if its update stamp did not move.
                    if (serverChat is { } changedChat) pendingChats.Add(changedChat);
                    break;
                default: forceFull = true; return Task.CompletedTask;
            }
            pendingDue = DateTime.UtcNow + Debounce;
        }
        return Task.CompletedTask;
    }

    // ---- Synchronization ----

    /// <summary>Resynchronizes right now (the "Synchroniser" button); returns what changed locally.</summary>
    public Task<SyncChanges> SyncNowAsync(CancellationToken ct = default) => SyncAsync(SyncScope.Full, null, null, ct);

    async Task<SyncChanges> SyncAsync(SyncScope scope, HashSet<Guid>? onlyProjects, HashSet<int>? forceChats, CancellationToken ct)
    {
        await running.WaitAsync(ct);
        SyncChanges changes = SyncChanges.None;
        try
        {
            lock (gate) syncing = true;
            Publish();
            changes = await RunAsync(scope, onlyProjects, forceChats, ct);
        }
        finally
        {
            lock (gate) syncing = false;
            running.Release();
            Publish();
        }
        if (changes.Any && Changed is { } handler)
            try { await handler(changes); }
            catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write(AppLogLevel.Warning, "server.sync_refresh_failed", ex); }
        return changes;
    }

    async Task<SyncChanges> RunAsync(SyncScope scope, HashSet<Guid>? onlyProjects, HashSet<int>? forceChats, CancellationToken ct)
    {
        var link = connection.Link;
        if (!link.Configured || !link.SignedIn) return SyncChanges.None;

        // 1. Is the server there, and is the account still accepted?
        var now = DateTime.UtcNow;
        var check = await connection.StatusAsync(ct);
        status = check;
        if (!check.Reachable)
        {
            lock (gate) { state = LinkState.Offline; nextRetry = now + OfflineRetry; }
            AddIssue("link", SyncLevel.Warning, "Connexion", "Serveur injoignable : " + Clean(check.Error) + " Nouvel essai automatique toutes les 30 secondes.");
            return SyncChanges.None;
        }
        if (check.ErrorCode == "session_lost" || !check.SignedIn)
        {
            lock (gate) state = LinkState.SessionLost;
            AddIssue("link", SyncLevel.Error, "Connexion", "Votre session n'est plus valide : reconnectez-vous au serveur (Réglages › Serveur).");
            return SyncChanges.None;
        }
        lock (gate)
        {
            if (state != LinkState.Online) Log(SyncLevel.Info, "Connexion", "Connecté à " + check.ServerName + ".");
            state = LinkState.Online; nextRetry = null;
            issues.Remove("link");
        }
        Conditions(check, link);

        bool Feature(string name) => !check.Features.TryGetValue(name, out var on) || on;
        bool Permission(string name) => check.User == null || check.User.Permissions.Contains(name);
        bool providersChanged = false, sharesChanged = false;
        List<int> projectsChanged = [], projectsRemoved = [], chatsChanged = [], chatsRemoved = [], messagesChanged = [];

        // 2. Providers
        if (scope.HasFlag(SyncScope.Providers))
            await Step("providers", "Providers", async () =>
            {
                if (!Feature("providers") || !Permission("providers.use")) { Skipped("providers", Feature("providers") ? "Votre compte n'a pas accès aux providers partagés." : "Les providers partagés sont désactivés sur le serveur."); return; }
                await using var db = openDb();
                providersChanged = (await ServerSync.SyncProvidersDetailedAsync(db, connection, ct)).Changed;
            });

        // 3. Cloud projects, their conversations and messages
        if (scope.HasFlag(SyncScope.Projects))
            await Step("projects", "Projets cloud", async () =>
            {
                if (!Feature("cloud") || !Permission("cloud.use")) { Skipped("projects", Feature("cloud") ? "Votre compte n'a pas accès aux projets cloud." : "L'exécution cloud est désactivée sur le serveur."); return; }
                var remote = await connection.Api.ProjectsAsync(ct);
                await using var db = openDb();
                // Renames made on this device go first, so that reconciling does not bring the old title back.
                await ServerSync.PushRenamesAsync(db, connection, ct);
                foreach (var project in remote.Where(x => onlyProjects == null || onlyProjects.Contains(x.Id)))
                {
                    var result = await ServerSync.ReconcileProjectAsync(db, connection, project, IsChatBusy, ct);
                    if (result.Created || result.Renamed) projectsChanged.Add(result.Project.Id);
                    chatsChanged.AddRange(result.NewChats); chatsChanged.AddRange(result.RenamedChats); chatsRemoved.AddRange(result.RemovedChats);
                    if (result.Created || result.NewChats.Count > 0 || result.RemovedChats.Count > 0 || result.RenamedChats.Count > 0) projectsChanged.Add(result.Project.Id);
                    if (scope.HasFlag(SyncScope.Chats)) messagesChanged.AddRange(await PullChangedChatsAsync(db, project, result, forceChats, ct));
                }
                // The listing is always complete (only the work above is limited to the projects a signal named): what it lacks has disappeared.
                projectsRemoved.AddRange(await ServerSync.RemoveMissingProjectsAsync(db, connection, remote, IsChatBusy, ct));
                foreach (var removed in projectsRemoved) Log(SyncLevel.Info, "Projets cloud", "Un projet cloud a disparu du serveur : sa copie locale a été retirée.");
                await EnsureProjectWatchesAsync(remote.Select(x => x.Id));
            });

        // 4. Conversations shared with the account
        if (scope.HasFlag(SyncScope.Shares))
            await Step("shares", "Conversations partagées", async () =>
            {
                if (!Feature("sharing") || !Permission("sharing.view")) { lock (gate) { sharedWithMe = []; publicShares = []; } Skipped("shares", "Le partage de conversations n'est pas disponible pour ce compte."); return; }
                var mine = (await connection.Api.SharesAsync(ct)).Where(x => !x.IsHost && x.RevokedUtc == null).ToList();
                var open = (await connection.Api.PublicSharesAsync(ct)).Where(x => x.RevokedUtc == null && mine.All(m => m.Id != x.Id)).ToList();
                var signature = string.Join('|', mine.Concat(open).Select(x => $"{x.Id}:{x.Title}:{x.Live}:{x.IsPublic}"));
                lock (gate) { sharedWithMe = mine; publicShares = open; sharesChanged = signature != sharesSignature; sharesSignature = signature; }
            });

        lock (gate)
        {
            if (scope == SyncScope.Full)
            {
                lastSync = DateTime.UtcNow;
                nextFull = link.SyncMinutes == 0 ? null : lastSync.Value.AddMinutes(link.SyncMinutes);
            }
        }
        return new SyncChanges(providersChanged, [.. projectsChanged.Distinct()], [.. projectsRemoved], [.. chatsChanged.Distinct()], [.. chatsRemoved], [.. messagesChanged.Distinct()], sharesChanged);

        async Task Step(string key, string area, Func<Task> work)
        {
            try
            {
                await work();
                lock (gate) issues.Remove(key);
            }
            catch (ServerException ex) { Failed(key, area, ex); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            { AddIssue(key, SyncLevel.Warning, area, "Le serveur ne répond pas."); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AddIssue(key, SyncLevel.Error, area, "Erreur locale pendant la synchronisation : " + Clean(ex.Message));
                AppLog.Write(AppLogLevel.Warning, "server.sync_step_failed", ex);
            }
        }
    }

    /// <summary>Listens to the requests of every project of the account, to follow those sent from elsewhere.</summary>
    async Task EnsureProjectWatchesAsync(IEnumerable<Guid> ids)
    {
        var wanted = ids.ToHashSet();
        List<Guid> missing;
        lock (gate) missing = wanted.Where(x => !projectWatches.ContainsKey(x)).ToList();
        foreach (var id in missing)
        {
            var watch = await connection.WatchAsync("project:" + id, OnProjectEventAsync);
            lock (gate) projectWatches[id] = watch;
        }
        lock (gate)
            foreach (var gone in projectWatches.Keys.Where(x => !wanted.Contains(x)).ToList()) { projectWatches[gone].Dispose(); projectWatches.Remove(gone); }
    }

    Task OnProjectEventAsync(ServerEvent evt)
    {
        if (evt.Name != "run.state" || evt.Data.ValueKind != JsonValueKind.Object) return Task.CompletedTask;
        ServerRun? run;
        try { run = JsonSerializer.Deserialize<ServerRun>(evt.Data.GetRawText(), ServerApi.Json); }
        catch (JsonException) { return Task.CompletedTask; }
        // Off the socket's reading loop: the host may take a moment to say whether a conversation is busy.
        if (run != null) _ = Task.Run(() => HandleRemoteRunAsync(run));
        return Task.CompletedTask;
    }

    async Task HandleRemoteRunAsync(ServerRun run)
    {
        try
        {
            if (RemoteRun is { } handler) await handler(run);
            var local = ServerSync.LocalChatOf(connection.Link, run.ProjectId, run.ChatId);
            if (local is not { } chat) return;
            var final = run.State is "completed" or "failed" or "cancelled";
            Follow? existing;
            lock (gate) follows.TryGetValue(run.Id, out existing);
            if (final)
            {
                // A run this device sent itself finishes through its own session; one followed from elsewhere gets its last pull here.
                if (existing != null) await FinishFollowAsync(existing, run.State);
                return;
            }
            if (existing != null) { existing.State = run.State; await RaiseProgressAsync(new(chat, run.Id, existing.Requester, run.State, null, "", "", false)); return; }
            if (IsChatBusy?.Invoke(chat) == true) return;
            bool full;
            lock (gate) full = follows.Count >= MaxFollows;
            if (full) return;
            var follow = new Follow(run.Id, chat, run.Requester ?? "") { State = run.State };
            follow.Watch = await connection.WatchAsync("run:" + run.Id, evt => OnFollowEventAsync(follow, evt));
            lock (gate) follows[run.Id] = follow;
            await RaiseProgressAsync(new(chat, run.Id, follow.Requester, run.State, null, "", "", false));
        }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException or HttpRequestException)
        {
            AppLog.Write(AppLogLevel.Information, "server.follow_skipped", ex);
        }
    }

    Task OnFollowEventAsync(Follow follow, ServerEvent evt)
    {
        follow.LastEvent = DateTime.UtcNow;
        switch (evt.Name)
        {
            case "stream":
                if (evt.Data.ValueKind != JsonValueKind.Object) break;
                follow.Pending = (evt.Data.TryGetProperty("messageId", out var id) && id.TryGetInt32(out var number) ? number : null,
                    Text(evt.Data, "text"), Text(evt.Data, "reasoning"));
                if (Interlocked.Exchange(ref follow.StreamScheduled, 1) == 0) _ = Task.Run(() => FlushStreamAsync(follow));
                break;
            case "message":
                if (Interlocked.Exchange(ref follow.PullScheduled, 1) == 0) _ = Task.Run(() => PullFollowedAsync(follow));
                break;
            case "run.state":
                if (evt.Data.ValueKind == JsonValueKind.Object && Text(evt.Data, "state") is { } state && state is "completed" or "failed" or "cancelled")
                    _ = Task.Run(() => FinishFollowAsync(follow, state));
                break;
        }
        return Task.CompletedTask;
    }

    async Task FlushStreamAsync(Follow follow)
    {
        await Task.Delay(StreamEvery);
        Interlocked.Exchange(ref follow.StreamScheduled, 0);
        bool active;
        lock (gate) active = follows.ContainsKey(follow.RunId);
        if (!active || follow.Pending is not { } pending) return;
        follow.Pending = null;
        await RaiseProgressAsync(new(follow.LocalChat, follow.RunId, follow.Requester, follow.State, pending.Id, pending.Text, pending.Reasoning, false));
    }

    async Task PullFollowedAsync(Follow follow)
    {
        await Task.Delay(PullEvery);
        Interlocked.Exchange(ref follow.PullScheduled, 0);
        await PullChatNowAsync(follow.LocalChat);
    }

    /// <summary>Reads a conversation from the server now and tells the host which messages changed.</summary>
    async Task PullChatNowAsync(int localChat)
    {
        try
        {
            await using var db = openDb();
            if (await db.Chats.FindAsync(localChat) is not { } chat) return;
            if ((await ServerSync.PullChatAsync(db, connection, chat)).Count == 0) return;
            if (Changed is { } handler) await handler(new SyncChanges(false, [], [], [], [], [localChat], false));
        }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            AppLog.Write(AppLogLevel.Information, "server.follow_pull_skipped", ex);
        }
    }

    async Task FinishFollowAsync(Follow follow, string state)
    {
        lock (gate) { if (!follows.Remove(follow.RunId)) return; }
        follow.Watch?.Dispose();
        // What was being written when the request ended is shown before it is replaced by the final messages.
        if (follow.Pending is { } last) { follow.Pending = null; await RaiseProgressAsync(new(follow.LocalChat, follow.RunId, follow.Requester, follow.State, last.Id, last.Text, last.Reasoning, false)); }
        // The last messages are written just after the final state: give them a moment, then read the whole conversation.
        await Task.Delay(400);
        await PullChatNowAsync(follow.LocalChat);
        await RaiseProgressAsync(new(follow.LocalChat, follow.RunId, follow.Requester, state, null, "", "", true));
    }

    async Task RaiseProgressAsync(RemoteProgress progress)
    {
        if (RemoteRunProgress is not { } handler) return;
        try { await handler(progress); }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write(AppLogLevel.Warning, "server.follow_refresh_failed", ex); }
    }

    static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /// <summary>The conversations of a project whose content moved since the last pull (or was never pulled) get their messages.</summary>
    async Task<List<int>> PullChangedChatsAsync(HarnessDb db, ServerProject project, ServerSync.ProjectSync result, HashSet<int>? forceChats, CancellationToken ct)
    {
        var pulled = new List<int>();
        var key = project.Id.ToString();
        var links = connection.Link.Chats.Where(x => x.Value.ServerProjectId == key).ToList();
        foreach (var remote in result.RemoteChats)
        {
            if (links.FirstOrDefault(x => x.Value.ServerChatId == remote.Id) is not { Value: not null } entry) continue;
            var localId = entry.Key;
            if (IsChatBusy?.Invoke(localId) == true) continue;
            var forced = forceChats?.Contains(remote.Id) == true;
            if (!forced && entry.Value.PulledUtc == remote.UpdatedUtc && (remote.UpdatedUtc != null || entry.Value.PulledUtc != null)) continue;
            if (!forced && remote.UpdatedUtc == null && entry.Value.PulledUtc == null && !result.NewChats.Contains(localId)) continue;
            if (await db.Chats.FindAsync([localId], ct) is not { } chat) continue;
            var changed = await ServerSync.PullChatAsync(db, connection, chat, ct);
            connection.Store.Update(l => { if (l.Chats.TryGetValue(localId, out var stored)) stored.PulledUtc = remote.UpdatedUtc; });
            if (changed.Count > 0) pulled.Add(localId);
        }
        return pulled;
    }

    // ---- Conditions worth showing even when everything works ----

    void Conditions(ServerStatus check, ServerLinkData link)
    {
        var insecure = link.AllowHttp && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback;
        if (insecure) AddIssue("http", SyncLevel.Warning, "Sécurité", "Connexion non chiffrée (HTTP) : le jeton de session et vos données circulent en clair sur le réseau.");
        else ClearIssueQuietly("http");
        if (check.PasswordChangeSuggested) AddIssue("password", SyncLevel.Info, "Compte", "Votre mot de passe est temporaire ou fourni par un administrateur : changez-le (Réglages › Serveur).");
        else ClearIssueQuietly("password");
        // Only a running service keeps the channel open: a one-off synchronization (a command) has none, which is not a problem.
        bool started;
        lock (gate) started = loop != null;
        if (started && !connection.IsRealtimeConnected) AddIssue("realtime", SyncLevel.Info, "Temps réel", "Canal temps réel indisponible : les changements arrivent à la prochaine synchronisation périodique.");
        else ClearIssueQuietly("realtime");
    }

    void ClearIssueQuietly(string key) { lock (gate) issues.Remove(key); }

    /// <summary>A feature the server keeps off (or the role does not grant) is a configuration, not a problem: noted once in the log, no badge.</summary>
    void Skipped(string key, string message)
    {
        lock (gate) if (skipNotes.Add(key + message)) Log(SyncLevel.Info, "Synchronisation", message);
    }

    void Failed(string key, string area, ServerException ex)
    {
        switch (ex.Code)
        {
            case "unreachable" or "timeout":
                lock (gate) { state = LinkState.Offline; nextRetry = DateTime.UtcNow + OfflineRetry; }
                AddIssue("link", SyncLevel.Warning, "Connexion", Clean(ex.Message));
                break;
            case "feature_disabled": AddIssue(key, SyncLevel.Info, area, "Cette fonction est désactivée sur le serveur."); break;
            case "missing_permission" or "forbidden": AddIssue(key, SyncLevel.Info, area, "Votre compte n'a pas ce droit sur le serveur."); break;
            default:
                if (ex.Status == 401) { lock (gate) state = LinkState.SessionLost; AddIssue("link", SyncLevel.Error, "Connexion", "Votre session n'est plus valide : reconnectez-vous au serveur."); }
                else AddIssue(key, SyncLevel.Error, area, Clean(ex.Message));
                break;
        }
    }

    /// <summary>One line without control characters (the text may come from a server).</summary>
    static string Clean(string? text) => string.IsNullOrWhiteSpace(text) ? "erreur inconnue" : new string(text.Split('\n')[0].Where(c => !char.IsControl(c)).ToArray()).Trim();
}
