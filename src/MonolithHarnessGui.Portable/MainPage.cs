using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using MonolithHarness.Core.Mobile;

namespace MonolithHarnessGui.Portable;

public sealed partial class MainPage : Page
{
    readonly MobileConversationStore store=new(MobileServices.DataDirectory);
    readonly AndroidSecretVault vault=new();
    readonly HttpClient http=new() { Timeout=Timeout.InfiniteTimeSpan };
    List<Project> projectItems=[];
    Project? currentProject;
    Chat? currentChat;
    readonly ComboBox providers=new() { PlaceholderText="Fournisseur API",HorizontalAlignment=HorizontalAlignment.Stretch };
    readonly StackPanel messages=new() {Spacing=12};
    readonly ScrollViewer scroll=new() {VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBox input=new() { PlaceholderText="Écrivez votre message…",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=48,MaxHeight=128,HorizontalAlignment=HorizontalAlignment.Stretch };
    readonly TextBlock status=new() { Text="Monolith Harness Portable",TextWrapping=TextWrapping.Wrap,Opacity=.75 };
    readonly TextBlock attachmentsLabel=new() {TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
    readonly Button send=new() {Content="Envoyer",MinWidth=84};
    readonly Button stop=new() {Content="Arrêter",Visibility=Visibility.Collapsed};
    readonly Button attach=new() {Content=new SymbolIcon(Symbol.Add)};
    readonly Button settings=new() {Content=new SymbolIcon(Symbol.Setting)};
    readonly List<Attachment> attachments=[];
    CancellationTokenSource? generation;
    bool initialized,changing,ready;
    int runRevision, navigationRevision, historyRevision;
    bool loadingHistory;
    MobileToolBubble? activeToolBubble;
    Border? activeReplyCard;
    public MainPage()
    {
        BuildLayout();SetBusy(true);
        providers.SelectionChanged+=(_,_)=>UpdateProviderChip();
        send.Click+=async(_,_)=>await Guard(SendAsync);stop.Click+=(_,_)=>generation?.Cancel();settings.Click+=async(_,_)=>await Guard(ShowSettingsAsync);
        attach.Click+=(_,_)=>ShowAttachmentMenu();
        input.TextChanged+=(_,_)=>{RefreshInputHeight();RefreshSkillSuggestions();};input.SizeChanged+=(_,_)=>RefreshInputHeight();input.SelectionChanged+=(_,_)=>RefreshSkillSuggestions();
        Loaded+=async(_,_)=>{if(initialized)return;initialized=true;await Guard(InitializeAsync);ApplyViewport();};
        SizeChanged+=(_,_)=>ApplyViewport();
    }
    async Task Guard(Func<Task> action) { try {await action();} catch(OperationCanceledException) {SetStatus("Réponse interrompue ; le texte reçu est conservé.");} catch(Exception ex){SetStatus(ex.Message,true);} }
    async Task InitializeAsync()
    {
        await store.InitializeAsync();mobileSettings=MobileSettings.Load(MobileServices.DataDirectory);ApplySettings();projectItems=await store.ProjectsAsync();currentProject=projectItems.FirstOrDefault();providers.ItemsSource=await store.ProvidersAsync();providers.SelectedIndex=0;
        await LoadChatsAsync();ready=true;SetBusy(false);SetStatus(providers.SelectedItem==null?"Configurez un fournisseur API pour commencer.":"Prêt");
    }
    Task LoadChatsAsync()=>LoadChatsAsync(null);
    async Task LoadChatsAsync(int? preferredChat)
    {
        if(currentProject is not Project project)return;
        var revision=++navigationRevision;var previous=preferredChat??(currentChat?.ProjectId==project.Id?currentChat.Id:null);changing=true;
        try {
            var items=await store.ChatsAsync(project.Id);
            if(revision!=navigationRevision || currentProject?.Id!=project.Id)return;
            currentChat=items.FirstOrDefault(x=>x.Id==previous)??items.FirstOrDefault();
            await RenderHistoryAsync();
        } finally {if(revision==navigationRevision)changing=false;}
    }
    MobileMarkdownView Bubble(string role,string text)
    {
        var ink=Brush(role=="user"?"OnPrimaryContainerBrush":"OnSurfaceBrush");
        var body=new MobileMarkdownView(text,mobileSettings.TextSize,ink);
        var card=new StackPanel {Spacing=6};card.Children.Add(new TextBlock {Text=role=="user"?"Vous":role=="tool"?"Outil":"Assistant",FontSize=12,Foreground=ink,Opacity=.7});card.Children.Add(body);
        messages.Children.Add(new Border {Child=card,Padding=new(16),CornerRadius=role=="user"?new(24,24,8,24):new(24,24,24,8),
            HorizontalAlignment=role=="user"?HorizontalAlignment.Right:HorizontalAlignment.Left,MaxWidth=Math.Max(48,layoutMetrics.ContentWidth*.96),
            Background=Brush(role=="user"?"PrimaryContainerBrush":"SurfaceBrush"),BorderBrush=Brush("OutlineVariantBrush"),BorderThickness=new(role=="user"?0:1)});
        scroll.Content=messageRail;scroll.VerticalContentAlignment=VerticalAlignment.Top;return body;
    }
    async Task RenderHistoryAsync()
    {
        var revision=++historyRevision;var chat=currentChat;loadingHistory=true;
        try {
            var history=chat==null?[]:await store.MessagesAsync(chat.Id);
            if(revision!=historyRevision || currentChat?.Id!=chat?.Id)return;
            messages.Children.Clear();
            conversationDocuments=chat==null?[]:new MobileFiles(MobileServices.DataDirectory,chat.Id).List();RefreshAttachments();AccessibleName(projectMenu,"Projets et discussions · "+(chat?.Title??"Monolith"));projectCaption.Text=currentProject?.Name??"";scroll.Content=messageRail;scroll.VerticalContentAlignment=VerticalAlignment.Top;
            if(chat==null || history.Count==0) { ShowWelcome(chat==null);return; }
            MobileToolBubble? toolBubble=null;
            var knownCalls=new Dictionary<string,(string Name,string Arguments)>();
            foreach(var message in history) {
                if(message.Role=="user") {toolBubble=null;knownCalls.Clear();}
                foreach(var call in MobileToolBubble.Calls(message.WireJson))knownCalls[call.Key]=call.Value;
                if(message.Role=="tool") {
                    if(mobileSettings.ShowToolActivity) {
                        if(toolBubble==null) {toolBubble=NewToolBubble();messages.Children.Add(toolBubble);}
                        toolBubble.Update(MobileToolBubble.Stored(message,knownCalls));
                    }
                    continue;
                }
                var text=message.Content;if(message.Attachments.Count>0)text+="\n"+string.Join(", ",message.Attachments.Select(x=>x.Name));
                if(message.State=="interrupted" || message.State=="generating")text+="\n[Réponse interrompue]";
                if(text.Length>0)Bubble(message.Role,text);
            }
            ScrollDown();
        }finally {if(revision==historyRevision)loadingHistory=false;}
    }
    Task CreateChatAsync()=>CreateChatInProjectAsync(currentProject);
    async Task CreateChatInProjectAsync(Project? project,bool preserveDraft=false)
    {
        if(generation!=null || changing || project==null)return;
        CloseSidebar();SaveDraft();var carried=CaptureDraft();changing=true;SetBusy(true);
        try {
            var chat=await store.CreateChatAsync(project.Id);currentProject=project;
            await LoadChatsAsync(chat.Id);RestoreDraft(preserveDraft?carried:null);
            expandedSidebarProjects.Add(project.Id);
        } finally {changing=false;SetBusy(false);}
    }
    async Task SelectChatAsync(Project project,Chat chat)
    {
        if(!ready || generation!=null || importingDocument || changing)return;
        CloseSidebar();SaveDraft();changing=true;SetBusy(true);
        try {
            currentProject=project;await LoadChatsAsync(chat.Id);RestoreDraft();
            expandedSidebarProjects.Add(project.Id);
        } finally {changing=false;SetBusy(false);}
    }
    async Task CreateProjectAsync()
    {
        if(generation!=null || importingDocument || changing)return;CloseSidebar();
        var name=new TextBox {PlaceholderText="Nom du projet"};var dialog=CreateDialog("Nouveau projet",name,"Créer","Annuler");
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text))return;
        SaveDraft();changing=true;SetBusy(true);
        try {
            var project=await store.CreateProjectAsync(name.Text);projectItems=await store.ProjectsAsync();currentProject=project;
            await LoadChatsAsync();RestoreDraft();expandedSidebarProjects.Add(project.Id);
        } finally {changing=false;SetBusy(false);}
    }
    void SetBusy(bool busy)
    {
        var blocked=busy || !ready || changing || importingDocument || generation!=null;
        input.IsEnabled=providers.IsEnabled=settings.IsEnabled=attach.IsEnabled=send.IsEnabled=providerButton.IsEnabled=projectMenu.IsEnabled=newChatButton.IsEnabled=!blocked;
        if(sidebarList!=null)sidebarList.IsHitTestVisible=!blocked;
        if(sidebarTopActions!=null)sidebarTopActions.IsEnabled=!blocked;
        if(sidebarFooter!=null)sidebarFooter.IsHitTestVisible=!blocked;
        stop.Visibility=generation!=null?Visibility.Visible:Visibility.Collapsed;
        send.Visibility=generation==null?Visibility.Visible:Visibility.Collapsed;
    }
    async Task SendAsync()
    {
        if(!ready || importingDocument || generation!=null || changing || loadingHistory || string.IsNullOrWhiteSpace(input.Text) && attachments.Count==0)return;
        if(providers.SelectedItem is not Provider provider) {await ConfigureProviderAsync();return;}
        if(currentChat==null)await CreateChatInProjectAsync(currentProject,true);if(currentChat is not Chat chat)return;
        var text=input.Text;var images=attachments.ToArray();bool accepted=false;var previousBubbles=messages.Children.Count;input.Text="";attachments.Clear();RefreshAttachments();
        if(previousBubbles==0) {scroll.Content=messageRail;scroll.VerticalContentAlignment=VerticalAlignment.Top;}
        Bubble("user",text+(images.Length>0?"\n"+string.Join(", ",images.Select(x=>x.Name)):""));var reply=Bubble("assistant","");activeReplyCard=(Border)messages.Children.Last();activeReplyCard.Visibility=Visibility.Collapsed;activeToolBubble=null;
        generation=new();var revision=++runRevision;SetBusy(true);skillSuggestions.Visibility=Visibility.Collapsed;SetStatus("Envoi…");
        try
        {
            using var tools=new MobileTools(MobileServices.DataDirectory,chat.ProjectId,chat.Id,mobileSettings,ApproveToolAsync);
            var prompt=MobileSkills.Prompt(mobileSettings,text,MobileTools.Information(mobileSettings));
            prompt+="\nCONVERSATION DOCUMENTS (data; inspect with file tools): "+System.Text.Json.JsonSerializer.Serialize(new MobileFiles(MobileServices.DataDirectory,chat.Id).List());
            var options=new ConversationRunOptions(prompt,tools,mobileSettings.MaxToolRounds,activity=>DispatcherQueue.TryEnqueue(()=>{if(runRevision==revision)UpdateToolActivity(activity);}));
            var service=new ConversationService(store,vault,new ChatEngine(http,MobileServices.ChatRuntime));
            // Android native response streams may synchronously read before their async continuation.
            // Keep the entire request/tool loop on a worker; approval and rendering marshal explicitly.
            var answer=await Task.Run(()=>service.SendAsync(provider,chat.Id,text,images,update=>DispatcherQueue.TryEnqueue(()=> {
                if(runRevision!=revision)return;reply.Text=update.Text;activeReplyCard!.Visibility=update.Text.Length>0?Visibility.Visible:Visibility.Collapsed;SetStatus(update.Retry?.Describe("fr")??(update.Reasoning.Length>0 && update.Text.Length==0?"Réflexion…":"Réponse en cours…"));ScrollDown();
            }),generation.Token,userSaved:()=>accepted=true,options:options),generation.Token);
            reply.Text=answer.Content;reply.Flush();activeReplyCard!.Visibility=answer.Content.Length>0?Visibility.Visible:Visibility.Collapsed;SetStatus("Réponse terminée.");
        }
        catch(Exception ex) when(ex is not OperationCanceledException) {
            SetStatus(ex.Message,true);
        }
        catch(OperationCanceledException){reply.Text+="\n[Réponse interrompue]";SetStatus("Réponse interrompue ; le texte reçu est conservé.");}
        finally {
            if(!accepted) {while(messages.Children.Count>previousBubbles)messages.Children.RemoveAt(messages.Children.Count-1);input.Text=text;attachments.AddRange(images);RefreshAttachments();}
            reply.Flush();activeToolBubble=null;activeReplyCard=null;runRevision++;generation.Dispose();generation=null;SetBusy(false);ScrollDown();
        }
        if(accepted)await LoadChatsAsync();
    }
    public void SuspendGeneration()=>generation?.Cancel();
    void ScrollDown()=>scroll.DispatcherQueue.TryEnqueue(()=>{scroll.UpdateLayout();scroll.ChangeView(null,scroll.ScrollableHeight,null,true);});
    void RefreshAttachments() {attachmentsLabel.Text=string.Join(", ",attachments.Select(x=>x.Name).Concat(conversationDocuments.Select(x=>x.Path)));attachmentsLabel.Visibility=attachments.Count+conversationDocuments.Count>0?Visibility.Visible:Visibility.Collapsed;}
    async Task AttachAsync() {if(generation!=null)return;var image=await MainActivity.PickImageAsync();if(image!=null){attachments.Add(image);RefreshAttachments();}}
    async Task ConfigureProviderAsync()
    {
        if(generation!=null)return;
        var existing=providers.SelectedItem as Provider;var draft=existing==null?new Provider():System.Text.Json.JsonSerializer.Deserialize<Provider>(System.Text.Json.JsonSerializer.Serialize(existing))!;
        var name=new TextBox {Header="Nom",Text=draft.Name};var url=new TextBox {Header="URL API (HTTPS)",Text=draft.BaseUrl};var model=new TextBox {Header="Modèle",Text=draft.Model};var key=new PasswordBox {Header="Clé API",PlaceholderText=existing==null?"Votre clé API":"Laissez vide pour conserver la clé"};
        var list=new ComboBox {PlaceholderText="Modèles disponibles",HorizontalAlignment=HorizontalAlignment.Stretch};var detect=new Button {Content="Charger les modèles"};
        var detail=new TextBlock {TextWrapping=TextWrapping.Wrap};var panel=new StackPanel {Spacing=10};foreach(var item in new UIElement[]{name,url,key,detect,list,model,detail})panel.Children.Add(item);
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is string selected)model.Text=selected;};
        detect.Click+=async(_,_)=> {
            detect.IsEnabled=false;
            try {ValidateEndpoint(url.Text);draft.BaseUrl=url.Text.Trim();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));var credential=key.Password.Length>0?key.Password:vault.Unprotect(draft.ProtectedKey);var names=await Task.Run(()=>new ChatEngine(http,MobileServices.ChatRuntime).ModelsAsync(draft,credential,timeout.Token),timeout.Token);list.ItemsSource=names;draft.DetectedModelsJson=System.Text.Json.JsonSerializer.Serialize(names);detail.Text=$"{names.Count} modèles disponibles.";}
            catch(Exception ex){detail.Text=ex.Message;}finally{detect.IsEnabled=true;}
        };
        var dialog=CreateDialog("Fournisseur API",panel,"Enregistrer","Fermer");
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        ValidateEndpoint(url.Text);if(string.IsNullOrWhiteSpace(model.Text))throw new ArgumentException("Le nom du modèle est requis.");
        draft.Name=name.Text.Trim();draft.BaseUrl=url.Text.Trim();draft.Model=model.Text.Trim();draft.Kind="openai";
        if(key.Password.Length>0)draft.ProtectedKey=vault.Protect(key.Password);key.Password="";
        await store.SaveProviderAsync(draft);providers.ItemsSource=await store.ProvidersAsync();providers.SelectedItem=((List<Provider>)providers.ItemsSource).First(x=>x.Id==draft.Id);SetStatus("Configuration enregistrée.");
    }
    static void ValidateEndpoint(string url) {if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0)throw new ArgumentException("Utilisez une URL HTTPS sans identifiants dans l’URL.");}
}
