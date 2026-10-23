using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

/// <summary>Read-only presentation of the exact edit arguments. Never reads or modifies the file.</summary>
internal sealed class SourceEditArgumentsView : Grid
{
    readonly string arguments;
    readonly SegmentedOptions mode = new() { Width = 176, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Border body = new() { CornerRadius = new(6), BorderThickness = new(1), BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card, Padding = new(8) };
    Task<Preview>? pending;
    UIElement? raw, formatted;
    bool requested;
    static string L(string fr, string en) => UiText.Resolve(fr, en);
    static TextBlock Text(string value, double size = 12) => new() { Text = value, FontSize = size,
        Foreground = FluentDesign.Primary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    sealed record Line(int? Before, int? After, string Text, string Kind, bool Header = false);
    sealed record Preview(string Path, IReadOnlyList<Line> Lines, int Added, int Removed, int Modified, int Blocks, string? Notice);

    public SourceEditArgumentsView(string arguments)
    {
        this.arguments = arguments;
        RowSpacing = 6;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Text(L("Paramètres envoyés :", "Sent parameters:"), 11);
        title.Foreground = FluentDesign.Secondary; title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        mode.SetLabels("Raw", "Formatted"); mode.SelectedIndex = 1;
        AutomationProperties.SetName(mode, L("Affichage des paramètres d’édition", "Edit parameter display"));
        Grid.SetColumn(mode, 1); header.Children.Add(mode); Children.Add(header);
        Grid.SetRow(body, 1); Children.Add(body);
        body.Child = Text(L("Ouvrez les détails pour afficher les modifications.", "Open details to display the changes."));
        mode.SelectionChanged += async (_, _) => { if (requested) await PrepareAsync(); };
    }

    // Invoked only when the tool's Details section is opened, including historical messages.
    public async Task PrepareAsync()
    {
        requested = true;
        if (mode.SelectedIndex == 0)
        {
            if (raw == null)
            {
                var text = Text(arguments); text.TextWrapping = TextWrapping.NoWrap;
                text.FontFamily = new FontFamily("Cascadia Code,Consolas,monospace");
                raw = new ScrollViewer { Content = text, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled };
            }
            body.Child = raw; return;
        }
        if (formatted != null) { body.Child = formatted; return; }
        body.Child = Text(L("Préparation de la comparaison…", "Preparing comparison…"));
        try
        {
            // Parsing JSON, unescaping code and computing the bounded Myers diff stay off the UI thread.
            var preview = await (pending ??= Task.Run(() => Compare(arguments)));
            formatted ??= Render(preview);
            if (mode.SelectedIndex == 1) body.Child = formatted;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or InvalidOperationException)
        {
            var note = new StackPanel { Spacing = 8 };
            note.Children.Add(Text(L("Comparaison indisponible pour ces paramètres. La vue Raw conserve le contenu original.",
                "Comparison unavailable for these parameters. Raw view preserves the original content.")));
            formatted = note;
            if (mode.SelectedIndex == 1) body.Child = formatted;
        }
    }

    static Preview Compare(string arguments)
    {
        // Keep malformed or oversized historical payloads reviewable through Raw without blocking the UI.
        if (arguments.Length > 4_000_000) throw new ArgumentException("Edit parameters too large.");
        var data = JsonNode.Parse(arguments) as JsonObject ?? throw new JsonException("Expected an argument object.");
        if (data["old_text"] is not JsonValue oldValue || !oldValue.TryGetValue<string>(out var before) ||
            data["new_text"] is not JsonValue newValue || !newValue.TryGetValue<string>(out var after))
            throw new JsonException("Expected old_text and new_text strings.");
        var path = data["path"]?.GetValue<string>() ?? "";
        var diff = ProposalDiff.Create(new FileProposal(path, "", Encoding.UTF8.GetBytes(before), after));
        var rows = new List<Line>(); int added = 0, removed = 0, modified = 0;
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new(hunk.BeforeStart + 1, hunk.AfterStart + 1, (hunk.Id + 1).ToString(), "header", true));
            for (int index = 0; index < hunk.Lines.Count;)
            {
                var line = hunk.Lines[index];
                if (line.Kind == ProposalLineKind.Context)
                { rows.Add(new(line.BeforeLine, line.AfterLine, line.Text, "context")); index++; continue; }
                var previous = new List<ProposalDiffLine>(); var next = new List<ProposalDiffLine>();
                while (index < hunk.Lines.Count && hunk.Lines[index].Kind != ProposalLineKind.Context)
                {
                    var change = hunk.Lines[index++];
                    (change.Kind == ProposalLineKind.Removed ? previous : next).Add(change);
                }
                int paired = Math.Min(previous.Count, next.Count);
                modified += paired; removed += previous.Count - paired; added += next.Count - paired;
                // Pair replacement lines so each orange Before row sits immediately above its After row.
                for (int i = 0; i < Math.Max(previous.Count, next.Count); i++)
                {
                    if (i < previous.Count) rows.Add(new(previous[i].BeforeLine, null, previous[i].Text, i < paired ? "modified-before" : "removed"));
                    if (i < next.Count) rows.Add(new(null, next[i].AfterLine, next[i].Text, i < paired ? "modified-after" : "added"));
                }
            }
        }
        return new(path, rows, added, removed, modified, diff.Hunks.Count,
            before == after ? "unchanged" : diff.Hunks.Count == 0 ? "line-endings" : null);
    }

    static UIElement Render(Preview preview)
    {
        var panel = new StackPanel { Spacing = 8 };
        var path = Text(preview.Path.Length == 0 ? L("Extrait à remplacer", "Replacement excerpt") : preview.Path, 13);
        path.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; panel.Children.Add(path);
        var totals = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        void Total(string value, string brush) { var label = Text(value); label.Foreground = FluentDesign.Resource(brush); totals.Children.Add(label); }
        Total(L($"+{preview.Added} ajoutées", $"+{preview.Added} added"), "DiffAddedTextBrush");
        Total(L($"~{preview.Modified} modifiées", $"~{preview.Modified} modified"), "DiffModifiedTextBrush");
        Total(L($"−{preview.Removed} supprimées", $"−{preview.Removed} removed"), "DiffRemovedTextBrush");
        panel.Children.Add(totals);
        var caption = Text(L($"{preview.Blocks} bloc(s) · Ancien / Nouveau : numéros de ligne relatifs à l’extrait envoyé.",
            $"{preview.Blocks} block(s) · Old / New: line numbers relative to the supplied excerpt."), 11);
        caption.Foreground = FluentDesign.Secondary; panel.Children.Add(caption);
        if (preview.Notice != null)
        {
            panel.Children.Add(Text(preview.Notice == "unchanged" ? L("Aucune différence textuelle.", "No text differences.") :
                L("Seules les fins de ligne diffèrent. Consultez Raw pour le contenu exact.", "Only line endings differ. See Raw for the exact content.")));
            return panel;
        }
        var rows = preview.Lines.Select(line => Row(line)).ToArray();
        var list = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
            ItemsSource = rows, ItemTemplate = (DataTemplate)XamlReader.Load(RowTemplate),
            Height = Math.Clamp(rows.Sum(row => row.HeaderVisibility == Visibility.Visible ? 38d : 22d), 66, 360), Padding = new(0) };
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}"
                HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch" Padding="0" Margin="0" MinHeight="0" />
            </ControlTemplate>
            """)));
        list.ItemContainerStyle = style;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(list, L("Modifications demandées, bloc par bloc", "Requested changes, block by block"));
        panel.Children.Add(list); return panel;
    }

    static DisplayRow Row(Line line)
    {
        var modified = line.Kind.StartsWith("modified", StringComparison.Ordinal);
        var color = modified ? "Modified" : line.Kind == "added" ? "Added" : line.Kind == "removed" ? "Removed" : null;
        return new DisplayRow
        {
            HeaderVisibility = line.Header ? Visibility.Visible : Visibility.Collapsed,
            CodeVisibility = line.Header ? Visibility.Collapsed : Visibility.Visible,
            Header = L($"Bloc {line.Text} · ancien L{line.Before} → nouveau L{line.After}", $"Block {line.Text} · old L{line.Before} → new L{line.After}"),
            BeforeLine = line.Before?.ToString() ?? "", AfterLine = line.After?.ToString() ?? "", Code = line.Text,
            Marker = line.Kind is "removed" or "modified-before" ? "−" : line.Kind is "added" or "modified-after" ? "+" : "",
            Side = modified ? line.Kind == "modified-before" ? L("Avant", "Before") : L("Après", "After") : "",
            Background = FluentDesign.Resource(color == null ? "TransparentBrush" : $"Diff{color}FillBrush"),
            Foreground = FluentDesign.Resource(color == null ? "TextFillColorPrimaryBrush" : $"Diff{color}TextBrush")
        };
    }
    public sealed class DisplayRow
    {
        public Visibility HeaderVisibility { get; init; }
        public Visibility CodeVisibility { get; init; }
        public string Header { get; init; } = "";
        public string BeforeLine { get; init; } = "";
        public string AfterLine { get; init; } = "";
        public string Code { get; init; } = "";
        public string Marker { get; init; } = "";
        public string Side { get; init; } = "";
        public Brush Background { get; init; } = FluentDesign.Card;
        public Brush Foreground { get; init; } = FluentDesign.Primary;
        public Brush Muted => FluentDesign.Secondary;
    }
    const string RowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid HorizontalAlignment="Stretch">
            <Border Visibility="{Binding HeaderVisibility}" Background="{Binding Background}" Padding="6,10">
              <TextBlock Text="{Binding Header}" Foreground="{Binding Foreground}" FontSize="12" FontWeight="SemiBold" TextWrapping="Wrap" />
            </Border>
            <Grid Visibility="{Binding CodeVisibility}" Background="{Binding Background}" Padding="4,2" MinHeight="22">
              <Grid.ColumnDefinitions><ColumnDefinition Width="44"/><ColumnDefinition Width="44"/><ColumnDefinition Width="22"/><ColumnDefinition Width="52"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBlock Text="{Binding BeforeLine}" Foreground="{Binding Muted}" HorizontalAlignment="Right" Margin="0,0,8,0" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12" />
              <TextBlock Grid.Column="1" Text="{Binding AfterLine}" Foreground="{Binding Muted}" HorizontalAlignment="Right" Margin="0,0,8,0" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12" />
              <TextBlock Grid.Column="2" Text="{Binding Marker}" Foreground="{Binding Foreground}" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12" />
              <TextBlock Grid.Column="3" Text="{Binding Side}" Foreground="{Binding Foreground}" FontSize="11" VerticalAlignment="Center" />
              <TextBlock Grid.Column="4" Text="{Binding Code}" Foreground="{Binding Foreground}" TextWrapping="NoWrap" IsTextSelectionEnabled="True" FontFamily="Cascadia Code,Consolas,monospace" FontSize="12" />
            </Grid>
          </Grid>
        </DataTemplate>
        """;
}
