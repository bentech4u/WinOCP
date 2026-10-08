using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
namespace WinOCP;
public partial class AboutWindow : Window {
    public const string ReleaseVersion = "0.29";
    public AboutWindow() { InitializeComponent(); VersionText.Text = "Version " + ReleaseVersion + " · Portable Edition"; }
    void OpenLink(object sender, RequestNavigateEventArgs e) { Launch(e.Uri.AbsoluteUri); e.Handled = true; }
    void OpenSupport(object sender, RoutedEventArgs e) => Launch("https://buymeacoffee.com/bentech4u");
    void Launch(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open link"); } }
    void CloseAbout(object sender, RoutedEventArgs e) => Close();
}
