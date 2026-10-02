using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeApplicationUpdates(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed.Add(name);
        }
        void Mode(AutomaticUpdateMode mode)
        {
            var features = FeatureSettings.Read(state.FeaturesJson);
            features.GuiUpdateMode = mode; state.FeaturesJson = features.Json();
            ConfigureAutomaticUpdates();
        }
        var fixture = new GitHubUpdate("9.0.0", "v9.0.0", GitHubUpdates.Repository + "/releases/tag/v9.0.0",
            "MonolithHarness-v9.0.0-win-x64.zip", GitHubUpdates.Repository + "/releases/download/v9.0.0/fixture.zip", new string('a', 64), 123);
        var calls = 0; var downloads = 0; var installs = 0;
        smokeGuiUpdateCheck = _ => { calls++; return Task.FromResult<GitHubUpdate?>(fixture); };
        var staged = Path.Combine(output, "fixture-update.exe");
        await File.WriteAllTextAsync(staged, "offline fixture");
        smokeGuiUpdateDownload = _ => { downloads++; return Task.FromResult(staged); };
        smokeGuiUpdateInstall = _ => { installs++; guiUpdate = null; };
        Check(guiUpdateTimer.Interval == TimeSpan.FromHours(2), "Automatic checks use the two-hour interval.");
        guiUpdateTimer.Interval = TimeSpan.FromMilliseconds(80);

        Mode(AutomaticUpdateMode.Disabled);
        await Task.Delay(220);
        await CheckAutomaticGuiUpdateAsync();
        Check(calls == 0 && !guiUpdateTimer.IsEnabled && !guiUpdateInstallTimer.IsEnabled, "Disabled mode makes no automatic requests.");
        await CheckGuiUpdateAsync(false);
        Check(calls == 1 && guiUpdateButton.Visibility == Visibility.Visible, "Manual checking still works when automatic updates are disabled.");
        var foot = VisualTreeHelper.GetParent(guiUpdateButton) as StackPanel;
        var buttonIndex = foot?.Children.IndexOf(guiUpdateButton) ?? -1;
        Check(buttonIndex >= 0 && foot!.Children[buttonIndex + 1] is Grid, "The update button sits immediately above Scheduled tasks.");
        state.Language = UiText.Language = "en"; ApplyLanguage();
        Check(guiUpdateButton.Content?.ToString() == "Update", "The persistent button follows language changes.");
        state.Language = UiText.Language = "fr"; ApplyLanguage();
        await Task.Delay(150);
        Check(guiUpdateButton.ActualHeight > 0, "The available-update button is rendered in the sidebar.");
        await Capture(root, Path.Combine(output, "update-button.png"));

        var options = BuildUpdateSettings();
        var picker = options.Panel.Children.OfType<ComboBox>().Single();
        Check(picker.Items.Count == 3 && picker.SelectedIndex == 0, "About exposes all three update levels and restores the saved selection.");
        foreach (var mode in Enum.GetValues<AutomaticUpdateMode>())
        {
            picker.SelectedIndex = (int)mode;
            var draft = FeatureSettings.Read(state.FeaturesJson); options.Save(draft);
            Check(FeatureSettings.Read(draft.Json()).EffectiveGuiUpdateMode == mode, "About saves mode " + mode + ".");
        }
        picker.SelectedIndex = 1;
        var about = new Window { Title = "Monolith Harness · About", Content = new Border { Padding = new(24),
            RequestedTheme = root.RequestedTheme, Background = FluentDesign.Card, Child = options.Panel } };
        about.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 680, Height = 620 });
        settingsWindow = about; about.Activate();
        await Task.Delay(150);
        await Capture((FrameworkElement)about.Content, Path.Combine(output, "update-settings.png"));
        about.Close(); settingsWindow = null;

        Mode(AutomaticUpdateMode.Notify);
        var atStartup = calls;
        Check(atStartup == 2, "Enabling notification mode checks immediately.");
        await Task.Delay(220);
        Check(calls > atStartup && installs == 0, "Notification mode checks periodically without installing.");
        guiUpdateTimer.Stop();
        smokeGuiUpdateCheck = _ => Task.FromException<GitHubUpdate?>(new IOException("offline fixture"));
        await CheckGuiUpdateAsync(true);
        Check(guiUpdate == fixture && guiUpdateButton.Visibility == Visibility.Visible, "A failed background check preserves the available update.");
        smokeGuiUpdateCheck = _ => Task.FromResult<GitHubUpdate?>(null);
        await CheckGuiUpdateAsync(false);
        Check(guiUpdate == null && guiUpdateButton.Visibility == Visibility.Collapsed, "No newer release hides the button.");

        var gate = new TaskCompletionSource<GitHubUpdate?>();
        calls = 0;
        smokeGuiUpdateCheck = _ => { calls++; return gate.Task; };
        var background = CheckGuiUpdateAsync(true);
        var manual = CheckGuiUpdateAsync(false);
        Check(calls == 1, "Concurrent manual and background checks share one request.");
        gate.SetResult(fixture); await Task.WhenAll(background, manual);

        smokeGuiUpdateCheck = _ => Task.FromResult<GitHubUpdate?>(fixture);
        composer.Text = "unsent draft";
        Mode(AutomaticUpdateMode.Install); guiUpdateTimer.Stop();
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == 0 && installs == 0, "Automatic installation waits for conversation drafts.");
        composer.Text = "";
        conversationDrafts[int.MaxValue] = ("draft in another conversation", []);
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == 0, "Automatic installation waits for drafts in other conversations.");
        conversationDrafts.Remove(int.MaxValue);
        editingSettings = true;
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == 0, "Automatic installation waits for settings edits.");
        editingSettings = false;
        smokeGuiUpdateDownload = _ => { downloads++; composer.Text = "draft started during download"; return Task.FromResult(staged); };
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == 1 && installs == 0 && guiUpdate == fixture, "Work started during download prevents restart.");
        composer.Text = "";
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == 1 && installs == 1, "Installation resumes when idle and reuses the verified staged download.");

        guiUpdate = fixture;
        await File.WriteAllTextAsync(staged, "changed staged file");
        smokeGuiUpdateDownload = async ct => { downloads++; await File.WriteAllTextAsync(staged, "offline fixture", ct); return staged; };
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == 2 && installs == 2, "A modified staged file is downloaded again instead of being reused.");
        guiUpdate = fixture;
        await scheduler!.RunMaintenanceAsync(TryInstallAutomaticGuiUpdateAsync);
        Check(installs == 2, "A scheduler operation prevents the automatic restart.");
        await TryInstallAutomaticGuiUpdateAsync();
        Check(installs == 3, "Automatic installation resumes after the scheduler releases its gate.");
        stagedGuiUpdate = null; stagedGuiUpdatePath = null;

        guiUpdate = fixture;
        var entered = new TaskCompletionSource();
        smokeGuiUpdateDownload = async ct =>
        {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return staged;
        };
        var installing = TryInstallAutomaticGuiUpdateAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Mode(AutomaticUpdateMode.Disabled);
        await installing;
        Check(installs == 3 && !updatingApplication, "Disabling automatic updates cancels an active automatic download.");

        var checking = new TaskCompletionSource();
        smokeGuiUpdateCheck = async ct => { checking.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return fixture; };
        Mode(AutomaticUpdateMode.Notify);
        await checking.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var pending = guiUpdateCheck!;
        Mode(AutomaticUpdateMode.Disabled);
        try { await pending; } catch (OperationCanceledException) { }
        Check(pending.IsCanceled && !guiUpdateTimer.IsEnabled, "Disabling checks cancels the pending background request.");

        smokeGuiUpdateCheck = _ => Task.FromResult<GitHubUpdate?>(fixture);
        smokeGuiUpdateDownload = _ => { downloads++; return Task.FromException<string>(new IOException("download fixture")); };
        Mode(AutomaticUpdateMode.Install); guiUpdateTimer.Stop();
        await TryInstallAutomaticGuiUpdateAsync();
        var failedDownloads = downloads;
        await TryInstallAutomaticGuiUpdateAsync();
        Check(downloads == failedDownloads && nextAutomaticInstallAttempt > DateTimeOffset.UtcNow.AddMinutes(119),
            "A failed automatic download backs off for two hours.");
        Mode(AutomaticUpdateMode.Disabled);
        File.Delete(staged);
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
