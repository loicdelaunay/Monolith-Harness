using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using MonolithHarness.Core.Mobile;
namespace MonolithHarnessGui.Portable;

public sealed partial class MainPage
{
    MobileSettings mobileSettings=new();
    static TextBlock SettingsText(string text,double size=14)=>new() {Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush("OnSurfaceVariantBrush")};
    static StackPanel SettingsPanel()=>new() {Spacing=12};
    static Button SettingsButton(string text)=>new() {Background=Brush("SecondaryContainerBrush"),Foreground=Brush("OnSecondaryContainerBrush"),Content=text,MinHeight=48,CornerRadius=new(24),HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new(16,8,16,8)};
    static void Option(StackPanel panel,string name,string detail,bool initial,Action<bool> changed)
    {
        // Material switch templates do not render WinUI Header/OnContent consistently on Skia.
        // Keep the label outside the control and expose it to accessibility explicitly.
        var toggle=new ToggleSwitch {IsOn=initial,OnContent="",OffContent="",MinHeight=48,VerticalAlignment=VerticalAlignment.Center};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,name);
        var label=SettingsText(name,16);label.Foreground=Brush("OnSurfaceBrush");label.VerticalAlignment=VerticalAlignment.Center;
        var row=new Grid {ColumnSpacing=12};row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.Children.Add(label);Grid.SetColumn(toggle,1);row.Children.Add(toggle);
        toggle.Toggled+=(_,_)=>changed(toggle.IsOn);var card=SettingsPanel();card.Children.Add(row);card.Children.Add(SettingsText(detail));
        panel.Children.Add(new Border {Child=card,Padding=new(16),CornerRadius=new(20),Background=Brush("SurfaceBrush"),BorderBrush=Brush("OutlineVariantBrush"),BorderThickness=new(1)});
    }
    void ApplySettings()
    {
        input.FontSize=mobileSettings.TextSize;RefreshInputHeight();
        RefreshSkillSuggestions();
    }
    async Task ShowSettingsAsync()
    {
        if(generation!=null)return;
        while(generation==null)
        {
            string? section=null;var menu=SettingsPanel();
            foreach(var item in new[]{("Général","Lecture et comportement du chat"),("Fournisseurs","Modèles API et clés"),("Skills","Capacités et informations transmises au modèle"),("Autorisations","Web, modifications et mémoire partagée"),("Mémoire","Souvenirs de cette discussion et de son projet"),("À propos","Version et capacités Android")}) {
                var card=SettingsPanel();card.Children.Add(SettingsText(item.Item1,18));card.Children.Add(SettingsText(item.Item2));
                foreach(var label in card.Children.OfType<TextBlock>())label.Foreground=Brush("OnSecondaryContainerBrush");
                var button=SettingsButton("");button.Content=card;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                button.Click+=(_,_)=>{section=item.Item1;DismissDialog();};menu.Children.Add(button);
            }
            await ShowDialogAsync(CreateDialog("Paramètres",menu,"","Fermer"));if(section==null)return;
            if(section=="Fournisseurs")await ShowProvidersSettingsAsync();
            else if(section=="Mémoire")await ShowMemorySettingsAsync();
            else if(section=="À propos")await ShowAboutAsync();
            else await EditSettingsAsync(section);
        }
    }
    async Task EditSettingsAsync(string section)
    {
        var draft=mobileSettings.Copy();var panel=SettingsPanel();
        if(section=="Général") {
            panel.Children.Add(SettingsText("Ces réglages sont propres à l’application Android."));
            panel.Children.Add(SettingsText("Langue des réponses"));
            var language=new ComboBox {ItemsSource=new[]{"Langue de votre message","Français","English"},SelectedIndex=draft.ReplyLanguage=="fr"?1:draft.ReplyLanguage=="en"?2:0,HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=48};
            language.SelectionChanged+=(_,_)=>draft.ReplyLanguage=language.SelectedIndex switch {1=>"fr",2=>"en",_=>"auto"};panel.Children.Add(language);
            panel.Children.Add(SettingsText("Taille du texte"));var size=new Slider {Minimum=14,Maximum=22,StepFrequency=1,Value=draft.TextSize,MinHeight=48};size.ValueChanged+=(_,_)=>draft.TextSize=size.Value;panel.Children.Add(size);
            Option(panel,"Afficher les activités des outils","Regroupe les appels et leurs résultats dans une bulle dépliable qui se met à jour. Les résultats restent conservés même si l’affichage est masqué.",draft.ShowToolActivity,x=>draft.ShowToolActivity=x);
            var rounds=new NumberBox {Header="Nombre maximal de tours d’outils par message",Minimum=1,Maximum=16,Value=draft.MaxToolRounds,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline};rounds.ValueChanged+=(_,_)=>{if(double.IsFinite(rounds.Value))draft.MaxToolRounds=(int)rounds.Value;};panel.Children.Add(rounds);
        }
        else if(section=="Skills") {
            panel.Children.Add(SettingsText("Activez les skills utiles. Tapez /summary, /planning, /review, /informations, /web, /sources ou /memory dans votre message pour en demander explicitement l’utilisation à l’envoi."));
            foreach(var skill in MobileSkills.All)Option(panel,skill.FrenchName,skill.FrenchDescription,draft.Enabled(skill.Id),x=>{draft.EnabledSkills.Remove(skill.Id);if(x)draft.EnabledSkills.Add(skill.Id);});
            panel.Children.Add(SettingsText("Informations · catégories",18));
            Option(panel,"Informations automatiques","Ajoute les catégories choisies à chaque message quand le skill Informations est activé. Sinon le modèle peut appeler son outil.",draft.AutomaticInformation,x=>draft.AutomaticInformation=x);
            Option(panel,"Date et heure","Date, jour et heures locale/UTC actualisés à l’envoi.",draft.InformationDateTime,x=>draft.InformationDateTime=x);
            Option(panel,"Fuseau horaire","Fuseau horaire actuel du téléphone.",draft.InformationTimeZone,x=>draft.InformationTimeZone=x);
            Option(panel,"Système Android","Nom, version Android et niveau API. Aucun identifiant de l’appareil.",draft.InformationSystem,x=>draft.InformationSystem=x);
            Option(panel,"Langue et région","Langue et formats régionaux du téléphone.",draft.InformationLocale,x=>draft.InformationLocale=x);
        }
        else {
            panel.Children.Add(SettingsText("Approbation des appels d’outils",18));
            var mode=new ComboBox {ItemsSource=new[]{"Refuser tout","Demander tout","Autoriser tout"},SelectedIndex=draft.ApprovalMode=="deny"?0:draft.ApprovalMode=="allow"?2:1,HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=48};
            AccessibleName(mode,"Mode d’autorisation des outils");panel.Children.Add(mode);
            var modeHelp=SettingsText("");panel.Children.Add(modeHelp);
            void DescribeMode() {
                draft.ApprovalMode=mode.SelectedIndex switch {0=>"deny",2=>"allow",_=>"ask"};
                modeHelp.Text=draft.ApprovalMode switch {
                    "deny"=>"Tous les appels d’outils du modèle sont refusés. Le chat reste disponible.",
                    "allow"=>"Les outils activés sont autorisés sans confirmation, y compris le web et les écritures. Les accès désactivés et les limites des fichiers restent respectés.",
                    _=>"Demande votre accord avant chaque appel d’outil. Mode par défaut."
                };
            }
            mode.SelectionChanged+=(_,_)=>DescribeMode();DescribeMode();
            panel.Children.Add(SettingsText("Les skills et les autorisations s’appliquent ensemble. Les fichiers accessibles restent limités aux copies importées dans cette discussion."));
            Option(panel,"Autoriser le web","Recherche et lecture de pages Internet publiques HTTPS. Les pages lues sont envoyées au modèle API.",draft.AllowNetwork,x=>draft.AllowNetwork=x);
            Option(panel,"Autoriser les modifications de fichiers","Permet de modifier les copies privées de cette discussion. L’export vers Android reste une action manuelle.",draft.AllowFileWrites,x=>draft.AllowFileWrites=x);
            Option(panel,"Autoriser la mémoire partagée","Rend les souvenirs de portée Partagée disponibles entre projets. Conversation et Projet restent toujours séparés.",draft.AllowSharedMemory,x=>draft.AllowSharedMemory=x);
        }
        if(await ShowDialogAsync(CreateDialog(section,panel,"Enregistrer","Retour"))==ContentDialogResult.Primary) {
            await draft.SaveAsync(MobileServices.DataDirectory);mobileSettings=draft;ApplySettings();await RenderHistoryAsync();SetStatus("Paramètres enregistrés.");
        }
    }
    async Task ShowProvidersSettingsAsync()
    {
        while(true) {
            Func<Task>? action=null;var panel=SettingsPanel();panel.Children.Add(SettingsText("Les clés API sont chiffrées avec Android Keystore. Le modèle choisi est utilisé pour la discussion et ses skills."));
            var existing=await store.ProvidersAsync();
            foreach(var provider in existing) {
                var button=SettingsButton(provider.Name+" · "+provider.Model);button.Click+=(_,_)=>{action=async()=>{providers.SelectedItem=((IEnumerable<Provider>)providers.ItemsSource).FirstOrDefault(p=>p.Id==provider.Id);await ConfigureProviderAsync();};DismissDialog();};panel.Children.Add(button);
            }
            var add=SettingsButton("Ajouter un fournisseur");add.Click+=(_,_)=>{action=async()=>{var previous=providers.SelectedItem as Provider;providers.SelectedItem=null;await ConfigureProviderAsync();if(providers.SelectedItem==null && previous!=null)providers.SelectedItem=((IEnumerable<Provider>)providers.ItemsSource).FirstOrDefault(p=>p.Id==previous.Id);};DismissDialog();};panel.Children.Add(add);
            await ShowDialogAsync(CreateDialog("Fournisseurs",panel,"","Retour"));if(action==null)return;await action();
        }
    }
    async Task ShowMemorySettingsAsync()
    {
        var panel=SettingsPanel();panel.Children.Add(SettingsText("Les souvenirs sont conservés localement. Le modèle peut consulter ceux de cette conversation, de son projet et, si activée, de la portée Partagée. Ne mémorisez pas de clés ni de mots de passe."));
        if(currentChat is not Chat chat || currentProject is not Project project) {panel.Children.Add(SettingsText("Créez ou sélectionnez une discussion pour gérer sa mémoire."));await ShowDialogAsync(CreateDialog("Mémoire",panel,"","Retour"));return;}
        var memory=new MobileMemory(MobileServices.DataDirectory,project.Id,chat.Id,mobileSettings.AllowSharedMemory);
        var query=new TextBox {Header="Rechercher un souvenir"};var find=SettingsButton("Rechercher");var entries=SettingsPanel();var scope=new ComboBox {Header="Portée",ItemsSource=mobileSettings.AllowSharedMemory?new[]{"Conversation","Projet","Partagée"}:new[]{"Conversation","Projet"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        var content=new TextBox {Header="Nouveau souvenir",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=4000,MaxHeight=160};var add=SettingsButton("Enregistrer ce souvenir");var error=SettingsText("");
        foreach(var item in new UIElement[]{query,find,entries,scope,content,add,error})panel.Children.Add(item);
        async Task Refresh() {
            entries.Children.Clear();foreach(var entry in await memory.SearchAsync(query.Text)) {
                var card=SettingsPanel();card.Children.Add(SettingsText($"{entry.Scope} · #{entry.Id}",12));card.Children.Add(SettingsText(entry.Content));var remove=SettingsButton("Supprimer ce souvenir");
                remove.Click+=async(_,_)=> {remove.IsEnabled=false;try{await memory.DeleteAsync(entry.Id);await Refresh();}catch(Exception ex){error.Text=ex.Message;remove.IsEnabled=true;}};card.Children.Add(remove);entries.Children.Add(new Border {Child=card,Padding=new(12),CornerRadius=new(16),BorderBrush=Brush("OutlineVariantBrush"),BorderThickness=new(1)});
            }
            if(entries.Children.Count==0)entries.Children.Add(SettingsText("Aucun souvenir dans ces portées."));
        }
        find.Click+=async(_,_)=>{find.IsEnabled=false;try{await Refresh();}catch(Exception ex){error.Text=ex.Message;}finally{find.IsEnabled=true;}};
        add.Click+=async(_,_)=>{add.IsEnabled=false;try{await memory.SaveAsync(scope.SelectedIndex switch {1=>"project",2=>"shared",_=>"conversation"},content.Text);content.Text="";await Refresh();}catch(Exception ex){error.Text=ex.Message;}finally{add.IsEnabled=true;}};
        await Refresh();await ShowDialogAsync(CreateDialog("Mémoire",panel,"","Retour"));
    }
    async Task ShowAboutAsync()
    {
        var panel=SettingsPanel();var context=Android.App.Application.Context;
#pragma warning disable CS0618
        var version=context.PackageManager!.GetPackageInfo(context.PackageName!,0)?.VersionName??"";
#pragma warning restore CS0618
        panel.Children.Add(SettingsText("Monolith · "+version,24));panel.Children.Add(SettingsText("Android · application portable",18));
        panel.Children.Add(SettingsText("Chat avec modèles API, images, documents texte UTF-8/UTF-16, skills, recherche web et mémoires locales. Les documents peuvent être lus et modifiés dans la copie privée, puis exportés."));
        panel.Children.Add(SettingsText("Markdown natif : titres, listes, tableaux, citations, liens et blocs de code. Les diagrammes Mermaid et formules restent présentés comme du texte ou du code.\n\nLes outils desktop de terminal, Git, Python, contrôle d’applications et les modèles locaux ne sont pas disponibles dans cette version. Le web lit le texte HTML/JSON ; les pages qui demandent JavaScript ou une connexion peuvent être inaccessibles."));
        await ShowDialogAsync(CreateDialog("À propos",panel,"","Retour"));
    }
    Task<bool> ApproveToolAsync(string heading,string detail,CancellationToken ct)
    {
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!DispatcherQueue.TryEnqueue(async()=> {
            try {
                ct.ThrowIfCancellationRequested();using var registration=ct.Register(()=>DispatcherQueue.TryEnqueue(()=>DismissDialog()));ct.ThrowIfCancellationRequested();
                var result=await ShowDialogAsync(CreateDialog(heading,SettingsText(detail,16),"Autoriser","Refuser"));ct.ThrowIfCancellationRequested();completion.TrySetResult(result==ContentDialogResult.Primary);
            } catch(OperationCanceledException){completion.TrySetCanceled(ct);}catch(Exception ex){completion.TrySetException(ex);}
        }))completion.TrySetException(new InvalidOperationException("Interface indisponible pour demander l’autorisation."));
        return completion.Task;
    }
}
