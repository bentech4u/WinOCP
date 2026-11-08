using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using WinOCP;

// The same executable stands in for oc when launched with CLI arguments.
if (args.Length>0) {
    var remote=Path.Combine(Environment.CurrentDirectory,"remote");
    var separator=Array.IndexOf(args,"--");var script=separator<0?"":args[Array.IndexOf(args,"-c",separator)+1];
    string Stamp(string path) {var f=new FileInfo(path);return $"{f.Length}:1:{f.LastWriteTimeUtc.Ticks}";}
    if(args.Contains("whoami")) { Console.WriteLine("demo"); return; }
    if(args.Contains("get")) { Console.WriteLine("pod/pod"); return; }
    if(args.Contains("-i")) {
        using var output=File.Create(Path.Combine(remote,Path.GetFileName(args[^1])));
        await Console.OpenStandardInput().CopyToAsync(output);return;
    }
    if(script.StartsWith("test -d") && script.Contains("test -w")) return;
    if(script.Contains("wc -c")) { Console.WriteLine(new FileInfo(Path.Combine(remote,Path.GetFileName(args[^1]))).Length); return; }
    if(script.Contains("--update=none-fail")) {
        if(script.Contains("--help"))return;
        var name=args[^2];
        var archive=Path.Combine(Environment.CurrentDirectory,"archive",name);
        var final=Path.Combine(Environment.CurrentDirectory,"local",name);
        if(!File.Exists(final))throw new Exception("Archiving before the Windows file was published.");
        try { File.Move(Path.Combine(remote,name),archive,overwrite:false); }
        catch(IOException ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
        return;
    }
    if(script.Contains("find ")) {
        foreach(var file in Directory.GetFiles(remote,args[^1])) Console.Write($"/incoming/{Path.GetFileName(file)}\0{Stamp(file)}\0");
    } else if(!script.StartsWith("command -v")) {
        var file=Path.Combine(remote,Path.GetFileName(args[^1]));
        if(script.Contains("sha256sum"))Console.WriteLine(Path.GetFileName(file).EndsWith("bad.csv")?new string('0',64):Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
        else if(script.Contains("stat -c"))Console.WriteLine(Stamp(file));
        else if(script.Contains("cat --")) {
            if(Path.GetFileName(file)=="cancel.csv")await Task.Delay(30000);
            await Console.OpenStandardOutput().WriteAsync(File.ReadAllBytes(file));
        } else throw new Exception("Unexpected CLI call.");
    }
    return;
}

var root=Path.Combine(AppContext.BaseDirectory,"WinOCP-download-test-"+Guid.NewGuid().ToString("N"));
var remoteDir=Directory.CreateDirectory(Path.Combine(root,"remote")).FullName;
var localDir=Directory.CreateDirectory(Path.Combine(root,"local")).FullName;
try {
    var expected=new Dictionary<string,string>{{"good.csv","verified"},{"existing.csv","remote content"},{"bad.csv","invalid hash"},{"cancel.csv","never publish"}};
    foreach(var (file,content) in expected)File.WriteAllText(Path.Combine(remoteDir,file),content);
    File.WriteAllText(Path.Combine(localDir,"existing.csv"),"keep me");
    var job=new AutomationJob{Download=true,Source=localDir,Destination="/incoming",Project="test",Pod="pod",Container="app",Parallel=3,StableSeconds=1};
    var client=new AutomationClient(Environment.ProcessPath!,"unused",root,false);
    var receipts=Path.Combine(root,"receipts.json");
    var rows=new System.Collections.Concurrent.ConcurrentDictionary<string,TransferItem>();
    using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var runner=new FolderDownloadRunner(job,client,Console.WriteLine,row=>rows[row.Name]=row,receipts);
    var running=runner.Run(stop.Token);
    while (!rows.TryGetValue("cancel.csv",out var cancel) || cancel.State!="Transferring")await Task.Delay(50,stop.Token);
    rows["cancel.csv"].Cancellation.Cancel();
    while(rows.Count<4||rows.Values.Any(x=>x.CanCancel))await Task.Delay(50,stop.Token);
    stop.Cancel();
    try{await running;}catch(OperationCanceledException){}
    void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    Check(rows["good.csv"].State=="Completed"&&File.ReadAllText(Path.Combine(localDir,"good.csv"))=="verified","Verified download did not arrive.");
    Check(rows["existing.csv"].State=="Skipped"&&File.ReadAllText(Path.Combine(localDir,"existing.csv"))=="keep me","Existing destination was modified.");
    Check(rows["bad.csv"].State=="Failed"&&!File.Exists(Path.Combine(localDir,"bad.csv")),"Hash mismatch published a final file.");
    Check(rows["cancel.csv"].State=="Cancelled"&&!File.Exists(Path.Combine(localDir,"cancel.csv")),"Cancellation published a final file.");
    Check(!Directory.GetFiles(localDir,"*.part").Any(),"Temporary files were not cleaned up.");
    Check(expected.All(x=>File.ReadAllText(Path.Combine(remoteDir,x.Key))==x.Value),"Remote files were modified.");
    File.Delete(Path.Combine(localDir,"good.csv"));
    job.Pattern="good.csv";
    using var restart=new CancellationTokenSource(TimeSpan.FromSeconds(4));
    int redispatched=0;
    var restarted=new FolderDownloadRunner(job,client,_=>{},_=>Interlocked.Increment(ref redispatched),receipts);
    try{await restarted.Run(restart.Token);}catch(OperationCanceledException){}
    Check(redispatched==0&&!File.Exists(Path.Combine(localDir,"good.csv")),"Restart ignored the completion receipt.");
    File.WriteAllText(Path.Combine(localDir,"legacy.csv"),"legacy upload");
    var legacyPath=Path.Combine(root,"legacy.json");
    File.WriteAllText(legacyPath,System.Text.Json.JsonSerializer.Serialize(new { Source=localDir, Destination="/incoming", Project="test", Pod="pod", Container="app", Pattern="legacy.csv", StableSeconds=1 }));
    var legacy=AutomationStore.Load(legacyPath).Single();
    Check(!legacy.Download,"A legacy configuration became a download.");
    using var uploadStop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    TransferItem? uploadRow=null;
    var upload=new FolderUploadRunner(legacy,client,Console.WriteLine,row=>Volatile.Write(ref uploadRow,row),Path.Combine(root,"upload-receipts.json"));
    var uploading=upload.Run(uploadStop.Token);
    while(Volatile.Read(ref uploadRow)?.State!="Completed")await Task.Delay(100,uploadStop.Token);
    uploadStop.Cancel();
    try{await uploading;}catch(OperationCanceledException){}
    Check(File.ReadAllText(Path.Combine(remoteDir,"legacy.csv"))=="legacy upload","Legacy upload did not arrive.");
    Console.WriteLine("Legacy upload regression passed.");
    var archiveDir=Directory.CreateDirectory(Path.Combine(root,"archive")).FullName;
    File.WriteAllText(Path.Combine(remoteDir,"archive-new.csv"),"move after verification");
    File.WriteAllText(Path.Combine(remoteDir,"archive-clash.csv"),"retain remote on collision");
    File.WriteAllText(Path.Combine(remoteDir,"archive-skip.csv"),"retain remote on skip");
    File.WriteAllText(Path.Combine(archiveDir,"archive-clash.csv"),"existing archive");
    File.WriteAllText(Path.Combine(localDir,"archive-skip.csv"),"existing Windows copy");
    File.WriteAllText(Path.Combine(remoteDir,"archive-bad.csv"),"invalid hash");
    job.Pattern="archive-*.csv";
    job.CompletionAction="RemoteArchive";
    job.ArchiveDirectory="/archive";
    job.VerifySha256=false; // Archive policy must still hash.
    rows.Clear();
    using var archiveStop=new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var archiver=new FolderDownloadRunner(job,client,Console.WriteLine,row=>rows[row.Name]=row,Path.Combine(root,"archive-receipts.json"));
    var archiving=archiver.Run(archiveStop.Token);
    while(rows.Count<4||rows.Values.Any(x=>x.CanCancel))await Task.Delay(50,archiveStop.Token);
    archiveStop.Cancel();
    try{await archiving;}catch(OperationCanceledException){}
    Check(!File.Exists(Path.Combine(remoteDir,"archive-new.csv"))&&File.ReadAllText(Path.Combine(archiveDir,"archive-new.csv"))=="move after verification","Verified remote source was not archived.");
    Check(File.ReadAllText(Path.Combine(archiveDir,"archive-clash.csv"))=="existing archive"&&File.ReadAllText(Path.Combine(remoteDir,"archive-clash.csv"))=="retain remote on collision","Archive collision changed the source or destination.");
    Check(File.ReadAllText(Path.Combine(remoteDir,"archive-skip.csv"))=="retain remote on skip"&&!File.Exists(Path.Combine(archiveDir,"archive-skip.csv")),"Skipped download archived its source.");
    Check(rows["archive-bad.csv"].State=="Failed"&&File.Exists(Path.Combine(remoteDir,"archive-bad.csv"))&&!File.Exists(Path.Combine(archiveDir,"archive-bad.csv")),"Archive moved an unverified source.");
    Console.WriteLine("Remote archive checks passed: successful move, no overwrite, skipped source retained.");
    Console.WriteLine("Download checks passed: verified copy, skip, bad hash, cancel, temporary cleanup, remote retention, restart deduplication.");
} finally { if(Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))Directory.Delete(root,true); }
