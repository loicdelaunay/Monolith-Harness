using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

/// <summary>
/// The message of the moment set by the server's administrator: a band above the whole application, colored by its alert level (blue, amber,
/// red). It appears, changes and goes away by itself as the server publishes; the user can close it until the next message.
/// </summary>
public sealed partial class MainWindow
{
    Border? announcementBand;
    TextBlock? announcementText;
    FontIcon? announcementIcon;
    string announcementShown = "";

    // Fluent "dark" tones that keep white text readable.
    static readonly SolidColorBrush AnnouncementInfo = new(ColorHelper.FromArgb(255, 15, 108, 189));
    static readonly SolidColorBrush AnnouncementWarning = new(ColorHelper.FromArgb(255, 157, 93, 0));
    static readonly SolidColorBrush AnnouncementError = new(ColorHelper.FromArgb(255, 196, 43, 28));

    FrameworkElement BuildAnnouncementBand()
    {
        announcementIcon = FluentDesign.Icon("", 16);
        announcementIcon.Foreground = new SolidColorBrush(Colors.White);
        announcementIcon.VerticalAlignment = VerticalAlignment.Center;
        announcementText = new TextBlock
        {
            FontSize = 13, Foreground = new SolidColorBrush(Colors.White), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true
        };
        var close = new Button
        {
            Content = FluentDesign.Icon("", 12), Padding = new(8, 6, 8, 6), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new(0),
            Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center
        };
        ((FontIcon)close.Content).Foreground = new SolidColorBrush(Colors.White);
        ToolTipService.SetToolTip(close, WorkflowText("Masquer ce message jusqu'au prochain", "Hide this message until the next one"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, WorkflowText("Masquer le message du serveur", "Hide the server message"));
        close.Click += (_, _) => { serverSync?.DismissAnnouncement(); RefreshAnnouncement(); };
        var content = new Grid { ColumnSpacing = 12, Padding = new(16, 6, 8, 6) };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(announcementIcon);
        Grid.SetColumn(announcementText, 1); content.Children.Add(announcementText);
        Grid.SetColumn(close, 2); content.Children.Add(close);
        announcementBand = new Border { Child = content, Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(announcementBand, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        return announcementBand;
    }

    /// <summary>Shows, updates or hides the band from the synchronization state (the service already left out a message that is over or closed).</summary>
    void RefreshAnnouncement()
    {
        if (announcementBand == null || announcementText == null || announcementIcon == null) return;
        var message = serverSync?.Snapshot.Announcement;
        if (message == null)
        {
            announcementBand.Visibility = Visibility.Collapsed;
            announcementShown = "";
            return;
        }
        announcementBand.Background = message.Level switch { "error" => AnnouncementError, "warning" => AnnouncementWarning, _ => AnnouncementInfo };
        announcementIcon.Glyph = message.Level switch { "error" => "", "warning" => "", _ => "" };
        var heading = message.Level switch
        {
            "error" => WorkflowText("Incident", "Incident"),
            "warning" => WorkflowText("Attention", "Warning"),
            _ => WorkflowText("Information", "Information")
        };
        var text = $"{heading} · {message.Text}";
        if (announcementText.Text != text) announcementText.Text = text;
        ToolTipService.SetToolTip(announcementBand, message.By is { Length: > 0 } by ? WorkflowText($"Publié par {by}", $"Published by {by}") : null);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(announcementBand, text);
        announcementBand.Visibility = Visibility.Visible;
        // A new message is announced to assistive technologies once.
        if (announcementShown != message.Stamp)
        {
            announcementShown = message.Stamp;
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(announcementBand);
            peer?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }
}
