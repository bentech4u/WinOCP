using System.IO;
using System.Text.Json;

namespace WinOCP;

public interface IAutomationRunner {
    bool Paused { get; set; }
    void Retry(string path);
    Task Test(CancellationToken token);
    Task Run(CancellationToken token);
}

public sealed class FolderDownloadRunner(AutomationJob job, AutomationClient client, Action<string> log, Action<TransferItem> add, string receiptsPath) : IAutomationRunner {
    readonly record struct RemoteFile(string Path, long Size, string Stamp);
    readonly Dictionary<string,(string Stamp, DateTime Since)> observed = new(StringComparer.Ordinal);
    readonly Dictionary<string,string> attempted = new(StringComparer.Ordinal);
    readonly Dictionary<string,Task> active = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> receipts = ReadReceipts(receiptsPath);
    public bool Paused { get; set; }

    static Dictionary<string,string> ReadReceipts(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path)) ?? new() : new();

    public static void Validate(AutomationJob job) {
        if (!Directory.Exists(job.Source)) throw new IOException("Choose an existing Windows destination directory.");
        if (!job.Destination.StartsWith('/') || job.Destination.Contains('\0')) throw new IOException("Enter an absolute pod source directory.");
        if (new[]{job.Project,job.Pod,job.Container}.Any(string.IsNullOrWhiteSpace)) throw new IOException("Select a namespace, pod, and container.");
        if (string.IsNullOrWhiteSpace(job.Pattern) || job.Pattern.IndexOfAny(['/', '\\', '\0']) >= 0) throw new IOException("Enter a filename pattern without directory separators.");
        if (job.Parallel is < 1 or > 16 || job.StableSeconds is < 1 or > 300) throw new IOException("Choose a valid stability wait and parallel file count.");
        if (job.Recursive || job.CompletionAction != "Keep") throw new IOException("Download jobs keep remote files and scan only the selected directory.");
    }

    Task<string> Remote(CancellationToken token, string script, params string[] arguments) =>
        client.Query(token, ["exec","-n",job.Project,job.Pod,"-c",job.Container,"--","sh","-c",script,"sh",..arguments]);

    async Task<RemoteFile[]> Scan(CancellationToken token) {
        var result = await Remote(token, """
            test -d "$1" && test -r "$1" && test -x "$1" || { echo 'Remote source is not readable' >&2; exit 1; }
            command -v stat >/dev/null || exit 1
            find "$1" -mindepth 1 -maxdepth 1 -type f -name "$2" -exec sh -c '
              for path do
                stamp=$(stat -c "%s:%i:%y:%z" -- "$path") || continue
                printf "%s\000%s\000" "$path" "$stamp"
              done
            ' sh {} +
            """, job.Destination, job.Pattern);
        var fields = result.Split('\0');
        var files = new List<RemoteFile>();
        for (int i=0; i+1<fields.Length; i+=2) {
            var stamp=fields[i+1];
            if (long.TryParse(stamp.Split(':',2)[0], out var size)) files.Add(new(fields[i],size,stamp));
        }
        return files.ToArray();
    }

    public async Task Test(CancellationToken token) {
        Validate(job);
        await Scan(token);
        await Remote(token, job.VerifySha256 ? "command -v cat >/dev/null && command -v sha256sum >/dev/null" : "command -v cat >/dev/null");
        using var probe = new FileStream(TemporaryPath(), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
    }

    string TemporaryPath() => Path.Combine(job.Source, ".winocp-" + Guid.NewGuid().ToString("N") + ".part");
    string Key(string path) => JsonSerializer.Serialize(new[]{job.Cluster,job.Project,job.Pod,job.Container,job.Source,path});
    public void Retry(string path) => attempted.Remove(path);

    public async Task Run(CancellationToken token) {
        await Test(token);
        log("Watching remote directory " + job.Destination);
        try {
            while (true) {
                token.ThrowIfCancellationRequested();
                foreach (var key in active.Where(x=>x.Value.IsCompleted).Select(x=>x.Key).ToArray()) active.Remove(key);
                RemoteFile[] files;
                try { files = await Scan(token); }
                catch (Exception ex) when (!token.IsCancellationRequested) {
                    observed.Clear();
                    log("Warning: remote scan failed; retrying: " + ex.Message);
                    await Task.Delay(5000,token);
                    continue;
                }
                var present = files.Select(x=>x.Path).ToHashSet(StringComparer.Ordinal);
                foreach (var path in observed.Keys.Where(x=>!present.Contains(x)).ToArray()) observed.Remove(path);
                foreach (var file in files) {
                    var now=DateTime.UtcNow;
                    if (!observed.TryGetValue(file.Path,out var previous) || previous.Stamp!=file.Stamp) { observed[file.Path]=(file.Stamp,now); continue; }
                    if (Paused || active.Count>=job.Parallel || now-previous.Since<TimeSpan.FromSeconds(job.StableSeconds) ||
                        active.ContainsKey(file.Path) || attempted.GetValueOrDefault(file.Path)==file.Stamp || receipts.GetValueOrDefault(Key(file.Path))==file.Stamp) continue;
                    attempted[file.Path]=file.Stamp;
                    active[file.Path]=Send(file,token);
                }
                await Task.Delay(1000,token);
            }
        } finally { await Task.WhenAll(active.Values); }
    }

    async Task Send(RemoteFile file, CancellationToken token) {
        var name=file.Path[(file.Path.LastIndexOf('/')+1)..];
        var row=new TransferItem(name,false,file.Path);
        row.Prepare(file.Size);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,row.Cancellation.Token);
        token=linked.Token;
        var temporary=TemporaryPath();
        var ownsTemporary=false;
        try {
            add(row);
            if (name.Length==0 || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.EndsWith('.') || name.EndsWith(' ') ||
                System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0].TrimEnd(),@"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new IOException("Filename is not valid on Windows.");
            var target=Path.Combine(job.Source,name);
            if (File.Exists(target) || Directory.Exists(target)) { row.Finish("Skipped"); return; }
            string hash;
            await using (var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)) {
                ownsTemporary=true;
                row.Start(file.Size);
                hash=await client.Download(job,file.Path,output,row,token);
            }
            if (row.Bytes!=file.Size) throw new IOException("Downloaded size does not match the remote file.");
            if (job.VerifySha256) {
                var remoteHash=(await Remote(token,"sha256sum < \"$1\"",file.Path)).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.Equals(hash,remoteHash,StringComparison.OrdinalIgnoreCase)) throw new IOException("Downloaded SHA-256 does not match the remote file.");
            }
            var stamp=(await Remote(token,"test -f \"$1\" && test ! -L \"$1\" && stat -c '%s:%i:%y:%z' -- \"$1\"",file.Path)).TrimEnd('\r','\n');
            if (stamp!=file.Stamp) throw new IOException("Remote file changed during download.");
            token.ThrowIfCancellationRequested();
            try { File.Move(temporary,target,overwrite:false); }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { row.Finish("Skipped"); return; }
            ownsTemporary=false;
            row.Finish("Completed");
            receipts[Key(file.Path)]=file.Stamp;
            try {
                var receiptTemp=receiptsPath+".tmp";
                File.WriteAllText(receiptTemp,JsonSerializer.Serialize(receipts));
                File.Move(receiptTemp,receiptsPath,overwrite:true);
            } catch (Exception ex) { log("Warning: download completed but receipt could not be saved: "+ex.Message); }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { row.Finish("Cancelled"); }
        catch (Exception ex) { row.Finish("Failed"); log("Failed: "+file.Path+" · "+ex.Message); }
        finally {
            if (ownsTemporary) try { File.Delete(temporary); } catch (IOException ex) { log("Warning: could not remove temporary download: "+ex.Message); } catch (UnauthorizedAccessException ex) { log("Warning: could not remove temporary download: "+ex.Message); }
            row.Cancellation.Dispose();
        }
    }
}
