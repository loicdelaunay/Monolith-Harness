using Android.App;
using MonolithHarness.Core;
namespace MonolithHarnessGui.Portable;

public partial class MainActivity
{
    static TaskCompletionSource<Attachment?>? documentPicker;
    static TaskCompletionSource<bool>? exportCompletion;
    static byte[]? exportData;
    public static Task<Attachment?> PickDocumentAsync()
    {
        if(current==null || picker!=null || documentPicker!=null || exportCompletion!=null)throw new InvalidOperationException("Sélecteur déjà ouvert ou activité indisponible.");
        documentPicker=new(TaskCreationOptions.RunContinuationsAsynchronously);var task=documentPicker.Task;
        var intent=new global::Android.Content.Intent(global::Android.Content.Intent.ActionOpenDocument);
        intent.SetType("*/*");intent.AddCategory(global::Android.Content.Intent.CategoryOpenable);
        try{current.StartActivityForResult(intent,108);}catch{documentPicker=null;throw;}return task;
    }
    public static Task<bool> ExportDocumentAsync(string name,string text)
    {
        if(current==null || picker!=null || documentPicker!=null || exportCompletion!=null)throw new InvalidOperationException("Sélecteur déjà ouvert ou activité indisponible.");
        exportCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);exportData=System.Text.Encoding.UTF8.GetBytes(text);var task=exportCompletion.Task;
        var intent=new global::Android.Content.Intent(global::Android.Content.Intent.ActionCreateDocument);
        intent.SetType("text/plain");intent.AddCategory(global::Android.Content.Intent.CategoryOpenable);intent.PutExtra(global::Android.Content.Intent.ExtraTitle,Path.GetFileName(name));
        try{current.StartActivityForResult(intent,109);}catch{exportCompletion=null;exportData=null;throw;}return task;
    }
    async Task<bool> HandleDocumentResultAsync(int code,Result result,global::Android.Content.Intent? data)
    {
        if(code==109) {
            var completion=exportCompletion;var bytes=exportData;exportCompletion=null;exportData=null;
            if(completion==null)return true;
            if(result!=Result.Ok || data?.Data==null || bytes==null){completion.TrySetResult(false);return true;}
            try {await Task.Run(async()=>{using var stream=ContentResolver!.OpenOutputStream(data.Data,"wt")??throw new IOException("Document non accessible en écriture.");await stream.WriteAsync(bytes);await stream.FlushAsync();});completion.TrySetResult(true);}
            catch(Exception ex){completion.TrySetException(ex);}return true;
        }
        if(code!=108)return false;
        var pick=documentPicker;documentPicker=null;if(pick==null)return true;
        if(result!=Result.Ok || data?.Data==null){pick.TrySetResult(null);return true;}
        try {
            await Task.Run(async()=>{
            var name="document.txt";
            using(var cursor=ContentResolver!.Query(data.Data,null,null,null,null)) {
                if(cursor?.MoveToFirst()==true) {var index=cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);if(index>=0)name=cursor.GetString(index)??name;}
            }
            using var source=ContentResolver!.OpenInputStream(data.Data)??throw new IOException("Document non accessible.");using var buffer=new MemoryStream();var chunk=new byte[16384];int read;
            while((read=await source.ReadAsync(chunk))>0){if(buffer.Length+read>2*1024*1024)throw new IOException("Le document doit faire moins de 2 Mo.");await buffer.WriteAsync(chunk.AsMemory(0,read));}
            var bytes=buffer.ToArray();MonolithHarness.Core.Mobile.MobileFiles.Decode(bytes);
            pick.TrySetResult(new() {Name=name,Mime="text/plain",Data=bytes});
            });
        }catch(Exception ex){pick.TrySetException(ex);}return true;
    }
}
