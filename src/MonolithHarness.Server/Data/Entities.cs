namespace MonolithHarness.Server.Data;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Lower-case, trimmed e-mail address; unique.</summary>
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>The password is initial or administrator-issued: the user is offered (or, by policy, required) to change it.</summary>
    public bool PasswordChangePending { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
    public DateTime? PasswordChangedUtc { get; set; }
    public List<UserRole> Roles { get; set; } = [];
}

public sealed class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>System roles cannot be renamed or deleted; Administrator always holds every permission.</summary>
    public bool IsSystem { get; set; }
    /// <summary>Comma-separated permission identifiers.</summary>
    public string Permissions { get; set; } = "";
    public List<UserRole> Users { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public User? User { get; set; }
    public Role? Role { get; set; }
}

public static class SessionKinds
{
    public const string Web = "web";
    public const string Client = "client";
    /// <summary>Short-lived token handed to a cloud worker so that it can only call the provider relay.</summary>
    public const string Run = "run";
}

public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string Kind { get; set; } = SessionKinds.Client;
    public string Label { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedUtc { get; set; }
    public string Address { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public Guid? RunId { get; set; }
}

public sealed class RecoveryToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
    public string IssuedBy { get; set; } = "";
}

public sealed class SettingEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid? ActorId { get; set; }
    public string ActorEmail { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Address { get; set; } = "";
    public bool Success { get; set; } = true;
}

public sealed class SharedProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>OpenAI-compatible API root, e.g. https://api.openai.com/v1. Never exposed to clients.</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>Encrypted with ASP.NET Core Data Protection; never returned by the API.</summary>
    public byte[] ProtectedSecret { get; set; } = [];
    /// <summary>JSON array of {id, contextLimit, supportsImages}.</summary>
    public string ModelsJson { get; set; } = "[]";
    public bool IsEnabled { get; set; } = true;
    /// <summary>Lets a local (Desktop, CLI or Android) run use this provider through the relay.</summary>
    public bool AllowLocalUse { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class SubjectKinds
{
    public const string User = "user";
    public const string Role = "role";
}

public sealed class ProviderAccess
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public string SubjectKind { get; set; } = SubjectKinds.User;
    public Guid SubjectId { get; set; }
}

public sealed class CloudProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class ProjectAccess
{
    public const string Read = "read";
    public const string Run = "run";
}

public sealed class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string Access { get; set; } = ProjectAccess.Read;
}

public static class RunStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public static bool IsFinal(string state) => state is Completed or Failed or Cancelled;
}

public sealed class RunRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Monotonic arrival order used for fair, stable queue positions.</summary>
    public long Sequence { get; set; }
    public Guid ProjectId { get; set; }
    public int ChatId { get; set; }
    public Guid RequesterId { get; set; }
    /// <summary>"interactive" or "schedule".</summary>
    public string Source { get; set; } = "interactive";
    public Guid? ScheduleId { get; set; }
    public Guid ProviderId { get; set; }
    public string Model { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string State { get; set; } = RunStates.Queued;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string Error { get; set; } = "";
    public string ChangeSummary { get; set; } = "";
}

public static class ApprovalStates
{
    public const string Pending = "pending";
    public const string Allowed = "allowed";
    public const string Denied = "denied";
    public const string Answered = "answered";
    public const string Expired = "expired";
}

public sealed class Approval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    /// <summary>The user who must decide: the requester of the run.</summary>
    public Guid UserId { get; set; }
    /// <summary>"permission" for a tool approval or "question" for an agent question.</summary>
    public string Kind { get; set; } = "permission";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Command { get; set; } = "";
    public string QuestionsJson { get; set; } = "";
    /// <summary>Guarded commands can only be allowed once.</summary>
    public bool OneTimeOnly { get; set; }
    public string State { get; set; } = ApprovalStates.Pending;
    public Guid? DecidedById { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedUtc { get; set; }
}

public sealed class CloudSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string Cron { get; set; } = "0 * * * *";
    public string TimeZoneId { get; set; } = "UTC";
    public bool Enabled { get; set; } = true;
    public Guid ProviderId { get; set; }
    public string Model { get; set; } = "";
    /// <summary>"skip" ignores occurrences missed while the server was stopped; "run-once" runs the latest one on restart.</summary>
    public string MissedPolicy { get; set; } = "skip";
    public int? ChatId { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public Guid? LastRunId { get; set; }
    public string LastResult { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The composite key is the duplicate-execution guard: one row per task occurrence.</summary>
public sealed class ScheduleOccurrence
{
    public Guid ScheduleId { get; set; }
    public DateTime OccurrenceUtc { get; set; }
    public Guid? RunId { get; set; }
    public string Outcome { get; set; } = "queued";
}

public sealed class SharedConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HostId { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Identifier of the conversation on the host device, opaque to the server.</summary>
    public string ClientConversationId { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedUtc { get; set; }
    /// <summary>Secret code (10 symbols, normalized) that lets someone follow the conversation; null when turned off.</summary>
    public string? Code { get; set; }
    /// <summary>Public conversations are listed to every signed-in user allowed to follow conversations.</summary>
    public bool IsPublic { get; set; }
}

/// <summary>Who sent a user message of a cloud conversation (the messages themselves live in the project's engine database).</summary>
public sealed class MessageAuthor
{
    public Guid ProjectId { get; set; }
    public int MessageId { get; set; }
    public int ChatId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>
/// One model call a client chose to share (tokens, duration, speed): numbers only, never prompts, replies, project or conversation names.
/// <see cref="SampleKey"/> makes uploads idempotent: a client may send the same call again without counting it twice.
/// </summary>
public sealed class UsageSample
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string SampleKey { get; set; } = "";
    public DateTime CompletedUtc { get; set; }
    public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Activity { get; set; } = "chat";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? CachedInputTokens { get; set; }
    public double Seconds { get; set; }
    public string Status { get; set; } = "complete";
    /// <summary>Counts were estimated by the client rather than reported by the provider.</summary>
    public bool Estimated { get; set; }
    /// <summary>The call went through a provider of this server (relay).</summary>
    public bool ViaServer { get; set; }
    /// <summary>Output tokens per second of a complete call with a measurable duration; null otherwise (such calls do not enter speed averages).</summary>
    public double? Speed { get; set; }
}

public sealed class ShareParticipant
{
    public Guid ShareId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeftUtc { get; set; }
}
