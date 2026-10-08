using System.IO;
using System.Text.Json;
namespace WinOCP;
public sealed class AutomationJob {
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
    readonly HashSet<string> attempted=new(StringComparer.OrdinalIgnoreCase);
    public FolderUploadRunner(AutomationJob job,AutomationClient client,Action<string> log,Action<TransferItem> add,string receiptsPath){this.job=job;this.client=client;this.log=log;this.add=add;this.receiptsPath=receiptsPath;try{receipts=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(receiptsPath))??new();}catch{receipts=new();}}
    public static string Stamp(FileInfo file)=>$"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    public static void Validate(AutomationJob job){if(!Directory.Exists(job.Source))throw new IOException("Choose an existing local source directory.");if(string.IsNullOrWhiteSpace(job.Pattern)||job.Pattern.IndexOfAny(['/', '\\', '\0'])>=0)throw new IOException("Enter a filename pattern such as *.csv, without directory separators.");if(job.Parallel<1||job.Parallel>16)throw new IOException("Parallel files must be between 1 and 16.");if(job.StableSeconds<1||job.StableSeconds>300)throw new IOException("Stability wait must be 1–300 seconds.");if(new[]{job.Project,job.Pod,job.Container}.Any(string.IsNullOrWhiteSpace))throw new IOException("Select a namespace, pod, and container.");if(!job.Destination.StartsWith('/')||job.Destination.Contains('\0'))throw new IOException("Enter an absolute pod destination directory.");}
    public async Task Test(CancellationToken token){Validate(job);await client.Query(token,"whoami");await client.Query(token,"exec","-n",job.Project,job.Pod,"-c",job.Container,"--","sh","-c","test -d \"$1\" && test -w \"$1\"","sh",job.Destination);}
    string Key(string path)=>job.Cluster+"|"+job.Project+"|"+job.Pod+"|"+job.Container+"|"+job.Destination+"|"+path;
    public async Task Run(CancellationToken token){using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);token=lifetime.Token;Validate(job);try{await Test(token);log("Watching "+job.Source);while(true){token.ThrowIfCancellationRequested();
        // Recheck connectivity while idle and active, rather than silently queueing into a dead session.
        using(var health=CancellationTokenSource.CreateLinkedTokenSource(token)){health.CancelAfter(TimeSpan.FromSeconds(15));await client.Query(health.Token,"get","pod",job.Pod,"-n",job.Project,"-o","name");}
        foreach(var path in active.Where(x=>x.Value.IsCompleted).Select(x=>x.Key).ToArray())active.Remove(path);
        var files=new DirectoryInfo(job.Source).EnumerateFiles(job.Pattern,new EnumerationOptions{RecurseSubdirectories=job.Recursive,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false,MatchType=MatchType.Win32}).ToArray();var present=files.Select(x=>x.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);foreach(var stale in observed.Keys.Where(x=>!present.Contains(x)).ToArray())observed.Remove(stale);
        foreach(var file in files){if(active.ContainsKey(file.FullName))continue;string stamp;try{stamp=Stamp(file);}catch(IOException){continue;}
            if(!observed.TryGetValue(file.FullName,out var prior)||prior.Stamp!=stamp){observed[file.FullName]=(stamp,DateTime.UtcNow);continue;}
            var key=Key(file.FullName);if(receipts.GetValueOrDefault(key)==stamp||attempted.Contains(key+"|"+stamp)||DateTime.UtcNow-prior.Since<TimeSpan.FromSeconds(job.StableSeconds)||active.Count>=job.Parallel||targets.Contains(file.Name))continue;
            targets.Add(file.Name);attempted.Add(key+"|"+stamp);active[file.FullName]=Send(file.FullName,stamp,key,token);
        }
        await Task.Delay(1000,token);
    }}finally{lifetime.Cancel();try{await Task.WhenAll(active.Values);}catch{}}}
    async Task Send(string path,string stamp,string key,CancellationToken token){var row=new TransferItem(Path.GetFileName(path),true);add(row);using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,row.Cancellation.Token);try{
        for(int attempt=1;attempt<=3;attempt++){try{linked.Token.ThrowIfCancellationRequested();if(Stamp(new FileInfo(path))!=stamp)throw new IOException("Source changed before transfer; waiting for the next stable version.");var destination=job.Destination;
            if(job.Recursive){var relative=Path.GetDirectoryName(Path.GetRelativePath(job.Source,path));if(!string.IsNullOrEmpty(relative)){destination=job.Destination.TrimEnd('/' )+"/"+relative.Replace(Path.DirectorySeparatorChar,'/');await client.Query(linked.Token,"exec","-n",job.Project,job.Pod,"-c",job.Container,"--","sh","-c","mkdir -p -- \"$1\"","sh",destination);}}
            await client.Upload(path,job.Project,job.Pod,job.Container,destination,row,linked.Token);
            receipts[key]=stamp;File.WriteAllText(receiptsPath,JsonSerializer.Serialize(receipts));row.Finish("Completed");log("Uploaded and size-verified "+path);return;
        }catch(OperationCanceledException){throw;}catch(Exception ex){if(attempt==3)throw;log($"Retry {attempt}/3: {Path.GetFileName(path)} · {ex.Message}");await Task.Delay(5000,linked.Token);}}
    }catch(OperationCanceledException){row.Finish("Cancelled");log("Cancelled "+path+"; a partial destination may remain.");}catch(Exception ex){row.Finish("Failed");log(Path.GetFileName(path)+": "+ex.Message);}finally{targets.Remove(Path.GetFileName(path));row.Cancellation.Dispose();}}
}
