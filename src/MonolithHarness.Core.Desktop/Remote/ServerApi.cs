using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core.Remote;

/// <summary>The message of the moment set by an administrator; <see cref="Level"/> is info, warning or error.</summary>
public sealed record ServerAnnouncement(string Text, string Level, DateTime UpdatedUtc, DateTime? ExpiresUtc, string? By)
{
    /// <summary>Identifies this message for "do not show it again": a new or edited message has another stamp.</summary>
    public string Stamp => UpdatedUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    public bool Expired(DateTime nowUtc) => ExpiresUtc is { } end && end <= nowUtc;
}

public sealed record ServerInfo(string Name, string Version, int ApiVersion, Dictionary<string, bool> Features, ServerAnnouncement? Announcement = null);

public sealed record ServerUser(Guid Id, string Email, string DisplayName, bool PasswordChangePending, bool PasswordChangeRequired, List<string> Roles, List<string> Permissions);

public sealed record ServerLogin(string Token, DateTime ExpiresUtc, ServerUser User, bool PasswordChangeSuggested, bool PasswordChangeRequired);

public sealed record ServerModel(string Id, int ContextLimit, bool SupportsImages);

public sealed record ServerProvider(Guid Id, string Name, List<ServerModel> Models, bool AllowLocalUse, string RelayPath);

public sealed record ServerProject(Guid Id, string Name, string OwnerEmail, string Access, long FilesBytes);

public sealed record ServerChat(int Id, string Title, DateTime? UpdatedUtc = null);

/// <summary>A message of a cloud conversation. <see cref="Wire"/> carries the tool calls / call id needed to draw tool cards; the author is known for user messages.</summary>
public sealed record ServerMessage(int Id, string Role, string Content, string State, DateTime? CompletedUtc, int? InputTokens, int? OutputTokens,
    int? CachedInputTokens = null, double Seconds = 0, string Reasoning = "", Guid? AuthorId = null, string? AuthorEmail = null, string? AuthorName = null, string? Wire = null);

/// <summary>A request of the queue. For a call of a local conversation through a shared provider (<c>Source == "relay"</c>) the project and conversation are empty and the provider and model say what is waited for.</summary>
public sealed record ServerRun(Guid Id, Guid ProjectId, int ChatId, string State, int? QueuePosition, string Error, string ChangeSummary, Guid? RequesterId = null, string? Requester = null, string? Source = null,
    Guid? ProviderId = null, string? Model = null);

public sealed record ServerApproval(Guid Id, Guid RunId, Guid ProjectId, string Kind, string Title, string Details, string Command, JsonElement? Questions, bool OneTimeOnly, string State);

public sealed record ServerParticipant(Guid UserId, string Email, DateTime? LeftUtc);

/// <summary>A shared conversation. <see cref="Code"/> is only filled for the host; <see cref="Joined"/> tells a viewer who joined (by invitation or code) from one who only sees a public conversation.</summary>
public sealed record ServerShare(Guid Id, string Title, string ClientConversationId, string HostEmail, bool IsHost, bool Live, DateTime? RevokedUtc, List<ServerParticipant> Participants,
    string? Code = null, bool IsPublic = false, bool Joined = false);

public sealed record ServerUserSuggestion(Guid Id, string Email, string DisplayName);

/// <summary>One model call a client shares with its server: numbers only, never a prompt, a reply or a name of project or conversation.</summary>
public sealed record UsageSampleDto(string Id, DateTime CompletedUtc, string Provider, string Model, string Activity, long InputTokens, long OutputTokens, long? CachedInputTokens,
    double Seconds, string Status, bool Estimated, bool ViaServer);

public sealed record UsageAcceptedDto(int Accepted, int Duplicates, int Rejected);

public sealed record UsageStatusDto(bool Enabled, int Samples, DateTime? FirstUtc, DateTime? LastUtc, int RetentionDays);

/// <summary>Typed REST client of the Monolith Harness Server. Errors surface as <see cref="ServerException"/> with the server's stable codes.</summary>
public sealed class ServerApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient http;
    readonly Func<string?> token;

    public Uri BaseUri { get; }

    public ServerApi(HttpClient http, Uri baseUri, Func<string?> token)
    {
        this.http = http;
        this.token = token;
        BaseUri = baseUri;
    }

    public static HttpClient CreateClient() => new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) })
    { Timeout = TimeSpan.FromSeconds(60) };

    // ---- Connection and account ----

    public Task<ServerInfo> InfoAsync(CancellationToken ct = default) => GetAsync<ServerInfo>("api/server", ct);

    public Task<ServerLogin> LoginAsync(string email, string password, string label, CancellationToken ct = default) =>
        SendAsync<ServerLogin>(HttpMethod.Post, "api/auth/login", new { email, password, label }, ct, authenticate: false);

    public Task<ServerUser> MeAsync(CancellationToken ct = default) => GetAsync<ServerUser>("api/auth/me", ct);

    public Task LogoutAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, "api/auth/logout", new { }, ct);

    public Task ChangePasswordAsync(string current, string next, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/password", new { currentPassword = current, newPassword = next }, ct);

    // ---- Providers ----

    public Task<List<ServerProvider>> ProvidersAsync(CancellationToken ct = default) => GetAsync<List<ServerProvider>>("api/providers", ct);

    // ---- Cloud projects ----

    public Task<List<ServerProject>> ProjectsAsync(CancellationToken ct = default) => GetAsync<List<ServerProject>>("api/projects", ct);

    public Task<ServerProject> CreateProjectAsync(string name, CancellationToken ct = default) => SendAsync<ServerProject>(HttpMethod.Post, "api/projects", new { name }, ct);

    public Task<List<ServerChat>> ChatsAsync(Guid project, CancellationToken ct = default) => GetAsync<List<ServerChat>>($"api/projects/{project}/chats", ct);

    /// <summary>Replaces the members of a project (owner only); access is "read" or "run".</summary>
    public Task SetMembersAsync(Guid project, IEnumerable<(string Email, string Access)> members, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/projects/{project}/members", new { members = members.Select(x => new { email = x.Email, access = x.Access }).ToList() }, ct);

    public Task DeleteProjectAsync(Guid project, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/projects/{project}", null, ct);

    public Task DeleteChatAsync(Guid project, int chat, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/projects/{project}/chats/{chat}", null, ct);

    public Task RenameChatAsync(Guid project, int chat, string title, CancellationToken ct = default) => SendAsync(HttpMethod.Patch, $"api/projects/{project}/chats/{chat}", new { title }, ct);

    public Task<ServerChat> CreateChatAsync(Guid project, string title, CancellationToken ct = default) =>
        SendAsync<ServerChat>(HttpMethod.Post, $"api/projects/{project}/chats", new { title }, ct);

    public Task<List<ServerMessage>> MessagesAsync(Guid project, int chat, int after = 0, CancellationToken ct = default) =>
        GetAsync<List<ServerMessage>>($"api/projects/{project}/chats/{chat}/messages?after={after}", ct);

    public Task<ServerRun> SendRequestAsync(Guid project, int chat, string text, Guid provider, string model, CancellationToken ct = default) =>
        SendAsync<ServerRun>(HttpMethod.Post, $"api/projects/{project}/chats/{chat}/requests", new { text, providerId = provider, model }, ct);

    public Task<ServerRun> RunAsync(Guid run, CancellationToken ct = default) => GetAsync<ServerRun>($"api/runs/{run}", ct);

    public Task<ServerRun> CancelRunAsync(Guid run, CancellationToken ct = default) => SendAsync<ServerRun>(HttpMethod.Post, $"api/runs/{run}/cancel", new { }, ct);

    public Task<List<ServerApproval>> ApprovalsAsync(CancellationToken ct = default) => GetAsync<List<ServerApproval>>("api/approvals?pending=true", ct);

    public Task DecideAsync(Guid approval, string choice, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/approvals/{approval}/decision", new { choice }, ct);

    public Task AnswerAsync(Guid approval, bool cancelled, IReadOnlyList<IReadOnlyList<string>>? answers, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/approvals/{approval}/answer", new { cancelled, answers }, ct);

    // ---- Shared conversations ----

    public Task<List<ServerShare>> SharesAsync(CancellationToken ct = default) => GetAsync<List<ServerShare>>("api/shares", ct);

    public Task<ServerShare> CreateShareAsync(string title, string clientConversationId, CancellationToken ct = default, bool isPublic = false) =>
        SendAsync<ServerShare>(HttpMethod.Post, "api/shares", new { title, clientConversationId, isPublic }, ct);

    public Task<ServerShare> ShareAsync(Guid share, CancellationToken ct = default) => GetAsync<ServerShare>($"api/shares/{share}", ct);

    /// <summary>Conversations their host made public (the ones the account already follows are included).</summary>
    public Task<List<ServerShare>> PublicSharesAsync(CancellationToken ct = default) => GetAsync<List<ServerShare>>("api/shares/public", ct);

    /// <summary>Follows a conversation from its sharing code (any case, separators optional).</summary>
    public Task<ServerShare> JoinShareAsync(string code, CancellationToken ct = default) => SendAsync<ServerShare>(HttpMethod.Post, "api/shares/join", new { code }, ct);

    public Task<ServerShare> SetSharePublicAsync(Guid share, bool isPublic, CancellationToken ct = default) => SendAsync<ServerShare>(HttpMethod.Patch, $"api/shares/{share}", new { isPublic }, ct);

    public Task<ServerShare> NewShareCodeAsync(Guid share, CancellationToken ct = default) => SendAsync<ServerShare>(HttpMethod.Post, $"api/shares/{share}/code", new { }, ct);

    public Task<ServerShare> DisableShareCodeAsync(Guid share, CancellationToken ct = default) => SendAsync<ServerShare>(HttpMethod.Delete, $"api/shares/{share}/code", null, ct);

    public Task LeaveShareAsync(Guid share, CancellationToken ct = default) => SendAsync(HttpMethod.Post, $"api/shares/{share}/leave", new { }, ct);

    public Task CommentShareAsync(Guid share, string text, CancellationToken ct = default) => SendAsync(HttpMethod.Post, $"api/shares/{share}/comments", new { text }, ct);

    /// <summary>Active accounts matching a text (e-mail or name), to pick people to invite.</summary>
    public Task<List<ServerUserSuggestion>> SearchUsersAsync(string query, CancellationToken ct = default) => GetAsync<List<ServerUserSuggestion>>("api/users/search?query=" + Uri.EscapeDataString(query), ct);

    public Task InviteAsync(Guid share, string email, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/shares/{share}/participants", new { email }, ct);

    public Task RevokeShareAsync(Guid share, CancellationToken ct = default) => SendAsync(HttpMethod.Post, $"api/shares/{share}/revoke", new { }, ct);

    // ---- Usage statistics ----

    public Task<UsageAcceptedDto> UploadUsageAsync(string deviceId, IEnumerable<UsageSampleDto> samples, CancellationToken ct = default) =>
        SendAsync<UsageAcceptedDto>(HttpMethod.Post, "api/usage", new { deviceId, samples = samples.ToList() }, ct);

    public Task<UsageStatusDto> UsageStatusAsync(CancellationToken ct = default) => GetAsync<UsageStatusDto>("api/usage/status", ct);

    /// <summary>Erases everything this account shared; returns how many measurements were deleted.</summary>
    public async Task<int> DeleteUsageAsync(CancellationToken ct = default) =>
        (await SendAsync<JsonObject>(HttpMethod.Delete, "api/usage", null, ct))["deleted"]?.GetValue<int>() ?? 0;

    // ---- Transport ----

    Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, ct);

    async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, bool authenticate = true)
    {
        var text = await RequestAsync(method, path, body, ct, authenticate);
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new ServerException("invalid_response", "Réponse du serveur invalide / Invalid server response.");
    }

    async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct) => await RequestAsync(method, path, body, ct, true);

    async Task<string> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool authenticate)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUri, path));
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        if (authenticate && token() is { Length: > 0 } value) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new ServerException("unreachable", "Serveur injoignable / Server unreachable: " + (ex.InnerException?.Message ?? ex.Message)); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ServerException("timeout", "Le serveur ne répond pas / The server does not respond."); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) return text.Length == 0 ? "null" : text;
            string code = "http_" + (int)response.StatusCode, message = response.ReasonPhrase ?? "error";
            try
            {
                var error = JsonNode.Parse(text);
                code = error?["code"]?.GetValue<string>() ?? code;
                message = error?["message"]?.GetValue<string>() ?? message;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            int? retry = response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter?.Delta is { } delta ? (int)delta.TotalSeconds : null;
            throw new ServerException(code, message, (int)response.StatusCode, retry);
        }
    }
}
