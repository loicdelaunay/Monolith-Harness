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

    void InitializeChatBackground()
    {
        void RefreshMotion() => animatedChatBackground.SetSuspended(
            mainArea.Visibility != Microsoft.UI.Xaml.Visibility.Visible ||
            AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized });
        mainArea.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.UIElement.VisibilityProperty, (_, _) => RefreshMotion());
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(RefreshMotion);
        Activated += (_, _) => RefreshMotion();
        Closed += (_, _) => animatedChatBackground.Shutdown();
        ConfigureChatBackground(FeatureSettings.Read(state.FeaturesJson));
    }
}
