using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;

namespace MonolithHarnessGui.Portable;

public sealed partial class MainPage
{
    sealed record MobileDialog(string Title,UIElement Content,string Primary,string Close);
    Border? dialogShell;
    ScrollViewer? dialogScroll;
    TextBlock? dialogHeading;
    Grid? dialogBody;
    TaskCompletionSource<ContentDialogResult>? dialogCompletion;
    MobileDialog CreateDialog(string heading,UIElement content,string primary,string close) => new(heading,content,primary,close);

    // In-page modal: the same Android/IME safe area applies to its scrollable body and fixed actions.
    async Task<ContentDialogResult> ShowDialogAsync(MobileDialog dialog)
    {
        if(dialogCompletion!=null)return ContentDialogResult.None;
        MainActivity.HideKeyboard();
        var completion=new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialogCompletion=completion;MainActivity.UpdateBackHandling();
        var overlay=new Grid {Background=new SolidColorBrush(Windows.UI.Color.FromArgb(100,0,0,0))};
        Grid.SetRowSpan(overlay,layout.RowDefinitions.Count);
        var body=dialogBody=new Grid {RowSpacing=16,Padding=new(24)};
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=dialogHeading=new TextBlock {Text=dialog.Title,FontSize=24,TextWrapping=TextWrapping.Wrap,Foreground=Brush("OnSurfaceBrush")};
        AutomationProperties.SetName(heading,dialog.Title);
        body.Children.Add(heading);
        dialogScroll=new ScrollViewer {Content=dialog.Content,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollMode=ScrollMode.Enabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetRow(dialogScroll,1);body.Children.Add(dialogScroll);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        var close=new Button {Content=dialog.Close,MinHeight=48,Padding=new(16,8,16,8),FontSize=14,Style=(Style)Application.Current.Resources["TextButtonStyle"]};
        var primary=new Button {Content=dialog.Primary,MinHeight=48,CornerRadius=new(24),Padding=new(16,8,16,8),FontSize=14};
        close.Click+=(_,_)=>completion.TrySetResult(ContentDialogResult.None);primary.Click+=(_,_)=>completion.TrySetResult(ContentDialogResult.Primary);
        primary.Visibility=string.IsNullOrWhiteSpace(dialog.Primary)?Visibility.Collapsed:Visibility.Visible;actions.Children.Add(close);actions.Children.Add(primary);Grid.SetRow(actions,2);body.Children.Add(actions);
        dialogShell=new Border {Child=body,Background=Brush("SurfaceBrush"),CornerRadius=new(28),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        overlay.Children.Add(dialogShell);SizeDialog();
        layout.Children.Add(overlay);close.Focus(FocusState.Programmatic);
        try {return await completion.Task;}
        finally {layout.Children.Remove(overlay);dialogShell=null;dialogScroll=null;dialogBody=null;dialogHeading=null;dialogCompletion=null;MainActivity.UpdateBackHandling();input.Focus(FocusState.Programmatic);MainActivity.HideKeyboard();}
    }
    public bool HasOpenDialog=>dialogCompletion!=null || sidebarShell!=null;
    public bool DismissDialog()=>dialogCompletion?.TrySetResult(ContentDialogResult.None)??CloseSidebar();
    void SizeDialog()
    {
        if(dialogShell==null)return;
        dialogShell.Width=Math.Min(560,layoutMetrics.ContentWidth);
        dialogShell.MaxHeight=layoutMetrics.UsableHeight;
        if(dialogScroll!=null)dialogScroll.MaxHeight=layoutMetrics.FormMaxHeight;
        if(dialogHeading!=null)dialogHeading.Visibility=layoutMetrics.Minimal?Visibility.Collapsed:Visibility.Visible;
        if(dialogBody!=null) {dialogBody.Padding=new(layoutMetrics.Minimal?8:24);dialogBody.RowSpacing=layoutMetrics.Minimal?8:16;}
    }
}
