using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

internal sealed partial class ModelToolsWindow
{
    FormattedTextEditor proofInput = null!, rewritten = null!;
    ComboBox proofLanguage = null!;
    readonly StackPanel suggestions = new() { Spacing = 10 };
    ProofreadingDocument? proofDocument;
    bool rewriting, applyingProofEdit;
    TabView proofTabs = null!;
    TabViewItem correctTab = null!, rewriteTab = null!;
    Button applyAll = null!, useRewrite = null!;

    UIElement BuildProofreader()
    {
        proofInput = RichEditor(L("Collez votre message ici : sa mise en forme sera conservée.", "Paste your message here: formatting is preserved."));
        rewritten = RichEditor(L("La reformulation apparaîtra ici.", "Your rewritten text will appear here."), true);
        proofLanguage = LanguagePicker(); proofLanguage.Header = L("Langue du texte", "Text language");
        var count = Text("", 12, true);
        var clear = Button(L("Supprimer", "Clear"), () => { proofInput.Text = ""; return Task.CompletedTask; });
        var left = StretchGrid();
        At(left, proofLanguage, 0); At(left, proofInput, 1); At(left, Row(CopyFormatted(() => proofInput), clear, count), 2);
        var right = new Grid { RowSpacing = 10 };
        right.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var suggestionScroll = new ScrollViewer { Content = suggestions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        proofTabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.Equal };
        correctTab = new TabViewItem { Header = L("Corrections", "Corrections"), IsClosable = false, Content = suggestionScroll };
        rewriteTab = new TabViewItem { Header = L("Reformulation", "Rephrase"), IsClosable = false, Content = rewritten };
        proofTabs.TabItems.Add(correctTab); proofTabs.TabItems.Add(rewriteTab); proofTabs.SelectedItem = correctTab;
        At(right, proofTabs, 0);
        applyAll = Button(L("Tout appliquer", "Apply all"), async () => { await proofInput.CaptureAsync(); ApplyProof(null); });
        useRewrite = Button(L("Utiliser cette version", "Use this version"), async () => { proofInput.SetDocument(await rewritten.CaptureAsync()); });
        var copyResult = CopyFormatted(() => rewriting ? rewritten : proofInput);
        var resultActions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        resultActions.Children.Add(applyAll); resultActions.Children.Add(useRewrite); resultActions.Children.Add(copyResult);
        At(right, resultActions, 1);
        run = Button(L("Vérifier le texte", "Check text"), CheckWriting, true);
        busyControls.AddRange([proofInput, proofLanguage, clear, proofTabs]);
        refreshActions = () =>
        {
            run.Content = rewriting ? L("Reformuler", "Rephrase") : L("Vérifier le texte", "Check text");
            run.IsEnabled = !Busy && model.SelectedItem != null && !string.IsNullOrWhiteSpace(proofInput.Text);
            applyAll.IsEnabled = !Busy && proofDocument?.Corrections.Count > 0;
            useRewrite.IsEnabled = !Busy && !string.IsNullOrWhiteSpace(rewritten.Text);
            copyResult.IsEnabled = !string.IsNullOrWhiteSpace(rewriting ? rewritten.Text : proofInput.Text);
            correctTab.Header = L("Corrections", "Corrections") + (proofDocument == null ? "" : $" ({proofDocument.Corrections.Count})");
            applyAll.Visibility = rewriting ? Visibility.Collapsed : Visibility.Visible;
            useRewrite.Visibility = rewriting ? Visibility.Visible : Visibility.Collapsed;
            count.Text = $"{proofInput.Text.Length:N0} " + L("caractères", "characters") + " · " +
                proofInput.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length.ToString("N0") + " " + L("mots", "words");
        };
        proofTabs.SelectionChanged += (_, _) => { rewriting = proofTabs.SelectedItem == rewriteTab; refreshActions(); };
        void Invalidate(bool languageChanged = false)
        {
            // Uno may dispatch TextChanged after a programmatic replacement has returned.
            // Match the document's current source as well as the synchronous edit guard.
            if (!applyingProofEdit && (languageChanged || proofDocument?.Text != proofInput.Text))
            { proofDocument = null; rewritten.Text = ""; usage.Text = ""; RenderCorrections(); }
            refreshActions();
        }
        proofInput.TextChanged += (_, _) => Invalidate();
        proofLanguage.SelectionChanged += (_, _) => Invalidate(true);
        rewritten.TextChanged += (_, _) => refreshActions();
        RenderCorrections();
        return ResponsivePair(left, right, .72);
    }

    Task CheckWriting() => StartOperation(async (selected, ct) =>
    {
        var document = await proofInput.CaptureAsync();
        var text = document.Text; var language = proofLanguage.SelectedItem?.ToString() ?? "French";
        if (text.Length > ModelUtilityPrompts.MaxTextLength) throw new IOException(L("Texte trop long (20 000 caractères maximum).", "Text too long (20,000 characters maximum)."));
        if (rewriting)
        {
            rewritten.Text = "";
            var response = await Request(selected, document.TransformRequest($"Rephrase in {language}, improving clarity and fluency while preserving meaning, facts and tone."), null, ct);
            rewritten.SetDocument(document.ReadTransformation(response.Text));
            SetNotice(L("Reformulation prête. Vous pouvez remplacer le texte d’origine.", "Rephrasing ready. You can replace the original text."));
        }
        else
        {
            proofDocument = null; suggestions.Children.Clear();
            suggestions.Children.Add(Text(L("Analyse de l’orthographe, de la grammaire et de la ponctuation…", "Checking spelling, grammar and punctuation…"), 14, true));
            try
            {
                var response = await Request(selected, ModelUtilityPrompts.Proofread(text, language), null, ct);
                proofDocument = ProofreadingDocument.Parse(text, response.Text);
                SetNotice(proofDocument.Corrections.Count == 0 ? L("Aucune erreur détectée par le modèle.", "The model found no errors.") : L("Sélectionnez une suggestion pour retrouver le passage dans le texte.", "Select a suggestion to locate it in the text."));
            }
            finally { RenderCorrections(); }
        }
        refreshActions();
    });

    void RenderCorrections()
    {
        suggestions.Children.Clear();
        if (proofDocument == null || proofDocument.Corrections.Count == 0)
        {
            suggestions.Children.Add(Card(Text(proofDocument == null
                ? L("Lancez la vérification pour afficher les suggestions ici.", "Run a check to see suggestions here.")
                : L("Aucune suggestion restante.", "No remaining suggestions."), 14, true)));
            return;
        }
        foreach (var correction in proofDocument.Corrections)
        {
            var body = new StackPanel { Spacing = 8 };
            var locate = Button(L("Voir dans le texte", "Locate in text"), () =>
            {
                proofInput.Focus(FocusState.Programmatic); proofInput.Select(correction.Start, correction.Length);
                return Task.CompletedTask;
            });
            locate.HorizontalAlignment = HorizontalAlignment.Stretch;
            locate.HorizontalContentAlignment = HorizontalAlignment.Left;
            body.Children.Add(locate);
            body.Children.Add(Text(correction.Replacement.Length == 0 ? L("Supprimer ce passage", "Remove this text") : "→ " + correction.Replacement, 16));
            body.Children.Add(Text(correction.Reason, 12, true));
            body.Children.Add(Row(
                Button(L("Appliquer", "Apply"), async () => { await proofInput.CaptureAsync(); ApplyProof(correction); }),
                Button(L("Ignorer", "Ignore"), () => { proofDocument?.Ignore(correction); RenderCorrections(); refreshActions(); return Task.CompletedTask; })));
            var header = Text((correction.Original.Length == 0 ? L("Ponctuation", "Punctuation") : correction.Original) + " → " + correction.Replacement, 14);
            header.MaxLines = 2; header.TextTrimming = TextTrimming.CharacterEllipsis;
            var suggestion = new Expander { Header = header, Content = body, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            suggestion.Expanding += (_, _) => proofInput.Select(correction.Start, correction.Length);
            suggestions.Children.Add(suggestion);
        }
    }

    void ApplyProof(TextCorrection? correction)
    {
        if (Busy || proofDocument == null || proofDocument.Text != proofInput.Text) return;
        applyingProofEdit = true;
        try
        {
            var formatted = proofInput.Document.Apply(correction == null ? proofDocument.Corrections : [correction]);
            if (correction == null) proofDocument.ApplyAll(); else proofDocument.Apply(correction);
            proofInput.SetDocument(formatted); rewritten.Text = "";
        }
        catch (Exception ex) { SetNotice(ex.Message, true); }
        finally { applyingProofEdit = false; }
        RenderCorrections(); refreshActions();
    }
}
