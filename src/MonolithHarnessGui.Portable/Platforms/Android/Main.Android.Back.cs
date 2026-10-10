using Android.Window;
namespace MonolithHarnessGui.Portable;

public partial class MainActivity
{
    BackHandler? modernBack;
    bool modernBackRegistered;
    sealed class BackHandler(MainActivity activity) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            var page=App.Page;
            if(page==null || !page.DispatcherQueue.TryEnqueue(()=>activity.InvokeModernBack()))activity.Finish();
        }
    }
    void InitializeBackHandling()
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(33))modernBack=new(this);
        UpdateBackHandling();
    }
    // Register only while transient UI is visible, so Android keeps normal root navigation.
    // https://developer.android.com/guide/navigation/custom-back/predictive-back-gesture
    public static void UpdateBackHandling()
    {
        if(!OperatingSystem.IsAndroidVersionAtLeast(33) || current?.modernBack==null)return;
        var activity=current;var insets=ReadInsets();
        var intercept=App.Page?.HasOpenDialog==true || insets.KeyboardBottom>insets.Bottom+80;
        if(intercept==activity.modernBackRegistered)return;
        if(intercept)activity.OnBackInvokedDispatcher!.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault,activity.modernBack);
        else activity.OnBackInvokedDispatcher!.UnregisterOnBackInvokedCallback(activity.modernBack);
        activity.modernBackRegistered=intercept;
    }
    bool HandleTransientBack()
    {
        var insets=ReadInsets();
        if(insets.KeyboardBottom>insets.Bottom+80){HideKeyboard();return true;}
        if(App.Page is {HasOpenDialog:true} page){page.DismissDialog();return true;}
        return false;
    }
    void InvokeModernBack()
    {
        if(!HandleTransientBack())Finish();
        UpdateBackHandling();
    }
    void ReleaseBackHandling()
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(33) && modernBackRegistered && modernBack!=null)
            OnBackInvokedDispatcher!.UnregisterOnBackInvokedCallback(modernBack);
        modernBackRegistered=false;modernBack?.Dispose();modernBack=null;
    }
}
