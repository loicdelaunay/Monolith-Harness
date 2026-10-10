namespace MonolithHarnessGui.Portable;

/// <summary>All values are logical pixels, including Android insets converted from physical pixels.</summary>
public readonly record struct MobileInsets(double Left, double Top, double Right, double Bottom,
    double KeyboardBottom, double WindowHeight);

public readonly record struct MobileLayout(double Left, double Top, double Right, double Bottom,
    double ContentWidth, double UsableHeight, bool Compact, double InputMaxHeight, double FormMaxHeight)
{
    public const double TouchTarget=48;
    // Landscape with an open keyboard keeps the composer accessible first.
    public bool Minimal => UsableHeight<220;
    public bool ShowWelcomeIllustration => UsableHeight>=480;
    public static MobileLayout Calculate(double width,double height,MobileInsets insets)
    {
        width=Math.Max(0,width);height=Math.Max(0,height);
        var gutter=width>=600?24d:12d;
        // adjustResize may already have removed part or all of the IME from the XAML viewport.
        var resized=Math.Max(0,insets.WindowHeight-height);
        var keyboard=Math.Max(0,insets.KeyboardBottom-resized);
        var left=Math.Max(0,insets.Left)+gutter;var right=Math.Max(0,insets.Right)+gutter;
        var top=Math.Max(0,insets.Top)+8;var bottom=Math.Max(Math.Max(0,insets.Bottom),keyboard)+8;
        var available=Math.Max(0,height-top-bottom);
        var compact=available<520 || insets.KeyboardBottom>insets.Bottom+80;
        return new(left,top,right,bottom,Math.Min(880,Math.Max(0,width-left-right)),available,compact,
            Math.Clamp(available*.22,48,128),Math.Max(0,available-(available<220?72:184)));
    }
}
