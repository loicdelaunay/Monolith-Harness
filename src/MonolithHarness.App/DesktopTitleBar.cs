using Microsoft.UI;
using MonolithHarness.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Windows.Foundation;
using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static readonly (string Glyph, string French, string English)[] WorkspaceToolsChrome =
    [
        ("\uE774", "Navigateur", "Browser"), ("\uE756", "Terminal", "Terminal"),
        ("\uE943", "Git", "Git"), ("\uE8B7", "Fichiers", "Files"),
        ("\uE790", "Assets", "Assets"), ("\uE8A5", "Aperçu", "Preview")
    ];
    readonly Grid desktopTitleBar = new() { MinHeight = 32, ColumnSpacing = 6, Padding = new(0, 6, 0, 1) };
    readonly Grid titleDragRegion = new() { Background = new SolidColorBrush(Colors.Transparent), MinWidth = 40 };
    readonly StackPanel titleToolButtons = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    readonly List<Button> workspaceToolButtons = [];
    readonly ColumnDefinition titleLeftInset = new() { Width = new(0) }, titleRightInset = new() { Width = new(0) };
    Button titleToolsMenu = null!;
    Button? hoveredTitleTool;
    bool desktopTitleBarHovered;
    bool customDesktopTitleBar, layingOutTitleBar, desktopChromeClosed;

    FrameworkElement BuildDesktopTitleBar()
    {
        desktopTitleBar.Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        desktopTitleBar.ColumnDefinitions.Add(titleLeftInset);
        desktopTitleBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        desktopTitleBar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        desktopTitleBar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        desktopTitleBar.ColumnDefinitions.Add(titleRightInset);
        var brand = new Grid { ColumnSpacing = 8, Margin = new(14, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left };
        brand.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        brand.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        brandLogo.Width = brandLogo.Height = 22;
        brand.Children.Add(brandLogo);
        var name = new Viewbox { Child = brandName, MaxHeight = 28, MaxWidth = 240, Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1); brand.Children.Add(name); titleDragRegion.Children.Add(brand);
        Grid.SetColumn(titleDragRegion, 1); desktopTitleBar.Children.Add(titleDragRegion);
        var version = BuildVersionChip(); version.Margin = new(0, 0, 8, 0);
        Grid.SetColumn(version, 2); desktopTitleBar.Children.Add(version);
        for (var index = 0; index < WorkspaceToolsChrome.Length; index++)
        {
            var selected = index; var definition = WorkspaceToolsChrome[index];
            var button = ToolAction(definition.Glyph, WorkflowText(definition.French, definition.English), () => ToggleWorkspaceToolAsync(selected));
            StyleChromeAction(button); ObserveTitleToolHover(button); workspaceToolButtons.Add(button); titleToolButtons.Children.Add(button);
        }
        titleToolsMenu = ToolAction("\uE712", WorkflowText("Panneaux du chat", "Chat panels"), () => Task.CompletedTask);
        StyleChromeAction(titleToolsMenu); ObserveTitleToolHover(titleToolsMenu);
        titleToolsMenu.Click += (_, _) => BuildTitleToolsMenu().ShowAt(titleToolsMenu);
        titleToolButtons.Children.Add(titleToolsMenu);
        titleToolButtons.Margin = new(0, 0, 8, 0);
        Grid.SetColumn(titleToolButtons, 3); desktopTitleBar.Children.Add(titleToolButtons);
        desktopTitleBar.PointerEntered += (_, _) => { desktopTitleBarHovered = true; RefreshDesktopTitleBarHover(); };
        desktopTitleBar.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(desktopTitleBar).Position;
            if (point.X >= 0 && point.X < desktopTitleBar.ActualWidth && point.Y >= 0 && point.Y < desktopTitleBar.ActualHeight) return;
            desktopTitleBarHovered = false; hoveredTitleTool = null; RefreshDesktopTitleBarHover();
        };
        desktopTitleBar.PointerCanceled += (_, _) => { desktopTitleBarHovered = false; hoveredTitleTool = null; RefreshDesktopTitleBarHover(); };
        desktopTitleBar.Unloaded += (_, _) => { desktopTitleBarHovered = false; hoveredTitleTool = null; RefreshDesktopTitleBarHover(); };
        desktopTitleBar.Loaded += (_, _) => { ConfigureDesktopTitleBar(); LayoutDesktopTitleBar(); };
        desktopTitleBar.SizeChanged += (_, _) => LayoutDesktopTitleBar();
        titleToolButtons.SizeChanged += (_, _) => LayoutDesktopTitleBar();
        Closed += (_, _) => desktopChromeClosed = true;
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(LayoutDesktopTitleBar);
        desktopTitleBar.LayoutUpdated += (_, _) => UpdateTitleBarInputRegions();
        return desktopTitleBar;
    }

    void ConfigureDesktopTitleBar()
    {
        if (customDesktopTitleBar || !AppWindowTitleBar.IsCustomizationSupported()) return;
        try
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            // Only this region is draggable. Tool buttons and the version chip are siblings,
            // so native hit testing never turns them into a window drag handle.
            SetTitleBar(titleDragRegion);
#if WINDOWS
            AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
#endif
            customDesktopTitleBar = true;
            if (desktopTitleBar.XamlRoot is { } xamlRoot) xamlRoot.Changed += (_, _) => LayoutDesktopTitleBar();
            ApplyDesktopTitleBarTheme();
        }
        catch (Exception ex)
        {
            customDesktopTitleBar = false;
            SetTitleBar(null); ExtendsContentIntoTitleBar = false;
            AppLog.Write(MonolithHarness.Core.AppLogLevel.Warning, "ui.titlebar_unavailable", ex);
        }
    }

    void LayoutDesktopTitleBar()
    {
        if (desktopChromeClosed || layingOutTitleBar || desktopTitleBar.ActualWidth <= 0) return;
        layingOutTitleBar = true;
        try
        {
            var scale = desktopTitleBar.XamlRoot?.RasterizationScale ?? 1;
            var bar = AppWindow.TitleBar;
            var height = TextZoom.ForWindow(root) > 120 ? TitleBarHeightOption.Tall : TitleBarHeightOption.Standard;
            if (customDesktopTitleBar && bar.PreferredHeightOption != height) bar.PreferredHeightOption = height;
#if WINDOWS
            titleLeftInset.Width = new(customDesktopTitleBar ? bar.LeftInset / scale : 0);
            titleRightInset.Width = new(customDesktopTitleBar ? bar.RightInset / scale : 0);
            var captionWidth = titleRightInset.Width.Value;
#else
            // Uno already renders its own native-backed caption buttons. Its inset
            // API is a stub in 6.7, so measure the real built-in container instead.
            var captionWidth = customDesktopTitleBar ? UnoCaptionWidth() : 0;
            titleRightInset.Width = new(captionWidth);
#endif
            desktopTitleBar.MinHeight = Math.Max(32, customDesktopTitleBar ? bar.Height / scale : 0);
            // On platforms with system-only title bars, keep the system's single branding.
            brandLogo.Visibility = customDesktopTitleBar ? Visibility.Visible : Visibility.Collapsed;
            var available = desktopTitleBar.ActualWidth - titleLeftInset.Width.Value - captionWidth;
            var buttonWidth = Math.Max(32, workspaceToolButtons.FirstOrDefault()?.ActualWidth ?? 32) + titleToolButtons.Spacing;
            var count = Math.Clamp((int)((available - 140) / buttonWidth), 1, workspaceToolButtons.Count);
            var selected = browserVisible ? toolTabs.SelectedIndex : -1;
            var visible = Enumerable.Range(0, workspaceToolButtons.Count).OrderBy(i => i == selected ? -1 : i).Take(count).ToHashSet();
            for (var i = 0; i < workspaceToolButtons.Count; i++)
                workspaceToolButtons[i].Visibility = visible.Contains(i) ? Visibility.Visible : Visibility.Collapsed;
            brandName.Visibility = customDesktopTitleBar && available > count * buttonWidth + 200 ? Visibility.Visible : Visibility.Collapsed;
            if (versionChip != null) versionChip.Visibility = available >= 720 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { layingOutTitleBar = false; }
    }

    void ApplyDesktopTitleBarTheme()
    {
        RefreshDesktopTitleBarHover();
        if (!customDesktopTitleBar) return;
#if WINDOWS
        var bar = AppWindow.TitleBar;
        var background = ((SolidColorBrush)FluentDesign.Resource("SolidBackgroundFillColorBaseBrush")).Color;
        var text = ((SolidColorBrush)FluentDesign.Primary).Color;
        var muted = ((SolidColorBrush)FluentDesign.Secondary).Color;
        bar.BackgroundColor = bar.InactiveBackgroundColor = background;
        bar.ForegroundColor = bar.ButtonForegroundColor = text;
        bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = muted;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = background;
        bar.ButtonHoverBackgroundColor = ((SolidColorBrush)FluentDesign.Resource("ControlHoverBrush")).Color;
        bar.ButtonPressedBackgroundColor = ((SolidColorBrush)FluentDesign.Resource("ControlPressedBrush")).Color;
#endif
    }

    readonly Dictionary<NonClientRegionKind, RectInt32[]> titleInputRegions = [];
    RectInt32? titleDragBounds;
#if !WINDOWS
    FrameworkElement? systemCaptionContainer;
    double UnoCaptionWidth()
    {
        if (systemCaptionContainer == null)
        {
            for (DependencyObject? parent = root; parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent.GetType().Name != "WindowChrome") continue;
                systemCaptionContainer = ChromeElements(parent).FirstOrDefault(item => item.Name == "TitleBarMinMaxCloseContainer");
                if (systemCaptionContainer != null) systemCaptionContainer.SizeChanged += (_, _) => LayoutDesktopTitleBar();
                break;
            }
        }
        return systemCaptionContainer?.ActualWidth is > 0 ? systemCaptionContainer.ActualWidth : 138;
    }
    static IEnumerable<FrameworkElement> ChromeElements(DependencyObject parent)
    {
        if (parent is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in ChromeElements(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
#endif
    RectInt32 TitleBarBounds(FrameworkElement element)
    {
        var bounds = element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var scale = root.XamlRoot?.RasterizationScale ?? 1;
        return new RectInt32 { X = (int)Math.Round(bounds.X * scale), Y = (int)Math.Round(bounds.Y * scale),
            Width = (int)Math.Round(bounds.Width * scale), Height = (int)Math.Round(bounds.Height * scale) };
    }
    void SetTitleBarInputRegion(NonClientRegionKind kind, params RectInt32[] rectangles)
    {
        if (titleInputRegions.TryGetValue(kind, out var current) && current.SequenceEqual(rectangles)) return;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(kind, rectangles);
        titleInputRegions[kind] = rectangles;
    }
    void UpdateTitleBarInputRegions()
    {
        if (desktopChromeClosed || !customDesktopTitleBar || desktopTitleBar.ActualWidth <= 0 || root.XamlRoot == null) return;
        var drag = TitleBarBounds(titleDragRegion);
        if (drag.Width > 0 && drag.Height > 0 && !Equals(titleDragBounds, drag))
        {
            titleDragBounds = drag;
#if WINDOWS
            SetTitleBarInputRegion(NonClientRegionKind.Caption, drag);
#else
            // SetTitleBar alone ADDS our region to Uno's default whole-width drag
            // region. Explicit drag rectangles replace that default and free the tools.
            AppWindow.TitleBar.SetDragRectangles([drag]);
#endif
        }
        var interactive = new List<RectInt32> { TitleBarBounds(titleToolButtons) };
        if (versionChip?.Visibility == Visibility.Visible) interactive.Add(TitleBarBounds(versionChip));
        SetTitleBarInputRegion(NonClientRegionKind.Passthrough, interactive.ToArray());
    }

    async Task ToggleWorkspaceToolAsync(int index)
    {
        if (browserVisible && toolTabs.SelectedIndex == index) await ToggleBrowser();
        else await ShowToolAsync(index);
    }

    void RefreshWorkspaceToolChrome()
    {
        for (var i = 0; i < workspaceToolButtons.Count; i++)
        {
            var button = workspaceToolButtons[i]; var item = WorkspaceToolsChrome[i];
            var selected = browserVisible && toolTabs.SelectedIndex == i;
            var caption = WorkflowText(item.French, item.English);
            AutomationProperties.SetName(button, caption);
            AutomationProperties.SetHelpText(button, selected ? WorkflowText("Panneau ouvert · cliquer pour masquer", "Panel open · click to hide") : WorkflowText("Ouvrir le panneau", "Open panel"));
            ToolTipService.SetToolTip(button, caption);
        }
        var options = WorkflowText("Panneaux du chat", "Chat panels");
        if (titleToolsMenu != null) { AutomationProperties.SetName(titleToolsMenu, options); ToolTipService.SetToolTip(titleToolsMenu, options); }
        RefreshDesktopTitleBarHover();
        LayoutDesktopTitleBar();
    }

    void ObserveTitleToolHover(Button button)
    {
        button.PointerEntered += (_, _) => { hoveredTitleTool = button; desktopTitleBarHovered = true; RefreshDesktopTitleBarHover(); };
        void Leave()
        {
            if (!ReferenceEquals(hoveredTitleTool, button)) return;
            hoveredTitleTool = null; RefreshDesktopTitleBarHover();
        }
        button.PointerExited += (_, _) => Leave();
        button.PointerCanceled += (_, _) => Leave();
    }

    void RefreshDesktopTitleBarHover()
    {
        desktopTitleBar.Background = FluentDesign.Resource(desktopTitleBarHovered ? "TitleBarHoverBrush" : "SolidBackgroundFillColorBaseBrush");
        void Paint(Button button, bool selected)
        {
            var hovered = ReferenceEquals(hoveredTitleTool, button);
            var hoverBrush = FluentDesign.Resource(selected ? "ControlSelectedHoverBrush" : "ControlHoverBrush");
            button.Background = hovered ? hoverBrush : FluentDesign.Resource(selected ? "ControlSelectedBrush" : "TransparentBrush");
            button.Foreground = selected ? FluentDesign.Resource("AccentTextFillColorPrimaryBrush") : hovered ? FluentDesign.Primary : FluentDesign.Secondary;
            button.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
            button.Resources["ButtonForegroundPointerOver"] = selected ? FluentDesign.Resource("AccentTextFillColorPrimaryBrush") : FluentDesign.Primary;
            button.Resources["ButtonBackgroundPressed"] = FluentDesign.Resource("ControlPressedBrush");
        }
        for (var i = 0; i < workspaceToolButtons.Count; i++) Paint(workspaceToolButtons[i], browserVisible && toolTabs.SelectedIndex == i);
        if (titleToolsMenu != null) Paint(titleToolsMenu, false);
    }

    MenuFlyout BuildTitleToolsMenu()
    {
        var menu = new MenuFlyout();
        AddWorkspaceToolMenuItems(menu.Items);
        var hide = new MenuFlyoutItem { Text = WorkflowText("Revenir au chat", "Return to chat"), Icon = FluentDesign.Icon("\uE8F2"), IsEnabled = browserVisible };
        hide.Click += async (_, _) => await Guard(ToggleBrowser); menu.Items.Add(hide);
        return menu;
    }

    void AddWorkspaceToolMenuItems(IList<MenuFlyoutItemBase> items)
    {
        for (var i = 0; i < WorkspaceToolsChrome.Length; i++)
        {
            var index = i; var tool = WorkspaceToolsChrome[i];
            var item = new ToggleMenuFlyoutItem { Text = WorkflowText(tool.French, tool.English), Icon = FluentDesign.Icon(tool.Glyph), IsChecked = browserVisible && toolTabs.SelectedIndex == i };
            item.Click += async (_, _) => await Guard(() => ToggleWorkspaceToolAsync(index)); items.Add(item);
        }
    }
}
