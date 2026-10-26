using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    Window? settingsWindow;
    SettingsNavigation? settingsNavigation;
    int settingsProviderTab = -1, settingsPermissionTab = -1, settingsServerTab = -1, settingsMcpTab = -1;
    bool providersRequested, permissionsRequested, serverRequested, mcpSettingsRequested;
    bool editingSettings, settingsSaveInProgress;

    Task Settings() => Settings(showProviders: false);

    /// <summary>Opens the settings window on its Server page (connection, account, synchronization).</summary>
    async Task OpenServerSettingsAsync()
    {
        serverRequested = true;
        if (settingsNavigation != null && settingsServerTab >= 0)
        {
            settingsNavigation.SelectedIndex = settingsServerTab; serverRequested = false;
            settingsWindow?.Activate();
            return;
        }
        await Settings();
    }

    async Task Settings(bool showProviders, bool showPermissions = false)
    {
        if (showProviders)
        {
            providersRequested = true; permissionsRequested = false;
            if (settingsNavigation != null)
            {
                settingsNavigation.SelectedIndex = settingsProviderTab;
                providersRequested = false;
            }
        }
        if (showPermissions)
        {
            permissionsRequested = true; providersRequested = false;
            if (settingsNavigation != null)
            {
                settingsNavigation.SelectedIndex = settingsPermissionTab;
                permissionsRequested = false;
            }
        }
        if (editingSettings)
        {
            settingsWindow?.Activate();
            return;
        }
        editingSettings = true;
        try { await EditSettingsAsync(); }
        finally { editingSettings = false; }
        await MaintainConversationsAsync();
    }

    async Task EditSettingsAsync()
    {
        var window = new Window { Title = $"{DisplayApplicationName} · {T("Réglages")}" };
        settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(settingsWindow, window)) return;
            settingsWindow = null; settingsNavigation = null; settingsProviderTab = -1; settingsPermissionTab = -1; settingsServerTab = -1; settingsMcpTab = -1; mcpSettingsRequested = false; providersRequested = false; permissionsRequested = false; serverRequested = false;
        };
        window.Content = new Border
        {
            RequestedTheme = root.RequestedTheme,
            Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"),
            Child = LoadingPlaceholder(WorkflowText("Chargement des réglages…", "Loading settings…"))
        };
        FluentDesign.WindowChrome(window);
        ApplyBrandingIcon(window);
        ResizeSettingsWindow(window);
        window.Activate();
        try { await YieldSettingsAsync(window); await PopulateSettingsAsync(window); }
        catch (OperationCanceledException) when (!ReferenceEquals(settingsWindow, window)) { }
        finally
        {
            settingsSaveInProgress = false;
            if (ReferenceEquals(settingsWindow, window)) window.Close();
            var savedAppearance = MonolithHarness.Core.FeatureSettings.Read(state.FeaturesJson);
            ApplyTheme(savedAppearance.Theme);
            ConfigureChatBackground(savedAppearance);
        }
    }

    static void ResizeSettingsWindow(Window window)
    {
        int width = 1360, height = 960;
        // Uno Desktop does not implement DisplayArea; native WinUI can constrain the window to the monitor.
#if WINDOWS
        try
        {
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            if (display is { WorkArea.Width: > 0, WorkArea.Height: > 0 })
            {
                width = Math.Min(width, Math.Max(640, display.WorkArea.Width - 64));
                height = Math.Min(height, Math.Max(480, display.WorkArea.Height - 64));
            }
        }
        catch (NotImplementedException) { } // Uno Desktop does not expose DisplayArea yet.
        catch (NotSupportedException) { }
#endif
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
    }

    async Task YieldSettingsAsync(Window window)
    {
        await Task.Delay(1);
        if (!ReferenceEquals(settingsWindow, window)) throw new OperationCanceledException();
    }

    async Task<bool> ShowSettingsWindowAsync(Window window, UIElement content, Func<bool> validate)
    {
        // Settings must never reserve approvalQueue or a ContentDialog slot:
        // agents still need to display permission requests in the main window.
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ReferenceEquals(settingsWindow, window)) return false;
        var panel = new Grid
        {
            RequestedTheme = root.RequestedTheme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"),
            Padding = new Thickness(0), RowSpacing = 0
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = Label(T("Réglages"), 28);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        heading.Margin = new(24, 20, 24, 12);
        if (content is SettingsNavigation navigation)
        {
            var top = new StackPanel { Spacing = 12, Margin = new(24, 20, 24, 12) };
            heading.Margin = new(0);
            top.Children.Add(heading); top.Children.Add(navigation.SearchBox);
            panel.Children.Add(top);
        }
        else panel.Children.Add(heading);
        Grid.SetRow((FrameworkElement)content, 1);
        panel.Children.Add(content);
        var save = new Button { Content = T("Enregistrer"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = T("Annuler") };
        var validationError = Label("", 12); validationError.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush");
        validationError.MaxWidth = 480; validationError.Visibility = Visibility.Collapsed;
        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var buttons = Row(cancel, save); Grid.SetColumn(buttons, 1);
        var saveNotes = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (content is SettingsNavigation settings) saveNotes.Children.Add(settings.SaveStatus);
        saveNotes.Children.Add(validationError); actions.Children.Add(saveNotes); actions.Children.Add(buttons);
        actions.HorizontalAlignment = HorizontalAlignment.Stretch;
        actions.Margin = new(24, 16, 24, 16);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);
        save.Click += (_, _) =>
        {
            if (databaseMaintenanceBusy) return;
            validationError.Visibility = Visibility.Collapsed;
            try
            {
                if (!validate())
                {
                    validationError.Text = WorkflowText("Vérifiez le réglage signalé dans l’onglet sélectionné.", "Check the highlighted setting in the selected tab.");
                    validationError.Visibility = Visibility.Visible; return;
                }
            }
            catch (Exception ex)
            {
                validationError.Text = MonolithHarness.Core.ToolAuthorization.Clean(ex.Message);
                validationError.Visibility = Visibility.Visible;
                MonolithHarness.Core.AppLog.Write(MonolithHarness.Core.AppLogLevel.Warning, "settings.validation", ex); return;
            }
            if (settingsSaveInProgress) return;
            settingsSaveInProgress = true;
            save.IsEnabled = cancel.IsEnabled = false;
            content.IsHitTestVisible = false;
            save.Content = WorkflowText("Enregistrement…", "Saving…");
            MonolithHarness.Core.AppLog.Write(MonolithHarness.Core.AppLogLevel.Information, "settings.save.begin");
            // Keep the controls alive until their values have been read and persisted.
            completed.TrySetResult(true);
        };
        window.AppWindow.Closing += (_, args) => { if (settingsSaveInProgress) args.Cancel = true; };
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(settingsWindow, window)) settingsWindow = null;
            completed.TrySetResult(false);
        };
        FluentDesign.WindowChrome(window);
        ApplyBrandingIcon(window);
        window.Content = panel;
        ObserveTextZoom(panel);
        ResizeSettingsWindow(window);
        window.Activate();
        return await completed.Task;
    }
}
