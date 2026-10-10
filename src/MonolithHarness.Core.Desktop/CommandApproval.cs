using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record CommandApproval(string Command, string Shell, string Directory, int TimeoutSeconds = 30)
{
    public string Json() => JsonSerializer.Serialize(new { monolith_command_approval = 1, Command, Shell, Directory, TimeoutSeconds });
    public string Display => Directory + "\nShell: " + Shell + " · Timeout: " + TimeoutSeconds + " s\n\n" + Command;
    public static bool IsExecution(string scope) => scope.StartsWith("terminal|", StringComparison.Ordinal)
        || scope.StartsWith("sandbox-terminal|", StringComparison.Ordinal)
        || scope.StartsWith("python|execute|", StringComparison.Ordinal)
        || scope.StartsWith("opencode|", StringComparison.Ordinal) && !KnownNonExecution(scope.Split('|').ElementAtOrDefault(3))
        || scope.StartsWith("acp|", StringComparison.Ordinal);
    // Unknown external tools can execute arbitrary commands; require human review without exact metadata.
    static bool KnownNonExecution(string? action) => action?.ToLowerInvariant() is "read" or "glob" or "grep" or "list" or "webfetch" or "websearch"
        or "edit" or "write" or "apply_patch" or "question" or "todowrite" or "todoread" or "lsp" or "doom_loop";
    public static CommandApproval? Read(string scope, string details)
    {
        if (!IsExecution(scope)) return null;
        try
        {
            using var json = JsonDocument.Parse(details);
            if (!json.RootElement.TryGetProperty("monolith_command_approval", out var tag) || tag.GetInt32() != 1) return null;
            return JsonSerializer.Deserialize<CommandApproval>(details);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }
    public static CommandApproval ForTerminal(string command, string directory, bool sandbox, int timeout)
    {
        // Report the shell that will really execute the command, never relabel zsh/dash as Bash.
        var shell = sandbox ? "sh" : OperatingSystem.IsWindows() ? "powershell" : OperatingSystem.IsMacOS() ? "zsh" : "sh";
        if (shell == "sh" && !sandbox)
        {
            try { if (File.ResolveLinkTarget("/bin/sh", true)?.Name == "bash") shell = "bash"; }
            catch (IOException) { }
        }
        return new(command, shell, directory, timeout);
    }
    public static string OpenCodeDetails(string action, string details, string directory, IEnumerable<string> resources) =>
        FromOpenCode(action, details, directory)?.Json() ?? string.Join('\n', resources) + "\n" + details;
    public static CommandApproval? FromOpenCode(string action, string details, string directory)
    {
        action = action.ToLowerInvariant();
        if (action is not ("bash" or "shell" or "powershell" or "terminal" or "execute")) return null;
        try
        {
            var data = JsonNode.Parse(details);
            var command = data?["metadata"]?["command"]?.GetValue<string>() ?? data?["command"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(command)) return new(command, action == "bash" ? "bash" : action == "powershell" ? "powershell" : "unknown", directory);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return null;
    }
}

public sealed record CommandGuardResult(string Classification, double? Score, string? Reason, string Validator = "LANCET", string? Explanation = null, bool ExplicitApproval = false)
{
    public bool AllowsAutomatic => Classification == "not_flagged" && Reason == null && (ExplicitApproval || Score is double score && double.IsFinite(score) && score >= 0 && score <= 1);
    public static CommandGuardResult Review(string reason) => new("review", null, reason);
}
public sealed record CommandGuardProgress(string Phase, long Downloaded = 0, long Total = 0);
