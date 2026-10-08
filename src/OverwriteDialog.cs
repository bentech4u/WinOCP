using System.Windows;
using System.Windows.Controls;
namespace WinOCP;
public enum OverwriteChoice { Ask, Skip, Replace, Cancel }
public sealed class OverwriteDialog : Window {
    public OverwriteChoice Choice {get;private set;}=OverwriteChoice.Cancel;
    public bool ApplyToRemaining {get;private set;}
    public OverwriteDialog(Window owner,string path,bool directory){Owner=owner;Title="Destination already exists";Width=460;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(BackgroundProperty,"PageBrush");SetResourceReference(ForegroundProperty,"TextBrush");var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;panel.Children.Add(new TextBlock{Text="The destination already exists:\n"+path+(directory?"\n\nReplace merges folders and overwrites matching files.":"\n\nReplace overwrites the destination file."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});var apply=new CheckBox{Content="Apply to remaining conflicts in this batch",Margin=new Thickness(0,0,0,16)};panel.Children.Add(apply);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);void Add(string label,OverwriteChoice choice){var button=new Button{Content=label,MinWidth=85,IsCancel=choice==OverwriteChoice.Cancel,IsDefault=choice==OverwriteChoice.Skip};button.Click+=(_,_)=>{Choice=choice;ApplyToRemaining=apply.IsChecked==true;DialogResult=choice!=OverwriteChoice.Cancel;};buttons.Children.Add(button);}Add("Skip existing",OverwriteChoice.Skip);Add("Replace",OverwriteChoice.Replace);Add("Cancel batch",OverwriteChoice.Cancel);}
}
