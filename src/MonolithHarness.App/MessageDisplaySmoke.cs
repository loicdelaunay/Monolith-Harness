using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using System.Text.Json;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeMessageDisplay(string output)
    {
        var host = new StackPanel { Spacing = 12, Padding = new(16) };
        var overlay = new Border { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Width = 1000,
            HorizontalAlignment = HorizontalAlignment.Center, Child = new ScrollViewer { Content = host } };
        root.Children.Add(overlay);
        state.ShowReasoningDetails = false;
        AddMessage("user", "Explique la formule et montre les étapes dans un schéma.", target: host);
        var first = AddAssistantMessage("Je vérifie les éléments nécessaires.", "Première réflexion.", target: host); MoveToActivity(first);
        AddToolMessage("read_source", "{}", "Données disponibles.", target: host);
        var second = AddAssistantMessage("La vérification est terminée.", "Deuxième réflexion.", target: host); MoveToActivity(second);
        AddToolMessage("asset_capture", "{}", "Capture vérifiée.", target: host);
        var final = AddAssistantMessage("# Résultat\n\n**Date :** 5 octobre 2026\n**Statut :** terminé\n\nCe paragraphe est séparé du titre et de la liste.\n\n## Points vérifiés\n\n- Espacement des paragraphes\n- Retour à la ligne et lisibilité\n\nUne formule inline : $a^2+b^2=c^2$.\n\n$$\n\\frac{1}{2}+\\frac{1}{2}=1\n$$\n\n```mermaid\nflowchart LR\n A[Question] --> B[Réflexion]\n B --> C[Réponse]\n```", "Dernière réflexion : préparer la réponse.", target: host);
        var group = first.Group!;
        if (!ReferenceEquals(group, final.Group) || group.Expanded || group.Details.Visibility != Visibility.Collapsed || !group.Summary.Text.Contains("Dernière réflexion")) throw new Exception("Collapsed activity group does not summarize the latest step.");
        if (host.Children.OfType<Border>().Count(x => ReferenceEquals(x, group.Card)) != 1 || !group.Details.Children.Contains(first.Container!) || !host.Children.Contains(final.Container!)) throw new Exception("Intermediate messages and final response are not grouped correctly.");
        var visual = final.BodyContainer.Children.OfType<AdvancedMarkdownView>().Single();
        JsonDocument? rendered = null;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            await Task.Delay(250);
            var raw = await visual.InspectAsync();
            rendered?.Dispose(); rendered = JsonDocument.Parse(raw.StartsWith('"') ? JsonSerializer.Deserialize<string>(raw)! : raw);
            if (rendered.RootElement.TryGetProperty("diagrams", out var diagrams) && diagrams.GetInt32() == 1) break;
        }
        using (rendered)
        {
            if (rendered == null || !rendered.RootElement.TryGetProperty("math", out var math) || math.GetInt32() != 2 || rendered.RootElement.GetProperty("diagrams").GetInt32() != 1 || rendered.RootElement.GetProperty("errors").GetInt32() != 0)
                throw new Exception("Embedded math/mermaid did not render: " + rendered?.RootElement.ToString());
            await File.WriteAllTextAsync(Path.Combine(output, "visual-render.json"), rendered.RootElement.ToString());
        }
        await Capture(root, Path.Combine(output, "messages-collapsed.png"));
        state.ShowReasoningDetails = true; group.Refresh(true);
        if (!group.Expanded || group.Details.Visibility != Visibility.Visible) throw new Exception("Reasoning preference did not expand the group.");
        group.SetExpanded(false); final.UpdateThinking("Dernière activité actualisée.");
        if (group.Expanded) throw new Exception("Streaming overrode the manual collapse.");
        group.Refresh(false); state.ShowReasoningDetails = true; final.RefreshReasoningPreference();
        if (!group.Expanded) throw new Exception("Enabled preference did not apply.");
        await Task.Delay(200); await Capture(root, Path.Combine(output, "messages-expanded.png"));
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-ok.txt"), "Single activity group, latest summary, default and manual expansion, two math expressions and one Mermaid SVG passed.");
        host.Children.Clear();
        AddAssistantMessage("# Android 17 QPR3 Beta 1 — Release notes\n\n**Date de sortie :** 2 octobre 2026\n**Builds :** DP11.260918.005 / DP11.260918.006\n**Support émulateur :** x86 (64-bit), ARM (v8-A)\n\nLes QPR sont livrées trimestriellement via les Feature Drops. Ces mises à jour **n’incluent pas de changements d’API impactant les apps**.\n\n## Principaux problèmes corrigés\n\n- Sortie audio : impossible de changer de périphérique de sortie pendant la lecture.\n- Crash d’apps au démarrage d’un service de premier plan depuis les réglages rapides.\n- Icônes d’apps trop espacées dans le menu des applications.\n\nSource : https://developer.android.com", target: host);
        await Task.Delay(200); await Capture(root, Path.Combine(output, "markdown-spacing.png"));
        root.Children.Remove(overlay);
    }
}
