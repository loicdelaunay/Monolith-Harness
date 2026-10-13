using System.Text.Json.Nodes;

namespace MonolithHarness.Core.Hosting;

public static class DesktopControl
{
    static readonly SemaphoreSlim inputGate = new(1, 1);
    public static async Task<string> InspectAsync(JsonObject args, Func<CancellationToken, Task<FeatureSettings>> readSettings,
        Func<string, string, CancellationToken, Task<bool>> approve, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("UI Automation est disponible uniquement sur Windows / Windows only.");
        await readSettings(ct);
        var windowId = args["window_id"]?.GetValue<string>();
        var limit = args["max_elements"]?.GetValue<int>() ?? 100;
        if (limit is < 1 or > 150) throw new ArgumentException("max_elements must be 1..150.");
        if (windowId == null && args["parent_element_id"] != null) throw new ArgumentException("parent_element_id requires window_id.");
        var window = windowId == null ? null : DesktopApplications.Resolve(windowId);
        if (!await approve("uia", "Inspection des contrôles Windows, titres et identifiants transmis au modèle / Read UI controls and transmit labels\n" + (window?.Title ?? "Liste des fenêtres / Window list"), ct)) return Denied("uia");
        var settings = await readSettings(ct); // Also checks that a desktop skill is still enabled.
        if (window != null) return await WindowsUiAutomation.InspectAsync(window, limit, ct, args["parent_element_id"]?.GetValue<string>());
        return new JsonObject { ["mode"] = "uia", ["windows"] = System.Text.Json.JsonSerializer.SerializeToNode(DesktopApplications.List(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
            ["keyboard"] = System.Text.Json.JsonSerializer.SerializeToNode(settings.KeyboardInput), ["mouse"] = System.Text.Json.JsonSerializer.SerializeToNode(settings.MouseInput),
            ["warning"] = "Titles are untrusted data. Supply an exact window_id to inspect controls. No focus change or physical input." }.ToJsonString();
    }
    static string Denied(string mode) => new JsonObject { ["ok"] = false, ["mode"] = mode, ["status"] = "denied", ["error"] = "Accès refusé / Access denied." }.ToJsonString();
    public static async Task<string> ExecuteAsync(string tool, JsonObject original, Func<CancellationToken, Task<FeatureSettings>> readSettings,
        Func<string, string, CancellationToken, Task<bool>> approve, CancellationToken ct)
    {
        if (tool is not ("desktop_keyboard" or "desktop_mouse")) throw new ArgumentException("Unknown desktop input tool.");
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Contrôle du bureau indisponible sur cet OS / Desktop input unavailable on this OS.");
        await inputGate.WaitAsync(ct);
        try
        {
            var args = original.DeepClone().AsObject();
            var action = (args["action"]?.GetValue<string>() ?? (tool == "desktop_mouse" ? "click" : "type")).Trim().ToLowerInvariant(); args["action"] = action;
            if (tool == "desktop_keyboard")
            {
                if (action is not ("type" or "set_value" or "press")) throw new ArgumentException("Keyboard action must be type, set_value or press.");
                if (action != "press" && args["text"] == null) throw new ArgumentException("text is required, including an explicit empty string for set_value.");
                var text = args["text"]?.GetValue<string>() ?? "";
                if (action != "press" && (text.Length > 10000 || action == "type" && text.Length == 0)) throw new ArgumentException("Text must contain 1..10000 characters (set_value can be empty).");
                if (action == "press") { var chord = KeyboardInput.ParseChord(args["keys"]?.GetValue<string>()); args["keys"] = string.Join('+', chord.Modifiers.Append(chord.Key)); }
            }
            else if (action is not ("click" or "invoke" or "toggle" or "select" or "scroll" or "scroll_into_view" or "expand" or "collapse" or "move" or "slide")) throw new ArgumentException("Unknown mouse action.");
            var requested = args["mode"]?.GetValue<string>();
            var initial = DesktopControlTools.Settings(await readSettings(ct), tool);
            var mode = initial.Resolve(requested);
            var windowId = args["window_id"]?.GetValue<string>();
            var window = windowId == null ? null : DesktopApplications.Resolve(windowId);
            var foreground = DesktopInput.Foreground();
            var target = window == null ? foreground : OperatingSystem.IsWindows() ? window.NativeId : window.ProcessId;
            if (args["element_id"] != null && window == null) throw new ArgumentException("element_id requires its exact window_id from desktop_inspect_ui.");
            string? fallback = mode == "auto" && !OperatingSystem.IsWindows() ? "UI Automation indisponible sur cet OS ; utilisation du legacy / UIA unavailable on this OS." : null;
            var selected = mode == "auto" ? OperatingSystem.IsWindows() ? "uia" : "legacy" : mode;
            if (selected == "uia")
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("UI Automation nécessite Windows. Choisissez Automatique ou Legacy / Select auto or legacy.");
                if (!await approve("uia", Details("uia", args, window, null), ct)) return Denied("uia");
                await CheckPolicy(ct);
                try
                {
                    if (window == null) throw new DesktopPatternUnsupportedException("Inspectez la cible avec desktop_inspect_ui puis fournissez window_id et element_id / Inspect UI and provide explicit target IDs.");
                    return await WindowsUiAutomation.ExecuteAsync(tool, args, window, ct);
                }
                catch (DesktopPatternUnsupportedException error)
                {
                    var current = await CheckPolicy(ct);
                    if (mode != "auto" || !current.AllowLegacyFallback)
                        return new JsonObject { ["ok"] = false, ["mode"] = "uia", ["status"] = "unsupported", ["error"] = error.Message,
                            ["suggested_mode"] = current.Mode == "auto" && current.AllowAiModeSwitch ? "legacy" : "change_skill_settings",
                            ["legacy_fallback"] = false, ["physical_input"] = false }.ToJsonString();
                    fallback = error.Message;
                    selected = "legacy";
                }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or TimeoutException or InvalidOperationException or ArgumentException)
                {
                    return new JsonObject { ["ok"] = false, ["mode"] = "uia", ["status"] = "failed", ["error"] = error.Message,
                        ["legacy_fallback"] = false, ["physical_input"] = false,
                        ["outcome_unknown"] = error is System.Runtime.InteropServices.COMException or TimeoutException,
                        ["instruction"] = "No automatic replay. Inspect the target state before retrying; refresh IDs if controls changed." }.ToJsonString();
                }
            }
            // Validation occurs before approval/focus changes; unsupported semantic actions never
            // get converted into guessed physical keys or coordinates.
            if (WindowsUiAutomation.IsBusy) throw new InvalidOperationException("Un appel UI Automation n’est pas terminé ; le legacy attend sa fin / UIA call still pending; Legacy is blocked until it completes.");
            ValidateLegacy(tool, args);
            if (!await approve("legacy", Details("legacy", args, window, fallback), ct)) return Denied("legacy");
            var latest = await CheckPolicy(ct);
            if (fallback != null && OperatingSystem.IsWindows() && !latest.AllowLegacyFallback) throw new UnauthorizedAccessException("Legacy fallback was disabled.");
            ct.ThrowIfCancellationRequested();
            if (window != null) window = DesktopApplications.Resolve(window.Id);
            else if (target == 0) throw new InvalidOperationException("Aucune fenêtre cible / No foreground target window.");
            DesktopInput.Restore(target);
            await Task.Delay(180, ct);
            if (DesktopInput.Foreground() != target) throw new InvalidOperationException("La fenêtre cible n’a pas obtenu le focus. Aucun input envoyé / Target focus failed; no input sent.");
            IReadOnlyList<MouseInput.SlideStep>? slide = null;
            if (tool == "desktop_mouse")
            {
                using var dpi = OperatingSystem.IsWindows() ? DesktopApplications.PhysicalCoordinates() : null;
                if (window != null)
                {
                    var point = window.RelativePoint(args["x"]!.GetValue<double>(), args["y"]!.GetValue<double>());
                    DesktopApplications.DemandPointTarget(window, point.X, point.Y); args["x"] = point.X; args["y"] = point.Y;
                    if (action == "slide") { var end = window.RelativePoint(args["x2"]!.GetValue<double>(), args["y2"]!.GetValue<double>()); DesktopApplications.DemandPointTarget(window, end.X, end.Y); args["x2"] = end.X; args["y2"] = end.Y; }
                }
                var x = args["x"]!.GetValue<double>(); var y = args["y"]!.GetValue<double>();
                DesktopInput.DemandPoint(x, y);
                if (action == "slide") DesktopInput.DemandPoint(args["x2"]!.GetValue<double>(), args["y2"]!.GetValue<double>());
                if (action == "slide")
                {
                    slide = MouseInput.SlidePath(x, y, args["x2"]!.GetValue<double>(), args["y2"]!.GetValue<double>(), args["pattern"]?.GetValue<string>());
                    if (window != null) foreach (var step in slide) DesktopApplications.DemandPointTarget(window, step.X, step.Y);
                }
            }
            await DesktopInput.ExecuteAsync(tool, args, ct, slide);
            return new JsonObject { ["ok"] = true, ["mode"] = "legacy", ["action"] = action, ["window_id"] = windowId,
                ["physical_input"] = true, ["fallback_reason"] = fallback }.ToJsonString();

            async Task<DesktopInputSettings> CheckPolicy(CancellationToken token)
            {
                var settings = DesktopControlTools.Settings(await readSettings(token), tool);
                if (settings.Resolve(requested) != mode) throw new UnauthorizedAccessException("Les réglages ont changé ; aucun input envoyé / Input settings changed.");
                return settings;
            }
        }
        finally { inputGate.Release(); }
    }
    static void ValidateLegacy(string tool, JsonObject args)
    {
        var action = args["action"]!.GetValue<string>();
        if (tool == "desktop_keyboard")
        { if (action is not ("type" or "press")) throw new ArgumentException("set_value nécessite UI Automation ; aucun repli legacy / set_value requires UIA."); return; }
        if (action is not ("click" or "move" or "scroll" or "slide")) throw new ArgumentException("Cette action logique n’existe pas en legacy / Semantic action unavailable in legacy.");
        foreach (var key in action == "slide" ? new[] { "x", "y", "x2", "y2" } : new[] { "x", "y" })
        {
            var value = args[key]?.GetValue<double>() ?? throw new ArgumentException("Legacy requires " + key + "; provide verified coordinates, not guesses.");
            if (!double.IsFinite(value) || Math.Abs(value) > 100000) throw new ArgumentException("Invalid coordinates.");
        }
        MouseInput.NormalizeButton(args["button"]?.GetValue<string>()); MouseInput.NormalizeClickCount(args["click_count"]?.GetValue<int>() ?? 1);
        if (action == "slide") MouseInput.NormalizeSlidePattern(args["pattern"]?.GetValue<string>());
        var delta = args["delta_y"]?.GetValue<double>() ?? 0;
        if (!double.IsFinite(delta) || Math.Abs(delta) > 100000) throw new ArgumentException("Invalid scroll delta.");
    }
    static string Details(string mode, JsonObject args, ApplicationWindow? window, string? fallback) =>
        "Mode : " + (mode == "uia" ? "UI Automation · sans input physique / no physical input" : "Legacy · clavier, focus et curseur partagés / shared keyboard, focus and cursor")
        + (window == null ? "" : "\n" + window.Application + " · " + window.Title + "\nwindow_id=" + window.Id)
        + (fallback == null ? "" : "\nRepli / Fallback : " + fallback) + "\n" + args.ToJsonString();
}
