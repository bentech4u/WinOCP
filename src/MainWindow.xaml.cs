using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
namespace WinOCP;
public record Entry(string Name, string FullPath, bool Directory, long? Size = null) {
    public string Kind => Directory ? "Folder" : "File";
    public string Icon => Directory ? "\uE8B7" : "\uE8A5";
    public string IconColor => Directory ? "#EAB134" : "#7D96B0";
}
public partial class MainWindow : Window
{
    readonly string session = Path.Combine(Path.GetTempPath(), "WinOCP-" + Guid.NewGuid().ToString("N"));
    string? source, config;
    bool connected, busy, loading, skipTls;
    CancellationTokenSource? operation;
    string themePreference = "System";
    readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    Point? dragStart;
    Entry? pressedEntry;
    bool preserveSelection;
    bool updatingLocations;
    const string DragFormat = "WinOCP.InternalFiles";
    readonly Guid windowId = Guid.NewGuid();
    record FileDrag(Guid WindowId, bool Upload, Entry[] Items, string? Project, string? Pod, string? Container);
    string Project => Projects.SelectedItem as string ?? throw new Exception("Choose a project.");
    string Pod => Pods.SelectedItem as string ?? throw new Exception("Choose a pod.");
    string Container => Containers.SelectedItem as string ?? throw new Exception("Choose a container.");
    public MainWindow() {
        InitializeComponent();
        try { if (File.Exists(settingsPath)) { using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath)); themePreference = settings.RootElement.GetProperty("theme").GetString() ?? "System"; } } catch { }
        if (themePreference is not ("Light" or "Dark" or "System")) themePreference = "System";
        ThemeChoice.SelectedIndex = themePreference == "Light" ? 1 : themePreference == "Dark" ? 2 : 0;
        ThemeManager.Apply(themePreference);
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        LocalPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); LoadLocal(); RefreshLocations();
        Closed += (_, _) => { SystemEvents.UserPreferenceChanged -= SystemThemeChanged; operation?.Cancel(); Cleanup(); };
        Loaded += (_, _) => { if (ShouldShowManager()) OpenManager(); };
    }
    void ThemeChanged(object s, SelectionChangedEventArgs e) {
        if (ThemeChoice == null || History == null) return;
        themePreference = ThemeChoice.SelectedIndex == 1 ? "Light" : ThemeChoice.SelectedIndex == 2 ? "Dark" : "System";
        ThemeManager.Apply(themePreference);
        try { File.WriteAllText(settingsPath, JsonSerializer.Serialize(new { theme = themePreference })); } catch { Log("Theme applied for this session; settings folder is not writable."); }
    }
    void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) {
        if (themePreference == "System") Dispatcher.BeginInvoke(() => ThemeManager.Apply(themePreference));
    }
    void Cleanup() { try { if (System.IO.Directory.Exists(session)) System.IO.Directory.Delete(session, true); } catch { } }
    void Log(string text) { Status.Text = text; History.AppendText($"{DateTime.Now:HH:mm:ss}  {text}\n"); History.ScrollToEnd(); }
    void ShowFiles(object s, RoutedEventArgs e) => LocalFiles.Focus();
    bool ShouldShowManager() { try { return ConnectionStore.Load().ShowAtStartup; } catch { return true; } }
    void OpenManager() { if (busy) { Log("Cancel the current operation before opening connections."); return; } new ConnectionManager(this) { Owner = this }.ShowDialog(); }
    void ShowConnection(object s, RoutedEventArgs e) => OpenManager();
    public string LastStatus => Status.Text;
    public void CancelLogin() => operation?.Cancel();
    public async Task<bool> LoginFromManager(ConnectionProfile profile, string secret) {
        if (busy) return false;
        Auth.SelectedIndex = profile.AuthMethod;
        Server.Text = profile.Server; User.Text = profile.Username; Secret.Password = secret;
        source = string.IsNullOrWhiteSpace(profile.KubeconfigPath) ? null : Path.GetFullPath(profile.KubeconfigPath, AppContext.BaseDirectory); SkipTls.IsChecked = profile.SkipTls;
        bool success = false;
        await Run(async () => { await LoginCore(); success = true; Title = "WinOCP — " + profile.Name; });
        if (success && !string.IsNullOrEmpty(profile.DefaultProject)) {
            if (Projects.Items.Contains(profile.DefaultProject)) Projects.SelectedItem = profile.DefaultProject;
            else Log("Connected. Default project is unavailable or hidden as a system project; choose a user project.");
        }
        return success;
    }
    void ShowHistory(object s, RoutedEventArgs e) => History.Focus();
    void ShowAbout(object s, RoutedEventArgs e) => MessageBox.Show("WinOCP 0.10 — Portable Edition\nOpenShift file transfer for Windows.\n\nBuilt with WPF and the OpenShift CLI.", "About WinOCP");
    void UpdateConnection(string? identity = null) {
        ConnectionTitle.Text = connected ? "Connected to OpenShift" : "Not connected";
        ConnectionDetail.Text = connected ? identity ?? "Authenticated" : "Choose a login method to begin";
        ConnectionDot.Fill = new System.Windows.Media.SolidColorBrush(connected ? System.Windows.Media.Color.FromRgb(25, 150, 74) : System.Windows.Media.Color.FromRgb(148, 163, 184));
    }
    void AuthChanged(object s, SelectionChangedEventArgs e) {
        if (SecretLabel == null || ConfigButton == null) return;
        Secret.Clear();
        UsernameField.Visibility = Auth.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ServerField.Visibility = SecretField.Visibility = Auth.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        ConfigButton.Visibility = ConfigLabel.Visibility = Auth.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        SecretLabel.Content = Auth.SelectedIndex == 1 ? "Password" : "Token";
        Secret.ToolTip = Auth.SelectedIndex == 1 ? "OpenShift password" : "OpenShift token";
    }
    void Lock(bool value) { foreach (var control in new Control[] { Auth, Server, User, Secret, SkipTls, ConfigButton, Projects, Pods, Containers, LocalLocations, LocalPath, RemotePath, LocalFiles, RemoteFiles }) control.IsEnabled = !value; }
    async Task Run(Func<Task> action) { if (busy) return; busy = true; Lock(true); operation = new(); try { await action(); } catch (OperationCanceledException) { Log("Cancelled. A partial destination may remain."); } catch (Exception ex) { Log(ex.Message); } finally { busy = false; Lock(false); operation.Dispose(); operation = null; } }
    Task<string> Oc(params string[] args) => OcAt(session, args);
    async Task<string> OcAt(string workingDirectory, params string[] args) {
        var exe = Path.Combine(AppContext.BaseDirectory, "tools", "oc.exe");
        if (!File.Exists(exe)) throw new Exception("Missing tools/oc.exe. Build the portable package first.");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        psi.Environment["KUBECONFIG"] = config!; psi.Environment["HOME"] = session;
        psi.ArgumentList.Add("--insecure-skip-tls-verify=" + (skipTls ? "true" : "false"));
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new Exception("Could not start oc.");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var registration = operation!.Token.Register(() => { try { p.Kill(true); } catch { } });
        await p.WaitForExitAsync(operation.Token); var result = await stdout; var error = await stderr;
        if (p.ExitCode != 0) throw new Exception(Secret.Password.Length > 0 ? error.Replace(Secret.Password, "[redacted]").Trim() : error.Trim());
        return result;
    }
    void ChooseConfig(object s, RoutedEventArgs e) { if (busy) return; var d = new OpenFileDialog { Title = "Choose kubeconfig", Filter = "All files|*.*" }; if (d.ShowDialog() == true) { source = d.FileName; ConfigLabel.Text = source; Auth.SelectedIndex = 2; } }
    void ClearRemote() { loading = true; Projects.ItemsSource = null; Pods.ItemsSource = null; Containers.ItemsSource = null; RemoteFiles.ItemsSource = null; loading = false; }
    async void Connect(object s, RoutedEventArgs e) => await Run(LoginCore);
    async Task LoginCore() {
        skipTls = SkipTls.IsChecked == true;
        connected = false; UpdateConnection(); ClearRemote(); System.IO.Directory.CreateDirectory(session); config = Path.Combine(session, "config"); if (File.Exists(config)) File.Delete(config);
        if (Auth.SelectedIndex == 2) {
            if (source == null) throw new Exception("Choose a kubeconfig first.");
            config = source;
            using var doc = JsonDocument.Parse(await Oc("config", "view", "--raw", "--flatten", "-o", "json"));
            if (doc.RootElement.GetProperty("users").EnumerateArray().Any(u => u.GetProperty("user").TryGetProperty("exec", out _) || u.GetProperty("user").TryGetProperty("auth-provider", out _))) throw new Exception("External kubeconfig credential plugins are not bundled. Use a token or self-contained kubeconfig.");
            config = Path.Combine(session, "config"); await File.WriteAllTextAsync(config, doc.RootElement.GetRawText());
        } else {
            if (!Uri.TryCreate(Server.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new Exception("Enter an HTTPS API URL.");
            if (string.IsNullOrWhiteSpace(Secret.Password)) throw new Exception("Enter a token or password.");
            var args = new List<string> { "login", "--server=" + uri.AbsoluteUri };
            if (Auth.SelectedIndex == 0) args.Add("--token=" + Secret.Password);
            else { if (string.IsNullOrWhiteSpace(User.Text)) throw new Exception("Enter a username."); args.Add("--username=" + User.Text); args.Add("--password=" + Secret.Password); }
            await Oc(args.ToArray());
        }
        var identity = (await Oc("whoami")).Trim(); Secret.Clear(); connected = true; UpdateConnection(identity); await LoadProjects(); Log("Connected as " + identity + (skipTls ? " · TLS certificate verification skipped" : " · TLS certificate verification enabled") + (Projects.Items.Count == 0 ? " · No accessible user projects found" : ""));
    }
    void Disconnect(object s, RoutedEventArgs e) { if (busy) return; connected = false; UpdateConnection(); ClearRemote(); Cleanup(); Secret.Clear(); Title = "WinOCP — OpenShift File Transfer"; Log("Disconnected. Temporary credentials removed."); if (ShouldShowManager()) OpenManager(); }
    void RequireConnection() { if (!connected) throw new Exception("Connect first."); }
    async Task LoadProjects() {
        var projects = ProjectCatalog.UserProjects(await Oc("get", "projects", "-o", "json"));
        ClearRemote(); Projects.ItemsSource = projects;
        Log(projects.Length == 0 ? "No accessible user projects found. System projects are hidden." : $"Loaded {projects.Length} accessible user project(s). System projects are hidden.");
    }
    async void RefreshCluster(object s, RoutedEventArgs e) => await Run(async () => { RequireConnection(); await LoadProjects(); });
    async void ProjectChanged(object s, SelectionChangedEventArgs e) { if (loading || Projects.SelectedItem == null || busy) return; await Run(async () => { loading = true; try { Pods.ItemsSource = null; Containers.ItemsSource = null; RemoteFiles.ItemsSource = null; using var d = JsonDocument.Parse(await Oc("get", "pods", "-n", Project, "-o", "json")); Pods.ItemsSource = d.RootElement.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("status").GetProperty("phase").GetString() == "Running").Select(x => x.GetProperty("metadata").GetProperty("name").GetString()!).Order().ToArray(); } finally { loading = false; } }); }
    async void PodChanged(object s, SelectionChangedEventArgs e) { if (loading || Pods.SelectedItem == null || busy) return; await Run(async () => { loading = true; try { RemoteFiles.ItemsSource = null; using var d = JsonDocument.Parse(await Oc("get", "pod", Pod, "-n", Project, "-o", "json")); Containers.ItemsSource = d.RootElement.GetProperty("spec").GetProperty("containers").EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToArray(); Containers.SelectedIndex = 0; RemotePath.Text = "/"; } finally { loading = false; } await LoadRemote(); }); }
    async void ContainerChanged(object s, SelectionChangedEventArgs e) { if (loading || Containers.SelectedItem == null || busy) return; await Run(LoadRemote); }
    bool LoadLocal() { try { var path = Path.GetFullPath(LocalPath.Text); LocalFiles.ItemsSource = new DirectoryInfo(path).EnumerateFileSystemInfos().Select(x => new Entry(x.Name, x.FullName, (x.Attributes & FileAttributes.Directory) != 0, x is FileInfo f ? f.Length : null)).OrderByDescending(x => x.Directory).ThenBy(x => x.Name).ToArray(); LocalPath.Text = path; return true; } catch (Exception ex) { Log(ex.Message); return false; } }
    void RefreshLocations() {
        if (busy) return;
        updatingLocations = true;
        try { var selected = (LocalLocations.SelectedItem as LocalLocation)?.Path; var items = LocalLocation.GetLocations(); LocalLocations.ItemsSource = items; LocalLocations.SelectedItem = items.FirstOrDefault(x => x.Path == selected) ?? items[0]; }
        catch (Exception ex) { Log("Could not refresh local locations: " + ex.Message); }
        finally { updatingLocations = false; }
    }
    void LocationsOpened(object s, EventArgs e) => RefreshLocations();
    void LocationSelected(object s, SelectionChangedEventArgs e) {
        if (busy || updatingLocations || LocalLocations.SelectedItem is not LocalLocation location || string.IsNullOrEmpty(location.Path)) return;
        var previous = LocalPath.Text; LocalPath.Text = location.Path;
        if (!LoadLocal()) { LocalPath.Text = previous; updatingLocations = true; LocalLocations.SelectedIndex = 0; updatingLocations = false; }
    }
    async Task LoadRemote() {
        RequireConnection(); var path = RemotePath.Text.Trim(); if (!path.StartsWith('/') || path.Contains('\0')) throw new Exception("Enter an absolute container path.");
        var result = await Oc("exec", "-n", Project, Pod, "-c", Container, "--", "sh", "-c", "cd -- \"$1\" && find . -mindepth 1 -maxdepth 1 -exec sh -c 'for p do if [ -d \"$p\" ]; then printf \"d\\000%s\\000\" \"$p\"; else printf \"f\\000%s\\000\" \"$p\"; fi; done' sh {} +", "sh", path);
        var parts = result.Split('\0'); var entries = new List<Entry>();
        for (int i = 0; i + 1 < parts.Length; i += 2) { var name = parts[i + 1]; if (name.StartsWith("./")) name = name[2..]; entries.Add(new(name, path.TrimEnd('/') + "/" + name, parts[i] == "d")); }
        RemoteFiles.ItemsSource = entries.OrderByDescending(x => x.Directory).ThenBy(x => x.Name).ToArray(); Log("Container directory loaded.");
    }
    void LocalGo(object s, RoutedEventArgs e) { if (!busy) LoadLocal(); }
    void LocalUp(object s, RoutedEventArgs e) { if (busy) return; LocalPath.Text = System.IO.Directory.GetParent(LocalPath.Text)?.FullName ?? LocalPath.Text; LoadLocal(); }
    void LocalOpen(object s, MouseButtonEventArgs e) { if (!busy && LocalFiles.SelectedItem is Entry { Directory: true } x) { LocalPath.Text = x.FullPath; LoadLocal(); } }
    async void RemoteGo(object s, RoutedEventArgs e) => await Run(LoadRemote);
    async void RemoteUp(object s, RoutedEventArgs e) => await Run(async () => { var p = RemotePath.Text.TrimEnd('/'); RemotePath.Text = p.Contains('/') ? p[..(p.LastIndexOf('/') + 1)] : "/"; await LoadRemote(); });
    async void RemoteOpen(object s, MouseButtonEventArgs e) { if (RemoteFiles.SelectedItem is Entry { Directory: true } x) await Run(async () => { RemotePath.Text = x.FullPath; await LoadRemote(); }); }
    void LocalKey(object s, KeyEventArgs e) { if (!busy && e.Key == Key.Enter) LoadLocal(); }
    async void RemoteKey(object s, KeyEventArgs e) { if (e.Key == Key.Enter) await Run(LoadRemote); }
    async void Upload(object s, RoutedEventArgs e) => await Transfer(true);
    async void Download(object s, RoutedEventArgs e) => await Transfer(false);
    async Task Transfer(bool upload, Entry[]? droppedItems = null) {
        if (busy) return;
        if (!connected || Projects.SelectedItem == null || Pods.SelectedItem == null || Containers.SelectedItem == null) { Log("Connect and select a project, pod, and container first."); return; }
        var items = droppedItems ?? (upload ? LocalFiles : RemoteFiles).SelectedItems.Cast<Entry>().ToArray();
        if (items.Length == 0) { Log("Select files or folders first."); return; }
        var destination = upload ? RemotePath.Text : LocalPath.Text;
        if (MessageBox.Show($"{(upload ? "Upload" : "Download")} {items.Length} item(s) to:\n{destination}\n\nExisting files may be overwritten.", "Confirm transfer", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await Run(async () => { RequireConnection(); var project = Project; var pod = Pod; var container = Container; var local = Path.GetFullPath(LocalPath.Text); var remote = RemotePath.Text.TrimEnd('/') + "/";
            if (!remote.StartsWith('/') || remote.Contains('\0')) throw new Exception("Enter an absolute container directory.");
            foreach (var item in items) {
                Log("Transferring " + item.Name + " …");
                if (upload) await OcAt(Path.GetDirectoryName(item.FullPath)!, "cp", "./" + item.Name, pod + ":" + remote, "-n", project, "-c", container);
                else { if (item.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || item.Name is "." or ".." || item.Name.EndsWith('.') || item.Name.EndsWith(' ')) throw new Exception("Filename is not valid on Windows: " + item.Name); await OcAt(local, "cp", pod + ":" + item.FullPath, "./" + item.Name, "-n", project, "-c", container); }
            }
            LoadLocal(); await LoadRemote(); Log("Transfer completed.");
        });
    }
    void FileDragStart(object s, MouseButtonEventArgs e) {
        dragStart = null; pressedEntry = null; preserveSelection = false;
        if (busy || !connected || s is not ListView list) return;
        var row = ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) as ListViewItem;
        if (row?.Content is not Entry entry) return;
        dragStart = e.GetPosition(list); pressedEntry = entry;
        preserveSelection = row.IsSelected && list.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None;
        if (preserveSelection) e.Handled = true;
    }
    void FileDragEnd(object s, MouseButtonEventArgs e) {
        if (preserveSelection && pressedEntry != null && s is ListView list) list.SelectedItem = pressedEntry;
        dragStart = null; pressedEntry = null; preserveSelection = false;
    }
    void FileDragMove(object s, MouseEventArgs e) {
        if (busy || dragStart == null || e.LeftButton != MouseButtonState.Pressed || s is not ListView list) return;
        var point = e.GetPosition(list);
        if (Math.Abs(point.X - dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragStart = null; preserveSelection = false;
        var items = list.SelectedItems.Cast<Entry>().ToArray();
        if (items.Length == 0 || Projects.SelectedItem == null || Pods.SelectedItem == null || Containers.SelectedItem == null) return;
        var payload = new FileDrag(windowId, list == LocalFiles, items, Project, Pod, Container);
        var data = new DataObject(); data.SetData(DragFormat, payload);
        DragDrop.DoDragDrop(list, data, DragDropEffects.Copy);
    }
    bool AcceptDrop(ListView target, IDataObject data) {
        if (busy || !connected || Projects.SelectedItem == null || Pods.SelectedItem == null || Containers.SelectedItem == null) return false;
        if (data.GetDataPresent(DragFormat)) {
            return data.GetData(DragFormat) is FileDrag payload && payload.WindowId == windowId && payload.Upload == (target == RemoteFiles)
                && payload.Project == Project && payload.Pod == Pod && payload.Container == Container;
        }
        return target == RemoteFiles && data.GetDataPresent(DataFormats.FileDrop);
    }
    void FilesDragOver(object s, DragEventArgs e) {
        e.Effects = s is ListView list && AcceptDrop(list, e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    async void FilesDrop(object s, DragEventArgs e) {
        e.Handled = true;
        if (s is not ListView list || !AcceptDrop(list, e.Data)) return;
        try {
            Entry[] items;
            if (e.Data.GetData(DragFormat) is FileDrag payload) items = payload.Items;
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files) items = files.Select(path => {
                var full = Path.GetFullPath(path);
                if (string.IsNullOrEmpty(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar))) || full == Path.GetPathRoot(full)) throw new Exception("Drop a file or folder, rather than an entire drive.");
                if (!File.Exists(full) && !System.IO.Directory.Exists(full)) throw new Exception("Dropped item no longer exists: " + full);
                return new Entry(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)), full, System.IO.Directory.Exists(full));
            }).ToArray();
            else return;
            await Transfer(list == RemoteFiles, items);
        } catch (Exception ex) { Log(ex.Message); }
    }
    void Cancel(object s, RoutedEventArgs e) => operation?.Cancel();
}
