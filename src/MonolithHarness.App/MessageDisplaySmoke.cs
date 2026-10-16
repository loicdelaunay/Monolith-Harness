using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        // Chat WebViews are opt-in (AllowWebViewInChat); by default math and Mermaid render natively.
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            yield return parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        static bool Rendered(NativeMathView view) => view.Content is Image || view.Content is ScrollViewer { Content: Image };
        List<NativeMathView> math = []; var diagrams = 0; var webViews = 0;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            await Task.Delay(100);
            var tree = Descendants(final.BodyContainer).ToList();
            math = tree.OfType<NativeMathView>().ToList(); webViews = tree.OfType<AdvancedMarkdownView>().Count();
            diagrams = tree.OfType<TextBlock>().Count(x => x.Text is "Diagramme Mermaid · aperçu natif simplifié" or "Mermaid diagram · simplified native preview");
            if (math.Count == 2 && math.All(Rendered) && diagrams == 1) break;
        }
        var nodes = Descendants(final.BodyContainer).OfType<Canvas>().Sum(x => x.Children.Count);
        var report = new { renderer = "native", math = math.Count, renderedMath = math.Count(Rendered), diagrams, diagramElements = nodes, webViews };
        if (math.Count != 2 || !math.All(Rendered) || diagrams != 1 || nodes == 0 || webViews != 0)
            throw new Exception("Embedded math/mermaid did not render natively: " + JsonSerializer.Serialize(report));
        await File.WriteAllTextAsync(Path.Combine(output, "visual-render.json"), JsonSerializer.Serialize(report));
        await Capture(root, Path.Combine(output, "messages-collapsed.png"));
        state.ShowReasoningDetails = true; group.Refresh(true);
        if (!group.Expanded || group.Details.Visibility != Visibility.Visible) throw new Exception("Reasoning preference did not expand the group.");
        group.SetExpanded(false); final.UpdateThinking("Dernière activité actualisée.");
        if (group.Expanded) throw new Exception("Streaming overrode the manual collapse.");
        group.Refresh(false); state.ShowReasoningDetails = true; final.RefreshReasoningPreference();
        if (!group.Expanded) throw new Exception("Enabled preference did not apply.");
        await Task.Delay(200); await Capture(root, Path.Combine(output, "messages-expanded.png"));
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-ok.txt"), "Single activity group, latest summary, default and manual expansion, two native math expressions and one native Mermaid diagram passed.");
        host.Children.Clear();
        AddAssistantMessage("# Android 17 QPR3 Beta 1 — Release notes\n\n**Date de sortie :** 2 octobre 2026\n**Builds :** DP11.260918.005 / DP11.260918.006\n**Support émulateur :** x86 (64-bit), ARM (v8-A)\n\nLes QPR sont livrées trimestriellement via les Feature Drops. Ces mises à jour **n’incluent pas de changements d’API impactant les apps**.\n\n## Principaux problèmes corrigés\n\n- Sortie audio : impossible de changer de périphérique de sortie pendant la lecture.\n- Crash d’apps au démarrage d’un service de premier plan depuis les réglages rapides.\n- Icônes d’apps trop espacées dans le menu des applications.\n\nSource : https://developer.android.com", target: host);
        await Task.Delay(200); await Capture(root, Path.Combine(output, "markdown-spacing.png"));
        root.Children.Remove(overlay);
    }
}
