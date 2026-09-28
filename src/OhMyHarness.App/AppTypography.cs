using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

enum FontArea { Interface, User, Assistant }

static class AppTypography
{
    sealed record Scope(FontArea Area);
    sealed record Original(FontFamily Font);
    static readonly ConditionalWeakTable<DependencyObject, Scope> scopes = new();
    static readonly ConditionalWeakTable<DependencyObject, Original> originals = new();
    static FontFamily?[] fonts = [null, null, null];
    public static bool HasCustomFonts => fonts.Any(x => x != null);
    public static void Configure(FeatureSettings settings) => fonts = new[] { settings.InterfaceFont, settings.UserMessageFont, settings.AssistantMessageFont }
        .Select(value => string.IsNullOrWhiteSpace(value) ? null : new FontFamily(value)).ToArray();
    public static void SetScope(DependencyObject node, FontArea area)
    {
        scopes.Remove(node); scopes.Add(node, new(area));
    }
    public static void Apply(DependencyObject root)
    {
        var area = FontArea.Interface;
        for (DependencyObject? parent = root; parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (scopes.TryGetValue(parent, out var scope)) { area = scope.Area; break; }
        Walk(root, area);
    }
    static bool FixedFont(FontFamily font) => font.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase)
        || font.Source.Contains("Fluent Icons", StringComparison.OrdinalIgnoreCase)
        || font.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
        || font.Source.Contains("Cascadia", StringComparison.OrdinalIgnoreCase);
    static FontFamily Resolve(DependencyObject node, FontFamily current, FontArea area)
    {
        var original = originals.GetValue(node, _ => new(current)).Font;
        return FixedFont(original) ? original : fonts[(int)area] ?? original;
    }
    static void Walk(DependencyObject node, FontArea area)
    {
        if (node is FrameworkElement { Tag: "font-preview" }) return;
        if (node is IconElement) return;
        if (scopes.TryGetValue(node, out var scope)) area = scope.Area;
        if (node is TextBlock text)
        {
            var font = Resolve(text, text.FontFamily, area);
            if (text.FontFamily.Source != font.Source) text.FontFamily = font;
            foreach (var inline in text.Inlines) ApplyInline(inline, area);
            return;
        }
        if (node is TextBox or PasswordBox)
        {
            var control = (Control)node;
            var font = Resolve(control, control.FontFamily, area);
            if (control.FontFamily.Source != font.Source) control.FontFamily = font;
            return;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), area);
    }
    static void ApplyInline(Inline inline, FontArea area)
    {
        if (inline.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family)
        {
            var font = Resolve(inline, family, area);
            if (inline.FontFamily.Source != font.Source) inline.FontFamily = font;
        }
        if (inline is Span span) foreach (var child in span.Inlines) ApplyInline(child, area);
    }
}

public sealed partial class MainWindow
{
    (FrameworkElement Panel, Action<FeatureSettings> Save) BuildFontSettings()
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        string[] installed;
        try { installed = SkiaSharp.SKFontManager.Default.FontFamilies.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        catch { installed = ["Segoe UI", "Arial", "Calibri", "Georgia", "Consolas"]; }
        var panel = new StackPanel { Spacing = 14 };
        (ComboBox Picker, string[] Values) Add(string title, string selected)
        {
            var values = new[] { "" }.Concat(installed.Concat(string.IsNullOrWhiteSpace(selected) ? [] : new[] { selected }).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)).ToArray();
            var picker = new ComboBox { Header = title, ItemsSource = values.Select(x => x == "" ? WorkflowText("Police par défaut", "Default font") : x).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(values, selected)), HorizontalAlignment = HorizontalAlignment.Stretch };
            var preview = Label(WorkflowText("Aperçu · Bonjour, voici un exemple de texte.", "Preview · Here is some sample text."), 15);
            preview.Tag = "font-preview";
            void UpdatePreview() { preview.FontFamily = new FontFamily(values[Math.Clamp(picker.SelectedIndex, 0, values.Length - 1)] is { Length: > 0 } family ? family : "Segoe UI"); }
            picker.SelectionChanged += (_, _) => UpdatePreview(); UpdatePreview();
            panel.Children.Add(picker); panel.Children.Add(preview);
            return (picker, values);
        }
        var ui = Add(WorkflowText("Interface", "Interface"), settings.InterfaceFont);
        var user = Add(WorkflowText("Vos messages et zone de saisie", "Your messages and composer"), settings.UserMessageFont);
        var assistant = Add(WorkflowText("Réponses du modèle", "Model replies"), settings.AssistantMessageFont);
        panel.Children.Add(Label(WorkflowText("Trois choix indépendants, appliqués à l’enregistrement. Les blocs de code conservent leur police à chasse fixe.", "Three independent choices, applied when saved. Code blocks keep their monospaced font."), 12));
        return (new Expander { Header = WorkflowText("Polices de caractères", "Fonts"), Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }, target => {
            target.InterfaceFont = ui.Values[Math.Max(0, ui.Picker.SelectedIndex)];
            target.UserMessageFont = user.Values[Math.Max(0, user.Picker.SelectedIndex)];
            target.AssistantMessageFont = assistant.Values[Math.Max(0, assistant.Picker.SelectedIndex)];
        });
    }
}
