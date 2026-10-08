using System.Windows;
using System.Windows.Controls;
namespace WinOCP;
public enum UnsavedChoice { Cancel, Save, Discard }
public sealed class UnsavedChangesDialog : Window {
    public UnsavedChoice Choice {get;private set;}=UnsavedChoice.Cancel;
    public UnsavedChangesDialog(Window owner,string name){Owner=owner;Title="Unsaved changes";Width=430;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(BackgroundProperty,"PageBrush");SetResourceReference(ForegroundProperty,"TextBrush");var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;panel.Children.Add(new TextBlock{Text=$"Save changes to {name} before continuing?",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,0,4,16),FontSize=14});var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);void Add(string label,UnsavedChoice choice,bool primary=false){var button=new Button{Content=label,MinWidth=85,IsCancel=choice==UnsavedChoice.Cancel,IsDefault=choice==UnsavedChoice.Save};if(primary)button.Style=(Style)Application.Current.FindResource("Primary");button.Click+=(_,_)=>{Choice=choice;DialogResult=choice!=UnsavedChoice.Cancel;};buttons.Children.Add(button);}Add("Save",UnsavedChoice.Save,true);Add("Discard",UnsavedChoice.Discard);Add("Cancel",UnsavedChoice.Cancel);}
    public static UnsavedChoice Ask(Window owner,string name){var dialog=new UnsavedChangesDialog(owner,name);dialog.ShowDialog();return dialog.Choice;}
}