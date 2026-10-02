using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeLocalModels(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
        static IEnumerable<FrameworkElement> Descendants(DependencyObject? parent)
        {
            if (parent == null) yield break;
            if (parent is FrameworkElement element) yield return element;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement => Descendants(parent).OfType<T>().Single(x => x.Name == name);
        ListView? BrowserFiles() => localModelsWindow == null ? null : Descendants(localModelsWindow.Panel).OfType<ListView>().FirstOrDefault(x => x.Name == "HubFiles");
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider ?? throw new Exception("Button invocation unavailable.")).Invoke();
        static async Task Wait(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 200; attempt++) { if (condition()) return; await Task.Delay(25); }
            throw new TimeoutException("Local model UI did not reach the expected state.");
        }
        UiText.Language = state.Language = "fr"; ApplyTheme("fluent-dark");
        var preferences = FeatureSettings.Read(state.FeaturesJson); preferences.GuiUpdateMode = AutomaticUpdateMode.Disabled; state.FeaturesJson = preferences.Json(); ConfigureAutomaticUpdates();
        var directory = Path.Combine(output, "chosen model directory"); Directory.CreateDirectory(directory);
        var draft = new ProviderDraft { Kind = "local", Name = "Local", PendingKey = "offline-fixture-token", ContextLimit = 8192 };
        var changed = 0;
        var local = BuildLocalProviderEditor(draft, () => changed++);
        var owner = new Window { Title = "Monolith Harness · Local provider", Content = new Border { Padding = new(24), RequestedTheme = root.RequestedTheme, Background = FluentDesign.Card,
            Child = new ScrollViewer { Content = local, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } };
        settingsWindow = owner; owner.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1000, Height = 820 }); owner.Activate();
        await Task.Delay(200);
        var browse = Find<Button>(local, "BrowseLocalModelDirectory"); var folder = Find<TextBox>(local, "LocalModelDirectory");
        smokeLocalModelFolderPicker = parent => { Check(ReferenceEquals(parent, owner), "The native folder picker belongs to the settings window."); return Task.FromResult<string?>(directory); };
        Click(browse); await Wait(() => folder.Text == directory && browse.IsEnabled);
        Check(LocalProviderSettings.Read(draft.LocalModelsJson).ModelDirectory == directory && changed >= 2, "Picking a folder updates and persists the local provider draft.");
        smokeLocalModelFolderPicker = _ => Task.FromResult<string?>(null); Click(browse); await Task.Delay(50);
        Check(folder.Text == directory && LocalProviderSettings.Read(draft.LocalModelsJson).ModelDirectory == directory, "Cancelling the folder picker preserves the current directory.");
        await Capture((FrameworkElement)owner.Content, Path.Combine(output, "local-folder-picker.png"));

        using var transport = new LocalModelsSmokeTransport(); using var client = new HttpClient(transport);
        var machine = new MachineCapabilities("Windows", "X64", 16, 32L << 30, 24L << 30, "NVIDIA RTX · offline fixture", 12L << 30, false);
        smokeLocalModelsWindowFactory = (config, token, context, theme) => new(config, token, context, theme, new HuggingFaceModels(client), _ => Task.FromResult(machine));
        var launch = Descendants(local).OfType<Button>().Single(x => x.Content?.ToString() == "Hugging Face…"); Click(launch);
        await Wait(() => BrowserFiles()?.Items.Count == 3);
        var browser = localModelsWindow!; var models = Find<ListView>(browser.Panel, "HubModels"); var files = Find<ListView>(browser.Panel, "HubFiles");
        var query = Find<TextBox>(browser.Panel, "ModelSearch"); var search = Find<Button>(browser.Panel, "SearchModels"); var type = Find<ComboBox>(browser.Panel, "ModelType");
        var download = Find<Button>(browser.Panel, "DownloadModel"); var close = Find<Button>(browser.Panel, "CloseModelBrowser");
        Check(!ReferenceEquals(browser, owner) && ReferenceEquals(settingsWindow, owner) && draft.LocalBusy, "Hugging Face opens independently while settings remain available.");
        Check(!browse.IsEnabled && models.Items.Count == 12 && download.IsEnabled, "Opening the browser loads popular models and protects the provider directory.");
        Check((files.SelectedItem as ListViewItem)?.Tag is HubModelFile f && f.Name.Contains("Q4_K_M"), "The browser prefers a compatible Q4_K_M file.");
        var ownerReference = settingsWindow; settingsWindow = null;
        Check(GuiUpdateHasOpenEditors(), "Automatic installation waits for the independent model browser."); settingsWindow = ownerReference;
        await Wait(() => Descendants(Find<StackPanel>(browser.Panel, "ModelReadme")).OfType<TextBlock>().Any(x => x.Text.Contains("Technical details")));
        await Task.Delay(150); await Capture(browser.Panel, Path.Combine(output, "hugging-face-wide.png"));
        files.SelectedItem = files.Items.Cast<ListViewItem>().Single(x => x.Tag is HubModelFile f && f.Name.Contains("mmproj"));
        Check(!download.IsEnabled, "Unsupported projector files remain visible but cannot be imported.");
        files.SelectedItem = files.Items.Cast<ListViewItem>().Single(x => x.Tag is HubModelFile f && f.Name.Contains("Q4_K_M"));
        models.SelectedIndex = 2; await Wait(() => (files.Items.FirstOrDefault() as ListViewItem)?.Tag is HubModelFile selected && selected.Name.StartsWith("Qwen"));
        Check(Find<HyperlinkButton>(browser.Panel, "ModelCardLink").NavigateUri?.AbsoluteUri.Contains("Qwen") == true, "Selecting a model refreshes its files and model-card link.");
        await Wait(() => Descendants(Find<StackPanel>(browser.Panel, "ModelReadme")).OfType<TextBlock>().Any(x => x.Text.Contains("Qwen2.5-14B")));
        Check(true, "The model card belongs to the currently selected repository.");
        browser.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 680, Height = 900 }); await Task.Delay(200);
        var columns = Find<Grid>(browser.Panel, "ModelBrowserColumns");
        await Capture(browser.Panel, Path.Combine(output, "hugging-face-narrow.png"));
        Check(Grid.GetRow(columns.Children.OfType<Border>().Last()) == 1 && download.ActualHeight > 0 && close.ActualHeight > 0, $"Small windows stack both panes and keep the action bar visible: {browser.Panel.ActualWidth}x{browser.Panel.ActualHeight}, row={Grid.GetRow(columns.Children.OfType<Border>().Last())}, buttons={download.ActualHeight}/{close.ActualHeight}.");
        browser.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1160, Height = 880 });
        ApplyTheme("fluent-light"); await Task.Delay(200); await Capture(browser.Panel, Path.Combine(output, "hugging-face-light.png"));
        Check(browser.Panel.RequestedTheme == ElementTheme.Light, "The independent browser follows live theme changes."); ApplyTheme("fluent-dark");
        query.Text = "no results"; Click(search); await Wait(() => models.Items.Count == 0);
        Check(!download.IsEnabled && files.Items.Count == 0 && Find<HyperlinkButton>(browser.Panel, "ModelCardLink").NavigateUri == null, "An empty search clears stale model files and disables download.");
        query.Text = "offline"; Click(search); await Wait(() => Descendants(browser.Panel).OfType<TextBlock>().Any(x => x.Text.Contains("HTTP 503")));
        Check(models.Items.Count == 0 && !download.IsEnabled, "Network errors stay in the browser and prevent stale imports.");
        query.Text = ""; type.SelectedIndex = 1; await Wait(() => models.Items.Count == 1 && files.Items.Count == 1 && (files.Items[0] as ListViewItem)?.Tag is HubModelFile x && x.Name.EndsWith(".safetensors"));
        Check((models.Items[0] as ListViewItem)?.Tag is HubModel { Purpose: "image", Family: "sdxl" } && download.IsEnabled, "The image filter lists complete SDXL checkpoints with compatibility estimates.");
        query.Text = "tiny"; type.SelectedIndex = 0; await Wait(() => models.Items.Count == 1 && (files.Items.FirstOrDefault() as ListViewItem)?.Tag is HubModelFile x && x.Name.StartsWith("tiny"));
        transport.BlockTransfer = true; Click(download);
        await Wait(() => Directory.EnumerateFiles(directory, "*.partial-*", SearchOption.AllDirectories).Any());
        Check(!query.IsEnabled && !models.IsEnabled && !download.IsEnabled, "Transfers lock search, model selection and duplicate downloads.");
        Click(close); await Wait(() => query.IsEnabled && close.IsEnabled);
        Check(ReferenceEquals(localModelsWindow, browser) && download.IsEnabled && !Directory.EnumerateFiles(directory, "*.partial-*", SearchOption.AllDirectories).Any(), "Cancelling a download keeps the browser open and removes its partial file.");
        transport.BlockTransfer = false; Click(download); await Wait(() => localModelsWindow == null && !draft.LocalBusy);
        var imported = LocalProviderSettings.Read(draft.LocalModelsJson).Models.Single();
        Check(File.Exists(imported.FullPath) && imported.Repository == "fixture/tiny-GGUF" && (await File.ReadAllBytesAsync(imported.FullPath)).SequenceEqual(transport.Bytes), "Verified download and inspection import the model into the selected provider directory.");
        Check(browse.IsEnabled && LocalProviderSettings.Read(draft.LocalModelsJson).ModelDirectory == directory && transport.ReadTokens.All(x => x == draft.PendingKey), "Returning from import restores controls, preserves the directory and passes the draft read token.");

        Click(launch); await Wait(() => BrowserFiles()?.Items.Count > 0);
        browser = localModelsWindow!; query = Find<TextBox>(browser.Panel, "ModelSearch"); query.Text = "closing"; Click(Find<Button>(browser.Panel, "SearchModels"));
        await Wait(() => (BrowserFiles()?.Items.FirstOrDefault() as ListViewItem)?.Tag is HubModelFile x && x.Name.StartsWith("closing"));
        transport.BlockTransfer = true; Click(Find<Button>(browser.Panel, "DownloadModel"));
        await Wait(() => Directory.EnumerateFiles(directory, "*.partial-*", SearchOption.AllDirectories).Any());
        browser.Close(); await Wait(() => localModelsWindow == null && !draft.LocalBusy);
        Check(LocalProviderSettings.Read(draft.LocalModelsJson).Models.Count == 1 && !Directory.EnumerateFiles(directory, "*.partial-*", SearchOption.AllDirectories).Any(), "Closing the independent window stops its transfer, removes the partial file and releases the provider.");
        transport.BlockTransfer = false;
        Click(launch); await Wait(() => localModelsWindow != null); owner.Close(); settingsWindow = null;
        await Wait(() => localModelsWindow == null && !draft.LocalBusy);
        Check(!Directory.EnumerateFiles(directory, "*.partial-*", SearchOption.AllDirectories).Any(), "Closing settings cancels its browser and leaves no orphan transfer.");
        smokeLocalModelsWindowFactory = null; smokeLocalModelFolderPicker = null;
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-ok.txt"), string.Join("\n", passed.Select((x, i) => $"{i + 1}. {x}")));
    }

    sealed class LocalModelsSmokeTransport : HttpMessageHandler
    {
        internal byte[] Bytes { get; } = "GGUF\u0003\0\0\0offline fixture model"u8.ToArray();
        internal bool BlockTransfer;
        internal List<string?> ReadTokens { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ReadTokens.Add(request.Headers.Authorization?.Parameter);
            var uri = request.RequestUri!;
            HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
            JsonObject Model(string id, string purpose = "chat") => new() { ["id"] = id, ["downloads"] = 10342, ["likes"] = 198, ["lastModified"] = "2026-09-28T09:00:00Z", ["sha"] = "fixture-revision", ["config"] = new JsonObject { ["model_type"] = purpose == "chat" ? id.Contains("Qwen") ? "qwen2" : "mistral" : "stable-diffusion-xl" },
                ["cardData"] = new JsonObject { ["license"] = "apache-2.0", ["base_model"] = purpose == "chat" ? id.Contains("Qwen") ? "Qwen/Qwen2.5-14B-Instruct" : "mistralai/Mistral-Nemo-Instruct-2407" : "stabilityai/stable-diffusion-xl-base-1.0", ["description"] = purpose == "chat" ? "Un modèle de conversation pour vos projets, disponible en plusieurs quantifications GGUF." : "Checkpoint complet SDXL pour la génération d’images en local." } };
            if (uri.AbsolutePath == "/api/models")
            {
                if (uri.Query.Contains("offline")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                if (uri.Query.Contains("no%20results")) return Task.FromResult(Json(new JsonArray()));
                if (uri.Query.Contains("filter=text-to-image")) return Task.FromResult(Json(new JsonArray(Model("fixture/SDXL", "image"))));
                if (uri.Query.Contains("closing")) return Task.FromResult(Json(new JsonArray(Model("fixture/closing-GGUF"))));
                if (uri.Query.Contains("tiny")) return Task.FromResult(Json(new JsonArray(Model("fixture/tiny-GGUF"))));
                var ids = new[] { "bartowski/Mistral-Nemo-Instruct-2407-GGUF", "bartowski/Qwen2.5-Coder-7B-Instruct-GGUF", "Qwen/Qwen2.5-14B-Instruct-GGUF", "bartowski/Yi-Coder-9B-Chat-GGUF", "NousResearch/Hermes-3-Llama-3.1-8B-GGUF", "bartowski/InternLM2.5-20B-Chat-GGUF", "bartowski/LLaVA-v1.5-7B-GGUF", "bartowski/Llama-3.1-8B-Instruct-GGUF", "bartowski/Meta-Llama-3.1-70B-Instruct-GGUF", "bartowski/Gemma-2-9B-It-GGUF", "bartowski/Phi-3.5-mini-Instruct-GGUF", "bartowski/DeepSeek-Coder-V2-Lite-GGUF" };
                return Task.FromResult(Json(new JsonArray(ids.Select(x => (JsonNode)Model(x)).ToArray())));
            }
            if (uri.AbsolutePath.StartsWith("/api/models/"))
            {
                var id = uri.AbsolutePath[12..]; var info = Model(id, id.EndsWith("SDXL") ? "image" : "chat");
                if (id.EndsWith("SDXL")) info["siblings"] = new JsonArray(new JsonObject { ["rfilename"] = "sdxl-complete.safetensors", ["size"] = 6L << 30 });
                else if (id.Contains("tiny") || id.Contains("closing")) info["siblings"] = new JsonArray(new JsonObject { ["rfilename"] = (id.Contains("closing") ? "closing" : "tiny") + "-Q4_K_M.gguf", ["lfs"] = new JsonObject { ["size"] = Bytes.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Bytes)) } });
                else
                {
                    var name = id.Contains("Qwen") ? "Qwen" : "Mistral-Nemo";
                    info["siblings"] = new JsonArray(new JsonObject { ["rfilename"] = name + "-Q4_K_M.gguf", ["size"] = 7L << 30 }, new JsonObject { ["rfilename"] = name + "-mmproj.gguf", ["size"] = 512L << 20 }, new JsonObject { ["rfilename"] = name + "-Q8_0.gguf", ["size"] = 12L << 30 });
                }
                return Task.FromResult(Json(info));
            }
            if (uri.AbsolutePath.Contains("/raw/")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("# " + uri.AbsolutePath.Split('/')[2].Replace("-GGUF", "") + "\n\nUn modèle de conversation pour travailler avec vos fichiers, générer du code et discuter en local.\n\n## Technical details\n\n- Quantifications GGUF autonomes\n- Plusieurs variantes pour adapter la mémoire utilisée\n- Licence disponible sur le dépôt Hugging Face\n\nChoisissez un fichier pour afficher l’estimation de compatibilité avec votre machine.") });
            HttpContent content = BlockTransfer ? new StreamContent(new LocalModelsSlowStream(Bytes)) : new ByteArrayContent(Bytes);
            content.Headers.ContentLength = Bytes.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    sealed class LocalModelsSlowStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == 0) return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 8)], cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0;
        }
    }
}
