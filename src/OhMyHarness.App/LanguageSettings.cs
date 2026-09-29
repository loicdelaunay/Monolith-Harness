using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    static UIElement LanguageFlag(string language)
    {
        var flag = new Canvas { Width = 24, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        void Rect(double x, double y, double w, double h, byte r, byte g, byte b)
        { var part = new Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromArgb(255, r, g, b)) }; Canvas.SetLeft(part, x); Canvas.SetTop(part, y); flag.Children.Add(part); }
        if (language == "fr") { Rect(0,0,8,16,0,45,120); Rect(8,0,8,16,255,255,255); Rect(16,0,8,16,220,35,45); }
        else if (language == "de") { Rect(0,0,24,5.33,20,20,20); Rect(0,5.33,24,5.34,210,25,35); Rect(0,10.67,24,5.33,255,205,0); }
        else if (language == "es") { Rect(0,0,24,4,190,20,40); Rect(0,4,24,8,255,200,0); Rect(0,12,24,4,190,20,40); Rect(7,6,2,4,190,20,40); }
        else
        {
            Rect(0,0,24,16,20,40,100);
            foreach (var reverse in new[] { false, true })
            {
                flag.Children.Add(new Line { X1=0,Y1=reverse?16:0,X2=24,Y2=reverse?0:16,Stroke=new SolidColorBrush(Microsoft.UI.Colors.White),StrokeThickness=4 });
                flag.Children.Add(new Line { X1=0,Y1=reverse?16:0,X2=24,Y2=reverse?0:16,Stroke=new SolidColorBrush(Color.FromArgb(255,210,30,45)),StrokeThickness=1.5 });
            }
            Rect(9,0,6,16,255,255,255); Rect(0,5,24,6,255,255,255);
            Rect(10,0,4,16,210,30,45); Rect(0,6,24,4,210,30,45);
        }
        return new Border { Child = flag, CornerRadius = new(2), BorderThickness = new(1), BorderBrush = FluentDesign.Stroke };
    }
    ComboBox BuildLanguagePicker()
    {
        var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (code, name) in new[] { ("fr", "Français"), ("en", "English"), ("de", "Deutsch"), ("es", "Español") })
        {
            var row = Row(LanguageFlag(code), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var item = new ComboBoxItem { Tag = code, Content = row };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, name);
            picker.Items.Add(item); if (state.Language == code) picker.SelectedItem = item;
        }
        if (picker.SelectedIndex < 0) picker.SelectedIndex = 0;
        return picker;
    }
}
