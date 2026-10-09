using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.DataTransfer;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly ConditionalWeakTable<FrameworkElement, object> sidebarDragWiring = new();
    readonly Canvas sidebarDropOverlay = new() { IsHitTestVisible = false };
    readonly Border sidebarDropMarker = new() { Height = 2, CornerRadius = new(1), IsHitTestVisible = false,
        Background = FluentDesign.Resource("AccentFillColorDefaultBrush"), Visibility = Visibility.Collapsed };
    FrameworkElement? sidebarDropTarget;
    bool sidebarDropRefreshPending, sidebarDropNeedsProjectReload, sidebarDropRefreshQueued;

    IEnumerable<T> SidebarOrder<T>(IEnumerable<T> rows, bool projectsOrder, Func<T, int> id)
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        var order = projectsOrder ? settings.SidebarProjectOrder : settings.SidebarChatOrder;
        return rows.OrderBy(row => { var index = order.IndexOf(id(row)); return index < 0 ? int.MaxValue : index; });
    }
    void ShowSidebarDropMarker(FrameworkElement target, bool after)
    {
        if (sidebarDropOverlay.Children.Count == 0) sidebarDropOverlay.Children.Add(sidebarDropMarker);
        if (sidebarDropOverlay.Parent == null) root.Children.Add(sidebarDropOverlay);
        var point = target.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, after ? target.ActualHeight : 0));
        sidebarDropMarker.Width = target.ActualWidth;
        Canvas.SetLeft(sidebarDropMarker, point.X); Canvas.SetTop(sidebarDropMarker, Math.Max(0, point.Y - 1));
        sidebarDropTarget = target; sidebarDropMarker.Visibility = Visibility.Visible;
    }
    void ClearSidebarDropMarker()
    { sidebarDropTarget = null; sidebarDropMarker.Visibility = Visibility.Collapsed; }

    void EnableSidebarDrag(FrameworkElement element, bool isProject, Func<int?> itemId)
    {
        // Recycled ListView containers can lose CanDrag/AllowDrop; restore both every time.
        // Keep event registration independent of their other hover/selection tags.
        element.CanDrag = true; element.AllowDrop = true;
        if (sidebarDragWiring.TryGetValue(element, out _)) return;
        sidebarDragWiring.Add(element, new object());
        element.DragStarting += (_, e) =>
        {
            ClearSidebarDropMarker();
            if (itemId() is not { } id || id <= 0) { e.Cancel = true; return; }
            e.Data.SetText("monolith-sidebar:" + (isProject ? "project:" : "chat:") + id);
            e.Data.Properties["MonolithSidebarKind"] = isProject ? "project" : "chat";
            e.Data.RequestedOperation = DataPackageOperation.Move;
        };
        element.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.Text) || !e.DataView.Properties.TryGetValue("MonolithSidebarKind", out var kind)
                || kind is not string type || type is not ("project" or "chat") || type == "project" && !isProject || itemId() == null) return;
            bool intoProject = type == "chat" && isProject;
            var after = intoProject || e.GetPosition(element).Y > element.ActualHeight / 2;
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = intoProject ? WorkflowText("Déplacer dans ce projet", "Move into this project")
                : after ? WorkflowText("Déplacer après", "Move after") : WorkflowText("Déplacer avant", "Move before");
            ShowSidebarDropMarker(element, after); e.Handled = true;
        };
        element.DragLeave += (_, _) => { if (ReferenceEquals(sidebarDropTarget, element)) ClearSidebarDropMarker(); };
        element.DropCompleted += (_, _) => { ClearSidebarDropMarker(); QueueSidebarDropRefresh(); };
        element.Drop += async (_, e) =>
        {
            e.Handled = true;
            var after = e.GetPosition(element).Y > element.ActualHeight / 2;
            var target = itemId(); var deferral = e.GetDeferral(); ClearSidebarDropMarker();
            try
            {
                await Guard(async () =>
                {
                    if (target == null || !e.DataView.Contains(StandardDataFormats.Text)) return;
                    var parts = (await e.DataView.GetTextAsync()).Split(':');
                    if (parts.Length != 3 || parts[0] != "monolith-sidebar" || !int.TryParse(parts[2], out var from) || from <= 0) return;
                    await PersistSidebarMoveAsync(parts[1], from, isProject, target.Value, after);
                });
            }
            finally
            {
                // Do not detach/recycle the drag source while the native drag operation owns it.
                deferral.Complete(); QueueSidebarDropRefresh();
            }
        };
    }
    async Task PersistSidebarMoveAsync(string kind, int from, bool targetIsProject, int target, bool after)
    {
        var settings = FeatureSettings.Read(state.FeaturesJson); bool projectChanged = false;
        if (kind == "project" && targetIsProject)
        {
            var ids = SidebarOrder((projects.ItemsSource as IEnumerable<Project> ?? []), true, p => p.Id).Select(p => p.Id).ToList();
            if (!MoveSidebarId(ids, from, target, after)) return;
            settings.SidebarProjectOrder = ids;
        }
        else if (kind == "chat")
        {
            if (!targetIsProject && from == target) return;
            var moved = await db.Chats.FindAsync(from); if (moved == null) return;
            var destination = targetIsProject ? target : (await db.Chats.FindAsync(target))?.ProjectId;
            if (destination == null || !await db.Projects.AnyAsync(p => p.Id == destination.Value)) return;
            projectChanged = moved.ProjectId != destination;
            if (projectChanged)
            {
                if (conversationRuns.ContainsKey(from)) throw new InvalidOperationException(WorkflowText("Arrêtez la conversation avant de changer son projet.", "Stop this conversation before moving it to another project."));
            }
            var ids = SidebarOrder(navigationChats.OrderByDescending(c => c.IsFavorite).ThenByDescending(c => c.UpdatedUtc), false, c => c.Id).Select(c => c.Id).ToList();
            if (!ids.Contains(from)) ids.Add(from);
            if (!targetIsProject) { if (!MoveSidebarId(ids, from, target, after)) return; }
            else { ids.Remove(from); ids.Insert(0, from); }
            settings.SidebarChatOrder = ids;
            if (projectChanged) moved.ProjectId = destination.Value;
        }
        else return;
        state.FeaturesJson = settings.Json(); await db.SaveChangesAsync();
        if (projectChanged && chat?.Id == from)
        {
            state.ProjectId = db.Chats.Local.First(c => c.Id == from).ProjectId;
            loading = true;
            try { projects.SelectedItem = (projects.ItemsSource as IEnumerable<Project>)?.FirstOrDefault(p => p.Id == state.ProjectId); }
            finally { loading = false; }
        }
        sidebarDropRefreshPending = true; sidebarDropNeedsProjectReload |= projectChanged;
    }
    void QueueSidebarDropRefresh()
    {
        if (!sidebarDropRefreshPending || sidebarDropRefreshQueued) return;
        sidebarDropRefreshQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () => await Guard(FlushSidebarDropRefreshAsync));
    }
    async Task FlushSidebarDropRefreshAsync()
    {
        try
        {
            await Task.Delay(1); // Let Drop/DropCompleted return before changing their visual tree.
            if (!sidebarDropRefreshPending) return;
            var reload = sidebarDropNeedsProjectReload;
            sidebarDropRefreshPending = sidebarDropNeedsProjectReload = false;
            if (reload) await SelectProject();
            else ApplyChatSearch(chat?.Id); // Reordering alone preserves the displayed chat and draft.
        }
        finally { sidebarDropRefreshQueued = false; }
    }
    static bool MoveSidebarId(List<int> ids, int from, int target, bool after)
    {
        if (from == target || !ids.Contains(from) || !ids.Contains(target)) return false;
        ids.Remove(from); ids.Insert(ids.IndexOf(target) + (after ? 1 : 0), from); return true;
    }
}
