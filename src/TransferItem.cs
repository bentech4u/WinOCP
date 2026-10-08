using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
namespace WinOCP;
public sealed class TransferItem : INotifyPropertyChanged {
    public string Name { get; }
    public string Direction { get; }
    public string SourcePath { get; }
    public DateTime Created { get; private set; } = DateTime.Now;
    public DateTime? Started { get; private set; }
    public DateTime? Completed { get; private set; }
    public long? Total { get; private set; }
    public long Bytes { get; private set; }
    public string State { get; private set; } = "Queued";
    public bool CanCancel => State is "Queued" or "Transferring";
    public bool Indeterminate => Total == null && CanCancel;
    public double Percentage => Total is > 0 ? Math.Min(100, Bytes * 100d / Total.Value) : State == "Completed" ? 100 : 0;
    public string ProgressText => Total.HasValue ? $"{Percentage:0.0}% · {Format(Bytes)} / {Format(Total.Value)} · {Format(Math.Max(0, Total.Value - Bytes))} remaining" : "Folder size unavailable";
    public string Speed => watch.Elapsed.TotalSeconds > 0 && Bytes > 0 ? Format((long)(Bytes / watch.Elapsed.TotalSeconds)) + "/s" : "—";
    readonly Stopwatch watch = new();
    public CancellationTokenSource Cancellation { get; } = new();
    public TransferItem(string name, bool upload, string sourcePath = "") { SourcePath = sourcePath; Name = name; Direction = upload ? "Upload →" : "← Download"; }
    public static TransferItem Restore(string name,string source,long? total,long bytes,string state,DateTime created,DateTime? started,DateTime? completed) { var row=new TransferItem(name,true,source){Total=total,Bytes=bytes,State=state,Created=created,Started=started,Completed=completed};return row; }
    public void Prepare(long? total){Total=total;Notify();}
    public void Start(long? total) { Total = total; State = "Transferring"; Bytes = 0; Started ??= DateTime.Now; watch.Restart(); Notify(); }
    public void Update(long bytes) { Bytes = bytes; Notify(false); }
    public void Finish(string state) { State = state; Completed = DateTime.Now; watch.Stop(); Notify(); }
    void Notify(bool stateChanged = true) { foreach (var name in stateChanged ? new[]{nameof(State),nameof(Total),nameof(Started),nameof(Completed),nameof(CanCancel),nameof(Indeterminate),nameof(Percentage),nameof(ProgressText),nameof(Speed)} : new[]{nameof(Percentage),nameof(ProgressText),nameof(Speed)}) PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name)); }
    public static string Format(long bytes) { string[] units = ["B","KiB","MiB","GiB","TiB"]; double value=bytes; int i=0; while(value>=1024 && i<units.Length-1){value/=1024;i++;} return $"{value:0.##} {units[i]}"; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
