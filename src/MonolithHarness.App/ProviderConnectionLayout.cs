using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

// The list and details own their scroll areas; the settings footer stays outside them.
internal sealed class ProviderConnectionLayout : Grid
{
    readonly Grid workspace = new() { ColumnSpacing = 12 };
    readonly Border listSurface, detailSurface;
    readonly ScrollViewer detailScroll;
    readonly ToggleButton[] tabs;
    readonly Button back = new();
    readonly TextBlock empty;
    readonly Grid details = new() { RowSpacing = 16 };
    bool showList, hasSelection;
    public StackPanel Rows { get; } = new() { Spacing = 4 };
    public TextBox Search { get; } = new() { Name = "ProviderConnectionSearch", MaxLength = 160,
        FontSize = 13, PlaceholderText = UiText.Resolve("Rechercher…", "Search…") };
    public DropDownButton AddConnection { get; } = new() { Name = "AddProvider", Content = UiText.Resolve("+ Ajouter", "+ Add"),
        MinWidth = 0, Padding = new(10, 7, 10, 7) };
    public TextBlock Title { get; } = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary };
    public TextBlock Description { get; } = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    public ContentControl Logo { get; } = new() { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center, IsTabStop = false };
    public Button More { get; } = new() { Name = "ProviderConnectionActions", MinWidth = 32, Padding = new(6) };
    public StackPanel Connection { get; } = new() { Spacing = 16 };
    public StackPanel Models { get; } = new() { Spacing = 14, Visibility = Visibility.Collapsed };
    public StackPanel Advanced { get; } = new() { Spacing = 14, Visibility = Visibility.Collapsed };
    public int SelectedPage { get; private set; }
    public event Action<int>? PageChanged;

    public ProviderConnectionLayout()
    {
        RowSpacing = 12;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Children.Add(new TextBlock { Text = UiText.Resolve("Vos connexions et leurs modèles.", "Your connections and their models."),
            Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        Grid.SetRow(workspace, 1); Children.Add(workspace);
        workspace.ColumnDefinitions.Add(new() { Width = new(268) });
        workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var list = new Grid { RowSpacing = 12 };
        list.RowDefinitions.Add(new() { Height = GridLength.Auto });
        list.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var tools = new Grid { ColumnSpacing = 8 };
        tools.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var searchHost = new Grid();
        Search.Padding = new(30, 7, 8, 7);
        var searchIcon = FluentDesign.Icon("\uE721", 14); searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.Margin = new(10, 0, 0, 0); searchIcon.IsHitTestVisible = false; searchIcon.Foreground = FluentDesign.Secondary;
        searchHost.Children.Add(Search); searchHost.Children.Add(searchIcon);
        tools.Children.Add(searchHost); Grid.SetColumn(AddConnection, 1); tools.Children.Add(AddConnection);
        AutomationProperties.SetName(Search, UiText.Resolve("Rechercher une connexion", "Search connections"));
        list.Children.Add(tools);
        var listScroll = new ScrollViewer { Content = Rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(listScroll, 1); list.Children.Add(listScroll);
        listSurface = FluentDesign.Surface(list, 12);
        workspace.Children.Add(listSurface);
        details.RowDefinitions.Add(new() { Height = GridLength.Auto });
        details.RowDefinitions.Add(new() { Height = GridLength.Auto });
        details.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var identity = new Grid { ColumnSpacing = 12 };
        identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.Children.Add(Logo);
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(Title); copy.Children.Add(Description); Grid.SetColumn(copy, 1); identity.Children.Add(copy);
        FluentDesign.IconButton(More, "\uE712", UiText.Resolve("Actions de la connexion", "Connection actions"), false);
        Grid.SetColumn(More, 2); identity.Children.Add(More);
        var top = new StackPanel { Spacing = 12 };
        FluentDesign.IconButton(back, "\uE72B", UiText.Resolve("Connexions", "Connections"));
        back.Name = "BackToProviders"; back.Visibility = Visibility.Collapsed;
        back.Click += (_, _) => { showList = true; Reflow(); Search.Focus(FocusState.Programmatic); };
        top.Children.Add(back); top.Children.Add(identity); details.Children.Add(top);
        var tabBar = new Grid();
        for (var i = 0; i < 3; i++) tabBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        string[] labels = [UiText.Resolve("Connexion", "Connection"), UiText.Resolve("Modèles", "Models"), UiText.Resolve("Avancé", "Advanced")];
        tabs = new ToggleButton[3];
        for (var i = 0; i < tabs.Length; i++)
        {
            var page = i;
            var tab = new ToggleButton { Name = "ProviderTab" + i, Content = labels[i], MinWidth = 0,
                Padding = new(6, 10, 6, 10), CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0), FontSize = 14 };
            AutomationProperties.SetName(tab, labels[i]);
            foreach (var suffix in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                tab.Resources["ToggleButtonBackground" + suffix] = FluentDesign.Resource("TransparentBrush");
                tab.Resources["ToggleButtonForeground" + suffix] = FluentDesign.Primary;
                tab.Resources["ToggleButtonBorderBrush" + suffix] = FluentDesign.Resource("AccentFillColorDefaultBrush");
            }
            tab.Checked += (_, _) => { if (SelectedPage != page) ShowPage(page); };
            tab.Unchecked += (_, _) => { if (SelectedPage == page) tab.IsChecked = true; };
            tab.Click += (_, _) => ShowPage(page);
            tabs[i] = tab; Grid.SetColumn(tab, i); tabBar.Children.Add(tab);
        }
        tabBar.PreviewKeyDown += (_, e) =>
        {
            var next = e.Key switch
            {
                Windows.System.VirtualKey.Left => Math.Max(0, SelectedPage - 1),
                Windows.System.VirtualKey.Right => Math.Min(2, SelectedPage + 1),
                Windows.System.VirtualKey.Home => 0, Windows.System.VirtualKey.End => 2, _ => -1
            };
            if (next < 0) return;
            ShowPage(next); tabs[next].Focus(FocusState.Keyboard); e.Handled = true;
        };
        Grid.SetRow(tabBar, 1); details.Children.Add(tabBar);
        var pages = new Grid(); pages.Children.Add(Connection); pages.Children.Add(Models); pages.Children.Add(Advanced);
        detailScroll = new ScrollViewer { Content = pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 4, 8) };
        Grid.SetRow(detailScroll, 2); details.Children.Add(detailScroll);
        empty = new TextBlock { Text = UiText.Resolve("Sélectionnez une connexion ou ajoutez un fournisseur.", "Select a connection or add a provider."),
            Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new(20), MaxWidth = 320 };
        var detailHost = new Grid(); detailHost.Children.Add(empty); detailHost.Children.Add(details);
        detailSurface = FluentDesign.Surface(detailHost, 20); Grid.SetColumn(detailSurface, 1); workspace.Children.Add(detailSurface);
        SizeChanged += (_, _) => Reflow();
        ShowPage(0); ShowSelection(false);
    }
    public void ShowPage(int page)
    {
        SelectedPage = Math.Clamp(page, 0, 2);
        Connection.Visibility = SelectedPage == 0 ? Visibility.Visible : Visibility.Collapsed;
        Models.Visibility = SelectedPage == 1 ? Visibility.Visible : Visibility.Collapsed;
        Advanced.Visibility = SelectedPage == 2 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < tabs.Length; i++)
        {
            tabs[i].IsChecked = i == SelectedPage;
            tabs[i].Foreground = i == SelectedPage ? FluentDesign.Primary : FluentDesign.Secondary;
            tabs[i].BorderBrush = FluentDesign.Resource(i == SelectedPage ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
            tabs[i].BorderThickness = new(0, 0, 0, i == SelectedPage ? 2 : 1);
            tabs[i].FontWeight = i == SelectedPage ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
        detailScroll?.ChangeView(null, 0, null, true); PageChanged?.Invoke(SelectedPage);
    }
    public void ShowSelection(bool selected)
    {
        hasSelection = selected; showList = false;
        details.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        empty.Visibility = selected ? Visibility.Collapsed : Visibility.Visible; Reflow();
    }
    void Reflow()
    {
        var compact = ActualWidth > 0 && ActualWidth < 720;
        var listOnly = !hasSelection || showList;
        workspace.ColumnSpacing = compact ? 0 : 12;
        workspace.ColumnDefinitions[0].Width = compact ? new(listOnly ? 1 : 0, GridUnitType.Star) : new(Math.Clamp(ActualWidth * .34, 240, 320));
        workspace.ColumnDefinitions[1].Width = compact ? new(listOnly ? 0 : 1, GridUnitType.Star) : new(1, GridUnitType.Star);
        listSurface.Visibility = compact && !listOnly ? Visibility.Collapsed : Visibility.Visible;
        detailSurface.Visibility = compact && listOnly ? Visibility.Collapsed : Visibility.Visible;
        back.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }
}
