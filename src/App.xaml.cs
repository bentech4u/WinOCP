using System.Windows;
namespace WinOCP;
public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;ThemeManager.Apply("System");if(new MasterPasswordDialog().ShowDialog()!=true){Shutdown();return;}MainWindow=new MainWindow();ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow.Show();}
    protected override void OnExit(ExitEventArgs e){ConnectionStore.Vault.Dispose();base.OnExit(e);}
}