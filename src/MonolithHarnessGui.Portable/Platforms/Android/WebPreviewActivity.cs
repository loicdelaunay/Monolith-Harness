using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using WebView=Android.Webkit.WebView;
using Button=Android.Widget.Button;
using Orientation=Android.Widget.Orientation;
using MonolithHarness.Core;
using System.Collections.Concurrent;
namespace MonolithHarnessGui.Portable;

[Activity(Exported=false,Theme="@style/AppTheme",HardwareAccelerated=true,ConfigurationChanges=Uno.UI.ActivityHelper.AllConfigChanges,WindowSoftInputMode=SoftInput.AdjustResize)]
public sealed class WebPreviewActivity : Activity
{
    sealed record Preview(ReplyWebDocument Document,ReplyRenderOptions Options);
    static readonly ConcurrentDictionary<string,Preview> pending=new();
    WebView? web;
    ReplyWebDocument? artifact;
    internal static Intent CreateIntent(Context context,ReplyWebDocument document,ReplyRenderOptions options)
    {
        // Pass a short token, not HTML (Android Binder has a transaction size limit).
        var key=Guid.NewGuid().ToString("N");pending[key]=new(document,options);
        return new Intent(context,typeof(WebPreviewActivity)).PutExtra("preview",key);
    }
    internal static void Discard(Intent intent)=>pending.TryRemove(intent.GetStringExtra("preview")??"",out _);
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        var key=Intent?.GetStringExtra("preview")??"";
        if(!pending.TryRemove(key,out var preview)) {
            Toast.MakeText(this,"Rouvrez l’aperçu depuis la discussion.",ToastLength.Long)?.Show();Finish();return;
        }
        artifact=preview.Document;
        var dark=MaterialColors.IsDark;
        var background=global::Android.Graphics.Color.ParseColor(dark?"#16181D":"#FFFBFE");var ink=global::Android.Graphics.Color.ParseColor(dark?"#E6E0E9":"#1C1B1F");
        var density=Resources!.DisplayMetrics!.Density;int Dp(int value)=>(int)Math.Round(value*density);
        var layout=new LinearLayout(this) {Orientation=Orientation.Vertical};layout.SetBackgroundColor(background);
        var toolbar=new LinearLayout(this) {Orientation=Orientation.Horizontal};toolbar.SetGravity(GravityFlags.CenterVertical);toolbar.SetPadding(Dp(12),0,Dp(8),0);
        var title=new TextView(this) {Text=artifact.Title,TextSize=18,Ellipsize=global::Android.Text.TextUtils.TruncateAt.End};title.SetMaxLines(1);title.SetTextColor(ink);
        toolbar.AddView(title,new LinearLayout.LayoutParams(0,Dp(56),1));title.Gravity=GravityFlags.CenterVertical;
        var close=new Button(this) {Text="Fermer",ContentDescription="Fermer l’application web"};close.SetTextColor(ink);close.Click+=(_,_)=>Finish();toolbar.AddView(close,new LinearLayout.LayoutParams(Dp(96),Dp(48)));
        layout.AddView(toolbar,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,Dp(56)));
        try {
            web=new WebView(this);web.SetBackgroundColor(background);
            var settings=web.Settings;settings.JavaScriptEnabled=true;settings.DomStorageEnabled=false;settings.AllowFileAccess=false;settings.AllowContentAccess=false;
            settings.BlockNetworkLoads=true;settings.MixedContentMode=MixedContentHandling.NeverAllow;settings.SetSupportMultipleWindows(false);settings.JavaScriptCanOpenWindowsAutomatically=false;
            settings.MediaPlaybackRequiresUserGesture=true;settings.SafeBrowsingEnabled=true;
            web.SetWebViewClient(new IsolatedClient(this));web.SetWebChromeClient(new IsolatedChrome());
            web.SetDownloadListener(new NoDownloads());
            layout.AddView(web,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,0,1));SetContentView(layout);
            // Fit the preview around status, navigation and display cutout areas.
            layout.SetOnApplyWindowInsetsListener(new InsetsListener());layout.RequestApplyInsets();
            web.LoadDataWithBaseURL("https://preview.monolith.invalid/",artifact.SandboxedPage(true,dark,preview.Options),"text/html","UTF-8",null);
        }catch(Exception ex) {var error=new TextView(this) {Text="Aperçu indisponible : "+ex.Message,TextSize=16};error.SetTextColor(ink);layout.AddView(error);SetContentView(layout);}
    }
    sealed class InsetsListener : Java.Lang.Object,View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view,WindowInsets? insets)
        {
            if(view!=null && insets!=null) {
                if(OperatingSystem.IsAndroidVersionAtLeast(30)) {var bars=insets.GetInsets(WindowInsets.Type.SystemBars()|WindowInsets.Type.DisplayCutout())!;view.SetPadding(bars.Left,bars.Top,bars.Right,bars.Bottom);}
#pragma warning disable CS0618,CA1422
                else view.SetPadding(insets.SystemWindowInsetLeft,insets.SystemWindowInsetTop,insets.SystemWindowInsetRight,insets.SystemWindowInsetBottom);
#pragma warning restore CS0618,CA1422
            }
            return insets!;
        }
    }
    sealed class IsolatedClient(WebPreviewActivity owner) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view,IWebResourceRequest? request)=>request?.Url?.Scheme!="about";
#pragma warning disable CS0618
        public override bool ShouldOverrideUrlLoading(WebView? view,string? url)=>url!=null && !url.StartsWith("about:",StringComparison.Ordinal);
#pragma warning restore CS0618
        public override WebResourceResponse? ShouldInterceptRequest(WebView? view,IWebResourceRequest? request)
        {
            var scheme=request?.Url?.Scheme;
            return scheme is "data" or "blob" or "about"?null:new WebResourceResponse("text/plain","UTF-8",403,"Blocked",new Dictionary<string,string>(),new MemoryStream([]));
        }
        public override bool OnRenderProcessGone(WebView? view,RenderProcessGoneDetail? detail)
        {
            Toast.MakeText(owner,"L’aperçu s’est arrêté. Votre discussion est conservée.",ToastLength.Long)?.Show();owner.ReleaseWeb();owner.Finish();return true;
        }
    }
    sealed class IsolatedChrome : WebChromeClient
    {
        public override void OnPermissionRequest(PermissionRequest? request)=>request?.Deny();
        public override bool OnShowFileChooser(WebView? view,IValueCallback? callback,FileChooserParams? parameters) {callback?.OnReceiveValue(null);return true;}
        public override void OnGeolocationPermissionsShowPrompt(string? origin,GeolocationPermissions.ICallback? callback)=>callback?.Invoke(origin,false,false);
    }
    sealed class NoDownloads : Java.Lang.Object,IDownloadListener
    {public void OnDownloadStart(string? url,string? agent,string? disposition,string? mime,long length) {}}
    void ReleaseWeb()
    {
        if(web==null)return;
        web.StopLoading();web.OnPause();(web.Parent as ViewGroup)?.RemoveView(web);web.Destroy();web.Dispose();web=null;
    }
    protected override void OnPause() {web?.OnPause();base.OnPause();}
    protected override void OnResume() {base.OnResume();web?.OnResume();}
    protected override void OnDestroy() {ReleaseWeb();artifact=null;MainActivity.WebPreviewClosed();base.OnDestroy();}
}
