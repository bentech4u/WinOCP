using System.IO;
using System.Windows;
using System.Text.Json;
namespace WinOCP;
public partial class MainWindow {
    AutomationWindow? automationWindow;
    void ShowAutomation(object sender,RoutedEventArgs e){
        if(automationWindow!=null){automationWindow.Activate();return;}
        if(!connected){ConfirmDialog.Ask(this,"Automation","Connect to the remote cluster before configuring automation.","Close");return;}
        if(busy){Log("Wait for the current operation before opening Automation.");return;}
        var client=new AutomationClient(Path.Combine(AppContext.BaseDirectory,"tools","oc.exe"),config!,session,skipTls);
        automationWindow=new AutomationWindow(client,row=>transfers.Add(row),LocalPath.Text,Projects.SelectedItem as string??"",Pods.SelectedItem as string??"",Containers.SelectedItem as string??"",RemotePath.Text){Owner=this,OpenRemoteFolder=OpenAutomationDestination};
        automationWindow.Closed+=(_,_)=>automationWindow=null;automationWindow.Show();
    }
    async Task OpenAutomationDestination(AutomationJob job){
        RequireConnection();
        if(busy)throw new IOException("Wait for the current operation before opening the remote folder.");
        if(!Projects.Items.Contains(job.Project))throw new IOException("The job project is no longer available. Refresh the cluster projects.");
        var opened=false;
        await Run(async()=>{
            loading=true;
            try{
                Projects.SelectedItem=job.Project;
                Pods.ItemsSource=null;Containers.ItemsSource=null;RemoteFiles.ItemsSource=null;
                using var pods=JsonDocument.Parse(await Oc("get","pods","-n",job.Project,"-o","json"));
                Pods.ItemsSource=pods.RootElement.GetProperty("items").EnumerateArray().Where(x=>x.GetProperty("status").GetProperty("phase").GetString()=="Running").Select(x=>x.GetProperty("metadata").GetProperty("name").GetString()!).Order().ToArray();
                if(!Pods.Items.Contains(job.Pod))throw new IOException("The job pod is no longer running or available.");
                Pods.SelectedItem=job.Pod;
                using var pod=JsonDocument.Parse(await Oc("get","pod",job.Pod,"-n",job.Project,"-o","json"));
                Containers.ItemsSource=pod.RootElement.GetProperty("spec").GetProperty("containers").EnumerateArray().Select(x=>x.GetProperty("name").GetString()!).ToArray();
                if(!Containers.Items.Contains(job.Container))throw new IOException("The job container is no longer available.");
                Containers.SelectedItem=job.Container;RemotePath.Text=job.Destination;
            }finally{loading=false;}
            await LoadRemote();opened=true;
        });
        if(!opened)throw new IOException("Could not open the remote destination. Check the main Activity log for details.");
        if(automationWindow!=null)automationWindow.WindowState=WindowState.Minimized;
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        Activate();
    }
}