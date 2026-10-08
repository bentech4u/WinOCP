using System.Windows;
using System.Windows.Controls;
namespace WinOCP;
public sealed class MasterPasswordDialog : Window {
    public MasterPasswordDialog(bool change=false){
        var create=!ConnectionStore.Vault.Exists;
        Title=change?"Change master password":create?"Set master password":"Unlock WinOCP";Width=450;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/WinOCP;component/Assets/WinOCP.ico"));
        SetResourceReference(BackgroundProperty,"PageBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;
        panel.Children.Add(new TextBlock{Text=Title,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,0,4,12)});
        panel.Children.Add(new TextBlock{Text=create?"Protect your saved connections with a master password. Existing saved credentials will be migrated.":change?"Enter your current password and choose a new one.":"Enter your master password to open WinOCP.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,0,4,12)});
        PasswordBox AddPassword(string label){panel.Children.Add(new Label{Content=label});var box=new PasswordBox();panel.Children.Add(box);return box;}
        var current=AddPassword(change?"Current master password":"Master password");PasswordBox? replacement=change?AddPassword("New master password"):null;PasswordBox? confirm=create||change?AddPassword("Confirm master password"):null;
        if(create||change)panel.Children.Add(new TextBlock{Text="Use at least 12 characters. There is no password recovery; keep a secure backup of your password and encrypted vault.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,12,4,8)});
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,8,4,8)};panel.Children.Add(feedback);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var ok=new Button{Content=change?"Change password":create?"Create vault":"Unlock",IsDefault=true,Style=(Style)Application.Current.FindResource("Primary")};var cancel=new Button{Content=change?"Cancel":"Exit",IsCancel=true};buttons.Children.Add(ok);buttons.Children.Add(cancel);
        ok.Click+=async(_,_)=>{var password=current.Password;var next=replacement?.Password??password;if(confirm!=null&&next!=confirm.Password){feedback.Text="Passwords do not match.";return;}panel.IsEnabled=false;try{var warning=await Task.Run(()=>{if(change){ConnectionStore.Vault.ChangePassword(password,next);return "";}if(create)return ConnectionStore.CreateVault(password);ConnectionStore.Vault.Unlock(password);ConnectionStore.Load();return "";});if(warning.Length>0)new ConfirmDialog("Migration notice",warning,"Continue"){Owner=this}.ShowDialog();current.Clear();replacement?.Clear();confirm?.Clear();panel.IsEnabled=true;DialogResult=true;}catch(Exception ex){feedback.Text=ex.Message;}finally{panel.IsEnabled=true;}};
        Loaded+=(_,_)=>current.Focus();Closing+=(_,e)=>{if(!panel.IsEnabled)e.Cancel=true;};
    }
}