using System.Diagnostics;
using System.IO;
namespace WinOCP;
public sealed class AutomationClient(string cli,string config,string home,bool skipTls) {
    Process Start(IEnumerable<string> args,bool input=false) {
        var psi=new ProcessStartInfo(cli){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=home};
        psi.Environment["KUBECONFIG"]=config;psi.Environment["HOME"]=home;
        psi.ArgumentList.Add("--insecure-skip-tls-verify="+(skipTls?"true":"false"));foreach(var arg in args)psi.ArgumentList.Add(arg);
        return Process.Start(psi)??throw new IOException("Could not start oc.");
    }
    public async Task<string> Query(CancellationToken token,params string[] args){
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));token=timeout.Token;
        using var p=Start(args);p.StandardInput.Close();using var cancel=token.Register(()=>{try{p.Kill(true);}catch{}});
        var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync(token);var text=await output;var error=await errors;
        token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException(error.Trim());return text;
    }
    public async Task Upload(string source,string project,string pod,string container,string directory,TransferItem row,CancellationToken token){
        // Hold the source exclusively for the full upload so a producer cannot modify it mid-stream.
        await using var file=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None,131072,true);
        row.Start(file.Length);var target=directory.TrimEnd('/')+"/"+Path.GetFileName(source);
        using var p=Start(new[]{"exec","-i","-n",project,pod,"-c",container,"--","sh","-c","cat > \"$1\"","sh",target});
        using var cancel=token.Register(()=>{try{p.Kill(true);}catch{}});var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
        try{var buffer=new byte[131072];long bytes=0;var clock=Stopwatch.StartNew();int count;while((count=await file.ReadAsync(buffer,token))>0){await p.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0,count),token);bytes+=count;if(clock.ElapsedMilliseconds>=100){row.Update(bytes);clock.Restart();}}
            await p.StandardInput.BaseStream.FlushAsync(token);p.StandardInput.Close();await p.WaitForExitAsync(token);var error=await errors;await output;token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException(error.Trim());row.Update(bytes);
            var size=await Query(token,"exec","-n",project,pod,"-c",container,"--","sh","-c","wc -c < \"$1\"","sh",target);
            if(!long.TryParse(size.Trim(),out var length)||length!=bytes)throw new IOException("Destination size verification failed.");
        }catch{try{p.Kill(true);}catch{}token.ThrowIfCancellationRequested();throw;}
    }
}
