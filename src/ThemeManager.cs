using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace WinOCP;
public static class ThemeManager
{
    public static bool SystemIsDark() {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch { return false; }
    }
    public static void Apply(string preference) {
        bool dark = preference == "Dark" || (preference == "System" && SystemIsDark());
        string[] keys = ["PageBrush", "CardBrush", "TextBrush", "MutedBrush", "BorderBrush", "InputBrush", "HeaderBrush", "HoverBrush", "SelectedBrush"];
        string[] colors = dark
            ? ["#0D1522", "#172334", "#E4EDF7", "#A6B9CD", "#34465C", "#111C2B", "#1E2D41", "#263F5B", "#234F7A"]
            : ["#EEF4F9", "#FFFFFF", "#172B45", "#52677B", "#CCD9E8", "#FBFDFF", "#F4F8FC", "#EAF3FF", "#D7E9FF"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
