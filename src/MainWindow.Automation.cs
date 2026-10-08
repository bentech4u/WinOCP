using System.IO;
using System.Windows;
namespace WinOCP;
public partial class MainWindow {
    AutomationWindow? automationWindow;
    void ShowAutomation(object sender,RoutedEventArgs e){
        if(automationWindow!=null){automationWindow.Activate();return;}
        if(!connected){ConfirmDialog.Ask(this,"Automation","Connect to the remote cluster before configuring automation.","Close");return;}
        if(busy){Log("Wait for the current operation before opening Automation.");return;}
        var client=new AutomationClient(Path.Combine(AppContext.BaseDirectory,"tools","oc.exe"),config!,session,skipTls);
        automationWindow=new AutomationWindow(client,row=>transfers.Add(row),LocalPath.Text,Projects.SelectedItem as string??"",Pods.SelectedItem as string??"",Containers.SelectedItem as string??"",RemotePath.Text){Owner=this};
        automationWindow.Closed+=(_,_)=>automationWindow=null;automationWindow.Show();
    }
}
