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
    readonly DispatcherTimer guiUpdateTimer = new() { Interval = TimeSpan.FromHours(1) };
    readonly DispatcherTimer guiUpdateInstallTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    readonly Button guiUpdateButton = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch,
        FontSize = 12, Padding = new(8), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly StackPanel guiUpdatePanel = new() { Spacing = 4, Visibility = Visibility.Collapsed };
    readonly ProgressBar guiUpdateProgress = new() { Height = 4, Maximum = 100, IsIndeterminate = true, Visibility = Visibility.Collapsed };
    readonly TextBlock guiUpdateProgressText = new() { FontSize = 11, Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    Task? guiUpdateCheck, automaticInstallAttempt;
    CancellationTokenSource? guiUpdateRequest, automaticUpdateInstall;
    AutomaticUpdateMode automaticUpdateMode;
    AutomaticUpdateFrequency automaticUpdateFrequency = AutomaticUpdateFrequency.Hourly;
    bool AutomaticGuiUpdatesEnabled => automaticUpdateMode != AutomaticUpdateMode.Disabled
        && automaticUpdateFrequency != AutomaticUpdateFrequency.Never;
    string automaticUpdateVersion = GitHubUpdates.Latest;
    long guiUpdateGeneration;
    DateTimeOffset nextAutomaticInstallAttempt;
    bool updateChecksStarted, updatingApplication;

    // Offline UI smoke fixtures never contact GitHub or replace a running executable.
    Func<CancellationToken, Task<GitHubUpdate?>>? smokeGuiUpdateCheck;
    Func<CancellationToken, IProgress<int>, Task<string>>? smokeGuiUpdateDownload;
    Action<string>? smokeGuiUpdateInstall;

    bool CanInstallGuiUpdate => smokeGuiUpdateInstall != null
        || GitHubUpdates.CanInstall && GitHubUpdates.IsStandalone(typeof(App).Assembly);

    StackPanel BuildGuiUpdateButton()
    {
        guiUpdateButton.Click += async (_, _) => await Guard(async () =>
        {
            if (editingSettings) throw new InvalidOperationException(WorkflowText(
                "Enregistrez ou fermez les réglages avant l’installation.", "Save or close settings before installing."));
            await InstallGuiUpdateAsync(ReportGuiUpdateProgress);
        });
        guiUpdatePanel.Children.Add(guiUpdateButton);
        guiUpdatePanel.Children.Add(guiUpdateProgress);
        guiUpdatePanel.Children.Add(guiUpdateProgressText);
        RefreshGuiUpdateButton();
        return guiUpdatePanel;
    }

    void ReportGuiUpdateProgress(string text)
    {
        guiUpdateProgressText.Text = text;
        guiUpdateProgressText.Visibility = updatingApplication ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshGuiUpdateButton()
    {
        var text = updatingApplication ? WorkflowText("Mise à jour…", "Updating…") : WorkflowText("Mettre à jour", "Update");
        guiUpdateButton.Content = text;
        guiUpdatePanel.Visibility = guiUpdate == null && !updatingApplication ? Visibility.Collapsed : Visibility.Visible;
        guiUpdateButton.Visibility = guiUpdatePanel.Visibility;
        guiUpdateProgress.Visibility = guiUpdateProgressText.Visibility = updatingApplication ? Visibility.Visible : Visibility.Collapsed;
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
        var previousFrequency = automaticUpdateFrequency;
        var features = FeatureSettings.Read(state.FeaturesJson);
        var targetChanged = automaticUpdateVersion != features.GuiUpdateVersion;
        automaticUpdateMode = features.EffectiveGuiUpdateMode;
        automaticUpdateFrequency = features.GuiUpdateFrequency;
        if (previousFrequency != automaticUpdateFrequency || guiUpdateTimer.Interval != features.GuiUpdateCheckInterval)
        {
            guiUpdateTimer.Stop();
            guiUpdateTimer.Interval = features.GuiUpdateCheckInterval;
        }
        automaticUpdateVersion = features.GuiUpdateVersion;
        if (targetChanged)
        {
            guiUpdateGeneration++;
            guiUpdateRequest?.Cancel(); automaticUpdateInstall?.Cancel();
            guiUpdateCheck = null; guiUpdate = null;
            RefreshGuiUpdateButton(); UpdateAutomaticInstallTimer();
        }
        if (automaticUpdateMode != AutomaticUpdateMode.Install || !AutomaticGuiUpdatesEnabled) automaticUpdateInstall?.Cancel();
        if (!AutomaticGuiUpdatesEnabled
            || Environment.GetEnvironmentVariable("MONOLITHHARNESS_UI_SMOKE") != null && smokeGuiUpdateCheck == null)
        {
            guiUpdateTimer.Stop(); guiUpdateInstallTimer.Stop();
            if (!AutomaticGuiUpdatesEnabled) guiUpdateRequest?.Cancel();
            return;
        }
        guiUpdateTimer.Start();
        UpdateAutomaticInstallTimer();
        if (previous != automaticUpdateMode || previousFrequency != automaticUpdateFrequency || targetChanged)
        {
            nextAutomaticInstallAttempt = DateTimeOffset.MinValue;
            _ = CheckAutomaticGuiUpdateAsync();
        }
    }

    void UpdateAutomaticInstallTimer()
    {
        if (AutomaticGuiUpdatesEnabled && automaticUpdateMode == AutomaticUpdateMode.Install && guiUpdate != null && CanInstallGuiUpdate && !updatingApplication)
            guiUpdateInstallTimer.Start();
        else guiUpdateInstallTimer.Stop();
    }

    async Task CheckAutomaticGuiUpdateAsync()
    {
        if (!AutomaticGuiUpdatesEnabled || updatingApplication || updateLifetime.IsCancellationRequested) return;
        await CheckGuiUpdateAsync(true);
        await TryInstallAutomaticGuiUpdateAsync();
    }

    async Task CheckGuiUpdateAsync(bool quiet)
    {
        if (updatingApplication || updateLifetime.IsCancellationRequested) return;
        var generation = guiUpdateGeneration;
        var request = guiUpdateCheck ??= FetchGuiUpdateAsync();
        try { await request; }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested
            || quiet && (!AutomaticGuiUpdatesEnabled || generation != guiUpdateGeneration)) { }
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
        var generation = guiUpdateGeneration;
        var target = FeatureSettings.Read(state.FeaturesJson).GuiUpdateVersion;
        try
        {
            using var client = new HttpClient();
            var update = smokeGuiUpdateCheck != null ? await smokeGuiUpdateCheck(cancellation.Token)
                : await new GitHubUpdates(client).CheckAsync(UpdateChannel.Gui, target, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != guiUpdateGeneration || target != FeatureSettings.Read(state.FeaturesJson).GuiUpdateVersion) return;
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
        if (!AutomaticGuiUpdatesEnabled || automaticUpdateMode != AutomaticUpdateMode.Install || guiUpdate == null || !CanInstallGuiUpdate
            || updatingApplication || updateLifetime.IsCancellationRequested || DateTimeOffset.UtcNow < nextAutomaticInstallAttempt
            || GuiUpdateHasActiveWork() || GuiUpdateHasOpenEditors()) return;
        try
        {
            if (await ReadStoreAsync(store => store.PendingInputs.Any())) return;
            await InstallGuiUpdateAsync(ReportGuiUpdateProgress, automatic: true);
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested || !AutomaticGuiUpdatesEnabled || automaticUpdateMode != AutomaticUpdateMode.Install) { }
        catch (Exception ex)
        {
            // Failed installations back off independently of the update-check frequency.
            nextAutomaticInstallAttempt = DateTimeOffset.UtcNow + TimeSpan.FromHours(2);
            AppLog.Write(AppLogLevel.Warning, "gui.automatic_update_failed", ex);
            if (!updateLifetime.IsCancellationRequested)
                ShowStatus(WorkflowText("Installation automatique impossible : ", "Automatic installation failed: ") + ex.Message, StatusKind.Error);
        }
    }

    bool GuiUpdateMatchesTarget(GitHubUpdate update)
    {
        var target = FeatureSettings.Read(state.FeaturesJson).GuiUpdateVersion;
        return GitHubUpdates.SelectTarget([update], target, GitHubUpdates.CurrentVersion) != null;
    }

    async Task InstallGuiUpdateAsync(Action<string> report, bool automatic = false, Func<bool>? ownerIsOpen = null, GitHubUpdate? selectedUpdate = null)
    {
        var update = selectedUpdate ?? guiUpdate;
        if (update == null || updatingApplication || !CanInstallGuiUpdate || updateLifetime.IsCancellationRequested || !GuiUpdateMatchesTarget(update)) return;
        if (conversationRuns.Count > 0) throw new InvalidOperationException(WorkflowText(
            "Terminez les agents avant l’installation. Les brouillons seront conservés.", "Finish agents before installing. Drafts will be preserved."));
        if (automatic && (!AutomaticGuiUpdatesEnabled || automaticUpdateMode != AutomaticUpdateMode.Install || GuiUpdateHasActiveWork() || GuiUpdateHasOpenEditors())) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        if (automatic) automaticUpdateInstall = cancellation;
        bool draftsSaved = false;
        updatingApplication = true; guiUpdateProgress.IsIndeterminate = true; guiUpdateProgress.Value = 0;
        ReportGuiUpdateProgress(WorkflowText("Préparation du téléchargement…", "Preparing download…"));
        RefreshGuiUpdateButton(); UpdateAutomaticInstallTimer();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(11) };
            void Report(string text) { ReportGuiUpdateProgress(text); report(text); }
            var progress = new Progress<int>(value =>
            {
                if (!updatingApplication || cancellation.IsCancellationRequested) return;
                guiUpdateProgress.IsIndeterminate = false; guiUpdateProgress.Value = Math.Clamp(value, 0, 100);
                Report(WorkflowText("Téléchargement : ", "Downloading: ") + value + "%");
            });
            async Task<string> Hash(string path)
            {
                await using var file = File.OpenRead(path);
                return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, cancellation.Token));
            }
            var reuse = stagedGuiUpdate == update && File.Exists(stagedGuiUpdatePath)
                && await Hash(stagedGuiUpdatePath!) == stagedGuiUpdateHash;
            var staged = reuse ? stagedGuiUpdatePath!
                : smokeGuiUpdateDownload != null ? await smokeGuiUpdateDownload(cancellation.Token, progress)
                : await new GitHubUpdates(client).DownloadAsync(update, UpdateChannel.Gui, Environment.ProcessPath!, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            guiUpdateProgress.IsIndeterminate = true;
            Report(WorkflowText("Vérification du téléchargement…", "Verifying download…"));
            stagedGuiUpdate = update; stagedGuiUpdatePath = staged;
            if (!reuse) stagedGuiUpdateHash = await Hash(staged);
            async Task Restart()
            {
                if (automatic && await ReadStoreAsync(store => store.PendingInputs.Any())) return;
                cancellation.Token.ThrowIfCancellationRequested();
                if (ownerIsOpen != null && !ownerIsOpen()) return;
                if (!GuiUpdateMatchesTarget(update)) return;
                if (conversationRuns.Count > 0)
                {
                    if (automatic) return;
                    throw new InvalidOperationException(WorkflowText(
                        "Un agent est actif ; relancez l’installation après sa fin.", "An agent is active; retry installation when finished."));
                }
                if (automatic && (!AutomaticGuiUpdatesEnabled || automaticUpdateMode != AutomaticUpdateMode.Install || GuiUpdateHasActiveWork() || GuiUpdateHasOpenEditors())) return;
                draftsSaved = true;
                await SaveUpdateDraftsAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (conversationRuns.Count > 0)
                {
                    if (automatic) return;
                    throw new InvalidOperationException(WorkflowText(
                        "Un agent a démarré pendant la préparation ; relancez l’installation après sa fin.",
                        "An agent started during preparation; retry installation when finished."));
                }
                if (!GuiUpdateMatchesTarget(update) || ownerIsOpen != null && !ownerIsOpen()) return;
                if (automatic && (!AutomaticGuiUpdatesEnabled || automaticUpdateMode != AutomaticUpdateMode.Install || GuiUpdateHasActiveWork() || GuiUpdateHasOpenEditors())) return;
                Report(WorkflowText("Installation et redémarrage…", "Installing and restarting…"));
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
            if (draftsSaved && !updateLifetime.IsCancellationRequested) ClearUpdateDrafts();
            if (automatic) automaticUpdateInstall = null;
            updatingApplication = false;
            if (!updateLifetime.IsCancellationRequested) { RefreshGuiUpdateButton(); UpdateAutomaticInstallTimer(); }
        }
    }

    (StackPanel Panel, Action<FeatureSettings> Save) BuildUpdateSettings()
    {
        var features = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label(WorkflowText("Mises à jour GitHub", "GitHub updates") + " · " + GitHubUpdates.CurrentVersion, 18));
        var target = new ComboBox { Header = WorkflowText("Version à utiliser", "Target version"),
            HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 300 };
        var choices = new List<ComboBoxItem> { new() { Content = "Latest", Tag = GitHubUpdates.Latest } };
        if (features.GuiUpdateVersion != GitHubUpdates.Latest)
            choices.Add(new() { Content = features.GuiUpdateVersion, Tag = features.GuiUpdateVersion });
        target.ItemsSource = choices;
        target.SelectedItem = choices.First(c => (string)c.Tag == features.GuiUpdateVersion);
        panel.Children.Add(target);
        var targetInfo = Label("", 12); panel.Children.Add(targetInfo);
        var mode = new ComboBox { Header = WorkflowText("Mises à jour automatiques", "Automatic updates"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { WorkflowText("Désactivées", "Disabled"), WorkflowText("Informer seulement", "Notify only"),
                WorkflowText("Installer automatiquement", "Install automatically") },
            SelectedIndex = (int)features.EffectiveGuiUpdateMode };
        panel.Children.Add(mode);
        var frequency = new ComboBox { Header = WorkflowText("Vérification mise à jour automatique", "Automatic update checks"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { WorkflowText("Jamais", "Never"), WorkflowText("Toutes les heures", "Every hour"),
                WorkflowText("Tous les jours", "Every day"), WorkflowText("Toutes les semaines", "Every week") },
            SelectedIndex = (int)features.GuiUpdateFrequency };
        panel.Children.Add(frequency);
        panel.Children.Add(Label(WorkflowText(
            "Vérification au démarrage puis à la fréquence choisie, lorsque l’application est ouverte. « Jamais » ou les mises à jour désactivées arrêtent la vérification et l’installation automatiques. La recherche et l’installation manuelles restent disponibles. L’installation automatique attend la fin des agents, des brouillons et la fermeture des fenêtres d’édition.",
            "Check at startup and at the selected interval while the app is open. Never or Disabled stops automatic checks and installation. Manual checking and installation remain available. Automatic installation waits for agents, drafts and editing windows to finish."), 12));
        var info = Label("", 13); panel.Children.Add(info);
        var check = new Button { Content = WorkflowText("Rechercher", "Check") };
        var install = new Button { Content = WorkflowText("Télécharger et redémarrer", "Download and restart") };
        panel.Children.Add(Row(check, install));
        panel.Children.Add(Label(WorkflowText("Les données et les brouillons sont conservés après le redémarrage. Enregistrez vos réglages avant l’installation ; les changements non enregistrés seront annulés. Les outils seront fermés. L’ancien EXE est conservé dans .updates.", "Data and drafts are preserved after restarting. Save settings before installing; unsaved changes will be discarded. Tools will close. The previous EXE is kept in .updates."), 12));
        if (!CanInstallGuiUpdate) panel.Children.Add(Label(WorkflowText(
            "L’installation nécessite une version Windows autonome.", "Installation requires a standalone Windows release."), 12));

        IReadOnlyList<GitHubUpdate> releases = [];
        GitHubUpdate? selectedUpdate = null;
        CancellationTokenSource? catalogRequest = null;
        bool catalogLoaded = false, catalogLoading = false, changingChoices = false;
        string SelectedVersion() => target.SelectedItem is ComboBoxItem { Tag: string version } ? version : features.GuiUpdateVersion;
        void RefreshSelection()
        {
            var version = SelectedVersion();
            var saved = version == FeatureSettings.Read(state.FeaturesJson).GuiUpdateVersion;
            targetInfo.Text = version == GitHubUpdates.Latest
                ? WorkflowText("Latest suit la dernière version disponible.", "Latest follows the newest available release.")
                : WorkflowText("Version fixe : ", "Fixed version: ") + version;
            if (!saved) targetInfo.Text += " " + WorkflowText("Enregistrez les réglages pour appliquer ce choix.", "Save settings to apply this choice.");
            selectedUpdate = GitHubUpdates.SelectTarget(catalogLoaded ? releases : guiUpdate == null ? [] : [guiUpdate], version, GitHubUpdates.CurrentVersion);
            info.Text = selectedUpdate != null ? "GUI " + selectedUpdate.Version + " · " + selectedUpdate.Page
                : version == GitHubUpdates.CurrentVersion ? WorkflowText("Cette version est déjà installée.", "This version is already installed.")
                : !catalogLoaded ? WorkflowText("Rechercher les versions disponibles sur GitHub.", "Check available versions on GitHub.")
                : version == GitHubUpdates.Latest ? WorkflowText("Aucune version GUI compatible plus récente.", "No newer compatible GUI release.")
                : WorkflowText("Cette version n’est pas disponible pour cet appareil.", "This version is not available for this device.");
            target.IsEnabled = check.IsEnabled = !catalogLoading && !updatingApplication;
            install.IsEnabled = selectedUpdate != null && saved && CanInstallGuiUpdate && !catalogLoading && !updatingApplication;
        }
        async Task LoadVersionsAsync()
        {
            if (catalogLoading || updatingApplication || updateLifetime.IsCancellationRequested) return;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
            catalogRequest = cancellation; catalogLoading = true;
            RefreshSelection(); info.Text = WorkflowText("Chargement des versions…", "Loading releases…");
            var failure = "";
            try
            {
                using var client = new HttpClient();
                var available = await new GitHubUpdates(client).ListAsync(UpdateChannel.Gui, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                var selected = SelectedVersion();
                releases = available; catalogLoaded = true;
                choices = [new() { Content = "Latest", Tag = GitHubUpdates.Latest }];
                choices.AddRange(releases.Select(r => new ComboBoxItem { Content = r.Version, Tag = r.Version }));
                // Preserve a saved pin even when its release disappears or cannot be downloaded on this machine.
                if (selected != GitHubUpdates.Latest && !choices.Any(c => (string)c.Tag == selected))
                    choices.Add(new() { Content = selected, Tag = selected });
                changingChoices = true;
                target.ItemsSource = choices;
                target.SelectedItem = choices.First(c => (string)c.Tag == selected);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                AppLog.Write(AppLogLevel.Warning, "gui.release_list_failed", ex);
                failure = WorkflowText("Liste des versions indisponible. Votre choix est conservé ; réessayez avec Rechercher.", "Release list unavailable. Your choice is preserved; retry with Check.");
            }
            finally
            {
                changingChoices = false; catalogLoading = false;
                if (ReferenceEquals(catalogRequest, cancellation)) catalogRequest = null;
                RefreshSelection();
                if (failure.Length > 0) info.Text = failure;
            }
        }
        target.SelectionChanged += (_, _) => { if (!changingChoices) RefreshSelection(); };
        panel.Loaded += async (_, _) => { if (!catalogLoaded) await LoadVersionsAsync(); };
        panel.Unloaded += (_, _) => catalogRequest?.Cancel();
        check.Click += async (_, _) => await LoadVersionsAsync();
        var owner = settingsWindow;
        install.Click += async (_, _) => await Guard(async () =>
        {
            var candidate = selectedUpdate;
            if (SelectedVersion() != FeatureSettings.Read(state.FeaturesJson).GuiUpdateVersion) return;
            target.IsEnabled = check.IsEnabled = install.IsEnabled = false;
            try { await InstallGuiUpdateAsync(text => info.Text = text,
                ownerIsOpen: () => owner != null && ReferenceEquals(owner, settingsWindow), selectedUpdate: candidate); }
            finally { RefreshSelection(); }
        });
        RefreshSelection();
        return (panel, config =>
        {
            config.GuiUpdateMode = (AutomaticUpdateMode)Math.Clamp(mode.SelectedIndex, 0, 2);
            config.GuiUpdateFrequency = (AutomaticUpdateFrequency)Math.Clamp(frequency.SelectedIndex, 0, 3);
            config.GuiUpdateVersion = SelectedVersion();
        });
    }
}
