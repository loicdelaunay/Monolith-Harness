using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MonolithHarness.App;

/// <summary>Follow growing output while at its bottom; user scrolling owns its position.</summary>
internal sealed class ContentTailFollower
{
    readonly ScrollViewer scroll;
    bool follow = true, dragging, resume, queued, pendingUpdate;
    int changing;
    long inputUntil;
    double offset, previousOffset, previousExtent, target = double.NaN;

    internal ContentTailFollower(ScrollViewer viewer)
    {
        scroll = viewer;
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
        {
            if (!Owns(args.OriginalSource)) return;
            var point = args.GetCurrentPoint(scroll);
            if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0) return;
            Input(point.Properties.MouseWheelDelta < 0);
        }), true);
        scroll.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (!Owns(args.OriginalSource)) return;
            if (args.Key is VirtualKey.Up or VirtualKey.PageUp or VirtualKey.Home) Input(false);
            else if (args.Key is VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End) Input(true);
        }), true);
        scroll.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (!Owns(args.OriginalSource)) return;
            bool scrollbar = false;
            for (var node = args.OriginalSource as DependencyObject; node != null && node != scroll; node = VisualTreeHelper.GetParent(node))
                if (node is ScrollBar) scrollbar = true;
            if (scrollbar || args.Pointer.PointerDeviceType is Microsoft.UI.Input.PointerDeviceType.Touch or Microsoft.UI.Input.PointerDeviceType.Pen)
            { dragging = true; Input(false); }
        }), true);
        void End(object sender, PointerRoutedEventArgs args)
        {
            if (!dragging) return;
            dragging = false; inputUntil = Environment.TickCount64 + 750;
            follow = NearBottom(); if (!follow) offset = scroll.VerticalOffset;
            pendingUpdate = false; Queue();
        }
        scroll.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(End), true);
        scroll.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(End), true);
        scroll.ViewChanged += (_, _) =>
        {
            var current = scroll.VerticalOffset;
            bool moved = Math.Abs(current - previousOffset) > .5;
            bool extentChanged = Math.Abs(scroll.ScrollableHeight - previousExtent) > .5;
            previousOffset = current; previousExtent = scroll.ScrollableHeight;
            if (changing > 0 || pendingUpdate || !double.IsNaN(target) && Math.Abs(current - target) <= 1) { target = double.NaN; return; }
            if ((dragging || Environment.TickCount64 < inputUntil) && moved && !extentChanged)
            {
                offset = current;
                follow = resume && NearBottom();
                if (dragging) follow = NearBottom();
            }
        };
        scroll.LayoutUpdated += (_, _) =>
        {
            if (changing == 0 && follow && !dragging && scroll.ScrollableHeight - scroll.VerticalOffset > 1) Queue();
        };
        scroll.Loaded += (_, _) => Queue();
    }
    bool NearBottom() => scroll.ScrollableHeight - scroll.VerticalOffset <= 4;
    bool Owns(object source)
    {
        for (var node = source as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer viewer && (ReferenceEquals(viewer, scroll) || viewer.ScrollableHeight > 0)) return ReferenceEquals(viewer, scroll);
        return false;
    }
    void Input(bool towardsBottom)
    {
        inputUntil = Environment.TickCount64 + 750; resume = towardsBottom;
        offset = scroll.VerticalOffset; previousOffset = offset; previousExtent = scroll.ScrollableHeight;
        target = double.NaN; pendingUpdate = false;
        if (!towardsBottom) follow = false;
    }
    internal void BeforeTextChange()
    {
        if (changing++ == 0 && !follow && !pendingUpdate) offset = scroll.VerticalOffset;
        pendingUpdate = true;
    }
    internal void AfterTextChange()
    {
        if (changing > 0) changing--;
        Queue();
    }
    internal void Queue()
    {
        if (queued) return;
        queued = true;
        if (!scroll.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            queued = false;
            if (!scroll.IsLoaded || changing > 0 || dragging && !pendingUpdate) return;
            // Read the current state, not a stale position captured before a user's gesture.
            // UpdateLayout measures the whole window, so only a held reading position needs it:
            // while following, LayoutUpdated queues the tail again after the next layout pass.
            if (!follow)
            {
                changing++;
                try { scroll.UpdateLayout(); } finally { changing--; }
            }
            pendingUpdate = false;
            target = follow ? scroll.ScrollableHeight : Math.Min(offset, scroll.ScrollableHeight);
            if (Math.Abs(scroll.VerticalOffset - target) > 1) scroll.ChangeView(null, target, null, true);
            else target = double.NaN;
        })) queued = false;
    }
}
