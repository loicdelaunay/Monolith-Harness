using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using MonolithHarness.Core.Mobile;
namespace MonolithHarnessGui.Portable;

public sealed partial class MainPage
{
    readonly Border skillSuggestions=new() {Visibility=Visibility.Collapsed,CornerRadius=new(16),Padding=new(4)};
    bool completingSkill,importingDocument;
    IReadOnlyList<MobileDocument> conversationDocuments=[];
    void RefreshSkillSuggestions()
    {
        if(completingSkill || generation!=null)return;
        if(layoutMetrics.Minimal){skillSuggestions.Visibility=Visibility.Collapsed;return;}
        var token=CommonSkills.CompletionAt(input.Text,input.SelectionStart,input.SelectionLength);
        if(token==null){skillSuggestions.Visibility=Visibility.Collapsed;return;}
        var matches=MobileSkills.All.Where(s=>mobileSettings.Enabled(s.Id) && s.Id.StartsWith(token.Prefix,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length==0){skillSuggestions.Visibility=Visibility.Collapsed;return;}
        var items=new StackPanel {Spacing=2};
        foreach(var skill in matches) {
            var button=new Button {Content=$"/{skill.Id} · {skill.FrenchName}",MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,CornerRadius=new(12)};
            button.Click+=(_,_)=> {
                completingSkill=true;
                var insert="/"+skill.Id+" ";input.Text=input.Text[..token.Start]+insert+input.Text[(token.Start+token.Length)..];input.SelectionStart=token.Start+insert.Length;input.SelectionLength=0;
                completingSkill=false;skillSuggestions.Visibility=Visibility.Collapsed;input.Focus(FocusState.Programmatic);
            };items.Children.Add(button);
        }
        skillSuggestions.Background=Brush("SecondaryContainerBrush");skillSuggestions.Child=new ScrollViewer {Content=items,MaxHeight=Math.Max(48,Math.Min(144,layoutMetrics.UsableHeight*.25)),HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};skillSuggestions.Visibility=Visibility.Visible;
    }
    void ShowAttachmentMenu()
    {
        if(generation!=null || importingDocument)return;
        var menu=new MenuFlyout();
        var image=new MenuFlyoutItem {Text="Joindre une image"};image.Click+=async(_,_)=>await Guard(AttachAsync);menu.Items.Add(image);
        var document=new MenuFlyoutItem {Text="Importer un document texte"};document.Click+=async(_,_)=>await Guard(ImportDocumentAsync);menu.Items.Add(document);
        var files=new MenuFlyoutItem {Text="Fichiers de cette discussion"};files.Click+=async(_,_)=>await Guard(ShowDocumentsAsync);menu.Items.Add(files);menu.ShowAt(attach);
    }
    async Task ImportDocumentAsync()
    {
        if(generation!=null || importingDocument)return;
        importingDocument=true;SetBusy(true);
        try {
            if(currentChat==null)await CreateChatInProjectAsync(currentProject,true);if(currentChat is not Chat chat)return;
            var document=await MainActivity.PickDocumentAsync();if(document==null)return;
            var workspace=new MobileFiles(MobileServices.DataDirectory,chat.Id);var path=await workspace.ImportAsync(document.Name,document.Data);
            conversationDocuments=workspace.List();RefreshAttachments();SetStatus("Document importé : "+path);
        } finally {importingDocument=false;SetBusy(false);}
    }
    async Task ShowDocumentsAsync()
    {
        if(currentChat is not Chat chat)return;
        var workspace=new MobileFiles(MobileServices.DataDirectory,chat.Id);
        while(true) {
            string? selected=null;var list=SettingsPanel();list.Children.Add(SettingsText("Copies privées de cette discussion. Les documents modifiés par le modèle peuvent être consultés puis exportés vers le dossier Android de votre choix."));
            foreach(var document in workspace.List()) {var button=SettingsButton($"{document.Path} · {document.Bytes/1024d:0.#} Ko");button.Click+=(_,_)=>{selected=document.Path;DismissDialog();};list.Children.Add(button);}
            if(list.Children.Count==1)list.Children.Add(SettingsText("Aucun document texte importé."));
            await ShowDialogAsync(CreateDialog("Fichiers",list,"","Fermer"));if(selected==null)return;
            var text=await workspace.ReadAsync(selected);var preview=new TextBox {Text=text[..Math.Min(50000,text.Length)],IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=320,FontSize=14};
            var body=SettingsPanel();body.Children.Add(SettingsText(text.Length>50000?"Aperçu limité à 50000 caractères. L’export contient tout le document.":"Document texte de cette discussion."));body.Children.Add(preview);
            if(await ShowDialogAsync(CreateDialog(selected,body,"Exporter","Retour"))==ContentDialogResult.Primary && await MainActivity.ExportDocumentAsync(selected,text))SetStatus("Document exporté.");
            conversationDocuments=workspace.List();RefreshAttachments();
        }
    }
    MobileToolBubble NewToolBubble()=>new() {MaxWidth=Math.Max(48,layoutMetrics.ContentWidth*.96)};
    void UpdateToolActivity(ConversationToolActivity activity)
    {
        if(!mobileSettings.ShowToolActivity)return;
        if(activeToolBubble==null) {
            activeToolBubble=NewToolBubble();
            var position=activeReplyCard==null?-1:messages.Children.IndexOf(activeReplyCard);
            if(position>=0)messages.Children.Insert(position,activeToolBubble);else messages.Children.Add(activeToolBubble);
        }
        activeToolBubble.Update(activity);ScrollDown();
    }
}
