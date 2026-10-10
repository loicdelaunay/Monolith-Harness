using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using MonolithHarness.Core.Mobile;

namespace MonolithHarnessGui.Portable;

public sealed class MainPage : Page
{
    readonly MobileConversationStore store=new(MobileServices.DataDirectory);
    readonly AndroidSecretVault vault=new();
    readonly HttpClient http=new() { Timeout=Timeout.InfiniteTimeSpan };
    readonly ComboBox projects=new() { PlaceholderText="Projet",MinWidth=100,HorizontalAlignment=HorizontalAlignment.Stretch };
    readonly ComboBox chats=new() { PlaceholderText="Discussion",HorizontalAlignment=HorizontalAlignment.Stretch };
    readonly ComboBox providers=new() { PlaceholderText="Fournisseur API",HorizontalAlignment=HorizontalAlignment.Stretch };
    readonly StackPanel messages=new() {Spacing=12};
    readonly ScrollViewer scroll=new() {VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBox input=new() { PlaceholderText="Écrivez votre message…",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=64,MaxHeight=140,HorizontalAlignment=HorizontalAlignment.Stretch };
    readonly TextBlock status=new() { Text="Monolith Harness Portable",TextWrapping=TextWrapping.Wrap,Opacity=.75 };
    readonly TextBlock attachmentsLabel=new() {TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
    readonly Button send=new() {Content="Envoyer",MinWidth=84};
    readonly Button stop=new() {Content="Arrêter",Visibility=Visibility.Collapsed};
    readonly Button attach=new() {Content=new SymbolIcon(Symbol.Attach)};
    readonly Button settings=new() {Content=new SymbolIcon(Symbol.Setting)};
    readonly List<Attachment> attachments=[];
    CancellationTokenSource? generation;
    bool initialized,changing;
    int runRevision, navigationRevision, historyRevision;
    bool loadingHistory;
    public MainPage()
    {
        Background=new SolidColorBrush(ColorHelper.FromArgb(255,27,28,32));
        var layout=new Grid {Padding=new(12),RowSpacing=10};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto}) layout.RowDefinitions.Add(new() {Height=height});
        var header=new Grid {ColumnSpacing=8}; header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.Children.Add(projects);
        var projectButton=new Button {Content="+ Projet"};Grid.SetColumn(projectButton,1);header.Children.Add(projectButton);
        projectButton.Click+=async(_,_)=>await Guard(CreateProjectAsync);
        var conversations=new Grid {ColumnSpacing=8};conversations.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});conversations.ColumnDefinitions.Add(new(){Width=GridLength.Auto});conversations.Children.Add(chats);
        var newChat=new Button {Content=new SymbolIcon(Symbol.Add)};Grid.SetColumn(newChat,1);conversations.Children.Add(newChat);newChat.Click+=async(_,_)=>await Guard(CreateChatAsync);
        scroll.Content=messages;
        var compose=new StackPanel {Spacing=8};compose.Children.Add(attachmentsLabel);compose.Children.Add(input);
        var controls=new Grid {ColumnSpacing=6};controls.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});foreach(var _ in Enumerable.Range(0,4)) controls.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        controls.Children.Add(providers);int column=1;foreach(var control in new UIElement[]{settings,attach,stop,send}) {Grid.SetColumn(control,column++);controls.Children.Add(control);}compose.Children.Add(controls);
        int row=0;foreach(var item in new UIElement[]{header,conversations,scroll,compose,status}) {Grid.SetRow(item,row++);layout.Children.Add(item);}Content=layout;
        projects.SelectionChanged+=async(_,_)=>{if(!changing && generation==null) await Guard(LoadChatsAsync);};
        chats.SelectionChanged+=async(_,_)=>{if(!changing && generation==null) await Guard(RenderHistoryAsync);};
        send.Click+=async(_,_)=>await Guard(SendAsync);stop.Click+=(_,_)=>generation?.Cancel();settings.Click+=async(_,_)=>await Guard(ConfigureProviderAsync);
        attach.Click+=async(_,_)=>await Guard(AttachAsync);
        Loaded+=async(_,_)=>{if(initialized)return;initialized=true;await Guard(InitializeAsync);};
    }
    async Task Guard(Func<Task> action) { try {await action();} catch(OperationCanceledException) {status.Text="Réponse interrompue ; le texte reçu est conservé.";} catch(Exception ex){status.Text=ex.Message;} }
    async Task InitializeAsync()
    {
        await store.InitializeAsync();changing=true;projects.ItemsSource=await store.ProjectsAsync();projects.SelectedIndex=0;providers.ItemsSource=await store.ProvidersAsync();providers.SelectedIndex=0;changing=false;
        await LoadChatsAsync();status.Text=providers.SelectedItem==null?"Configurez un fournisseur API pour commencer.":"Prêt";
    }
    async Task LoadChatsAsync()
    {
        if(projects.SelectedItem is not Project project)return;
        var revision=++navigationRevision;var previous=(chats.SelectedItem as Chat)?.Id;changing=true;
        try {
            var items=await store.ChatsAsync(project.Id);
            if(revision!=navigationRevision || (projects.SelectedItem as Project)?.Id!=project.Id)return;
            chats.ItemsSource=items;chats.SelectedItem=items.FirstOrDefault(x=>x.Id==previous)??items.FirstOrDefault();
            await RenderHistoryAsync();
        } finally {if(revision==navigationRevision)changing=false;}
    }
    TextBlock Bubble(string role,string text)
    {
        var body=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=15};
        var card=new StackPanel {Spacing=6};card.Children.Add(new TextBlock{Text=role=="user"?"Vous":"Assistant",Opacity=.6,FontSize=12});card.Children.Add(body);
        messages.Children.Add(new Border {Child=card,Padding=new(12),CornerRadius=new(14),Background=new SolidColorBrush(role=="user"?ColorHelper.FromArgb(255,44,65,79):ColorHelper.FromArgb(255,39,40,45))});return body;
    }
    async Task RenderHistoryAsync()
    {
        var revision=++historyRevision;var chat=chats.SelectedItem as Chat;loadingHistory=true;
        try {
            var history=chat==null?[]:await store.MessagesAsync(chat.Id);
            if(revision!=historyRevision || (chats.SelectedItem as Chat)?.Id!=chat?.Id)return;
            messages.Children.Clear();
            if(chat==null) {var launch=new Button{Content="Lancer une nouvelle discussion",HorizontalAlignment=HorizontalAlignment.Center};launch.Click+=async(_,_)=>await Guard(CreateChatAsync);messages.Children.Add(launch);return;}
            foreach(var message in history) {
                var text=message.Content;if(message.Attachments.Count>0)text+="\n"+string.Join(", ",message.Attachments.Select(x=>x.Name));
                if(message.State=="interrupted" || message.State=="generating")text+="\n[Réponse interrompue]";
                if(text.Length>0)Bubble(message.Role,text);
            }
            ScrollDown();
        }finally {if(revision==historyRevision)loadingHistory=false;}
    }
    async Task CreateChatAsync()
    {
        if(generation!=null || projects.SelectedItem is not Project project)return;
        var chat=await store.CreateChatAsync(project.Id);
        await LoadChatsAsync();
        if((projects.SelectedItem as Project)?.Id!=project.Id)return;
        changing=true;
        try {
            chats.SelectedItem=((List<Chat>)chats.ItemsSource).First(x=>x.Id==chat.Id);
            await RenderHistoryAsync();
        } finally { changing=false; }
    }
    async Task CreateProjectAsync()
    {
        if(generation!=null)return;var name=new TextBox {PlaceholderText="Nom du projet"};var dialog=new ContentDialog {XamlRoot=XamlRoot,Title="Nouveau projet",Content=name,PrimaryButtonText="Créer",CloseButtonText="Annuler",DefaultButton=ContentDialogButton.Primary};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text))return;
        var project=await store.CreateProjectAsync(name.Text);changing=true;projects.ItemsSource=await store.ProjectsAsync();projects.SelectedItem=((List<Project>)projects.ItemsSource).First(x=>x.Id==project.Id);changing=false;await LoadChatsAsync();
    }
    void SetBusy(bool busy)
    {
        input.IsEnabled=projects.IsEnabled=chats.IsEnabled=providers.IsEnabled=settings.IsEnabled=attach.IsEnabled=send.IsEnabled=!busy;
        stop.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
    }
    async Task SendAsync()
    {
        if(generation!=null || changing || loadingHistory || string.IsNullOrWhiteSpace(input.Text) && attachments.Count==0)return;
        if(providers.SelectedItem is not Provider provider) {await ConfigureProviderAsync();return;}
        if(chats.SelectedItem is not Chat)await CreateChatAsync();if(chats.SelectedItem is not Chat chat)return;
        var text=input.Text;var images=attachments.ToArray();bool accepted=false;var previousBubbles=messages.Children.Count;input.Text="";attachments.Clear();RefreshAttachments();
        Bubble("user",text+(images.Length>0?"\n"+string.Join(", ",images.Select(x=>x.Name)):""));var reply=Bubble("assistant","");
        generation=new();var revision=++runRevision;SetBusy(true);status.Text="Envoi…";
        try
        {
            var service=new ConversationService(store,vault,new ChatEngine(http,MobileServices.ChatRuntime));
            var answer=await service.SendAsync(provider,chat.Id,text,images,update=>DispatcherQueue.TryEnqueue(()=> {
                if(runRevision!=revision)return;reply.Text=update.Text;status.Text=update.Retry?.Describe("fr")??(update.Reasoning.Length>0 && update.Text.Length==0?"Réflexion…":"Réponse en cours…");ScrollDown();
            }),generation.Token,userSaved:()=>accepted=true);
            reply.Text=answer.Content;status.Text="Réponse terminée.";
        }
        catch(Exception ex) when(ex is not OperationCanceledException) {
            status.Text=ex.Message;
        }
        catch(OperationCanceledException){reply.Text+="\n[Réponse interrompue]";status.Text="Réponse interrompue ; le texte reçu est conservé.";}
        finally {
            if(!accepted) {while(messages.Children.Count>previousBubbles)messages.Children.RemoveAt(messages.Children.Count-1);input.Text=text;attachments.AddRange(images);RefreshAttachments();}
            runRevision++;generation.Dispose();generation=null;SetBusy(false);ScrollDown();
        }
        if(accepted)await LoadChatsAsync();
    }
    public void SuspendGeneration()=>generation?.Cancel();
    void ScrollDown()=>scroll.DispatcherQueue.TryEnqueue(()=>{scroll.UpdateLayout();scroll.ChangeView(null,scroll.ScrollableHeight,null,true);});
    void RefreshAttachments() {attachmentsLabel.Text=string.Join(", ",attachments.Select(x=>x.Name));attachmentsLabel.Visibility=attachments.Count>0?Visibility.Visible:Visibility.Collapsed;}
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
            try {ValidateEndpoint(url.Text);draft.BaseUrl=url.Text.Trim();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));var names=await new ChatEngine(http,MobileServices.ChatRuntime).ModelsAsync(draft,key.Password.Length>0?key.Password:vault.Unprotect(draft.ProtectedKey),timeout.Token);list.ItemsSource=names;draft.DetectedModelsJson=System.Text.Json.JsonSerializer.Serialize(names);detail.Text=$"{names.Count} modèles disponibles.";}
            catch(Exception ex){detail.Text=ex.Message;}finally{detect.IsEnabled=true;}
        };
        var dialog=new ContentDialog {XamlRoot=XamlRoot,Title="Fournisseur API",Content=new ScrollViewer {Content=panel,MaxHeight=480},PrimaryButtonText="Enregistrer",CloseButtonText="Fermer",DefaultButton=ContentDialogButton.Primary};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        ValidateEndpoint(url.Text);if(string.IsNullOrWhiteSpace(model.Text))throw new ArgumentException("Le nom du modèle est requis.");
        draft.Name=name.Text.Trim();draft.BaseUrl=url.Text.Trim();draft.Model=model.Text.Trim();draft.Kind="openai";
        if(key.Password.Length>0)draft.ProtectedKey=vault.Protect(key.Password);key.Password="";
        await store.SaveProviderAsync(draft);providers.ItemsSource=await store.ProvidersAsync();providers.SelectedItem=((List<Provider>)providers.ItemsSource).First(x=>x.Id==draft.Id);status.Text="Configuration enregistrée.";
    }
    static void ValidateEndpoint(string url) {if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0)throw new ArgumentException("Utilisez une URL HTTPS sans identifiants dans l’URL.");}
}
