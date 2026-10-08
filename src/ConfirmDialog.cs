using System.Windows;
using System.Windows.Controls;
namespace WinOCP;
public sealed class ConfirmDialog : Window {
    public ConfirmDialog(string title,string message) {
        Title=title;Width=430;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"PageBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;
        panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(0,0,0,18)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var ok=new Button{Content="OK",MinWidth=85,IsDefault=true,Style=(Style)Application.Current.FindResource("Primary")};var cancel=new Button{Content="Cancel",MinWidth=85,IsCancel=true};buttons.Children.Add(ok);buttons.Children.Add(cancel);
        ok.Click+=(_,_)=>DialogResult=true;
    }
    public static bool Ask(Window owner,string title,string message)=>new ConfirmDialog(title,message){Owner=owner}.ShowDialog()==true;
}
