using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class DiagnosticsWindow
{
    readonly StackPanel storageRows = new() { Spacing = 8 };
    readonly TextBlock storageSummary = Text(""), storageNotice = Text("", 12);
    Button storageRefresh = null!;
    bool storageMeasuring, storageMeasured;
    static string Size(long bytes) => bytes >= 1073741824 ? (bytes / 1073741824d).ToString("0.00") + " GB" :
        bytes >= 1048576 ? (bytes / 1048576d).ToString("0.0") + " MB" : bytes >= 1024 ? (bytes / 1024d).ToString("0.0") + " KB" : bytes + " B";
    UIElement BuildStorageTab()
    {
        storageRefresh = new Button { Content = L("Analyser le stockage", "Scan storage"), HorizontalAlignment = HorizontalAlignment.Right };
        storageRefresh.Click += async (_, _) => await RefreshStorageAsync();
        var panel = Stack(Text(L("Stockage des données de l’application", "Application data storage"), 20),
            Text(L("Analyse à l’ouverture de cet onglet ou sur demande. Les dossiers des projets externes et les liens ne sont pas parcourus. Les pièces jointes conservées dans SQLite sont comptées dans la base de données.",
                "Scans when this tab opens or on demand. External project folders and links are not traversed. Attachments stored in SQLite are included in the database category."), 12),
            storageSummary, storageRows, storageNotice, storageRefresh);
        return Scroll(panel);
    }
    async Task RefreshStorageAsync()
    {
        if (closed || storageMeasuring) return;
        storageMeasuring = true; storageRefresh.IsEnabled = false;
        storageNotice.Text = L("Calcul en cours…", "Calculating…");
        try
        {
            var snapshot = await StorageUsage.MeasureAsync(database, lifetime.Token);
            if (closed) return;
            storageRows.Children.Clear();
            foreach (var row in snapshot.Rows)
            {
                var name = row.Category switch
                {
                    StorageCategory.Database => L("Base de données et journal SQLite", "Database and SQLite journal"),
                    StorageCategory.Models => L("Modèles locaux", "Local models"),
                    StorageCategory.Runtimes => L("Environnements d’exécution", "Runtimes"),
                    StorageCategory.Browser => L("Profils et caches des navigateurs", "Browser profiles and caches"),
                    StorageCategory.Attachments => L("Images, captures et assets", "Images, captures and assets"),
                    StorageCategory.Workspaces => L("Espaces de travail, sandbox et propositions", "Workspaces, sandbox and proposals"),
                    StorageCategory.Skills => L("Skills et scripts", "Skills and scripts"),
                    StorageCategory.Logs => L("Journaux", "Logs"),
                    StorageCategory.Cache => L("Fichiers temporaires", "Temporary files"),
                    _ => L("Autres données et configuration", "Other data and configuration")
                };
                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                grid.Children.Add(Text(name));
                var value = Text(Size(row.Bytes) + " · " + row.Files.ToString("N0") + L(" fichiers", " files"));
                Grid.SetColumn(value, 1); grid.Children.Add(value);
                storageRows.Children.Add(FluentDesign.Surface(grid, 12));
            }
            storageSummary.Text = L("Total des données : ", "Total data: ") + Size(snapshot.TotalBytes) + "\n" + snapshot.Root;
            storageNotice.Text = L("Mesuré à ", "Measured at ") + snapshot.MeasuredUtc.ToLocalTime().ToString("T") +
                (snapshot.Skipped == 0 ? "" : " · " + snapshot.Skipped + L(" éléments ignorés (liens ou accès indisponible) : total partiel.", " skipped items (links or inaccessible): partial total."));
            storageMeasured = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) storageNotice.Text = ToolAuthorization.Clean(ex.Message); }
        finally { storageMeasuring = false; if (!closed) storageRefresh.IsEnabled = true; }
    }
}
