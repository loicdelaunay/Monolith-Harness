using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonolithHarness.App;

/// <summary>Keeps file menus on selectable inline text without changing its layout.</summary>
static class ChatPathMenus
{
    sealed record LinkTarget(string Path);
    sealed class PointerState { public string? Path; public bool SecondaryPress; public bool MenuShown; }
    static readonly ConditionalWeakTable<Hyperlink, LinkTarget> targets = new();
    static readonly ConditionalWeakTable<TextBlock, PointerState> pointers = new();

    // Uno 6.7's inline Hyperlink is not a UIElement and has no context event.
    // Reuse the pinned renderer's own hit test; it accounts for wrapping, fonts and scaling.
    // Keep this compatibility access isolated and retained in trimmed publications.
    [DynamicDependency("FindHyperlinkAt", typeof(TextBlock))]
    static MethodInfo? FindHitTest() => typeof(TextBlock).GetMethod("FindHyperlinkAt",
        BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(PointerRoutedEventArgs)], null);
    static readonly MethodInfo? hitTest = FindHitTest();

    public static void Register(Hyperlink link, string path) => targets.Add(link, new(path));
    public static bool IsSecondaryPress(TextBlock? text) => text != null && pointers.TryGetValue(text, out var state) && state.SecondaryPress;

    public static void Attach(UIElement element, Func<string, MenuFlyout> createMenu)
    {
        switch (element)
        {
            case TextBlock text when HasTargets(text.Inlines): AttachText(text, createMenu); break;
            case Panel panel: foreach (var child in panel.Children) Attach(child, createMenu); break;
            case Border { Child: { } child }: Attach(child, createMenu); break;
            case ScrollViewer { Content: UIElement child }: Attach(child, createMenu); break;
        }
    }

    static bool HasTargets(InlineCollection inlines) => inlines.Any(x =>
        x is Hyperlink link && targets.TryGetValue(link, out _) || x is Span span && HasTargets(span.Inlines));

    static void AttachText(TextBlock text, Func<string, MenuFlyout> createMenu)
    {
        var state = pointers.GetOrCreateValue(text);
        void ShowMenu(string path, Windows.Foundation.Point? position)
        {
            state.MenuShown = true;
            var menu = createMenu(path);
            menu.Closed += (_, _) => state.MenuShown = false;
            if (position is { } point) menu.ShowAt(text, new FlyoutShowOptions { Position = point });
            else menu.ShowAt(text);
        }
        void Track(object sender, PointerRoutedEventArgs args)
        {
            state.Path = null;
            try
            {
                if (hitTest?.Invoke(text, [args]) is Hyperlink link && targets.TryGetValue(link, out var target))
                    state.Path = target.Path;
            }
            catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException) { }
        }
        text.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Track), true);
        text.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((sender, args) =>
        {
            state.MenuShown = false;
            state.SecondaryPress = args.GetCurrentPoint(text).Properties.IsRightButtonPressed;
            Track(sender, args);
        }), true);
        text.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((sender, args) =>
        {
            if (!state.SecondaryPress) return;
            Track(sender, args);
            if (state.Path is { } path)
            {
                // Inline links can consume the right-tap gesture in Uno. Opening from
                // secondary release also prevents a link's normal Click from navigating.
                args.Handled = true;
                ShowMenu(path, args.GetCurrentPoint(text).Position);
            }
            text.DispatcherQueue.TryEnqueue(() => state.SecondaryPress = false);
        }), true);
        text.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler((_, args) =>
        {
            if (state.MenuShown) args.Handled = true;
        }), true);
        text.PointerExited += (_, _) => { state.Path = null; };
        text.ContextRequested += (_, args) =>
        {
            if (state.MenuShown) { args.Handled = true; return; }
            var path = state.Path;
            if (path == null && text.XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is Hyperlink focused && targets.TryGetValue(focused, out var target))
                path = target.Path;
            if (path == null) return;
            args.Handled = true;
            ShowMenu(path, args.TryGetPosition(text, out var point) ? point : null);
        };
        foreach (var link in Links(text.Inlines))
        {
            owners.Add(link, text);
        }
    }

    static readonly ConditionalWeakTable<Hyperlink, TextBlock> owners = new();
    public static bool IsSecondaryPress(Hyperlink link) => owners.TryGetValue(link, out var text) && IsSecondaryPress(text);
    static IEnumerable<Hyperlink> Links(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Hyperlink link && targets.TryGetValue(link, out _)) yield return link;
            if (inline is Span span) foreach (var child in Links(span.Inlines)) yield return child;
        }
    }
}
