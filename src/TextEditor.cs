using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
namespace WinOCP;
public sealed class TextFileDocument {
    public const int MaxBytes = 8 * 1024 * 1024;
    readonly Encoding encoding;
    readonly byte[] preamble;
    public string Text { get; }
    public TextFileDocument(byte[] bytes) {
        if (bytes.Length > MaxBytes) throw new IOException("The internal editor supports text files up to 8 MiB.");
        if (bytes.AsSpan().StartsWith(new byte[]{0xFF,0xFE,0,0}) || bytes.AsSpan().StartsWith(new byte[]{0,0,0xFE,0xFF})) throw new IOException("UTF-32 files are not supported by the editor.");
        if(bytes.AsSpan().StartsWith(new byte[]{0xFF,0xFE})) { encoding=new UnicodeEncoding(false,true,true); preamble=[0xFF,0xFE]; }
        else if(bytes.AsSpan().StartsWith(new byte[]{0xFE,0xFF})) { encoding=new UnicodeEncoding(true,true,true); preamble=[0xFE,0xFF]; }
        else { encoding=new UTF8Encoding(false,true); preamble=bytes.AsSpan().StartsWith(new byte[]{0xEF,0xBB,0xBF}) ? [0xEF,0xBB,0xBF] : []; }
        Text=encoding.GetString(bytes,preamble.Length,bytes.Length-preamble.Length);
        if(Text.Any(c => c=='\0' || (char.IsControl(c) && c is not ('\r' or '\n' or '\t')))) throw new IOException("This file contains binary data and cannot be edited as text.");
    }
    public byte[] Encode(string text) { var body=encoding.GetBytes(text); if(body.Length+preamble.Length>MaxBytes)throw new IOException("Text exceeds the 8 MiB editor limit.");return preamble.Concat(body).ToArray(); }
}
public sealed class TextEditor : Window {
    bool dirty, saving;
    public TextEditor(string path, TextFileDocument document, Func<byte[],Task<bool>> save) {
        Title="Edit — "+path; Width=900; Height=650; MinWidth=500; MinHeight=350;
        SetResourceReference(BackgroundProperty,"PageBrush");
        var panel=new DockPanel { Margin=new Thickness(12) };Content=panel;
        var toolbar=new DockPanel();DockPanel.SetDock(toolbar,Dock.Top);panel.Children.Add(toolbar);
        var button=new Button { Content="Save", ToolTip="Save changes (Ctrl+S)" };toolbar.Children.Add(button);
        var status=new TextBlock { Text=path,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),TextTrimming=TextTrimming.CharacterEllipsis };status.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");toolbar.Children.Add(status);
        var text=new TextBox { Text=document.Text,AcceptsReturn=true,AcceptsTab=true,FontFamily=new System.Windows.Media.FontFamily("Consolas"),FontSize=14,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,TextWrapping=TextWrapping.NoWrap };panel.Children.Add(text);
        text.TextChanged+=(_,_)=>{dirty=true;status.Text="Unsaved changes · "+path;};
        async Task Save() { if(saving)return;saving=true;button.IsEnabled=false;text.IsReadOnly=true;try { if(await save(document.Encode(text.Text))){dirty=false;status.Text="Saved · "+path;}else status.Text="Save failed · changes retained"; }catch(Exception ex){MessageBox.Show(this,ex.Message,"Save failed");}finally{saving=false;button.IsEnabled=true;text.IsReadOnly=false;} }
        button.Click+=async(_,_)=>await Save();
        PreviewKeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;await Save();}};
        Closing+=(_,e)=>{ if(saving){e.Cancel=true;return;} if(dirty && MessageBox.Show(this,"Discard unsaved changes?","Close editor",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)e.Cancel=true; };
    }
}
