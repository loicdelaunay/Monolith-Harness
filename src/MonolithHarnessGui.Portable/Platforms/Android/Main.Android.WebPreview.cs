using MonolithHarness.Core;
namespace MonolithHarnessGui.Portable;

public partial class MainActivity
{
    static bool webPreviewOpen;
    internal static void WebPreviewClosed()=>webPreviewOpen=false;
    public static void ShowWebPreview(ReplyWebDocument document,ReplyRenderOptions options)
    {
        if(current==null)throw new InvalidOperationException("Activité Android indisponible.");
        if(webPreviewOpen)return;
        var intent=WebPreviewActivity.CreateIntent(current,document,options);webPreviewOpen=true;
        try {current.StartActivity(intent);}catch {webPreviewOpen=false;WebPreviewActivity.Discard(intent);throw;}
    }
}
