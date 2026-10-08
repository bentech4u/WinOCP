using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
namespace WinOCP;
public sealed class AutomationRuntime : INotifyPropertyChanged {
    public AutomationJob Job {get;set;}=new();
    string state="Stopped";
    public string State {get=>state;set{state=value;Changed();}}
    public string StateColor=>State switch{"Running"=>"#22C55E","Error"=>"#F87171","Paused"=>"#FACC15",_=>"#94A3B8"};
    public string Name=>Job.Name;
    public string Summary=>$"{Job.Pattern} → {Job.Pod}";
    public ObservableCollection<TransferItem> Transfers {get;}=new();
    public ObservableCollection<AutomationLog> Logs {get;}=new();
    public CancellationTokenSource? Stop {get;set;}
    public FolderUploadRunner? Runner {get;set;}
    public Task? Task {get;set;}
    public bool Running=>Stop!=null;
    public void Changed(){foreach(var name in new[]{nameof(Name),nameof(Summary),nameof(State),nameof(StateColor),nameof(Running)})PropertyChanged?.Invoke(this,new(name));}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public record AutomationLog(DateTime Time,string Level,string Message);
public sealed record AutomationSnapshot(string JobId,string Name,string Source,long? Total,long Bytes,string State,DateTime Created,DateTime? Started,DateTime? Completed);
public static class AutomationStore {
    public static List<AutomationJob> Load(string path){try{using var doc=JsonDocument.Parse(File.ReadAllText(path));return doc.RootElement.ValueKind==JsonValueKind.Array?JsonSerializer.Deserialize<List<AutomationJob>>(doc.RootElement.GetRawText())??new():new(){JsonSerializer.Deserialize<AutomationJob>(doc.RootElement.GetRawText())??new()};}catch{return new();}}
    public static void Save(string path,IEnumerable<AutomationJob> jobs){var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(jobs));File.Move(temp,path,true);}
    public static List<AutomationSnapshot> History(string path){try{return JsonSerializer.Deserialize<List<AutomationSnapshot>>(File.ReadAllText(path))??new();}catch{return new();}}
    public static void SaveHistory(string path,IEnumerable<AutomationRuntime> jobs){var cutoff=DateTime.Now.AddDays(-7);var rows=jobs.SelectMany(job=>job.Transfers.Where(row=>row.Created>=cutoff).Select(row=>new AutomationSnapshot(job.Job.Id,row.Name,row.SourcePath,row.Total,row.Bytes,row.State,row.Created,row.Started,row.Completed)));var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(rows));File.Move(temp,path,true);}
}
