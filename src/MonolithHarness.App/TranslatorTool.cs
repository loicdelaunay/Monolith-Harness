using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

internal sealed partial class ModelToolsWindow
{
    FormattedTextEditor translationInput = null!, translationOutput = null!;
    ComboBox sourceLanguage = null!, targetLanguage = null!;
    bool swappingTranslation;
    string? translatedInputKey;
    string TranslationInputKey => sourceLanguage.SelectedIndex + "\0" + targetLanguage.SelectedIndex + "\0" + translationInput.DocumentVersion;

    UIElement BuildTranslator()
    {
        translationInput = RichEditor(L("Collez votre message ici : sa mise en forme sera conservée.", "Paste your message here: formatting is preserved."));
        translationOutput = RichEditor(L("La traduction apparaîtra ici.", "Your translation will appear here."), true);
        sourceLanguage = LanguagePicker(true, 1); targetLanguage = LanguagePicker(false, 1);
        sourceLanguage.Header = L("Langue source", "Source language"); targetLanguage.Header = L("Langue cible", "Target language");
        var count = Text("", 12, true);
        var clear = Button(L("Effacer", "Clear"), () => { translationInput.Text = ""; translationOutput.Text = ""; return Task.CompletedTask; });
        var copy = CopyFormatted(() => translationOutput);
        var swap = Button(L("Inverser", "Swap"), SwapTranslation);
        var left = StretchGrid(); var right = StretchGrid();
        var leftHeader = new Grid { ColumnSpacing = 8 };
        leftHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); leftHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        At(leftHeader, sourceLanguage, 0); swap.VerticalAlignment = VerticalAlignment.Bottom; At(leftHeader, swap, 0, 1);
        At(left, leftHeader, 0); At(left, translationInput, 1);
        At(left, Row(clear, count), 2);
        At(right, targetLanguage, 0); At(right, translationOutput, 1); At(right, copy, 2);
        run = Button(L("Traduire", "Translate"), Translate, true);
        busyControls.AddRange([translationInput, sourceLanguage, targetLanguage, clear, swap]);
        refreshActions = () =>
        {
            run.IsEnabled = !Busy && model.SelectedItem != null && !string.IsNullOrWhiteSpace(translationInput.Text);
            swap.IsEnabled = !Busy && sourceLanguage.SelectedIndex > 0;
            copy.IsEnabled = !string.IsNullOrWhiteSpace(translationOutput.Text);
            count.Text = $"{translationInput.Text.Length:N0} / {ModelUtilityPrompts.MaxTextLength:N0} " + L("caractères", "characters");
        };
        void InvalidateTranslation()
        {
            if (!Busy && !swappingTranslation && TranslationInputKey != translatedInputKey) { translationOutput.Text = ""; usage.Text = ""; }
            refreshActions();
        }
        translationInput.TextChanged += (_, _) => InvalidateTranslation();
        sourceLanguage.SelectionChanged += (_, _) => InvalidateTranslation();
        targetLanguage.SelectionChanged += (_, _) => InvalidateTranslation();
        translationOutput.TextChanged += (_, _) => refreshActions();
        return ResponsivePair(left, right);
    }

    Task Translate() => StartOperation(async (selected, ct) =>
    {
        var document = await translationInput.CaptureAsync();
        if (document.Text.Length > ModelUtilityPrompts.MaxTextLength) throw new IOException(L("Texte trop long (20 000 caractères maximum).", "Text too long (20,000 characters maximum)."));
        translatedInputKey = TranslationInputKey;
        translationOutput.Text = "";
        var source = sourceLanguage.SelectedIndex == 0 ? "the automatically detected source language" : sourceLanguage.SelectedItem?.ToString() ?? "French";
        var target = targetLanguage.SelectedItem?.ToString() ?? "English";
        var response = await Request(selected, document.TransformRequest($"Translate from {source} to {target}, preserving meaning and tone."), null, ct);
        translationOutput.SetDocument(document.ReadTransformation(response.Text));
        SetNotice(L("Traduction terminée.", "Translation complete."));
    });

    async Task SwapTranslation()
    {
        if (sourceLanguage.SelectedIndex == 0) return;
        var original = await translationInput.CaptureAsync();
        var translated = await translationOutput.CaptureAsync();
        swappingTranslation = true;
        var source = sourceLanguage.SelectedIndex - 1;
        sourceLanguage.SelectedIndex = targetLanguage.SelectedIndex + 1;
        targetLanguage.SelectedIndex = source;
        if (!string.IsNullOrEmpty(translationOutput.Text))
        { translationInput.SetDocument(translated); translationOutput.SetDocument(original); }
        translatedInputKey = TranslationInputKey;
        swappingTranslation = false; usage.Text = ""; refreshActions();
    }
}
