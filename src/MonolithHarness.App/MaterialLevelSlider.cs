using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MonolithHarness.App;

/// <summary>Discrete Material-inspired visuals over the native Slider input and automation.</summary>
internal sealed class MaterialLevelSlider : Slider
{
    readonly Border activeTrack = new() { Height = 16, CornerRadius = new(8, 2, 2, 8), IsHitTestVisible = false };
    readonly Border inactiveTrack = new() { Height = 16, CornerRadius = new(2, 8, 8, 2), IsHitTestVisible = false };
    readonly Border stateLayer = new() { Width = 40, Height = 48, CornerRadius = new(20), Opacity = 0, IsHitTestVisible = false };
    readonly List<Border> stops = [];
    Canvas? surface;
    Border? focusOutline, handleBar;
    bool pressed, hovered;

    public MaterialLevelSlider()
    {
        Style = (Style)Application.Current.Resources["MaterialLevelSliderStyle"];
        Foreground = FluentDesign.Resource("AccentFillColorDefaultBrush");
        activeTrack.Background = stateLayer.Background = Foreground;
        inactiveTrack.Background = FluentDesign.Resource("ControlSelectedBrush");
        SizeChanged += (_, _) => DrawTrack();
        ValueChanged += (_, _) => DrawTrack();
        foreach (var property in new[] { MinimumProperty, MaximumProperty, IntermediateValueProperty, StepFrequencyProperty, IsEnabledProperty })
            RegisterPropertyChangedCallback(property, (_, _) => DrawTrack());
        PointerEntered += (_, _) => { hovered = true; DrawTrack(); };
        PointerExited += (_, _) => { hovered = false; DrawTrack(); };
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { pressed = IsEnabled; DrawTrack(); }), true);
        void Release() { pressed = false; DrawTrack(); }
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => Release()), true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => Release()), true);
        GotFocus += (_, _) => DrawTrack();
        LostFocus += (_, _) => DrawTrack();
        Unloaded += (_, _) => { pressed = hovered = false; };
        foreach (var decoration in new[] { activeTrack, inactiveTrack, stateLayer })
            AutomationProperties.SetAccessibilityView(decoration, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        surface = GetTemplateChild("TrackSurface") as Canvas;
        focusOutline = GetTemplateChild("FocusOutline") as Border;
        handleBar = GetTemplateChild("HorizontalThumb") is DependencyObject thumb ? FindHandle(thumb) : null;
        if (surface == null) return;
        surface.Children.Clear(); stops.Clear();
        surface.Children.Add(activeTrack); surface.Children.Add(inactiveTrack); surface.Children.Add(stateLayer);
        DrawTrack();
    }
    static Border? FindHandle(DependencyObject parent)
    {
        if (parent is Border { Name: "HandleBar" } bar) return bar;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindHandle(VisualTreeHelper.GetChild(parent, i)) is { } match) return match;
        return null;
    }
    void DrawTrack()
    {
        if (surface == null || ActualWidth <= 44) return;
        const double inset = 22, gap = 8;
        var width = ActualWidth - inset * 2;
        var range = Maximum - Minimum;
        var fraction = range > 0 ? Math.Clamp((IntermediateValue - Minimum) / range, 0, 1) : 0;
        if (IsDirectionReversed) fraction = 1 - fraction;
        var center = inset + width * fraction;
        var middle = (surface.ActualHeight > 0 ? surface.ActualHeight : ActualHeight) / 2;
        // Same 44-pixel thumb geometry as the native template: hit testing and drawing agree.
        activeTrack.Width = Math.Max(0, center - inset - gap);
        inactiveTrack.Width = Math.Max(0, ActualWidth - inset - center - gap);
        Canvas.SetLeft(activeTrack, inset); Canvas.SetTop(activeTrack, middle - 8);
        Canvas.SetLeft(inactiveTrack, center + gap); Canvas.SetTop(inactiveTrack, middle - 8);
        Canvas.SetLeft(stateLayer, center - stateLayer.Width / 2); Canvas.SetTop(stateLayer, middle - stateLayer.Height / 2);
        stateLayer.Opacity = !IsEnabled ? 0 : pressed ? .13 : hovered || FocusState == FocusState.Keyboard ? .08 : 0;
        activeTrack.Opacity = inactiveTrack.Opacity = IsEnabled ? 1 : .38;
        if (focusOutline != null) focusOutline.Visibility = IsEnabled && FocusState == FocusState.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        // The narrow handle changes shape on press without changing native pointer geometry.
        if (handleBar == null && GetTemplateChild("HorizontalThumb") is DependencyObject thumb) handleBar = FindHandle(thumb);
        if (handleBar != null) { handleBar.Width = pressed ? 2 : 4; handleBar.Opacity = IsEnabled ? 1 : .38; }
        var count = range > 0 && StepFrequency > 0 ? Math.Min(10, (int)Math.Round(range / StepFrequency) + 1) : 1;
        while (stops.Count < count)
        {
            var stop = new Border { Width = 4, Height = 4, CornerRadius = new(2), IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(stop, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            stops.Add(stop); surface.Children.Add(stop);
        }
        while (stops.Count > count) { surface.Children.Remove(stops[^1]); stops.RemoveAt(stops.Count - 1); }
        for (var i = 0; i < stops.Count; i++)
        {
            var x = inset + (count > 1 ? width * i / (count - 1) : 0);
            var stop = stops[i];
            stop.Visibility = Math.Abs(x - center) < gap + 2 ? Visibility.Collapsed : Visibility.Visible;
            stop.Background = x < center ? FluentDesign.Resource("TextOnAccentFillColorPrimaryBrush") : Foreground;
            stop.Opacity = IsEnabled ? .8 : .3;
            Canvas.SetLeft(stop, x - 2); Canvas.SetTop(stop, middle - 2);
        }
    }
}
