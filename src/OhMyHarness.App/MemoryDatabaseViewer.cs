using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    Window? memoryDatabaseWindow;

    async Task ShowMemoryDatabaseAsync()
    {
        if (memoryDatabaseWindow is { } existing) { existing.Activate(); return; }
        var window = new Window { Title = DisplayApplicationName + " · " + WorkflowText("Mémoire · Vue de la base", "Memory · Database viewer") };
        memoryDatabaseWindow = window;
        var panel = new Grid { Padding = new Thickness(24), RowSpacing = 10, RequestedTheme = root.RequestedTheme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(210), GridLength.Auto, GridLength.Auto }) panel.RowDefinitions.Add(new() { Height = height });
        var title = Label(WorkflowText("Mémoire · Vue de la base", "Memory · Database viewer"), 25);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        panel.Children.Add(title);
        var hint = Label(WorkflowText("Toutes les mémoires de la base, tous projets et conversations confondus. Filtres combinés : texte contient, nombres exacts (ou NULL), dates AAAA-MM-JJ en UTC.", "All database memories across projects and conversations. Combined filters: text contains, exact numbers (or NULL), UTC dates YYYY-MM-DD."), 12);
        Grid.SetRow(hint, 1); panel.Children.Add(hint);
        var sort = new ComboBox { ItemsSource = MemoryBrowser.Columns, SelectedIndex = 0, MinWidth = 160 };
        var descending = new CheckBox { Content = WorkflowText("Décroissant", "Descending"), IsChecked = true };
        var progress = new ProgressBar { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
        Grid.SetRow(progress, 3); panel.Children.Add(progress);
        var details = new StackPanel { Spacing = 8 };
        var detailsScroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detailsBorder = FluentDesign.Surface(detailsScroll, 12); Grid.SetRow(detailsBorder, 5); panel.Children.Add(detailsBorder);
        var status = Label("", 12);
        var filters = new Dictionary<string, TextBox>();
        var columns = MemoryBrowser.Columns.ToArray();
        double Width(string column) => column switch { "Content" => 320, "Title" => 240, "Key" or "Tags" or "Partition" => 180, "CreatedUtc" or "UpdatedUtc" => 180, "Id" or "Version" => 65, _ => 115 };
        Grid Cells()
        {
            var grid = new Grid();
            foreach (var column in columns) grid.ColumnDefinitions.Add(new() { Width = new(Width(column)) });
            return grid;
        }
        var table = new Grid { Width = columns.Sum(Width) };
        table.RowDefinitions.Add(new() { Height = GridLength.Auto }); table.RowDefinitions.Add(new() { Height = GridLength.Auto }); table.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var headings = Cells(); var filterRow = Cells();
        for (int i = 0; i < columns.Length; i++)
        {
            var column = columns[i]; var heading = Label(column, 12); heading.Margin = new(6); Grid.SetColumn(heading, i); headings.Children.Add(heading);
            var filter = new TextBox { PlaceholderText = column.EndsWith("Utc") ? "AAAA-MM-JJ" : WorkflowText("Filtrer…", "Filter…"), Margin = new(3), MaxLength = 200 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(filter, WorkflowText("Filtrer ", "Filter ") + column);
            filters[column] = filter; Grid.SetColumn(filter, i); filterRow.Children.Add(filter);
        }
        var rows = new ListView { SelectionMode = ListViewSelectionMode.Single };
        rows.ItemContainerStyle = new Style(typeof(ListViewItem));
        rows.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        rows.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        table.Children.Add(headings); Grid.SetRow(filterRow, 1); table.Children.Add(filterRow); Grid.SetRow(rows, 2); table.Children.Add(rows);
        var horizontal = new ScrollViewer { Content = table, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(horizontal, 4); panel.Children.Add(horizontal);
        MemoryEntry? selected = null;
        rows.SelectionChanged += (_, _) =>
        {
            selected = (rows.SelectedItem as FrameworkElement)?.Tag as MemoryEntry;
            details.Children.Clear();
            if (selected == null) { details.Children.Add(Label(WorkflowText("Sélectionnez une ligne pour voir tous ses champs.", "Select a row to inspect every field."), 13)); return; }
            foreach (var column in columns)
            {
                details.Children.Add(Label(column, 11));
                details.Children.Add(new TextBlock { Text = MemoryBrowser.Cell(selected, column), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 14 });
            }
            detailsScroll.ChangeView(null, 0, null);
        };
        var previous = new Button { Content = WorkflowText("Précédent", "Previous"), IsEnabled = false };
        var next = new Button { Content = WorkflowText("Suivant", "Next"), IsEnabled = false };
        int offset = 0, revision = 0; bool closed = false, resetting = false;
        CancellationTokenSource? pending = null;
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        async Task Load(bool reset = false)
        {
            if (closed) return;
            if (reset) offset = 0;
            pending?.Cancel(); var cancellation = new CancellationTokenSource(); pending = cancellation; int stamp = ++revision;
            progress.Visibility = Visibility.Visible; previous.IsEnabled = next.IsEnabled = false;
            var filterValues = filters.ToDictionary(x => x.Key, x => x.Value.Text);
            var sortColumn = sort.SelectedItem as string ?? "Id"; var reverse = descending.IsChecked == true;
            try
            {
                var page = await MemoryBrowser.ReadAsync(HarnessDb.DatabasePath, filterValues, sortColumn, reverse, offset, cancellation.Token);
                if (closed || stamp != revision) return;
                offset = page.Offset; rows.Items.Clear(); details.Children.Clear(); selected = null;
                details.Children.Add(Label(WorkflowText("Sélectionnez une ligne pour voir tous ses champs.", "Select a row to inspect every field."), 13));
                foreach (var entry in page.Rows)
                {
                    var line = Cells(); line.Tag = entry;
                    for (int i = 0; i < columns.Length; i++)
                    {
                        var value = new TextBlock { Text = MemoryBrowser.Cell(entry, columns[i]).Replace('\r', ' ').Replace('\n', ' '), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(6, 8, 6, 8) };
                        Grid.SetColumn(value, i); line.Children.Add(value);
                    }
                    rows.Items.Add(line);
                }
                status.Text = WorkflowText($"{page.Matching} résultat(s) / {page.Total} ligne(s) · {offset + (page.Rows.Count > 0 ? 1 : 0)}–{offset + page.Rows.Count}", $"{page.Matching} matches / {page.Total} rows · {offset + (page.Rows.Count > 0 ? 1 : 0)}–{offset + page.Rows.Count}");
                previous.IsEnabled = offset > 0; next.IsEnabled = offset + page.Rows.Count < page.Matching;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { if (!closed && stamp == revision) { rows.Items.Clear(); details.Children.Clear(); selected = null; status.Text = ex.Message; } }
            finally { if (!closed && stamp == revision) progress.Visibility = Visibility.Collapsed; if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); }
        }
        var refresh = Action(WorkflowText("Actualiser", "Refresh"), () => Load());
        var reset = Action(WorkflowText("Effacer les filtres", "Clear filters"), async () => { resetting = true; foreach (var filter in filters.Values) filter.Text = ""; resetting = false; debounce.Stop(); await Load(true); });
        var controls = Row(Label(WorkflowText("Trier par", "Sort by"), 13), sort, descending, refresh, reset); Grid.SetRow(controls, 2); panel.Children.Add(controls);
        previous.Click += async (_, _) => { offset = Math.Max(0, offset - MemoryBrowser.PageSize); await Load(); };
        next.Click += async (_, _) => { offset += MemoryBrowser.PageSize; await Load(); };
        void Schedule() { if (!resetting && !closed) { debounce.Stop(); debounce.Start(); } }
        foreach (var filter in filters.Values) filter.TextChanged += (_, _) => Schedule();
        sort.SelectionChanged += (_, _) => Schedule(); descending.Checked += (_, _) => Schedule(); descending.Unchecked += (_, _) => Schedule();
        debounce.Tick += async (_, _) => { debounce.Stop(); await Load(true); };
        var copy = Action(WorkflowText("Copier la ligne", "Copy row"), () => { if (selected != null) { var package = new DataPackage(); package.SetText(System.Text.Json.JsonSerializer.Serialize(selected, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); Clipboard.SetContent(package); } return Task.CompletedTask; });
        var footer = new Grid(); footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Children.Add(status);
        var actions = Row(copy, previous, next); Grid.SetColumn(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 6); panel.Children.Add(footer);
        var close = new Button { Content = WorkflowText("Fermer", "Close"), Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 140 };
        close.Click += (_, _) => window.Close();
        var actionBar = Row(close); actionBar.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(actionBar, 7); panel.Children.Add(actionBar);
        window.Closed += (_, _) =>
        {
            closed = true; revision++; debounce.Stop(); pending?.Cancel();
            if (ReferenceEquals(memoryDatabaseWindow, window)) memoryDatabaseWindow = null;
        };
        window.Content = panel;
        ObserveTextZoom(panel);
        FluentDesign.WindowChrome(window);
        ApplyBrandingIcon(window);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1340, Height = 900 });
        window.Activate();
        await Load();
    }
}
