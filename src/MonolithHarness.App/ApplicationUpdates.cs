using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    GitHubUpdate? guiUpdate;
    GitHubUpdate? stagedGuiUpdate;
    string? stagedGuiUpdatePath;
    string? stagedGuiUpdateHash;
    readonly CancellationTokenSource updateLifetime = new();
    readonly DispatcherTimer guiUpdateTimer = new() { Interval = TimeSpan.FromHours(2) };
    readonly DispatcherTimer guiUpdateInstallTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    readonly Button guiUpdateButton = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch,
        FontSize = 12, Padding = new(8), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    Task? guiUpdateCheck, automaticInstallAttempt;
    CancellationTokenSource? guiUpdateRequest, automaticUpdateInstall;
    AutomaticUpdateMode automaticUpdateMode;
    DateTimeOffset nextAutomaticInstallAttempt;
    bool updateChecksStarted, updatingApplication;

    // Offline UI smoke fixtures never contact GitHub or replace a running executable.
    Func<CancellationToken, Task<GitHubUpdate?>>? smokeGuiUpdateCheck;
    Func<CancellationToken, Task<string>>? smokeGuiUpdateDownload;
    Action<string>? smokeGuiUpdateInstall;

    bool CanInstallGuiUpdate => smokeGuiUpdateInstall != null
        || GitHubUpdates.CanInstall && GitHubUpdates.IsStandalone(typeof(App).Assembly);

    Button BuildGuiUpdateButton()
    {
        guiUpdateButton.Click += async (_, _) => await Guard(async () =>
        {
            if (editingSettings) throw new InvalidOperationException(WorkflowText(
                "Enregistrez ou fermez les réglages avant l’installation.", "Save or close settings before installing."));
            await InstallGuiUpdateAsync(text => ShowStatus(text, StatusKind.Activity));
        });
        RefreshGuiUpdateButton();
        return guiUpdateButton;
    }

    void RefreshGuiUpdateButton()
    {
        var text = updatingApplication ? WorkflowText("Mise à jour…", "Updating…") : WorkflowText("Mettre à jour", "Update");
        guiUpdateButton.Content = text;
        guiUpdateButton.Visibility = guiUpdate == null ? Visibility.Collapsed : Visibility.Visible;
        guiUpdateButton.IsEnabled = guiUpdate != null && CanInstallGuiUpdate && !updatingApplication;
        var tip = CanInstallGuiUpdate
            ? WorkflowText("Télécharger et redémarrer", "Download and restart") + " · " + guiUpdate?.Version
            : WorkflowText("L’installation nécessite une version Windows autonome.", "Installation requires a standalone Windows release.");
        ToolTipService.SetToolTip(guiUpdateButton, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(guiUpdateButton, text + " · " + guiUpdate?.Version);
    }

    void StartUpdateCheck()
    {
        if (updateChecksStarted) return;
        updateChecksStarted = true;
        guiUpdateTimer.Tick += async (_, _) => await CheckAutomaticGuiUpdateAsync();
        guiUpdateInstallTimer.Tick += async (_, _) => await TryInstallAutomaticGuiUpdateAsync();
        Closed += (_, _) =>
        {
            guiUpdateTimer.Stop(); guiUpdateInstallTimer.Stop();
            updateLifetime.Cancel();
        };
        ConfigureAutomaticUpdates();
    }

    void ConfigureAutomaticUpdates()
    {
        if (!updateChecksStarted || updateLifetime.IsCancellationRequested) return;
        var previous = automaticUpdateMode;
        automaticUpdateMode = FeatureSettings.Read(state.FeaturesJson).EffectiveGuiUpdateMode;
        if (automaticUpdateMode != AutomaticUpdateMode.Install) automaticUpdateInstall?.Cancel();
        if (automaticUpdateMode == AutomaticUpdateMode.Disabled
            || Environment.GetEnvironmentVariable("MONOLITHHARNESS_UI_SMOKE") != null && smokeGuiUpdateCheck == null)
        {
            guiUpdateTimer.Stop(); guiUpdateInstallTimer.Stop();
            if (automaticUpdateMode == AutomaticUpdateMode.Disabled) guiUpdateRequest?.Cancel();
            return;
        }
        guiUpdateTimer.Start();
        UpdateAutomaticInstallTimer();
        if (previous != automaticUpdateMode)
        {
            nextAutomaticInstallAttempt = DateTimeOffset.MinValue;
            _ = CheckAutomaticGuiUpdateAsync();
        }
    }

    void UpdateAutomaticInstallTimer()
    {
        if (automaticUpdateMode == AutomaticUpdateMode.Install && guiUpdate != null && CanInstallGuiUpdate && !updatingApplication)
            guiUpdateInstallTimer.Start();
        else guiUpdateInstallTimer.Stop();
    }

    async Task CheckAutomaticGuiUpdateAsync()
    {
        if (automaticUpdateMode == AutomaticUpdateMode.Disabled || updatingApplication || updateLifetime.IsCancellationRequested) return;
        await CheckGuiUpdateAsync(true);
        await TryInstallAutomaticGuiUpdateAsync();
    }

    async Task CheckGuiUpdateAsync(bool quiet)
    {
        if (updatingApplication || updateLifetime.IsCancellationRequested) return;
        var request = guiUpdateCheck ??= FetchGuiUpdateAsync();
        try { await request; }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested
            || quiet && automaticUpdateMode == AutomaticUpdateMode.Disabled) { }
        catch (Exception ex)
        {
            if (!quiet) throw;
            AppLog.Write(AppLogLevel.Warning, "gui.update_check_failed", ex);
        }
        finally { if (ReferenceEquals(guiUpdateCheck, request)) guiUpdateCheck = null; }
    }

    async Task FetchGuiUpdateAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        guiUpdateRequest = cancellation;
        try
        {
            using var client = new HttpClient();
            var update = smokeGuiUpdateCheck != null ? await smokeGuiUpdateCheck(cancellation.Token)
                : await new GitHubUpdates(client).CheckAsync(UpdateChannel.Gui, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var previous = guiUpdate?.Version;
            guiUpdate = update;
            RefreshGuiUpdateButton(); UpdateAutomaticInstallTimer();
            if (guiUpdate != null && guiUpdate.Version != previous)
                ShowStatus(WorkflowText("Mise à jour GUI disponible : ", "GUI update available: ") + guiUpdate.Version);
        }
        finally { if (ReferenceEquals(guiUpdateRequest, cancellation)) guiUpdateRequest = null; }
    }

    bool GuiUpdateHasActiveWork() => conversationRuns.Count > 0 || !string.IsNullOrWhiteSpace(composer.Text) || pendingImages.Count > 0
        || conversationDrafts.Any(d => d.Key != chat?.Id && (!string.IsNullOrWhiteSpace(d.Value.Text) || d.Value.Images.Count > 0));

    bool GuiUpdateHasOpenEditors() => editingSettings || settingsWindow != null || tasksWindow != null || localModelsWindow != null
        || dedicatedTools.Count > 0 || aiDetectorWindow != null || agentSettingsWindows.Count > 0
        || memoryDatabaseWindow != null || approvalQueue.CurrentCount == 0 || loading
        || databaseMaintenanceBusy || conversationLoading || refreshingModels;

    async Task TryInstallAutomaticGuiUpdateAsync()
    {
        var attempt = automaticInstallAttempt ??= InstallAutomaticGuiUpdateAsync();
        try { await attempt; }
        finally { if (ReferenceEquals(automaticInstallAttempt, attempt)) automaticInstallAttempt = null; }
    }

    async Task InstallAutomaticGuiUpdateAsync()
    {
        if (automaticUpdateMode != AutomaticUpdateMode.Install || guiUpdate == null || !CanInstallGuiUpdate
            || updatingApplication || updateLifetime.IsCancellationRequested || DateTimeOffset.UtcNow < nextAutomaticInstallAttempt
            || GuiUpdateHasActiveWork() || GuiUpdateHasOpenEditors()) return;
        try
        {
            if (await ReadStoreAsync(store => store.PendingInputs.Any())) return;
            await InstallGuiUpdateAsync(text => ShowStatus(text, StatusKind.Activity), automatic: true);
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested || automaticUpdateMode != AutomaticUpdateMode.Install) { }
        catch (Exception ex)
        {
            // Failed downloads wait for the next two-hour cycle instead of retrying every idle minute.
            nextAutomaticInstallAttempt = DateTimeOffset.UtcNow + TimeSpan.FromHours(2);
            AppLog.Write(AppLogLevel.Warning, "gui.automatic_update_failed", ex);
            if (!updateLifetime.IsCancellationRequested)
                ShowStatus(WorkflowText("Installation automatique impossible : ", "Automatic installation failed: ") + ex.Message, StatusKind.Error);
        }
    }

    async Task InstallGuiUpdateAsync(Action<string> report, bool automatic = false, Func<bool>? ownerIsOpen = null)
    {
        var update = guiUpdate;
        if (update == null || updatingApplication || !CanInstallGuiUpdate || updateLifetime.IsCancellationRequested) return;
        if (GuiUpdateHasActiveWork()) throw new InvalidOperationException(WorkflowText(
            "Terminez les agents et envoyez ou effacez les brouillons avant l’installation.", "Finish agents and send or clear drafts before installing."));
        if (automatic && (automaticUpdateMode != AutomaticUpdateMode.Install || GuiUpdateHasOpenEditors())) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        if (automatic) automaticUpdateInstall = cancellation;
        updatingApplication = true; RefreshGuiUpdateButton(); UpdateAutomaticInstallTimer();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(11) };
            var progress = new Progress<int>(value => report(WorkflowText("Téléchargement : ", "Downloading: ") + value + "%"));
            async Task<string> Hash(string path)
            {
                await using var file = File.OpenRead(path);
                return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, cancellation.Token));
            }
            var reuse = stagedGuiUpdate == update && File.Exists(stagedGuiUpdatePath)
                && await Hash(stagedGuiUpdatePath!) == stagedGuiUpdateHash;
            var staged = reuse ? stagedGuiUpdatePath!
                : smokeGuiUpdateDownload != null ? await smokeGuiUpdateDownload(cancellation.Token)
                : await new GitHubUpdates(client).DownloadAsync(update, UpdateChannel.Gui, Environment.ProcessPath!, progress, cancellation.Token);
            stagedGuiUpdate = update; stagedGuiUpdatePath = staged;
            if (!reuse) stagedGuiUpdateHash = await Hash(staged);
            async Task Restart()
            {
                if (automatic && await ReadStoreAsync(store => store.PendingInputs.Any())) return;
                cancellation.Token.ThrowIfCancellationRequested();
                if (ownerIsOpen != null && !ownerIsOpen()) return;
                if (GuiUpdateHasActiveWork())
                {
                    if (automatic) return;
                    throw new InvalidOperationException(WorkflowText(
                        "Un agent ou brouillon est actif ; relancez l’installation après sa fin.", "An agent or draft is active; retry installation when finished."));
                }
                if (automatic && (automaticUpdateMode != AutomaticUpdateMode.Install || GuiUpdateHasOpenEditors())) return;
                if (smokeGuiUpdateInstall != null) { smokeGuiUpdateInstall(staged); return; }
                scheduleTimer.Stop();
                try { GitHubUpdates.InstallAfterExit(staged, Environment.ProcessPath!, [], GitHubUpdates.IsStandalone(typeof(App).Assembly)); }
                catch { scheduleTimer.Start(); throw; }
                settingsWindow?.Close(); Close(); Application.Current.Exit();
            }
            // The scheduler gate prevents a scheduled run from starting between the final idle check and exit.
            if (automatic && scheduler != null) await scheduler.TryRunMaintenanceAsync(Restart);
            else await Restart();
        }
        finally
        {
            if (automatic) automaticUpdateInstall = null;
            updatingApplication = false;
            if (!updateLifetime.IsCancellationRequested) { RefreshGuiUpdateButton(); UpdateAutomaticInstallTimer(); }
        }
    }

    (StackPanel Panel, Action<FeatureSettings> Save) BuildUpdateSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label(WorkflowText("Mises à jour GitHub", "GitHub updates") + " · " + GitHubUpdates.CurrentVersion, 18));
        var mode = new ComboBox { Header = WorkflowText("Mises à jour automatiques", "Automatic updates"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { WorkflowText("Désactivées", "Disabled"), WorkflowText("Informer seulement", "Notify only"),
                WorkflowText("Installer automatiquement", "Install automatically") },
            SelectedIndex = (int)FeatureSettings.Read(state.FeaturesJson).EffectiveGuiUpdateMode };
        panel.Children.Add(mode);
        panel.Children.Add(Label(WorkflowText(
            "Désactivées : aucune vérification automatique. Sinon, recherche au démarrage puis toutes les 2 heures. L’installation automatique attend la fin des agents, des brouillons et la fermeture des fenêtres d’édition.",
            "Disabled: no automatic checks. Otherwise, check at startup and every 2 hours. Automatic installation waits for agents, drafts and editing windows to finish."), 12));
        var info = Label(guiUpdate == null ? WorkflowText("Rechercher une version GUI plus récente.", "Check for a newer GUI release.") : "GUI " + guiUpdate.Version, 13);
        panel.Children.Add(info);
        var check = new Button { Content = WorkflowText("Rechercher", "Check") };
        var install = new Button { Content = WorkflowText("Télécharger et redémarrer", "Download and restart"), IsEnabled = guiUpdate != null && CanInstallGuiUpdate && !updatingApplication };
        panel.Children.Add(Row(check, install));
        panel.Children.Add(Label(WorkflowText("Les données sont conservées. Enregistrez vos réglages avant l’installation ; les changements non enregistrés seront annulés. Les outils seront fermés. L’ancien EXE est conservé dans .updates.", "Data is preserved. Save settings before installing; unsaved changes will be discarded. Tools will close. The previous EXE is kept in .updates."), 12));
        if (!CanInstallGuiUpdate) panel.Children.Add(Label(WorkflowText(
            "L’installation nécessite une version Windows autonome.", "Installation requires a standalone Windows release."), 12));
        check.Click += async (_, _) => await Guard(async () =>
        {
            check.IsEnabled = false; install.IsEnabled = false; info.Text = WorkflowText("Recherche sur GitHub…", "Checking GitHub…");
            try
            {
                await CheckGuiUpdateAsync(false);
                info.Text = guiUpdate == null ? WorkflowText("Aucune release GUI compatible plus récente avec empreinte SHA-256.", "No newer compatible GUI release with a SHA-256 digest.") : "GUI " + guiUpdate.Version + " · " + guiUpdate.Page;
            }
            catch { info.Text = WorkflowText("Recherche impossible. Réessayez plus tard.", "Unable to check. Try again later."); throw; }
            finally { check.IsEnabled = !updatingApplication; install.IsEnabled = guiUpdate != null && CanInstallGuiUpdate && !updatingApplication; }
        });
        var owner = settingsWindow;
        install.Click += async (_, _) => await Guard(async () =>
        {
            check.IsEnabled = install.IsEnabled = false;
            try { await InstallGuiUpdateAsync(text => info.Text = text, ownerIsOpen: () => owner != null && ReferenceEquals(owner, settingsWindow)); }
            finally { check.IsEnabled = !updatingApplication; install.IsEnabled = guiUpdate != null && CanInstallGuiUpdate && !updatingApplication; }
        });
        return (panel, config => config.GuiUpdateMode = (AutomaticUpdateMode)Math.Clamp(mode.SelectedIndex, 0, 2));
    }
}
