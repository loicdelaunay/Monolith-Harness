using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;
public sealed partial class MainWindow
{
    FrameworkElement BuildModelTestSettings(Window owner)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label(WorkflowText("Tests modèles", "Model tests"), 20));
        panel.Children.Add(Label(WorkflowText("Simple : nommage, RAG et autorisation automatique. Complet : ajoute chaque modèle sélectionné de tous les fournisseurs. Utilise la configuration enregistrée ; les requêtes API peuvent être facturées. Aucun outil ou commande de test n’est exécuté.",
            "Simple: naming, RAG and automatic approval. Complete: also checks every selected model of all providers. Uses saved configuration; API requests may be billed. No execution tools or test commands are run."), 13));
        var simple = new Button { Content = WorkflowText("Test simple", "Simple test") };
        var complete = new Button { Content = WorkflowText("Test complet", "Complete test") };
        var cancel = new Button { Content = T("Arrêter"), IsEnabled = false };
        var status = Label("", 12); var results = new StackPanel { Spacing = 8 };
        panel.Children.Add(Row(simple, complete, cancel)); panel.Children.Add(status); panel.Children.Add(results);
        CancellationTokenSource? running = null;
        cancel.Click += (_, _) => running?.Cancel(); owner.Closed += (_, _) => running?.Cancel();
        async Task Run(bool full)
        {
            if (running != null) return;
            using var lifetime = new CancellationTokenSource(); running = lifetime;
            simple.IsEnabled = complete.IsEnabled = false; cancel.IsEnabled = true; results.Children.Clear();
            var rows = new Dictionary<string, TextBlock>(); int passed = 0, failed = 0;
            var progress = new Progress<ModelProbeResult>(result => DispatcherQueue.TryEnqueue(() =>
            {
                if (lifetime.IsCancellationRequested) return;
                var key = result.Role + " · " + result.Model;
                if (!rows.TryGetValue(key, out var row)) { row = Label("", 13); rows[key] = row; results.Children.Add(FluentDesign.Surface(row, 10)); }
                row.Text = (result.Status == "success" ? "✓ " : result.Status == "failed" ? "✕ " : "… ") + key +
                    (result.Detail.Length == 0 ? "" : "\n" + result.Detail) + (result.Seconds > 0 ? $" · {result.Seconds:F1} s" : "");
                row.Foreground = result.Status == "failed" ? FluentDesign.Adapt(232, 91, 91) : FluentDesign.Primary;
                if (result.Status == "success") passed++; if (result.Status == "failed") failed++;
                status.Text = WorkflowText($"{passed} réussis · {failed} en échec", $"{passed} passed · {failed} failed");
            }));
            try
            {
                var configured = await ReadStoreAsync(store => store.Providers.AsNoTracking().ToList());
                using var consumption = TokenConsumption.Activity("model-diagnostics");
                await ModelDiagnostics.RunAsync(FeatureSettings.Read(state.FeaturesJson), configured, http,
                    async (selected, ct) => { if (selected.IsOpenCode) await EnsureOpenCodeServerAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), ct); }, full, progress, lifetime.Token);
            }
            catch (OperationCanceledException) { status.Text = WorkflowText("Tests arrêtés", "Tests stopped"); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { running = null; simple.IsEnabled = complete.IsEnabled = true; cancel.IsEnabled = false; }
        }
        simple.Click += async (_, _) => await Run(false); complete.Click += async (_, _) => await Run(true);
        return panel;
    }
}
