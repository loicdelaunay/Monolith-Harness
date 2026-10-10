using Microsoft.UI;
using Microsoft.UI.Xaml;
namespace MonolithHarnessGui.Portable;

/// <summary>Use Android's wallpaper palette on Android 12+, with Material fallback colors.</summary>
public static class MaterialColors
{
    public static bool IsDark => Application.Current?.RequestedTheme==ApplicationTheme.Dark;
    static Windows.UI.Color Color(string name,string fallback)
    {
        if(Android.OS.Build.VERSION.SdkInt>=Android.OS.BuildVersionCodes.S)
        {
            var resources=Android.App.Application.Context.Resources!;
            var id=resources.GetIdentifier(name,"color","android");
            if(id!=0) {var argb=(uint)resources.GetColor(id,null).ToArgb();return ColorHelper.FromArgb((byte)(argb>>24),(byte)(argb>>16),(byte)(argb>>8),(byte)argb);}
        }
        var rgb=Convert.ToUInt32(fallback,16);return ColorHelper.FromArgb(255,(byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);
    }
    public static ResourceDictionary Create()
    {
        var dark=new ResourceDictionary();var light=new ResourceDictionary();
        void Role(string key,string darkName,string darkHex,string lightName,string lightHex) {dark[key]=Color(darkName,darkHex);light[key]=Color(lightName,lightHex);}
        Role("PrimaryColor","system_accent1_200","D0BCFF","system_accent1_600","6750A4");
        Role("OnPrimaryColor","system_accent1_800","381E72","system_accent1_0","FFFFFF");
        Role("PrimaryContainerColor","system_accent1_700","4F378B","system_accent1_100","EADDFF");
        Role("OnPrimaryContainerColor","system_accent1_100","EADDFF","system_accent1_900","21005D");
        Role("SecondaryColor","system_accent2_200","CCC2DC","system_accent2_600","625B71");
        Role("OnSecondaryColor","system_accent2_800","332D41","system_accent2_0","FFFFFF");
        Role("SecondaryContainerColor","system_accent2_700","4A4458","system_accent2_100","E8DEF8");
        Role("OnSecondaryContainerColor","system_accent2_100","E8DEF8","system_accent2_900","1D192B");
        Role("TertiaryColor","system_accent3_200","EFB8C8","system_accent3_600","7D5260");
        Role("OnTertiaryColor","system_accent3_800","492532","system_accent3_0","FFFFFF");
        Role("BackgroundColor","system_neutral1_900","141218","system_neutral1_10","FFFBFE");
        Role("OnBackgroundColor","system_neutral1_100","E6E0E9","system_neutral1_900","1C1B1F");
        Role("SurfaceColor","system_neutral1_900","141218","system_neutral1_10","FFFBFE");
        Role("OnSurfaceColor","system_neutral1_100","E6E0E9","system_neutral1_900","1C1B1F");
        Role("SurfaceVariantColor","system_neutral2_700","49454F","system_neutral2_100","E7E0EC");
        Role("OnSurfaceVariantColor","system_neutral2_200","CAC4D0","system_neutral2_700","49454F");
        Role("OutlineColor","system_neutral2_400","938F99","system_neutral2_500","79747E");
        Role("OutlineVariantColor","system_neutral2_700","49454F","system_neutral2_200","CAC4D0");
        return new ResourceDictionary {ThemeDictionaries={{"Dark",dark},{"Light",light}}};
    }
}
