using Android.App;
using Android.Runtime;
namespace MonolithHarnessGui.Portable;
[Application(Label="Monolith",Icon="@mipmap/appicon",Theme="@style/AppTheme",HardwareAccelerated=true,AllowBackup=false)]
public class AndroidApplication(IntPtr reference,JniHandleOwnership transfer) : Microsoft.UI.Xaml.NativeApplication(() => new App(),reference,transfer) { }
[Activity(MainLauncher=true,Exported=true,ConfigurationChanges=Uno.UI.ActivityHelper.AllConfigChanges,WindowSoftInputMode=Android.Views.SoftInput.AdjustResize)]
public partial class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    static MainActivity? current;
    static TaskCompletionSource<MonolithHarness.Core.Attachment?>? picker;
    protected override void OnCreate(global::Android.OS.Bundle? state)
    {
        current=this;base.OnCreate(state);
        if(Window?.DecorView is { } decor)decor.ViewTreeObserver!.GlobalLayout+=OnNativeLayout;
        SyncSystemBars();InitializeBackHandling();
    }
    public static void SyncSystemBars()
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(30))
            current?.Window?.InsetsController?.SetSystemBarsAppearance(MaterialColors.IsDark?0:24,24);
    }
    void OnNativeLayout(object? sender,EventArgs args) {SyncSystemBars();App.Page?.RefreshViewport();UpdateBackHandling();}
    public static MobileInsets ReadInsets()
    {
        var decor=current?.Window?.DecorView;var insets=decor?.RootWindowInsets;var density=current?.Resources?.DisplayMetrics?.Density??1;
        if(decor==null || insets==null)return default;
        if(OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var bars=insets.GetInsets(global::Android.Views.WindowInsets.Type.SystemBars()|global::Android.Views.WindowInsets.Type.DisplayCutout())!;
            var ime=insets.GetInsets(global::Android.Views.WindowInsets.Type.Ime())!;
            var height=current!.WindowManager!.CurrentWindowMetrics!.Bounds!.Height();
            return new(bars.Left/density,bars.Top/density,bars.Right/density,bars.Bottom/density,ime.Bottom/density,height/density);
        }
        using var frame=new global::Android.Graphics.Rect();decor.GetWindowVisibleDisplayFrame(frame);
        var windowHeight=current!.Resources!.DisplayMetrics!.HeightPixels;
#pragma warning disable CS0618, CA1422
        return new(insets.SystemWindowInsetLeft/density,insets.SystemWindowInsetTop/density,insets.SystemWindowInsetRight/density,
            insets.SystemWindowInsetBottom/density,Math.Max(insets.SystemWindowInsetBottom,windowHeight-frame.Bottom)/density,windowHeight/density);
#pragma warning restore CS0618, CA1422
    }
    public static void HideKeyboard()
    {
        var manager=current?.GetSystemService(InputMethodService) as global::Android.Views.InputMethods.InputMethodManager;
        manager?.HideSoftInputFromWindow(current?.Window?.DecorView?.WindowToken,global::Android.Views.InputMethods.HideSoftInputFlags.None);
    }
    public override void OnBackPressed()
    {
        if(!HandleTransientBack())base.OnBackPressed();
    }
    protected override void OnDestroy() { ReleaseBackHandling();if(current==this) {if(Window?.DecorView?.ViewTreeObserver is {} observer)observer.GlobalLayout-=OnNativeLayout;current=null;picker?.TrySetCanceled();picker=null;documentPicker?.TrySetCanceled();documentPicker=null;exportCompletion?.TrySetCanceled();exportCompletion=null;exportData=null;} base.OnDestroy(); }
    public static Task<MonolithHarness.Core.Attachment?> PickImageAsync()
    {
        if(current==null || picker!=null) throw new InvalidOperationException("Sélecteur déjà ouvert ou activité indisponible.");
        picker=new(TaskCreationOptions.RunContinuationsAsynchronously);var task=picker.Task;
        var intent=new global::Android.Content.Intent(global::Android.Content.Intent.ActionOpenDocument);
        intent.SetType("image/*");intent.AddCategory(global::Android.Content.Intent.CategoryOpenable);
        try {current.StartActivityForResult(intent,107);} catch {picker=null;throw;}return task;
    }
    protected override async void OnActivityResult(int requestCode,Result resultCode,global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);if(await HandleDocumentResultAsync(requestCode,resultCode,data))return;if(requestCode!=107)return;
        var completion=picker;picker=null;if(completion==null)return;
        if(resultCode!=Result.Ok || data?.Data==null){completion.TrySetResult(null);return;}
        try {
            await Task.Run(async()=>{
            using var source=ContentResolver!.OpenInputStream(data.Data)!;using var buffer=new MemoryStream();var chunk=new byte[81920];int read;
            while((read=await source.ReadAsync(chunk))>0) {if(buffer.Length+read>8*1024*1024)throw new IOException("L’image doit faire moins de 8 Mo.");await buffer.WriteAsync(chunk.AsMemory(0,read));}
            var mime=ContentResolver.GetType(data.Data)??"image/jpeg";
            if(mime is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif"))throw new IOException("Format image non pris en charge.");
            completion.TrySetResult(new() {Name="Image jointe",Mime=mime,Data=buffer.ToArray()});
            });
        }catch(Exception ex){completion.TrySetException(ex);}
    }
    protected override void OnStop() { App.Page?.SuspendGeneration(); base.OnStop(); }
}
