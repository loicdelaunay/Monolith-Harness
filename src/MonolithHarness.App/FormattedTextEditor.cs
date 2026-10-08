using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace MonolithHarness.App;

// Native editing and preview: no WebView, JavaScript, navigation or HTML execution.
internal sealed partial class FormattedTextEditor : UserControl
{
    readonly TextBox editor;
    readonly Grid surface = new();
    readonly StackPanel preview = new() { Spacing = 8 };
    readonly ScrollViewer previewScroll;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly CancellationTokenSource lifetime = new();
    readonly RadioButton editChoice, previewChoice;
    readonly bool readOnly;
    readonly string placeholder;
    bool disposed, changing;
    int changeVersion, paintRevision;
    Task pending = Task.CompletedTask;
    FormattedText document = FormattedText.Empty;
    internal event EventHandler? TextChanged;
    internal event Action<string>? Error;
    internal event Action<string>? Notice;
    internal FormattedText Document => document;
    internal int DocumentVersion => changeVersion;
    internal int ScriptCallCount => 0;
    internal string Text { get => document.Text; set => SetDocument(FormattedText.Plain(value)); }

    internal FormattedTextEditor(string placeholder, bool readOnly = false)
    {
        this.readOnly = readOnly; this.placeholder = placeholder;
        MinHeight = 120; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        editor = new TextBox { PlaceholderText = placeholder, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            IsReadOnly = readOnly, MaxLength = readOnly ? ModelUtilityPrompts.MaxTextLength * 4 : ModelUtilityPrompts.MaxTextLength,
            MinHeight = 80, VerticalAlignment = VerticalAlignment.Stretch, BorderThickness = new(0), Padding = new(12) };
        previewScroll = new ScrollViewer { Content = preview, Padding = new(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var name = "native-editor-" + Guid.NewGuid().ToString("N");
        editChoice = new RadioButton { Content = UiText.Resolve("Texte", "Text"), GroupName = name, IsChecked = !readOnly };
        previewChoice = new RadioButton { Content = UiText.Resolve("Mise en forme", "Formatting"), GroupName = name, IsChecked = readOnly };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(8, 4, 8, 4) };
        controls.Children.Add(editChoice); controls.Children.Add(previewChoice);
        surface.RowDefinitions.Add(new() { Height = GridLength.Auto }); surface.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        surface.Children.Add(controls); Grid.SetRow(editor, 1); surface.Children.Add(editor); Grid.SetRow(previewScroll, 1); surface.Children.Add(previewScroll);
        Content = new Border { Child = surface, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(10) };
        void RefreshView()
        {
            var formatted = previewChoice.IsChecked == true;
            editor.Visibility = formatted ? Visibility.Collapsed : Visibility.Visible;
            previewScroll.Visibility = formatted ? Visibility.Visible : Visibility.Collapsed;
            if (formatted) QueuePreview();
        }
        editChoice.Checked += (_, _) => RefreshView(); previewChoice.Checked += (_, _) => RefreshView(); RefreshView();
        timer.Tick += async (_, _) => { timer.Stop(); await PaintAsync(); };
        editor.TextChanged += (_, _) =>
        {
            if (changing || disposed) return;
            FormattedText value;
            try { value = FormattedHtml.Edit(document, editor.Text); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                value = FormattedText.Plain(editor.Text);
                Notice?.Invoke(UiText.Resolve("La modification de la structure a réinitialisé la mise en forme ; le texte est conservé.", "Changing the structure reset formatting; the text is preserved."));
            }
            Accept(value);
        };
        editor.Paste += async (_, e) =>
        {
            e.Handled = true;
            if (readOnly || disposed || !IsEnabled) return;
            if (!pending.IsCompleted) { Error?.Invoke(UiText.Resolve("Un collage est déjà en cours.", "A paste is already in progress.")); return; }
            var start = editor.SelectionStart; var length = editor.SelectionLength; var version = changeVersion;
            pending = ReadClipboardAsync(start, length, version);
            await pending;
        };
    }
    void Accept(FormattedText value)
    { document = value; changeVersion++; QueuePreview(); TextChanged?.Invoke(this, EventArgs.Empty); }
    internal void SetDocument(FormattedText value)
    {
        if (disposed || document.Template == value.Template && document.Runs.SequenceEqual(value.Runs)) return;
        if (value.Text.Length > ModelUtilityPrompts.MaxTextLength * (readOnly ? 4 : 1)) throw new IOException("Texte trop long / Text is too long.");
        changing = true;
        try { editor.Text = value.Text; Accept(value); }
        finally { changing = false; }
    }
    async Task ReadClipboardAsync(int start, int length, int version)
    {
        try
        {
            var data = Clipboard.GetContent(); string html = "", plain = "";
            if (data.Contains(StandardDataFormats.Text)) plain = await data.GetTextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            if (data.Contains(StandardDataFormats.Html)) html = await data.GetHtmlFormatAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            await PasteHtmlAsync(html, plain, start, length, version);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) { if (!disposed) Error?.Invoke(ex.Message); }
    }
    async Task PasteHtmlAsync(string html, string plain, int start, int length, int version)
    {
        if (plain.Length > ModelUtilityPrompts.MaxTextLength) throw new IOException("Collage trop volumineux / Paste is too large.");
        var incoming = await Task.Run(() => html.Length > 0 ? FormattedHtml.Parse(html) : FormattedText.Plain(plain), lifetime.Token);
        if (disposed) return;
        if (version != changeVersion) throw new IOException(UiText.Resolve("Le texte a changé pendant le collage. Réessayez.", "Text changed during paste; please retry."));
        var old = document.Text;
        if (old.Length - length + incoming.Text.Length > ModelUtilityPrompts.MaxTextLength) throw new IOException("Texte trop long / Text is too long.");
        if (start == 0 && length == old.Length) SetDocument(incoming);
        else
        {
            var replacement = old[..start] + incoming.Text + old[(start + length)..];
            try { SetDocument(FormattedHtml.Edit(document, replacement)); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                SetDocument(FormattedText.Plain(replacement));
                Notice?.Invoke(UiText.Resolve("Ce collage a réinitialisé la mise en forme ; le texte est conservé.", "This paste reset formatting; the text is preserved."));
            }
        }
        editor.Select(Math.Min(start + incoming.Text.Length, editor.Text.Length), 0);
    }
    internal async Task<FormattedText> CaptureAsync()
    { await pending.WaitAsync(TimeSpan.FromSeconds(15), lifetime.Token); return document; }
    internal void Select(int start, int length)
    {
        if (disposed) return;
        editChoice.IsChecked = true;
        start = Math.Clamp(start, 0, editor.Text.Length); length = Math.Clamp(length, 0, editor.Text.Length - start);
        editor.Focus(FocusState.Programmatic); editor.Select(start, length);
    }
    void QueuePreview()
    {
        if (disposed || previewChoice.IsChecked != true) return;
        timer.Stop(); timer.Start();
    }
    async Task PaintAsync()
    {
        if (disposed || previewChoice.IsChecked != true) return;
        var value = document; var version = ++paintRevision;
        if (value.Text.Length == 0)
        {
            preview.Children.Clear();
            preview.Children.Add(new TextBlock { Text = placeholder, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary });
            return;
        }
        try
        {
            var pieces = await Task.Run(() => NativeFormattedRenderer.Prepare(value.Html), lifetime.Token);
            if (disposed || version != paintRevision || value != document) return;
            var offset = previewScroll.VerticalOffset;
            preview.Children.Clear(); preview.Children.Add(NativeFormattedRenderer.Build(pieces, value.Text));
            previewScroll.ChangeView(null, offset, null, true);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex)
        {
            if (!disposed && value == document)
            {
                preview.Children.Clear(); preview.Children.Add(new TextBlock { Text = value.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                Error?.Invoke(ex.Message);
            }
        }
    }
    internal async Task CopyAsync()
    {
        var value = await CaptureAsync(); var html = await Task.Run(() => value.Html, lifetime.Token);
        if (OperatingSystem.IsWindows()) { await FormattedClipboard.WriteAsync(html, value.Text); return; }
        var data = new DataPackage(); data.SetText(value.Text); data.SetHtmlFormat(FormattedClipboard.HtmlFormat(html)); Clipboard.SetContent(data); Clipboard.Flush();
    }
    internal void CloseEditor()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); lifetime.Cancel(); paintRevision++; Content = null;
    }
    internal async Task PasteForSmoke(string html, string plain)
    {
        try { await PasteHtmlAsync(html, plain, Text.Length, 0, changeVersion); }
        catch (Exception ex) { if (!disposed) Error?.Invoke(ex.Message); }
    }
}
