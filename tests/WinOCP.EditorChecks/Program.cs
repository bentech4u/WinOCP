using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WinOCP;
static class Checks {
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread]static void Main(){
        var settings=Path.Combine(AppContext.BaseDirectory,"automation.json");var history=Path.Combine(AppContext.BaseDirectory,"automation-history.json");if(File.Exists(settings)||File.Exists(history))throw new Exception("Use a clean test output directory; existing data was not changed.");
        var app=new App();app.InitializeComponent();ThemeManager.Apply("Dark");AutomationWindow? window=null;
        try{
            AutomationJob Job(string name)=>new(){Name=name,Source=AppContext.BaseDirectory,Project="test",Pod="pod",Container="app",Destination="/tmp"};
            AutomationStore.Save(settings,new[]{Job("One"),Job("Two")});window=new AutomationWindow(new AutomationClient("unused","unused",AppContext.BaseDirectory,false),_=>{},AppContext.BaseDirectory,"test","pod","app","/tmp");
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;var jobs=(ObservableCollection<AutomationRuntime>)typeof(AutomationWindow).GetField("jobs",flags)!.GetValue(window)!;var list=(ListBox)window.FindName("JobsList");var name=(TextBox)window.FindName("JobName");var description=(TextBox)window.FindName("Description");var source=(TextBox)window.FindName("SourcePath");int prompts=0;var choice=UnsavedChoice.Cancel;window.UnsavedPrompt=_=>{prompts++;return choice;};
            Assert(!(bool)typeof(AutomationWindow).GetField("dirty",flags)!.GetValue(window)!,"Initial fill marked dirty");
            name.Text="Edited One";list.SelectedItem=jobs[1];Assert(list.SelectedItem==jobs[0]&&name.Text=="Edited One"&&prompts==1,"Cancel did not retain current edits");
            choice=UnsavedChoice.Discard;list.SelectedItem=jobs[1];Assert(list.SelectedItem==jobs[1]&&jobs[0].Name=="One","Discard did not restore original job");
            description.Text="Saved notes";choice=UnsavedChoice.Save;list.SelectedItem=jobs[0];Assert(AutomationStore.Load(settings).Single(x=>x.Name=="Two").Description=="Saved notes","Save-before-switch did not persist edits");
            source.Text="";list.SelectedItem=jobs[1];Assert(list.SelectedItem==jobs[0]&&source.Text=="","Validation failure switched job or lost edits");
            choice=UnsavedChoice.Discard;typeof(AutomationWindow).GetMethod("NewJob",flags)!.Invoke(window,new object[]{window,new RoutedEventArgs()});Assert(jobs.Count==3,"New job failed");list.SelectedItem=jobs[0];Assert(jobs.Count==2,"Discard left an unsaved draft in the job list");
            ((TextBox)window.FindName("ArchiveDirectory")).Text=Path.Combine(AppContext.BaseDirectory,"archive");bool closed=false;window.Closed+=(_,_)=>closed=true;choice=UnsavedChoice.Cancel;window.Close();Assert(!closed,"Cancel did not prevent close");choice=UnsavedChoice.Discard;window.Close();Assert(closed,"Discard did not allow close");window=null;
            Console.WriteLine("Editor checks passed: clean initialization, switch Save/Discard/Cancel, validation failure, draft discard, Completion edits, and close cancellation.");
        }finally{if(window!=null){window.UnsavedPrompt=_=>UnsavedChoice.Discard;window.Close();}if(File.Exists(settings))File.Delete(settings);if(File.Exists(history))File.Delete(history);app.Shutdown();}
    }
}