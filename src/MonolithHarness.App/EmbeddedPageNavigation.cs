using System.Text;

namespace MonolithHarness.App;

internal static class EmbeddedPageNavigation
{
    internal static void RestrictToDocument(Microsoft.UI.Xaml.Controls.WebView2 view, string html)
    {
        // Uno 6.7 reports NavigateToString as this data URI in NavigationStarting;
        // native WinUI reports about:blank. Accept only this exact bundled page.
        var documentUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        view.CoreWebView2.NavigationStarting += (_, e) =>
            e.Cancel = e.Uri != "about:blank" && !string.Equals(e.Uri, documentUri, StringComparison.Ordinal);
        view.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
    }
}
