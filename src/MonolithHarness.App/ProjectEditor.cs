using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.Storage.Pickers;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    // Creation and management share their fields, validation and saved project defaults.
    async Task<ContentDialogResult> ShowProjectEditorAsync(Project owner, bool creating)
    {
        var name = new TextBox { Text = owner.Name, Header = T("Nom du projet"), MaxLength = 120 };
        var appearance = BuildProjectAppearance(owner);
        var folders = new TextBox { Header = WorkflowText("Dossiers par défaut · un par ligne", "Default folders · one per line"),
            Text = string.Join('\n', owner.GetSourceFolders()), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 180 };
        ScrollViewer.SetVerticalScrollBarVisibility(folders, ScrollBarVisibility.Auto);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"),
            IsTextSelectionEnabled = true, Visibility = Visibility.Collapsed };
        var permissionText = new TextBlock { Text = owner.PermissionProfileJson, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var imported = owner.PermissionProfileJson;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(name); panel.Children.Add(error); panel.Children.Add(appearance.Panel); panel.Children.Add(folders);
        var scroll = new ScrollViewer { Content = panel, MaxHeight = 560, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = T(creating ? "Créer un projet" : "Gérer le projet"),
            Content = scroll, PrimaryButtonText = creating ? WorkflowText("Créer", "Create") : T("Enregistrer"),
            SecondaryButtonText = creating ? "" : T("Supprimer…"), CloseButtonText = T("Annuler"), DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text) };
        bool busy = false;
        void ShowError(Exception exception)
        {
            error.Text = exception.Message; error.Visibility = Visibility.Visible; scroll.ChangeView(null, 0, null);
        }
        void Changed()
        {
            error.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = !busy && !string.IsNullOrWhiteSpace(name.Text);
        }
        async Task EditAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true; dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false;
            try { await action(); error.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; dialog.IsSecondaryButtonEnabled = true; dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text); }
        }
        panel.Children.Add(Action(WorkflowText("Ajouter un dossier", "Add folder"), () => EditAsync(async () =>
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker, this);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) folders.Text = string.Join('\n', ProjectResources.FolderLines(folders.Text).Append(folder.Path).Distinct(PlatformSupport.PathComparer));
        })));
        panel.Children.Add(Label(WorkflowText("Les conversations héritent de ces dossiers tant qu’elles n’ont pas de ressources personnalisées. AGENTS.md et Agent.md sont chargés automatiquement.",
            "Chats inherit these folders until their resources are customized. AGENTS.md and Agent.md load automatically."), 12));
        panel.Children.Add(Action(WorkflowText("Lire permission.json", "Read permission.json"), () => EditAsync(async () =>
        {
            var draft = new Project(); draft.SetSourceFolders(ProjectResources.Validate(ProjectResources.FolderLines(folders.Text)));
            imported = await ProjectResources.ReadPermissionsAsync(draft);
            permissionText.Text = string.IsNullOrEmpty(imported) ? WorkflowText("Aucune règle trouvée.", "No rules found.") : imported;
        })));
        panel.Children.Add(new ScrollViewer { Content = permissionText, MaxHeight = 160 });
        panel.Children.Add(Label(WorkflowText("Enregistrer applique les règles affichées (allow / ask / deny). Les modifications ultérieures du fichier nécessitent une nouvelle importation. Refuser tout reste prioritaire.",
            "Save applies the displayed rules (allow / ask / deny). Later file changes need a new import. Deny all takes priority."), 12));
        panel.Children.Add(Action(WorkflowText("Retirer les règles du projet", "Clear project rules"), () =>
        {
            if (!busy) { imported = ""; permissionText.Text = ""; } return Task.CompletedTask;
        }));
        name.TextChanged += (_, _) => Changed(); folders.TextChanged += (_, _) => Changed();
        dialog.Closing += (_, args) => { if (busy) args.Cancel = true; };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException(WorkflowText("Renseignez le nom du projet.", "Enter the project name."));
                var paths = ProjectResources.Validate(ProjectResources.FolderLines(folders.Text));
                var missing = paths.FirstOrDefault(path => !Directory.Exists(path));
                if (missing != null) throw new ArgumentException(WorkflowText("Choisissez uniquement des dossiers : ", "Choose folders only: ") + missing);
                owner.Name = name.Text.Trim(); appearance.Save(owner); owner.SetSourceFolders(paths); owner.PermissionProfileJson = imported;
            }
            catch (Exception ex) { args.Cancel = true; ShowError(ex); }
        };
        return await ShowDialogAsync(dialog);
    }
}
