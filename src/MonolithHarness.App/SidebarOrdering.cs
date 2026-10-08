using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    IEnumerable<T> SidebarOrder<T>(IEnumerable<T> rows, bool projectsOrder, Func<T, int> id)
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        var order = projectsOrder ? settings.SidebarProjectOrder : settings.SidebarChatOrder;
        return rows.OrderBy(row => { var index = order.IndexOf(id(row)); return index < 0 ? int.MaxValue : index; });
    }
    void EnableSidebarDrag(FrameworkElement element, bool isProject, Func<int?> itemId)
    {
        element.CanDrag = true; element.AllowDrop = true;
        element.DragStarting += (_, e) =>
        {
            if (itemId() is not { } id) { e.Cancel = true; return; }
            e.Data.SetText("monolith-sidebar:" + (isProject ? "project:" : "chat:") + id);
            e.Data.Properties["MonolithSidebarKind"] = isProject ? "project" : "chat";
            e.Data.RequestedOperation = DataPackageOperation.Move;
        };
        element.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.Text) || !e.DataView.Properties.TryGetValue("MonolithSidebarKind", out var kind) || kind is not string type || type == "project" && !isProject) return;
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = WorkflowText("Déplacer", "Move"); e.Handled = true;
        };
        element.Drop += async (_, e) =>
        {
            e.Handled = true;
            var after = e.GetPosition(element).Y > element.ActualHeight / 2;
            var target = itemId();
            var deferral = e.GetDeferral();
            try { await Guard(async () =>
            {
                if (target == null || !e.DataView.Contains(StandardDataFormats.Text)) return;
                var parts = (await e.DataView.GetTextAsync()).Split(':');
                if (parts.Length != 3 || parts[0] != "monolith-sidebar" || !int.TryParse(parts[2], out var from)) return;
                var settings = FeatureSettings.Read(state.FeaturesJson);
                if (parts[1] == "project" && isProject)
                {
                    var ids = SidebarOrder((projects.ItemsSource as IEnumerable<Project> ?? []), true, p => p.Id).Select(p => p.Id).ToList();
                    Move(ids, from, target.Value, after); settings.SidebarProjectOrder = ids;
                }
                else if (parts[1] == "chat")
                {
                    var moved = await db.Chats.FindAsync(from);
                    if (moved == null) return;
                    var destination = isProject ? target.Value : (await db.Chats.FindAsync(target.Value))?.ProjectId;
                    if (destination == null || !await db.Projects.AnyAsync(p => p.Id == destination.Value)) return;
                    if (moved.ProjectId != destination)
                    {
                        if (conversationRuns.ContainsKey(from)) throw new InvalidOperationException(WorkflowText("Arrêtez la conversation avant de changer son projet.", "Stop this conversation before moving it to another project."));
                        moved.ProjectId = destination.Value;
                    }
                    var ids = SidebarOrder(navigationChats.OrderByDescending(c => c.IsFavorite).ThenByDescending(c => c.UpdatedUtc), false, c => c.Id).Select(c => c.Id).ToList();
                    if (!ids.Contains(from)) ids.Add(from);
                    if (!isProject) Move(ids, from, target.Value, after);
                    else { ids.Remove(from); ids.Insert(0, from); }
                    settings.SidebarChatOrder = ids;
                }
                else return;
                state.FeaturesJson = settings.Json(); await db.SaveChangesAsync();
                if (chat?.Id == from && parts[1] == "chat")
                {
                    state.ProjectId = db.Chats.Local.First(c => c.Id == from).ProjectId;
                    loading = true;
                    try { projects.SelectedItem = (projects.ItemsSource as IEnumerable<Project>)?.FirstOrDefault(p => p.Id == state.ProjectId); }
                    finally { loading = false; }
                }
                await SelectProject();
            }); }
            finally { deferral.Complete(); }
        };
        static void Move(List<int> ids, int from, int target, bool after)
        {
            if (from == target || !ids.Contains(from) || !ids.Contains(target)) return;
            ids.Remove(from); ids.Insert(ids.IndexOf(target) + (after ? 1 : 0), from);
        }
    }
}
