using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed record HoverPopoverTarget(FrameworkElement Anchor, Flyout Flyout, Action Open);
    readonly List<HoverPopoverTarget> hoverPopoverTargets = [];
    Flyout? activeHoverPopover;
    bool hoverPointerAttached;

    static bool PopoverContainsPointer(PointerRoutedEventArgs e, FrameworkElement element)
    {
        if (!element.IsLoaded || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var point = e.GetCurrentPoint(element).Position;
        return point.X >= 0 && point.Y >= 0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }
    void SwitchHoverPopover(PointerRoutedEventArgs e)
    {
        if (activeHoverPopover == null) return;
        foreach (var target in hoverPopoverTargets)
        {
            if (ReferenceEquals(target.Flyout, activeHoverPopover) || !PopoverContainsPointer(e, target.Anchor)) continue;
            target.Open(); return;
        }
    }
    void AttachHoverPopover(FrameworkElement anchor, FrameworkElement body, Flyout flyout, Action update)
    {
        flyout.ShowMode = FlyoutShowMode.Transient;
        flyout.OverlayInputPassThroughElement = root;
        flyout.Placement = FlyoutPlacementMode.Top;
        body = new Border { Child = body, Padding = new Thickness(12), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        flyout.Content = body;
        flyout.FlyoutPresenterStyle = new Style { TargetType = typeof(FlyoutPresenter) };
        flyout.FlyoutPresenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        bool opened = false;
        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Open()
        {
            dismiss.Stop();
            if (opened && ReferenceEquals(activeHoverPopover, flyout)) return;
            var previous = activeHoverPopover;
            activeHoverPopover = flyout; opened = true;
            // Close the sibling before showing this popup; don't wait for pointer exit/light-dismiss.
            previous?.Hide();
            update();
            flyout.ShowAt(anchor, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });
        }
        void Exit(object sender, PointerRoutedEventArgs e)
        {
            SwitchHoverPopover(e);
            if (!ReferenceEquals(activeHoverPopover, flyout) || PopoverContainsPointer(e, anchor) || PopoverContainsPointer(e, body)) return;
            dismiss.Stop(); dismiss.Start();
        }
        hoverPopoverTargets.Add(new(anchor, flyout, Open));
        if (!hoverPointerAttached)
        {
            hoverPointerAttached = true;
            root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) => SwitchHoverPopover(e)), true);
        }
        // A popup can intercept sibling enter events; also observe the pointer from its surface.
        body.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) => SwitchHoverPopover(e)), true);
        anchor.PointerEntered += (_, _) => Open();
        anchor.PointerMoved += (_, _) => Open();
        anchor.PointerExited += Exit;
        anchor.Tapped += (_, _) => Open();
        body.PointerEntered += (_, e) => { SwitchHoverPopover(e); dismiss.Stop(); };
        body.PointerExited += Exit;
        dismiss.Tick += (_, _) => { dismiss.Stop(); flyout.Hide(); };
        refresh.Tick += (_, _) => update();
        flyout.Opened += (_, _) => refresh.Start();
        flyout.Closed += (_, _) =>
        {
            opened = false; dismiss.Stop(); refresh.Stop();
            if (ReferenceEquals(activeHoverPopover, flyout)) activeHoverPopover = null;
        };
        anchor.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => { if (anchor.Visibility != Visibility.Visible) flyout.Hide(); });
        anchor.Unloaded += (_, _) => { flyout.Hide(); dismiss.Stop(); refresh.Stop(); };
    }
}
