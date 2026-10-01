using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    // These bars deliberately remain indeterminate: an agent's step budget is not percent complete.
    static string ActivityBar(int tick, int width, bool complete = false, bool stopped = false, bool ascii = false)
    {
        char empty = ascii ? '.' : '·', filled = ascii ? '=' : '━';
        if (complete) return "[" + new string(filled, width) + "]";
        if (stopped) return "[" + new string(empty, width) + "]";
        int span = width - 3, cycle = tick / 3 % (span * 2), start = cycle <= span ? cycle : span * 2 - cycle;
        return "[" + new string(empty, start) + new string(filled, 3) + new string(empty, width - start - 3) + "]";
    }

    void DrawThinking(TerminalCanvas canvas, ChatView view, int x, int y, int width, Palette p, bool ascii)
    {
        bool workers = view.Children.Values.Any(child => child.Status == "running");
        canvas.Write(x, y, ActivityBar(frame, 10, ascii: ascii), p.Highlight);
        canvas.Write(x + 13, y, workers ? L("Sous-agents au travail…", "Subagents working…") : view.ContextRequest is { } preload ? preload.Label(workspace?.State.Language ?? "fr") : L("Réflexion en cours…", "Thinking…"), p.Highlight, width - 13);
    }

    void DrawAgents(TerminalCanvas canvas, ChatView view, IReadOnlyList<SubagentRecord> agents,
        int x, int y, int width, int visible, Palette p, bool ascii, bool compact)
    {
        int running = agents.Count(child => child.Status == "running"), pages = (agents.Count + visible - 1) / visible;
        view.AgentPage = Math.Clamp(view.AgentPage, 0, Math.Max(0, pages - 1));
        int first = view.AgentPage * visible;
        if (!compact) canvas.Write(x, y++, L("Sous-agents", "Subagents") + $" · {running} " + L("en cours", "running") + $" / {agents.Count}", p.Highlight, width);
        int nameWidth = Math.Clamp(width / 4, 12, 24), barWidth = 8;
        for (int row = 0; row < visible && first + row < agents.Count; row++)
        {
            var child = agents[first + row];
            bool complete = child.Status == "completed", stopped = child.Status is "failed" or "cancelled" or "limited";
            var ink = complete ? new Ink(p.Green, p.Background) : stopped ? new(p.Gold, p.Background) : p.Highlight;
            string activity = child.Status switch
            {
                "completed" => L("Terminé", "Completed"), "failed" => L("Échec", "Failed"),
                "cancelled" => L("Arrêté", "Stopped"), "limited" => L("Budget atteint", "Budget reached"),
                _ => AgentActivity(child.Activity)
            };
            canvas.Write(x, y + row, (compact ? $"{first + row + 1}/{agents.Count} " : "") + child.Name, p.Normal, nameWidth);
            canvas.Write(x + nameWidth + 1, y + row, ActivityBar(frame + (first + row) * 7, barWidth, complete, stopped, ascii), ink);
            canvas.Write(x + nameWidth + barWidth + 4, y + row, activity, complete || stopped ? ink : p.Dim, width - nameWidth - barWidth - 4);
        }
        if (pages > 1 && !compact)
            canvas.Write(x, y + visible, $"{first + 1}–{Math.Min(first + visible, agents.Count)}/{agents.Count} · " + L("Alt+Pg↑/Pg↓ : autres agents", "Alt+PgUp/PgDn: other agents"), p.Dim, width);
    }

    string AgentActivity(string activity)
    {
        if (string.IsNullOrWhiteSpace(activity)) return L("Démarrage…", "Starting…");
        return activity.Replace("Réflexion / Thinking", L("Réflexion", "Thinking"))
            .Replace("Outil terminé / Tool completed", L("Outil terminé", "Tool completed"))
            .Replace("Outil / Tool", L("Outil", "Tool"))
            .Replace("Réponse reçue / Response received", L("Réponse reçue", "Response received"))
            .Replace("Démarrage / Starting", L("Démarrage", "Starting"))
            .Replace("Compactage du contexte / Compacting context", L("Compactage du contexte", "Compacting context"))
            .Replace("Étape ", L("Étape ", "Step "));
    }
}
