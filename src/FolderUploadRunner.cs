using System.IO;
using System.Text.Json;
namespace WinOCP;
public sealed class AutomationJob {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string CompletionAction {get;set;}="Keep";
    public string ArchiveDirectory {get;set;}="";
    public bool VerifySha256 {get;set;}=true;
    public string Description {get;set;}="";
    public string Cluster {get;set;}="";
    public string Name {get;set;}="Automatic upload";
    public string Source {get;set;}="";
    public string Pattern {get;set;}="*.csv";
    public string Project {get;set;}="";
    public string Pod {get;set;}="";
    public string Container {get;set;}="";
    public string Destination {get;set;}="/tmp";
    public int Parallel {get;set;}=3;
    public int StableSeconds {get;set;}=10;
    public bool Recursive {get;set;}
}
public sealed class FolderUploadRunner {
    readonly AutomationJob job;
    readonly AutomationClient client;
    readonly Action<string> log;
    readonly Action<TransferItem> add;
    readonly string receiptsPath;
    readonly Dictionary<string,string> receipts;
    readonly Dictionary<string,(string Stamp,DateTime Since)> observed=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Task> active=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> targets=new(StringComparer.OrdinalIgnoreCase);
    public bool Paused {get;set;}
    readonly Dictionary<string,(string Stamp,string Key,TransferItem Row)> queued=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> attempted=new(StringComparer.OrdinalIgnoreCase);
    public FolderUploadRunner(AutomationJob job,AutomationClient client,Action<string> log,Action<TransferItem> add,string receiptsPath){this.job=job;this.client=client;this.log=log;this.add=add;this.receiptsPath=receiptsPath;try{receipts=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(receiptsPath))??new();}catch{receipts=new();}}
    public void Retry(string path){if(File.Exists(path)){var key=Key(path);attempted.Remove(key+"|"+Stamp(new FileInfo(path)));}}
    public static string Stamp(FileInfo file)=>$"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    public static void Validate(AutomationJob job){if(job.CompletionAction is not ("Keep" or "Archive"))throw new IOException("Choose a supported completion action.");if(job.CompletionAction=="Archive"){if(string.IsNullOrWhiteSpace(job.ArchiveDirectory))throw new IOException("Choose an archive directory.");var archive=Path.GetFullPath(job.ArchiveDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;var source=Path.GetFullPath(job.Source).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(archive.StartsWith(source,StringComparison.OrdinalIgnoreCase))throw new IOException("Archive directory must be outside the monitored source folder.");}if(!Directory.Exists(job.Source))throw new IOException("Choose an existing local source directory.");if(string.IsNullOrWhiteSpace(job.Pattern)||job.Pattern.IndexOfAny(['/', '\\', '\0'])>=0)throw new IOException("Enter a filename pattern such as *.csv, without directory separators.");if(job.Parallel<1||job.Parallel>16)throw new IOException("Parallel files must be between 1 and 16.");if(job.StableSeconds<1||job.StableSeconds>300)throw new IOException("Stability wait must be 1–300 seconds.");if(new[]{job.Project,job.Pod,job.Container}.Any(string.IsNullOrWhiteSpace))throw new IOException("Select a namespace, pod, and container.");if(!job.Destination.StartsWith('/')||job.Destination.Contains('\0'))throw new IOException("Enter an absolute pod destination directory.");}
    public async Task Test(CancellationToken token){Validate(job);await client.Query(token,"whoami");if(job.VerifySha256)await client.Query(token,"exec","-n",job.Project,job.Pod,"-c",job.Container,"--","sh","-c","command -v sha256sum >/dev/null || { echo 'SHA-256 verification requires sha256sum in the container' >&2; exit 1; }");await client.Query(token,"exec","-n",job.Project,job.Pod,"-c",job.Container,"--","sh","-c","test -d \"$1\" && test -w \"$1\"","sh",job.Destination);}
    string Key(string path)=>job.Cluster+"|"+job.Project+"|"+job.Pod+"|"+job.Container+"|"+job.Destination+"|"+path;
    public async Task Run(CancellationToken token){using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);token=lifetime.Token;Validate(job);try{await Test(token);log("Watching "+job.Source);while(true){token.ThrowIfCancellationRequested();
        // Recheck connectivity while idle and active, rather than silently queueing into a dead session.
        using(var health=CancellationTokenSource.CreateLinkedTokenSource(token)){health.CancelAfter(TimeSpan.FromSeconds(15));await client.Query(health.Token,"get","pod",job.Pod,"-n",job.Project,"-o","name");}
        foreach(var path in active.Where(x=>x.Value.IsCompleted).Select(x=>x.Key).ToArray())active.Remove(path);
        var files=new DirectoryInfo(job.Source).EnumerateFiles(job.Pattern,new EnumerationOptions{RecurseSubdirectories=job.Recursive,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false,MatchType=MatchType.Win32}).ToArray();var present=files.Select(x=>x.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);foreach(var stale in observed.Keys.Where(x=>!present.Contains(x)).ToArray())observed.Remove(stale);
        foreach(var file in files){if(active.ContainsKey(file.FullName))continue;string stamp;try{stamp=Stamp(file);}catch(IOException){continue;}
            if(!observed.TryGetValue(file.FullName,out var prior)||prior.Stamp!=stamp){observed[file.FullName]=(stamp,DateTime.UtcNow);continue;}
            var key=Key(file.FullName);if(receipts.GetValueOrDefault(key)==stamp||attempted.Contains(key+"|"+stamp)||DateTime.UtcNow-prior.Since<TimeSpan.FromSeconds(job.StableSeconds)||queued.ContainsKey(file.FullName))continue;
            var row=new TransferItem(file.Name,true,file.FullName);row.Prepare(file.Length);add(row);queued[file.FullName]=(stamp,key,row);attempted.Add(key+"|"+stamp);
        }
        foreach(var path in queued.Keys.ToArray()){var waiting=queued[path];if(waiting.Row.Cancellation.IsCancellationRequested||!File.Exists(path)||Stamp(new FileInfo(path))!=waiting.Stamp){waiting.Row.Finish("Cancelled");waiting.Row.Cancellation.Dispose();queued.Remove(path);continue;}if(Paused||active.Count>=job.Parallel||targets.Contains(Path.GetFileName(path)))continue;queued.Remove(path);targets.Add(Path.GetFileName(path));active[path]=Send(path,waiting.Stamp,waiting.Key,waiting.Row,token);}
        await Task.Delay(1000,token);
    }}finally{lifetime.Cancel();foreach(var pending in queued.Values){pending.Row.Finish("Cancelled");pending.Row.Cancellation.Dispose();}queued.Clear();try{await Task.WhenAll(active.Values);}catch{}}}
    void CompleteSource(string path,string stamp){
        if(job.CompletionAction!="Archive")return;
        try{
            if(Stamp(new FileInfo(path))!=stamp)throw new IOException("Source changed after upload; it was retained for the next scan.");
            var relative=Path.GetRelativePath(job.Source,path);var target=Path.Combine(Path.GetFullPath(job.ArchiveDirectory),relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if(File.Exists(target)||Directory.Exists(target))target=Path.Combine(Path.GetDirectoryName(target)!,Path.GetFileNameWithoutExtension(target)+"-"+Guid.NewGuid().ToString("N")+Path.GetExtension(target));
            File.Move(path,target);log("Archived source: "+target);
        }catch(Exception ex){log("Warning: upload verified, but source was not archived: "+ex.Message);}
    }    async Task Send(string path,string stamp,string key,TransferItem row,CancellationToken token){using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,row.Cancellation.Token);try{
        for(int attempt=1;attempt<=3;attempt++){try{linked.Token.ThrowIfCancellationRequested();if(Stamp(new FileInfo(path))!=stamp)throw new IOException("Source changed before transfer; waiting for the next stable version.");var destination=job.Destination;
            if(job.Recursive){var relative=Path.GetDirectoryName(Path.GetRelativePath(job.Source,path));if(!string.IsNullOrEmpty(relative)){destination=job.Destination.TrimEnd('/' )+"/"+relative.Replace(Path.DirectorySeparatorChar,'/');await client.Query(linked.Token,"exec","-n",job.Project,job.Pod,"-c",job.Container,"--","sh","-c","mkdir -p -- \"$1\"","sh",destination);}}
            await client.Upload(path,job.Project,job.Pod,job.Container,destination,row,linked.Token,job.VerifySha256);
            receipts[key]=stamp;File.WriteAllText(receiptsPath,JsonSerializer.Serialize(receipts));CompleteSource(path,stamp);row.Finish("Completed");log("Uploaded and "+(job.VerifySha256?"SHA-256 verified ":"size-verified ")+path);return;
        }catch(OperationCanceledException){throw;}catch(Exception ex){if(attempt==3)throw;log($"Retry {attempt}/3: {Path.GetFileName(path)} · {ex.Message}");await Task.Delay(5000,linked.Token);}}
    }catch(OperationCanceledException){row.Finish("Cancelled");log("Cancelled "+path+"; a partial destination may remain.");}catch(Exception ex){row.Finish("Failed");log(Path.GetFileName(path)+": "+ex.Message);}finally{targets.Remove(Path.GetFileName(path));row.Cancellation.Dispose();}}
}
