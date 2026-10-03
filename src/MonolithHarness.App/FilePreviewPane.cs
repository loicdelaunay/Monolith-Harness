using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MonolithHarness.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    const int PreviewToolIndex = 5;
    readonly TextBlock previewName = Label("Preview", 16), previewLocation = Label("", 11), previewInfo = Label("", 12), previewPageLabel = Label("", 11);
    readonly ProgressBar previewLoading = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
    readonly TextBox previewRaw = OutputBox();
    readonly StackPanel previewRendered = new() { Spacing = 10 };
    readonly TabView previewTabs = new() { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.Equal };
    readonly ScrollViewer previewScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(12) };
    Button previewBack = null!, previewNext = null!, previewRefresh = null!, previewCopy = null!;
    DropDownButton previewDiskActions = null!;
    ComboBox previewZoom = null!;
    FilePreviewDocument? previewDocument;
    Project? previewProject;
    List<(int Start, int Length)> previewPages = [];
    CancellationTokenSource? previewRequest, previewPageRequest;
    int previewRevision, previewPageRevision, previewPageIndex;
    bool previewClosed;
    Image? previewImage;

    Grid BuildFilePreviewPane()
    {
        var panel = new Grid { RowSpacing = 8 };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            panel.RowDefinitions.Add(new() { Height = height });
        var header = new StackPanel { Spacing = 4 };
        previewName.IsTextSelectionEnabled = previewLocation.IsTextSelectionEnabled = true;
        previewLocation.Foreground = previewInfo.Foreground = previewPageLabel.Foreground = FluentDesign.Secondary;
        previewLocation.MaxLines = 2; previewLocation.TextTrimming = TextTrimming.CharacterEllipsis;
        header.Children.Add(previewName); header.Children.Add(previewLocation); header.Children.Add(previewInfo);
        panel.Children.Add(header); Grid.SetRow(previewLoading, 1); panel.Children.Add(previewLoading);
        previewScroll.Content = previewRendered;
        ScrollViewer.SetHorizontalScrollBarVisibility(previewRaw, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(previewRaw, ScrollBarVisibility.Auto);
        previewRaw.TextWrapping = TextWrapping.NoWrap;
        previewTabs.TabItems.Add(new TabViewItem { Header = "Raw", IsClosable = false, Content = previewRaw });
        previewTabs.TabItems.Add(new TabViewItem { Header = "Preview", IsClosable = false, Content = previewScroll });
        previewTabs.SelectedIndex = 1;
        Grid.SetRow(previewTabs, 2); panel.Children.Add(previewTabs);
        previewBack = Action("‹", () => ChangePreviewPageAsync(-1));
        previewNext = Action("›", () => ChangePreviewPageAsync(1));
        ToolTipService.SetToolTip(previewBack, WorkflowText("Page précédente", "Previous page"));
        ToolTipService.SetToolTip(previewNext, WorkflowText("Page suivante", "Next page"));
        previewZoom = new ComboBox { MinWidth = 110, SelectedIndex = 0, Visibility = Visibility.Collapsed };
        foreach (var value in new[] { WorkflowText("Ajuster", "Fit"), "50 %", "100 %", "200 %" }) previewZoom.Items.Add(value);
        previewZoom.SelectionChanged += (_, _) => ResizePreviewImage();
        previewScroll.SizeChanged += (_, _) => ResizePreviewImage();
        var pagination = new Grid { ColumnSpacing = 6 };
        pagination.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); pagination.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        pagination.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        pagination.Children.Add(previewZoom);
        previewPageLabel.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(previewPageLabel, 1); pagination.Children.Add(previewPageLabel);
        var pageActions = Row(previewBack, previewNext); Grid.SetColumn(pageActions, 2); pagination.Children.Add(pageActions);
        Grid.SetRow(pagination, 3); panel.Children.Add(pagination);
        previewRefresh = Action("Actualiser", RefreshFilePreviewAsync);
        previewCopy = Action("Copier", CopyFilePreviewAsync);
        previewDiskActions = new DropDownButton { Content = "…", Visibility = Visibility.Collapsed };
        ToolTipService.SetToolTip(previewDiskActions, WorkflowText("Actions du fichier", "File actions"));
        var open = Action(WorkflowText("Ouvrir un fichier", "Open a file"), PickFilePreviewAsync);
        open.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        var actions = Row(previewDiskActions, previewRefresh, previewCopy, open); actions.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(actions, 4); panel.Children.Add(actions);
        Closed += (_, _) => { previewClosed = true; CancelFilePreview(); };
        ResetFilePreview();
        return panel;
    }

    void CancelFilePreview()
    {
        previewRevision++; previewPageRevision++;
        previewPageRequest?.Cancel(); previewPageRequest?.Dispose(); previewPageRequest = null;
        previewRequest?.Cancel(); previewRequest?.Dispose(); previewRequest = null;
    }
    void ResetFilePreview()
    {
        CancelFilePreview(); previewDocument = null; previewProject = null; previewPages.Clear(); previewImage = null;
        previewName.Text = ""; previewLocation.Text = ""; previewInfo.Text = "";
        previewName.Visibility = previewLocation.Visibility = previewInfo.Visibility = Visibility.Collapsed;
        previewRaw.Text = ""; previewRendered.Children.Clear();
        previewLoading.Visibility = Visibility.Collapsed;
        if (previewRefresh != null) UpdateFilePreviewActions();
    }

    async Task PickFilePreviewAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var extension in FilePreviewDocument.Extensions) picker.FileTypeFilter.Add(extension);
        InitializePicker(picker, this);
        var file = await picker.PickSingleFileAsync();
        if (file != null) await OpenFilePreviewAsync(file.Path, project);
    }
    async Task OpenChatItemAsync(string requested, Project? messageProject)
    {
        var owner = chat?.Id;
        try
        {
            var path = await Task.Run(() => ResolveChatDiskPath(requested, messageProject));
            if (previewClosed || owner != chat?.Id) return;
            if (FilePreviewDocument.Supports(path) && !Directory.Exists(path)) await OpenFilePreviewAsync(path, messageProject);
            else await OpenChatFileAsync(path, messageProject);
        }
        catch (Exception ex) { ShowStatus(ex.Message, StatusKind.Error); }
    }
    Task OpenFilePreviewAsync(string path, Project? sourceProject) => LoadFilePreviewAsync(
        ct => Task.Run(() => FilePreviewDocument.ReadAsync(path, ct), ct), Path.GetFileName(path), path, sourceProject);
    Task OpenAttachmentPreviewAsync(byte[] bytes, string mime, string name) => LoadFilePreviewAsync(
        ct => Task.Run(() => FilePreviewDocument.FromBytes(name, mime, bytes, ct), ct), name, null, project);

    async Task LoadFilePreviewAsync(Func<CancellationToken, Task<FilePreviewDocument>> load, string name, string? path, Project? sourceProject)
    {
        await ShowToolAsync(PreviewToolIndex);
        CancelFilePreview();
        var revision = previewRevision;
        previewZoom.SelectedIndex = 0;
        previewRequest = new(); var ct = previewRequest.Token;
        previewName.Visibility = previewLocation.Visibility = previewInfo.Visibility = Visibility.Visible;
        previewName.Text = name; previewLocation.Text = path ?? WorkflowText("Pièce jointe du chat", "Chat attachment");
        ToolTipService.SetToolTip(previewLocation, previewLocation.Text);
        previewInfo.Text = WorkflowText("Chargement…", "Loading…"); previewLoading.Visibility = Visibility.Visible;
        previewDocument = null; previewProject = sourceProject; previewImage = null; previewPages.Clear();
        previewRendered.Children.Clear(); previewRaw.Text = ""; previewPageIndex = 0; previewTabs.SelectedIndex = 1;
        UpdateFilePreviewActions();
        try
        {
            var document = await load(ct);
            var pages = await Task.Run(document.Pages, ct);
            if (ct.IsCancellationRequested || previewClosed || revision != previewRevision) return;
            previewDocument = document; previewPages = pages;
            previewInfo.Text = $"{document.Mime} · {document.Bytes.Length / 1024d:N1} " + WorkflowText("Ko", "KB") +
                (document.IsImage ? $" · {document.Width:N0} × {document.Height:N0} px" : " · " + document.EncodingName);
            ToolTipService.SetToolTip(previewInfo, document.ModifiedUtc is { } date
                ? WorkflowText("Dernière modification : ", "Last modified: ") + date.ToLocalTime().ToString("g") : previewInfo.Text);
            if (document.Path != null) previewDiskActions.Flyout = CreateChatFileMenu(document.Path, sourceProject);
            await RenderFilePreviewPageAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (previewClosed || revision != previewRevision) return;
            previewInfo.Text = WorkflowText("Aperçu indisponible : ", "Preview unavailable: ") + ex.Message;
            previewRendered.Children.Clear(); previewRendered.Children.Add(Label(previewInfo.Text, 13));
            ShowStatus(previewInfo.Text, StatusKind.Error);
        }
        finally
        {
            if (!previewClosed && revision == previewRevision) { previewLoading.Visibility = Visibility.Collapsed; UpdateFilePreviewActions(); }
        }
    }
    async Task RefreshFilePreviewAsync()
    {
        if (previewDocument is not { } document) return;
        if (document.Path != null) await OpenFilePreviewAsync(document.Path, previewProject);
        else await OpenAttachmentPreviewAsync(document.Bytes, document.Mime, document.Name);
    }
    async Task ChangePreviewPageAsync(int offset)
    {
        if (previewDocument == null) return;
        var page = Math.Clamp(previewPageIndex + offset, 0, previewPages.Count - 1);
        if (page == previewPageIndex) return;
        previewPageIndex = page; await RenderFilePreviewPageAsync();
    }
    async Task RenderFilePreviewPageAsync()
    {
        if (previewDocument is not { } document || previewRequest == null || previewPages.Count == 0) return;
        previewPageRequest?.Cancel(); previewPageRequest?.Dispose();
        previewPageRequest = CancellationTokenSource.CreateLinkedTokenSource(previewRequest.Token);
        var ct = previewPageRequest.Token; var revision = ++previewPageRevision;
        var page = previewPages[previewPageIndex]; var sourceProject = previewProject;
        previewLoading.Visibility = Visibility.Visible;
        try
        {
            var raw = await Task.Run(() => document.RawPage(page), ct);
            ct.ThrowIfCancellationRequested();
            previewRaw.Text = raw;
            if (document.IsImage && previewImage != null) return;
            previewRendered.Children.Clear(); previewImage = null; previewScroll.ChangeView(0, 0, null);
            previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            if (document.IsImage)
            {
                var image = await PreviewImageAsync(document.ImagePng!, ct);
                ct.ThrowIfCancellationRequested(); previewImage = image; previewRendered.Children.Add(image); ResizePreviewImage();
            }
            else if (document.IsMarkdown)
            {
                string Relative(string requested) => ResolveFilePreviewLink(requested, document, sourceProject);
                await MarkdownRenderer.RenderPreviewAsync(previewRendered, document.Text!.Substring(page.Start, page.Length),
                    requested => Guard(() => OpenChatItemAsync(Relative(requested), sourceProject)),
                    requested =>
                    {
                        string path;
                        try { path = Relative(requested); } catch { path = requested; }
                        return CreateChatFileMenu(path, sourceProject);
                    },
                    url => LoadMarkdownPreviewImageAsync(document, url, ct), ct);
            }
            else previewRendered.Children.Add(new TextBlock { Text = document.Text!.Substring(page.Start, page.Length), IsTextSelectionEnabled = true,
                FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (previewClosed || revision != previewPageRevision) return;
            previewRendered.Children.Clear(); previewRendered.Children.Add(Label(WorkflowText("Rendu indisponible. Le contenu reste accessible dans Raw.",
                "Rendering unavailable. The content remains available in Raw."), 13));
            previewRendered.Children.Add(Label(ex.Message, 12));
        }
        finally
        {
            if (!previewClosed && revision == previewPageRevision) { previewLoading.Visibility = Visibility.Collapsed; UpdateFilePreviewActions(); }
        }
    }
    string ResolveFilePreviewLink(string requested, FilePreviewDocument document, Project? sourceProject)
    {
        var path = LocalFileLinks.WithoutLocation(requested);
        if (!Path.IsPathFullyQualified(path) && !path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && document.Path != null)
        {
            var relative = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document.Path)!, path.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(relative) || Directory.Exists(relative)) return relative;
        }
        return ResolveChatDiskPath(path, sourceProject);
    }
    async Task<FrameworkElement?> LoadMarkdownPreviewImageAsync(FilePreviewDocument document, string url, CancellationToken ct)
    {
        var relative = LocalFileLinks.PathFromUrl(url);
        if (document.Path == null || relative == null || !FilePreviewDocument.Supports(relative)) return null;
        try
        {
            // Automatically embedded images stay inside this document's directory; no network fetches.
            var folder = Path.GetDirectoryName(document.Path)!;
            var path = await Task.Run(() => LocalPreview.ResolveResource(folder, relative.Replace('\\', '/')), ct);
            var imageDocument = await Task.Run(() => FilePreviewDocument.ReadAsync(path, ct, 1024), ct);
            if (!imageDocument.IsImage) return null;
            var image = await PreviewImageAsync(imageDocument.ImagePng!, ct); image.MaxHeight = 480;
            image.Tapped += async (_, _) => await Guard(() => OpenFilePreviewAsync(path, previewProject));
            ToolTipService.SetToolTip(image, imageDocument.Name); return image;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return Label(WorkflowText("Image locale indisponible : ", "Local image unavailable: ") + relative, 11); }
    }
    static async Task<Image> PreviewImageAsync(byte[] png, CancellationToken ct)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(png); using var random = stream.AsRandomAccessStream();
        ct.ThrowIfCancellationRequested(); await bitmap.SetSourceAsync(random); ct.ThrowIfCancellationRequested();
        return new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
    }
    void ResizePreviewImage()
    {
        if (previewImage == null || previewDocument is not { IsImage: true } document || previewZoom == null) return;
        var scale = previewZoom.SelectedIndex switch { 1 => .5, 2 => 1, 3 => 2, _ => 0 };
        previewScroll.HorizontalScrollBarVisibility = scale == 0 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        var available = Math.Max(80, previewScroll.ActualWidth - 30);
        previewImage.Width = scale == 0 ? Math.Min(document.Width, available) : document.Width * scale;
    }
    void UpdateFilePreviewActions()
    {
        var ready = previewDocument != null;
        previewRefresh.IsEnabled = previewCopy.IsEnabled = ready;
        previewDiskActions.Visibility = previewDocument?.Path == null ? Visibility.Collapsed : Visibility.Visible;
        previewZoom.Visibility = previewDocument?.IsImage == true ? Visibility.Visible : Visibility.Collapsed;
        previewBack.IsEnabled = ready && previewPageIndex > 0;
        previewNext.IsEnabled = ready && previewPageIndex + 1 < previewPages.Count;
        previewBack.Visibility = previewNext.Visibility = previewPages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        previewPageLabel.Text = !ready ? "" : previewDocument!.IsImage
            ? WorkflowText("Raw : octets en hexadécimal", "Raw: hexadecimal bytes") + (previewPages.Count > 1 ? $" · {previewPageIndex + 1:N0} / {previewPages.Count:N0}" : "")
            : previewPages.Count > 1 ? WorkflowText("Page ", "Page ") + $"{previewPageIndex + 1} / {previewPages.Count}" : "";
        ToolTipService.SetToolTip(previewCopy, previewDocument?.IsImage == true
            ? WorkflowText("Copier les informations et la page Raw affichée", "Copy the information and the displayed Raw page")
            : WorkflowText("Copier tout le texte du fichier", "Copy all the file text"));
    }
    Task CopyFilePreviewAsync()
    {
        if (previewDocument is not { } document) return Task.CompletedTask;
        var data = new DataPackage(); data.SetText(document.Text ?? previewRaw.Text); Clipboard.SetContent(data);
        ShowStatus(WorkflowText("Contenu brut copié.", "Raw content copied."), StatusKind.Notice);
        return Task.CompletedTask;
    }
}
