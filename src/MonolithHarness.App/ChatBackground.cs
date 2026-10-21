using Microsoft.UI.Windowing;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed record ChatBackgroundChoice(string Id, string Name) { public override string ToString() => Name; }
    readonly ChatBackgroundView animatedChatBackground = new();
    string chatBackgroundAnimation = ChatBackgroundStyles.Disabled;
    int chatBackgroundOpacityPercent = ChatBackgroundStyles.DefaultOpacityPercent;
    string? chatBackgroundColor;

    void ConfigureChatBackground(string? choice, int opacityPercent, string? color)
    {
        chatBackgroundAnimation = ChatBackgroundStyles.Normalize(choice);
        chatBackgroundOpacityPercent = ChatBackgroundStyles.NormalizeOpacity(opacityPercent);
        chatBackgroundColor = ChatBackgroundStyles.NormalizeColor(color);
        animatedChatBackground.Configure(chatBackgroundAnimation, FluentDesign.CurrentTheme, chatBackgroundOpacityPercent, chatBackgroundColor);
    }

    void ConfigureChatBackground(FeatureSettings settings) =>
        ConfigureChatBackground(settings.ChatBackgroundAnimation, settings.ChatBackgroundOpacityPercent, settings.ChatBackgroundColor);

    readonly Microsoft.UI.Xaml.DispatcherTimer chatBackgroundScrollIdle = new() { Interval = TimeSpan.FromMilliseconds(450) };
    bool chatBackgroundScrolling;

    // The scene pauses while a reply streams or the chat scrolls: both already repaint the
    // window, and an animated background would add a full frame on every display refresh.
    void RefreshChatBackgroundMotion() => animatedChatBackground.SetSuspended(!VisualUpdatesAllowed || mainArea is null ||
        mainArea.Visibility != Microsoft.UI.Xaml.Visibility.Visible ||
        AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } ||
        ActiveRun != null || chatBackgroundScrolling);

    void InitializeChatBackground()
    {
        mainArea.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.UIElement.VisibilityProperty, (_, _) => RefreshChatBackgroundMotion());
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(RefreshChatBackgroundMotion);
        Activated += (_, _) => RefreshChatBackgroundMotion();
        chatBackgroundScrollIdle.Tick += (_, _) => { chatBackgroundScrollIdle.Stop(); chatBackgroundScrolling = false; RefreshChatBackgroundMotion(); };
        scroll.ViewChanged += (_, _) =>
        {
            if (!chatBackgroundScrolling) { chatBackgroundScrolling = true; RefreshChatBackgroundMotion(); }
            chatBackgroundScrollIdle.Stop(); chatBackgroundScrollIdle.Start();
        };
        Closed += (_, _) => { chatBackgroundScrollIdle.Stop(); animatedChatBackground.Shutdown(); };
        ConfigureChatBackground(FeatureSettings.Read(state.FeaturesJson)); RefreshChatBackgroundMotion();
    }
}
