using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace WinOCP;
public partial class AutomationWindow : Window {
    readonly AutomationClient client;
    readonly Action<TransferItem> add;
    readonly CancellationTokenSource lifetime=new();
    CancellationTokenSource? run;
    readonly string settings=Path.Combine(AppContext.BaseDirectory,"automation.json");
    bool loading;
    string cluster="";
    AutomationJob saved=new();
    public bool IsRunning=>run!=null;
    public AutomationWindow(AutomationClient client,Action<TransferItem> add,string source,string project,string pod,string container,string destination){
        this.client=client;this.add=add;InitializeComponent();ParallelFiles.ItemsSource=Enumerable.Range(1,16);Stability.ItemsSource=new[]{1,5,10,20,30,60,120,300};
        try{saved=JsonSerializer.Deserialize<AutomationJob>(File.ReadAllText(settings))??new();}catch{}
        JobName.Text=saved.Name;SourcePath.Text=Directory.Exists(saved.Source)?saved.Source:source;Pattern.Text=saved.Pattern;Recursive.IsChecked=saved.Recursive;ParallelFiles.SelectedItem=Math.Clamp(saved.Parallel,1,16);Stability.SelectedItem=saved.StableSeconds;if(Stability.SelectedIndex<0)Stability.SelectedItem=10;Destination.Text=string.IsNullOrWhiteSpace(saved.Project)?destination:saved.Destination;
        Loaded+=async(_,_)=>{try{cluster=(await client.Query(lifetime.Token,"config","view","--minify","-o","jsonpath={.clusters[0].cluster.server}")).Trim();if(string.IsNullOrWhiteSpace(cluster))throw new IOException("Could not identify the connected cluster.");ClusterLabel.Text=cluster;await client.Query(lifetime.Token,"whoami");loading=true;Namespaces.ItemsSource=ProjectCatalog.UserProjects(await client.Query(lifetime.Token,"get","projects","-o","json"));Namespaces.SelectedItem=Namespaces.Items.Contains(saved.Project)?saved.Project:project;loading=false;await LoadPods(saved.Pod.Length>0?saved.Pod:pod,saved.Container.Length>0?saved.Container:container);JobStatus.Text="Ready. Choose settings and test or start.";}catch(Exception ex){Warn(ex);}};
        Closing+=(_,e)=>{if(IsRunning){e.Cancel=true;JobStatus.Text="Stop the automation before closing this window.";}else lifetime.Cancel();};Closed+=(_,_)=>lifetime.Dispose();
    }
    void Warn(Exception ex){loading=false;JobStatus.Text="Connect to the remote cluster and verify the destination. "+ex.Message;}
    async Task LoadPods(string preferred="",string container=""){
        if(Namespaces.SelectedItem is not string project)return;loading=true;Namespaces.IsEnabled=JobPods.IsEnabled=JobContainers.IsEnabled=false;try{using var doc=JsonDocument.Parse(await client.Query(lifetime.Token,"get","pods","-n",project,"-o","json"));JobPods.ItemsSource=doc.RootElement.GetProperty("items").EnumerateArray().Where(x=>x.GetProperty("status").GetProperty("phase").GetString()=="Running").Select(x=>x.GetProperty("metadata").GetProperty("name").GetString()!).Order().ToArray();JobPods.SelectedItem=JobPods.Items.Contains(preferred)?preferred:null;JobContainers.ItemsSource=null;}finally{loading=false;Namespaces.IsEnabled=JobPods.IsEnabled=JobContainers.IsEnabled=true;}await LoadContainers(container);
    }
    async Task LoadContainers(string preferred=""){
        if(Namespaces.SelectedItem is not string project||JobPods.SelectedItem is not string pod)return;using var doc=JsonDocument.Parse(await client.Query(lifetime.Token,"get","pod",pod,"-n",project,"-o","json"));JobContainers.ItemsSource=doc.RootElement.GetProperty("spec").GetProperty("containers").EnumerateArray().Select(x=>x.GetProperty("name").GetString()!).ToArray();JobContainers.SelectedItem=JobContainers.Items.Contains(preferred)?preferred:null;if(JobContainers.SelectedIndex<0)JobContainers.SelectedIndex=0;
    }
    async void NamespaceChanged(object s,SelectionChangedEventArgs e){if(loading)return;try{await LoadPods();}catch(Exception ex){Warn(ex);}}
    async void JobPodChanged(object s,SelectionChangedEventArgs e){if(loading)return;try{await LoadContainers();}catch(Exception ex){Warn(ex);}}
    void Browse(object s,RoutedEventArgs e){var picker=new OpenFolderDialog{Title="Choose source directory",InitialDirectory=SourcePath.Text};if(picker.ShowDialog(this)==true)SourcePath.Text=picker.FolderName;}
    AutomationJob Read()=>new(){Name=JobName.Text,Source=Path.GetFullPath(SourcePath.Text),Pattern=Pattern.Text,Project=Namespaces.SelectedItem as string??"",Pod=JobPods.SelectedItem as string??"",Container=JobContainers.SelectedItem as string??"",Destination=Destination.Text.Trim(),Parallel=(int?)ParallelFiles.SelectedItem??1,StableSeconds=(int?)Stability.SelectedItem??10,Recursive=Recursive.IsChecked==true,Cluster=cluster};
    void Log(string text){JobStatus.Text=text;JobLog.AppendText($"{DateTime.Now:HH:mm:ss} {text}\n");JobLog.ScrollToEnd();}
    FolderUploadRunner Runner(AutomationJob job)=>new(job,client,Log,add,Path.Combine(AppContext.BaseDirectory,"automation-receipts.json"));
    async void Test(object s,RoutedEventArgs e){TestButton.IsEnabled=StartButton.IsEnabled=false;try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(20));await Runner(Read()).Test(timeout.Token);Log("Configuration valid: connected and destination directory writable.");}catch(Exception ex){Warn(ex);}finally{TestButton.IsEnabled=StartButton.IsEnabled=true;}}
    async void StartJob(object s,RoutedEventArgs e){if(IsRunning)return;try{var job=Read();FolderUploadRunner.Validate(job);if(!ConfirmDialog.Ask(this,"Start automation",$"Watch {job.Source} for {job.Pattern} and upload to {job.Project}/{job.Pod}:{job.Destination}?\n\nExisting destination files may be overwritten. Source files are retained.","Start"))return;File.WriteAllText(settings,JsonSerializer.Serialize(job));run=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);SettingsPanel.IsEnabled=StartButton.IsEnabled=TestButton.IsEnabled=false;StopButton.IsEnabled=true;await Runner(job).Run(run.Token);}catch(OperationCanceledException) when (run?.IsCancellationRequested==true){Log("Automation stopped. Partial destinations may remain.");}catch(Exception ex){Warn(ex);}finally{run?.Dispose();run=null;SettingsPanel.IsEnabled=StartButton.IsEnabled=TestButton.IsEnabled=true;StopButton.IsEnabled=false;}}
    void StopJob(object s,RoutedEventArgs e){run?.Cancel();JobStatus.Text="Stopping active uploads…";}
    void CloseWindow(object s,RoutedEventArgs e)=>Close();
    public void StopForShutdown(){run?.Cancel();lifetime.Cancel();}
}
