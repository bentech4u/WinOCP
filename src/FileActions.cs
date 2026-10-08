using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace WinOCP;
public partial class MainWindow {
    void FileRightClick(object sender, MouseButtonEventArgs e) {
        if(sender is not ListView list || busy)return;
        var row=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListViewItem;
        if(row==null)list.SelectedItems.Clear();
        else if(!row.IsSelected)list.SelectedItem=row.Content;
    }
    void FileMenuOpening(object sender, ContextMenuEventArgs e) {
        if(sender is not ListView list)return;
        bool remote=list==RemoteFiles;
        if(busy || (remote && (!connected || Projects.SelectedItem==null || Pods.SelectedItem==null || Containers.SelectedItem==null))){e.Handled=true;return;}
        var menu=list.ContextMenu!;menu.Items.Clear();
        MenuItem Add(ItemsControl parent,string label,RoutedEventHandler action,bool enabled=true){var m=new MenuItem{Header=label,IsEnabled=enabled};m.Click+=action;parent.Items.Add(m);return m;}
        var create=new MenuItem{Header="New"};menu.Items.Add(create);
        Add(create,"File…",async(_,_)=>await CreateItem(remote,false));
        Add(create,"Directory…",async(_,_)=>await CreateItem(remote,true));
        menu.Items.Add(new Separator());
        Add(menu,"Delete…",async(_,_)=>await DeleteItems(remote),list.SelectedItems.Count>0);
        Add(menu,"Rename…",async(_,_)=>await RenameItem(remote),list.SelectedItems.Count==1);
        Add(menu,"Edit…",async(_,_)=>await EditItem(remote),list.SelectedItems.Count==1 && list.SelectedItem is Entry{Directory:false});
        menu.SetResourceReference(Control.BackgroundProperty,"CardBrush");menu.SetResourceReference(Control.ForegroundProperty,"TextBrush");
    }
    static string ValidateName(string name,bool remote) {
        if(string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\0') || name.Contains('\r') || name.Contains('\n'))throw new IOException("Enter a single file or directory name.");
        if(!remote && (name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.EndsWith('.') || name.EndsWith(' ')))throw new IOException("This name is not valid on Windows.");
        return name;
    }
    string? AskName(string title,string initial="") {
        var dialog=new Window{Title=title,Width=420,Height=170,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this};dialog.SetResourceReference(BackgroundProperty,"PageBrush");
        var panel=new StackPanel{Margin=new Thickness(12)};dialog.Content=panel;
        var input=new TextBox{Text=initial};panel.Children.Add(input);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var ok=new Button{Content="OK",IsDefault=true};var cancel=new Button{Content="Cancel",IsCancel=true};buttons.Children.Add(ok);buttons.Children.Add(cancel);ok.Click+=(_,_)=>dialog.DialogResult=true;
        dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};return dialog.ShowDialog()==true?input.Text:null;
    }
    Task<string> RemoteCommand(string script,params string[] paths) => Oc(new[]{"exec","-n",Project,Pod,"-c",Container,"--","sh","-c",script,"sh"}.Concat(paths).ToArray());
    string ChildPath(bool remote,string name)=>remote?RemotePath.Text.TrimEnd('/')+"/"+name:Path.Combine(Path.GetFullPath(LocalPath.Text),name);
    async Task RefreshFiles(bool remote){if(remote)await LoadRemote();else LoadLocal();}
    async Task CreateItem(bool remote,bool directory) {
        if(busy)return;var name=AskName(directory?"New directory":"New file");if(name==null)return;
        await Run(async()=>{var path=ChildPath(remote,ValidateName(name,remote));
            if(remote)await RemoteCommand(directory?"mkdir -- \"$1\"":"(set -C; : > \"$1\")",path);
            else if(directory){if(File.Exists(path)||Directory.Exists(path))throw new IOException("An item with that name already exists.");Directory.CreateDirectory(path);}
            else {using var file=new FileStream(path,FileMode.CreateNew);}
            await RefreshFiles(remote);Log("Created "+name);
        });
    }
    async Task DeleteItems(bool remote) {
        if(busy)return;var items=(remote?RemoteFiles:LocalFiles).SelectedItems.Cast<Entry>().ToArray();if(items.Length==0)return;
        if(MessageBox.Show(this,"Permanently delete these items, including directory contents?\n\n"+string.Join("\n",items.Select(x=>x.Name)),"Delete",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        await Run(async()=>{try{foreach(var item in items){if(remote)await RemoteCommand("rm -rf -- \"$1\"",item.FullPath);else if(item.Directory)Directory.Delete(item.FullPath,true);else File.Delete(item.FullPath);Log("Deleted "+item.Name);}}finally{await RefreshFiles(remote);}});
    }
    async Task RenameItem(bool remote) {
        if(busy || (remote?RemoteFiles:LocalFiles).SelectedItem is not Entry item)return;
        var name=AskName("Rename",item.Name);if(name==null || name==item.Name)return;
        await Run(async()=>{var target=ChildPath(remote,ValidateName(name,remote));
            if(remote)await RemoteCommand("if [ -e \"$2\" ] || [ -L \"$2\" ]; then echo 'An item with that name already exists.' >&2; exit 1; fi; mv -T -n -- \"$1\" \"$2\" && { [ ! -e \"$1\" ] && [ ! -L \"$1\" ]; }",item.FullPath,target);
            else if(item.Directory)Directory.Move(item.FullPath,target);else File.Move(item.FullPath,target);
            await RefreshFiles(remote);Log("Renamed "+item.Name+" to "+name);
        });
    }
    async Task EditItem(bool remote) {
        if(busy || (remote?RemoteFiles:LocalFiles).SelectedItem is not Entry{Directory:false} item)return;
        TextFileDocument? document=null;string? staging=null;
        // Modal editing keeps the selected cluster/container stable for subsequent saves.
        await Run(async()=>{
            var path=item.FullPath;
            if(remote){
                var size=long.Parse((await RemoteCommand("wc -c < \"$1\"",path)).Trim(),System.Globalization.CultureInfo.InvariantCulture);
                if(size>TextFileDocument.MaxBytes)throw new IOException("The internal editor supports text files up to 8 MiB.");
                Directory.CreateDirectory(session);staging=Path.Combine(session,"edit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
                var row=new TransferItem(item.Name,false);
                await StreamFile(false,item,row,Project,Pod,Container,staging,"/",operation!.Token);
                path=Path.Combine(staging,item.Name);
            }
            if(new FileInfo(path).Length>TextFileDocument.MaxBytes)throw new IOException("The internal editor supports text files up to 8 MiB.");
            document=new TextFileDocument(await File.ReadAllBytesAsync(path));
        });
        try {
            if(document==null)return;
            var editor=new TextEditor(item.FullPath,document,async bytes=>{
                bool saved=false;
                await Run(async()=>{
                    if(remote){
                        var temp=Path.Combine(staging!,item.Name);await File.WriteAllBytesAsync(temp,bytes);
                        var row=new TransferItem(item.Name,true);transfers.Add(row);row.Start(bytes.Length);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operation!.Token,row.Cancellation.Token);
                        try{await StreamFile(true,new Entry(item.Name,temp,false),row,Project,Pod,Container,staging!,item.FullPath[..(item.FullPath.LastIndexOf('/')+1)],linked.Token);row.Finish("Completed");}
                        catch{row.Finish("Failed");throw;}finally{row.Cancellation.Dispose();}
                    }else await File.WriteAllBytesAsync(item.FullPath,bytes);
                    saved=true;await RefreshFiles(remote);Log("Saved "+item.FullPath);
                });return saved;
            }){Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};editor.ShowDialog();
        }finally{if(staging!=null){try{Directory.Delete(staging,true);}catch{}}}
    }
}
