using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MonolithHarness.Core;
using Windows.Storage.Streams;
namespace MonolithHarness.Presentation;

internal sealed class ReplyImageView : UserControl
{
    readonly string source;
    readonly bool allowNetwork;
    readonly Func<string,CancellationToken,Task<byte[]?>>? localImage;
    readonly Image image=new() {Stretch=Stretch.Uniform,MaxWidth=640,MaxHeight=360,HorizontalAlignment=HorizontalAlignment.Left};
    readonly TextBlock caption;
    CancellationTokenSource? lifetime;
    bool loaded;
    internal ReplyImageView(string source,string alt,Brush foreground,bool allowNetwork=true,Func<string,CancellationToken,Task<byte[]?>>? localImage=null)
    {
        this.source=source;this.allowNetwork=allowNetwork;this.localImage=localImage;
        caption=new() {Text=string.IsNullOrWhiteSpace(alt)?"Image":alt,Foreground=foreground,FontSize=12,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
        var panel=new StackPanel {Spacing=4};panel.Children.Add(image);panel.Children.Add(caption);Content=panel;
        Loaded+=async(_,_)=>await LoadAsync();Unloaded+=(_,_)=>{lifetime?.Cancel();};
    }
    async Task LoadAsync()
    {
        if(loaded || lifetime is {IsCancellationRequested:false})return;
        lifetime?.Dispose();using var cts=new CancellationTokenSource();lifetime=cts;cts.CancelAfter(TimeSpan.FromSeconds(25));
        try {
            var bytes=await Task.Run(async()=> {
                if(!source.StartsWith("https:",StringComparison.OrdinalIgnoreCase) && !source.StartsWith("data:",StringComparison.OrdinalIgnoreCase) && localImage!=null) {
                    var local=await localImage(source,cts.Token);if(local!=null){ReplyImages.Validate(local);return local;}
                }
                return await ReplyImages.LoadAsync(source,allowNetwork,cts.Token);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var bitmap=new BitmapImage {DecodePixelWidth=1280};using var stream=new MemoryStream(bytes);using var random=stream.AsRandomAccessStream();
            await bitmap.SetSourceAsync(random);cts.Token.ThrowIfCancellationRequested();image.Source=bitmap;loaded=true;
        }catch(OperationCanceledException) {if(IsLoaded){caption.Text+="\nImage indisponible : délai de chargement dépassé.";loaded=true;}}catch(Exception ex) {caption.Text+="\nImage indisponible : "+ex.Message;loaded=true;}
        finally {if(ReferenceEquals(lifetime,cts))lifetime=null;}
    }
}
