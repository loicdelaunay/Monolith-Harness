using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using MonolithHarness.Core;

namespace MonolithHarnessGui.Portable;

public sealed partial class MainPage
{
    readonly Grid layout=new() {RowSpacing=8};
    readonly Grid topBar=new() {ColumnSpacing=8};
    readonly Grid messageRail=new() {HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly TextBlock inputSizing=new() {TextWrapping=TextWrapping.Wrap};
    readonly Border composer=new() {CornerRadius=new(28),Padding=new(8)};
    readonly TextBlock projectCaption=new() {FontSize=12,MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock modelLabel=new() {Text="Configurer le modèle",FontSize=14,MaxLines=1,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    readonly Button providerButton=new();
    readonly Button projectMenu=new();
    readonly Button newChatButton=new();
    MobileLayout layoutMetrics;
    Border? welcomeIllustration;

    static Brush Brush(string key) => Application.Current.Resources.TryGetValue(key,out var resource) && resource is Brush brush
        ? brush : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    static void AccessibleName(DependencyObject control,string name) {AutomationProperties.SetName(control,name);if(control is FrameworkElement element)ToolTipService.SetToolTip(element,name);}
    void Icon(Button button,string icon,string name,bool accent=false)
    {
        button.Style=(Style)Application.Current.Resources["IconButtonStyle"];
        button.Content=MobileIcons.Create(icon);button.Width=48;button.Height=48;button.MinWidth=48;button.MinHeight=48;
        button.Padding=new(12);button.CornerRadius=new(24);button.BorderThickness=new(0);
        button.Background=Brush(accent?"PrimaryBrush":"SurfaceBrush");button.Foreground=Brush(accent?"OnPrimaryBrush":"OnSurfaceBrush");AccessibleName(button,name);
    }
    void BuildLayout()
    {
        Background=Brush("BackgroundBrush");Foreground=Brush("OnBackgroundBrush");
        if(Application.Current.Resources.TryGetValue("MaterialRegularFontFamily",out var font) && font is FontFamily family)FontFamily=family;
        foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto}) layout.RowDefinitions.Add(new(){Height=height});
        var header=topBar;header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        Icon(projectMenu,"menu","Projets et discussions");Icon(newChatButton,"add","Nouvelle discussion");Icon(settings,"settings","Paramètres");
        projectMenu.Click+=async(_,_)=>await Guard(OpenSidebarAsync);
        newChatButton.Click+=async(_,_)=>await Guard(CreateChatAsync);
        var model=new Grid {ColumnSpacing=6};model.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});model.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var context=new StackPanel {Spacing=0,VerticalAlignment=VerticalAlignment.Center};modelLabel.FontSize=16;modelLabel.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;context.Children.Add(modelLabel);context.Children.Add(projectCaption);model.Children.Add(context);
        var arrow=MobileIcons.Create("expand_more",18);arrow.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(arrow,1);model.Children.Add(arrow);
        providerButton.Content=model;providerButton.MinWidth=0;providerButton.MinHeight=48;providerButton.HorizontalAlignment=HorizontalAlignment.Stretch;providerButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        providerButton.CornerRadius=new(24);providerButton.Padding=new(8,4,8,4);providerButton.BorderThickness=new(0);providerButton.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);providerButton.Foreground=modelLabel.Foreground=Brush("OnSurfaceBrush");projectCaption.Foreground=Brush("OnSurfaceVariantBrush");AccessibleName(providerButton,"Choisir ou configurer le modèle");
        providerButton.Click+=async(_,_)=>await Guard(ChooseProviderAsync);
        int column=0;foreach(var control in new UIElement[]{projectMenu,providerButton,newChatButton,settings}) {Grid.SetColumn(control,column++);header.Children.Add(control);}
        messages.Spacing=12;messageRail.Children.Add(messages);
        scroll.Content=messageRail;scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.HorizontalScrollMode=ScrollMode.Disabled;
        scroll.VerticalScrollMode=ScrollMode.Enabled;scroll.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var compose=new Grid {RowSpacing=4};foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto})compose.RowDefinitions.Add(new(){Height=height});
        attachmentsLabel.FontSize=12;attachmentsLabel.MaxLines=2;attachmentsLabel.TextTrimming=TextTrimming.CharacterEllipsis;attachmentsLabel.Margin=new(12,4,12,0);attachmentsLabel.Foreground=Brush("OnSurfaceVariantBrush");
        // Use the plain native input inside the Material composer surface. Form-field
        // floating labels reserve an extra baseline that is unsuitable for a chat row.
        input.Style=Application.Current.Resources.TryGetValue("DefaultTextBoxStyle",out var nativeInput) && nativeInput is Style nativeStyle
            ? nativeStyle : new Style {TargetType=typeof(TextBox)};
        var clearInputBrush=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach(var key in new[]{"TextControlBackground","TextControlBackgroundFocused","TextControlBackgroundPointerOver","TextControlBorderBrush","TextControlBorderBrushFocused","TextControlBorderBrushPointerOver"})input.Resources[key]=clearInputBrush;
        input.GotFocus+=(_,_)=>composer.BorderBrush=Brush("PrimaryBrush");input.LostFocus+=(_,_)=>composer.BorderBrush=Brush("OutlineVariantBrush");
        input.BorderThickness=new(0);input.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);input.Foreground=Brush("OnSurfaceBrush");input.FontSize=16;input.Padding=new(4,12,4,12);input.MinHeight=48;input.CornerRadius=new(20);input.VerticalAlignment=VerticalAlignment.Center;input.VerticalContentAlignment=VerticalAlignment.Center;input.Height=48;AccessibleName(input,"Message");
        compose.Children.Add(attachmentsLabel);Grid.SetRow(skillSuggestions,1);compose.Children.Add(skillSuggestions);
        // Empty/short drafts share one row with the actions. Wrapping and explicit newlines
        // grow the input naturally up to the viewport cap, then scroll inside the TextBox.
        var controls=new Grid {ColumnSpacing=8};foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})controls.ColumnDefinitions.Add(new(){Width=width});
        Icon(attach,"add","Joindre une image ou un document");Icon(send,"send","Envoyer le message",true);Icon(stop,"stop","Arrêter la réponse");
        attach.VerticalAlignment=send.VerticalAlignment=stop.VerticalAlignment=VerticalAlignment.Bottom;
        controls.Children.Add(attach);Grid.SetColumn(input,1);controls.Children.Add(input);Grid.SetColumn(send,2);controls.Children.Add(send);Grid.SetColumn(stop,2);controls.Children.Add(stop);
        Grid.SetRow(controls,2);compose.Children.Add(controls);
        composer.Child=compose;composer.Background=Brush("SurfaceBrush");composer.BorderBrush=Brush("OutlineVariantBrush");composer.BorderThickness=new(1);
        status.FontSize=12;status.MaxLines=2;status.TextTrimming=TextTrimming.CharacterEllipsis;status.Foreground=Brush("ErrorBrush");status.Visibility=Visibility.Collapsed;
        int row=0;foreach(var item in new FrameworkElement[]{header,scroll,status,composer}) {item.MaxWidth=880;item.HorizontalAlignment=HorizontalAlignment.Stretch;Grid.SetRow(item,row++);layout.Children.Add(item);}Content=layout;
    }
    public void RefreshViewport()=>DispatcherQueue.TryEnqueue(ApplyViewport);
    void ApplyViewport()
    {
        if(ActualWidth<=0 || ActualHeight<=0)return;
        var next=MobileLayout.Calculate(ActualWidth,ActualHeight,MainActivity.ReadInsets());
        if(next==layoutMetrics)return;layoutMetrics=next;
        layout.Padding=new(next.Left,next.Top,next.Right,next.Bottom);
        topBar.Visibility=next.Minimal?Visibility.Collapsed:Visibility.Visible;
        input.MaxHeight=next.InputMaxHeight;RefreshInputHeight();
        if(welcomeIllustration!=null)welcomeIllustration.Visibility=next.ShowWelcomeIllustration?Visibility.Visible:Visibility.Collapsed;
        foreach(var bubble in messages.Children.OfType<Border>())bubble.MaxWidth=Math.Max(48,next.ContentWidth*.96);
        SizeDialog();SizeSidebar();
    }
    void RefreshInputHeight()
    {
        // Material's default multiline field reserves extra height even when empty.
        // Measure the actual wrapped draft and cap it to the visible Android viewport.
        var width=input.ActualWidth-input.Padding.Left-input.Padding.Right;
        if(width<=0)return;
        inputSizing.FontSize=input.FontSize;inputSizing.FontFamily=input.FontFamily;
        inputSizing.Text=input.Text+"\u200b"; // Keep the caret's line after a trailing newline.
        inputSizing.Measure(new Windows.Foundation.Size(width,double.PositiveInfinity));
        var height=Math.Clamp(inputSizing.DesiredSize.Height+input.Padding.Top+input.Padding.Bottom,48,input.MaxHeight);
        if(double.IsFinite(height) && Math.Abs(input.Height-height)>.5)input.Height=height;
        attach.VerticalAlignment=send.VerticalAlignment=stop.VerticalAlignment=height>48.5?VerticalAlignment.Bottom:VerticalAlignment.Center;
    }
    void ShowWelcome(bool canCreate)
    {
        var welcome=new StackPanel {Spacing=16,Padding=new(20),MaxWidth=440,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        welcomeIllustration=new Border {Child=MobileIcons.Create("chat_bubble_outline",28),Width=72,Height=72,CornerRadius=new(24),Background=Brush("PrimaryContainerBrush"),HorizontalAlignment=HorizontalAlignment.Center,Visibility=layoutMetrics.ShowWelcomeIllustration?Visibility.Visible:Visibility.Collapsed};
        welcome.Children.Add(welcomeIllustration);
        welcome.Children.Add(new TextBlock {Text="Un espace pour vos idées",FontSize=28,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Brush("OnSurfaceBrush")});
        welcome.Children.Add(new TextBlock {Text="Posez une question, partagez une image et poursuivez la discussion.",FontSize=16,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Brush("OnSurfaceVariantBrush")});
        if(canCreate) {var launch=new Button {Content="Nouvelle discussion",CornerRadius=new(28),MinHeight=56,Padding=new(24,12,24,12),HorizontalAlignment=HorizontalAlignment.Center};launch.Click+=async(_,_)=>await Guard(CreateChatAsync);welcome.Children.Add(launch);}
        scroll.Content=welcome;scroll.VerticalContentAlignment=VerticalAlignment.Center;
    }
    void SetStatus(string text,bool error=false)
    {
        status.Foreground=Brush(error?"ErrorBrush":"OnSurfaceVariantBrush");
        status.Text=text;status.Visibility=text is "Prêt" or "Configuration enregistrée." or "Réponse terminée." || text.StartsWith("Configurez un fournisseur",StringComparison.Ordinal)?Visibility.Collapsed:Visibility.Visible;
    }
    void UpdateProviderChip()
    {
        modelLabel.Text=(providers.SelectedItem as Provider)?.Model??"Configurer le modèle";
        AccessibleName(providerButton,"Choisir le modèle · "+modelLabel.Text);
        ToolTipService.SetToolTip(providerButton,(providers.SelectedItem as Provider)?.ToString()??"Configurer un fournisseur API");
    }
    async Task ChooseProviderAsync()
    {
        if(generation!=null)return;
        if(providers.SelectedItem==null) {await ConfigureProviderAsync();return;}
        var menu=new MenuFlyout();foreach(var provider in ((IEnumerable<Provider>?)providers.ItemsSource)??[]) {
            var item=new MenuFlyoutItem {Text=$"{provider.Name} · {provider.Model}"};item.Click+=(_,_)=>providers.SelectedItem=provider;menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());var configure=new MenuFlyoutItem {Text="Configurer le fournisseur",Icon=MobileIcons.Create("settings")};
        configure.Click+=async(_,_)=>await Guard(ConfigureProviderAsync);menu.Items.Add(configure);menu.ShowAt(providerButton);
    }
}
