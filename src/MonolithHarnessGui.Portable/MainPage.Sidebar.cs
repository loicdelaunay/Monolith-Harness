using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarnessGui.Portable;

public sealed partial class MainPage
{
    Grid? sidebarOverlay;
    Border? sidebarShell;
    Grid? sidebarBody;
    StackPanel? sidebarList;
    Button? sidebarTopActions;
    Grid? sidebarFooter;
    int sidebarRevision;
    readonly HashSet<int> expandedSidebarProjects=[];
    readonly Dictionary<int,List<Chat>> sidebarChats=[];
    readonly Dictionary<(int Project,int? Chat),ComposerDraft> drafts=[];
    sealed record ComposerDraft(string Text,int Caret,int Selection,Attachment[] Images);
    ComposerDraft CaptureDraft()=>new(input.Text,input.SelectionStart,input.SelectionLength,attachments.ToArray());
    void SaveDraft()
    {
        if(currentProject!=null)drafts[(currentProject.Id,currentChat?.Id)]=CaptureDraft();
    }
    void RestoreDraft(ComposerDraft? carried=null)
    {
        var draft=carried;
        if(draft==null && currentProject!=null)drafts.TryGetValue((currentProject.Id,currentChat?.Id),out draft);
        input.Text=draft?.Text??"";input.SelectionStart=Math.Clamp(draft?.Caret??0,0,input.Text.Length);
        input.SelectionLength=Math.Clamp(draft?.Selection??0,0,input.Text.Length-input.SelectionStart);
        attachments.Clear();if(draft!=null)attachments.AddRange(draft.Images);RefreshAttachments();RefreshSkillSuggestions();
    }
    async Task OpenSidebarAsync()
    {
        if(!ready || generation!=null || importingDocument || changing || HasOpenDialog)return;
        MainActivity.HideKeyboard();var revision=++sidebarRevision;
        var overlay=sidebarOverlay=new Grid();Grid.SetRowSpan(overlay,layout.RowDefinitions.Count);
        var scrim=new Border {Background=new SolidColorBrush(Windows.UI.Color.FromArgb(100,0,0,0))};scrim.Tapped+=(_,_)=>CloseSidebar();overlay.Children.Add(scrim);
        var body=sidebarBody=new Grid {Padding=new(16),RowSpacing=12};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})body.RowDefinitions.Add(new(){Height=height});
        var header=new Grid {ColumnSpacing=8};header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        header.Children.Add(new TextBlock {Text="Discussions",FontSize=24,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("OnSurfaceBrush")});
        var close=new Button();Icon(close,"close","Fermer la liste des discussions");close.Click+=(_,_)=>CloseSidebar();Grid.SetColumn(close,1);header.Children.Add(close);body.Children.Add(header);
        var create=sidebarTopActions=new Button {Content="Nouvelle discussion",MinHeight=48,CornerRadius=new(24),Padding=new(16,8,16,8),HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brush("PrimaryBrush"),Foreground=Brush("OnPrimaryBrush")};
        create.Click+=async(_,_)=>await Guard(CreateChatAsync);Grid.SetRow(create,1);body.Children.Add(create);
        var list=sidebarList=new StackPanel {Spacing=4};list.Children.Add(SettingsText("Chargement…"));
        var scrollProjects=new ScrollViewer {Content=list,VerticalScrollMode=ScrollMode.Enabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        Grid.SetRow(scrollProjects,2);body.Children.Add(scrollProjects);
        var footer=sidebarFooter=new Grid {ColumnSpacing=8};footer.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var addProject=SettingsButton("Nouveau projet");addProject.Click+=async(_,_)=>{CloseSidebar();await Guard(CreateProjectAsync);};footer.Children.Add(addProject);
        var configure=new Button();Icon(configure,"settings","Paramètres");configure.Click+=async(_,_)=>{CloseSidebar();await Guard(ShowSettingsAsync);};Grid.SetColumn(configure,1);footer.Children.Add(configure);Grid.SetRow(footer,3);body.Children.Add(footer);
        sidebarShell=new Border {Child=body,Background=Brush("SurfaceBrush"),CornerRadius=new(0,28,28,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch};
        overlay.Children.Add(sidebarShell);SizeSidebar();layout.Children.Add(overlay);MainActivity.UpdateBackHandling();close.Focus(FocusState.Programmatic);
        create.IsEnabled=footer.IsHitTestVisible=false;
        try {
            var snapshot=await Task.Run(async()=>{
                var loadedProjects=await store.ProjectsAsync();var loadedChats=new Dictionary<int,List<Chat>>();
                foreach(var project in loadedProjects)loadedChats[project.Id]=await store.ChatsAsync(project.Id);
                return (Projects:loadedProjects,Chats:loadedChats);
            });
            if(revision!=sidebarRevision || sidebarShell==null)return;
            projectItems=snapshot.Projects;sidebarChats.Clear();foreach(var pair in snapshot.Chats)sidebarChats.Add(pair.Key,pair.Value);
            if(currentProject!=null)expandedSidebarProjects.Add(currentProject.Id);
            RenderSidebar();create.IsEnabled=footer.IsHitTestVisible=true;
        } catch {if(revision==sidebarRevision)CloseSidebar();throw;}
    }
    void RenderSidebar()
    {
        if(sidebarList==null)return;sidebarList.Children.Clear();
        foreach(var project in projectItems) {
            var expanded=expandedSidebarProjects.Contains(project.Id);var chats=sidebarChats.GetValueOrDefault(project.Id)??[];
            var row=new Grid {ColumnSpacing=4,Margin=new(0,8,0,0)};row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var content=new Grid {ColumnSpacing=8};foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})content.ColumnDefinitions.Add(new(){Width=width});
            var icon=MobileIcons.Create(expanded?"expand_more":"chevron_right",18);icon.Foreground=Brush("OnSurfaceVariantBrush");content.Children.Add(icon);
            var name=new TextBlock {Text=project.Name,FontSize=14,MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush(currentProject?.Id==project.Id?"PrimaryBrush":"OnSurfaceBrush")};Grid.SetColumn(name,1);content.Children.Add(name);
            var count=SettingsText(chats.Count.ToString(),12);count.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(count,2);content.Children.Add(count);
            var group=new Button {Content=content,MinWidth=0,MinHeight=48,Padding=new(8,4,8,4),CornerRadius=new(16),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=Brush("SurfaceBrush"),Foreground=Brush("OnSurfaceBrush"),BorderThickness=new(0)};
            AccessibleName(group,(expanded?"Replier ":"Déplier ")+project.Name);
            group.Click+=(_,_)=>{if(expandedSidebarProjects.Contains(project.Id))expandedSidebarProjects.Remove(project.Id);else expandedSidebarProjects.Add(project.Id);RenderSidebar();};row.Children.Add(group);
            var add=new Button();Icon(add,"add","Nouvelle discussion dans "+project.Name);add.Click+=async(_,_)=>await Guard(()=>CreateChatInProjectAsync(project));Grid.SetColumn(add,1);row.Children.Add(add);sidebarList.Children.Add(row);
            if(!expanded)continue;
            if(chats.Count==0) {
                var empty=SettingsText("Aucune discussion · utilisez + pour commencer.",12);empty.Margin=new(24,4,8,12);sidebarList.Children.Add(empty);
            }
            foreach(var chat in chats) {
                var selected=currentChat?.Id==chat.Id;
                var label=new TextBlock {Text=chat.Title,FontSize=14,MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush(selected?"OnSecondaryContainerBrush":"OnSurfaceBrush")};
                var button=new Button {Content=label,MinWidth=0,MinHeight=48,Padding=new(16,8,16,8),Margin=new(16,0,0,0),CornerRadius=new(24),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(0),Background=Brush(selected?"SecondaryContainerBrush":"SurfaceBrush"),Foreground=label.Foreground};
                AccessibleName(button,chat.Title+(selected?" · discussion active":""));button.Click+=async(_,_)=>await Guard(()=>SelectChatAsync(project,chat));sidebarList.Children.Add(button);
            }
        }
    }
    void SizeSidebar()
    {
        if(sidebarShell==null)return;
        sidebarShell.Width=Math.Min(360,layoutMetrics.ContentWidth);sidebarShell.Height=layoutMetrics.UsableHeight;
        if(sidebarBody!=null){sidebarBody.Padding=new(layoutMetrics.Minimal?8:16);sidebarBody.RowSpacing=layoutMetrics.Minimal?4:12;}
        if(sidebarTopActions!=null)sidebarTopActions.Visibility=layoutMetrics.UsableHeight<360?Visibility.Collapsed:Visibility.Visible;
        if(sidebarFooter!=null)sidebarFooter.Visibility=layoutMetrics.Minimal?Visibility.Collapsed:Visibility.Visible;
    }
    bool CloseSidebar()
    {
        if(sidebarOverlay==null)return false;
        sidebarRevision++;layout.Children.Remove(sidebarOverlay);sidebarOverlay=null;sidebarShell=null;sidebarBody=null;sidebarList=null;sidebarTopActions=null;sidebarFooter=null;
        MainActivity.UpdateBackHandling();projectMenu.Focus(FocusState.Programmatic);return true;
    }
}
