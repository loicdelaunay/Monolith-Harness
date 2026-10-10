using Android.App;
using Android.Runtime;
namespace MonolithHarnessGui.Portable;
[Application(Label="Monolith Harness Portable",Theme="@style/AppTheme",HardwareAccelerated=true,AllowBackup=false)]
public class AndroidApplication(IntPtr reference,JniHandleOwnership transfer) : Microsoft.UI.Xaml.NativeApplication(() => new App(),reference,transfer) { }
[Activity(MainLauncher=true,Exported=true,ConfigurationChanges=Uno.UI.ActivityHelper.AllConfigChanges,WindowSoftInputMode=Android.Views.SoftInput.AdjustResize)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    static MainActivity? current;
    static TaskCompletionSource<MonolithHarness.Core.Attachment?>? picker;
    protected override void OnCreate(global::Android.OS.Bundle? state) { current=this; base.OnCreate(state); }
    protected override void OnDestroy() { if(current==this) {current=null;picker?.TrySetCanceled();picker=null;} base.OnDestroy(); }
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
        base.OnActivityResult(requestCode,resultCode,data);if(requestCode!=107)return;
        var completion=picker;picker=null;if(completion==null)return;
        if(resultCode!=Result.Ok || data?.Data==null){completion.TrySetResult(null);return;}
        try {
            using var source=ContentResolver!.OpenInputStream(data.Data)!;using var buffer=new MemoryStream();var chunk=new byte[81920];int read;
            while((read=await source.ReadAsync(chunk))>0) {if(buffer.Length+read>8*1024*1024)throw new IOException("L’image doit faire moins de 8 Mo.");await buffer.WriteAsync(chunk.AsMemory(0,read));}
            var mime=ContentResolver.GetType(data.Data)??"image/jpeg";
            if(mime is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif"))throw new IOException("Format image non pris en charge.");
            completion.TrySetResult(new() {Name="Image jointe",Mime=mime,Data=buffer.ToArray()});
        }catch(Exception ex){completion.TrySetException(ex);}
    }
    protected override void OnStop() { App.Page?.SuspendGeneration(); base.OnStop(); }
}
