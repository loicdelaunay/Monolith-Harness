using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    static readonly (string Id, string Glyph, string French, string English)[] projectIcons = [
        ("folder", "\uE8B7", "Dossier", "Folder"), ("code", "\uE943", "Code", "Code"),
        ("web", "\uE774", "Web", "Web"), ("document", "\uE8A5", "Document", "Document"),
        ("book", "\uE82D", "Livre", "Book"), ("image", "\uEB9F", "Image", "Image"),
        ("music", "\uE8D6", "Musique", "Music"), ("game", "\uE7FC", "Jeu", "Game"),
        ("star", "\uE734", "Étoile", "Star"), ("idea", "\uEA80", "Idée", "Idea"),
        ("briefcase", "\uE821", "Travail", "Work"), ("chat", "\uE8F2", "Discussion", "Chat")
    ];
    static readonly (string Value, string French, string English)[] projectColors = [
        ("", "Par défaut", "Default"), ("#94A3B8", "Gris", "Gray"), ("#F87171", "Rouge", "Red"),
        ("#FB923C", "Orange", "Orange"), ("#FBBF24", "Ambre", "Amber"), ("#A3E635", "Citron vert", "Lime"),
        ("#34D399", "Émeraude", "Emerald"), ("#22D3EE", "Cyan", "Cyan"), ("#60A5FA", "Bleu", "Blue"),
        ("#818CF8", "Indigo", "Indigo"), ("#C084FC", "Violet", "Purple"), ("#F472B6", "Rose", "Pink")
    ];
    static string ProjectGlyph(string id, bool expanded = false) => id == "folder" && expanded ? "\uE838" :
        projectIcons.FirstOrDefault(x => x.Id == id).Glyph ?? "\uE8B7";
    static Brush ProjectBrush(string color)
    {
        if (color.Length == 7 && color[0] == '#' && uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        return FluentDesign.Secondary;
    }
    (StackPanel Panel, Action<Project> Save) BuildProjectAppearance(Project owner)
    {
        var panel = new StackPanel { Spacing = 10 };
        var icons = new ComboBox { Header = WorkflowText("Icône du projet", "Project icon"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in projectIcons)
            icons.Items.Add(new ComboBoxItem { Tag = choice.Id, Content = Row(FluentDesign.Icon(choice.Glyph, 16), Label(WorkflowText(choice.French, choice.English), 14)) });
        icons.SelectedIndex = Math.Max(0, Array.FindIndex(projectIcons, x => x.Id == owner.Icon));
        var colors = new ComboBox { Header = WorkflowText("Couleur de l’icône", "Icon color"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in projectColors)
            colors.Items.Add(new ComboBoxItem { Tag = choice.Value, Content = Row(new Border { Width = 16, Height = 16, CornerRadius = new(8), Background = ProjectBrush(choice.Value) }, Label(WorkflowText(choice.French, choice.English), 14)) });
        colors.SelectedIndex = Math.Max(0, Array.FindIndex(projectColors, x => x.Value == owner.Color));
        var preview = FluentDesign.Icon(ProjectGlyph(owner.Icon), 22); preview.Foreground = ProjectBrush(owner.Color);
        void Refresh()
        {
            preview.Glyph = ProjectGlyph((icons.SelectedItem as ComboBoxItem)?.Tag as string ?? "folder");
            preview.Foreground = ProjectBrush((colors.SelectedItem as ComboBoxItem)?.Tag as string ?? "");
        }
        icons.SelectionChanged += (_, _) => Refresh(); colors.SelectionChanged += (_, _) => Refresh();
        panel.Children.Add(icons); panel.Children.Add(colors); panel.Children.Add(Row(preview, Label(WorkflowText("Aperçu", "Preview"), 12)));
        return (panel, project => {
            project.Icon = (icons.SelectedItem as ComboBoxItem)?.Tag as string ?? "folder";
            project.Color = (colors.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        });
    }
}
