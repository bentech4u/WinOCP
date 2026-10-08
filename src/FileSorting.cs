using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace WinOCP;
public partial class MainWindow {
    readonly Dictionary<ListView,(string Property,ListSortDirection Direction)> fileSort = new();
    void FileColumnClicked(object sender,RoutedEventArgs e) {
        if(sender is not ListView list || e.OriginalSource is not GridViewColumnHeader { Column: not null } header || header.Role==GridViewColumnHeaderRole.Padding)return;
        var property=header.Column.DisplayMemberBinding is Binding binding?binding.Path.Path:"Name";
        var previous=fileSort.GetValueOrDefault(list,(Property: "Name", Direction: ListSortDirection.Ascending));
        fileSort[list]=(property,previous.Property==property && previous.Direction==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending);
        ApplyFileSort(list);
    }
    void ApplyFileSort(ListView list) {
        if(list.ItemsSource==null)return;
        var order=fileSort.GetValueOrDefault(list,(Property: "Name", Direction: ListSortDirection.Ascending));
        var view=CollectionViewSource.GetDefaultView(list.ItemsSource);
        using(view.DeferRefresh()) {
            view.SortDescriptions.Clear();view.SortDescriptions.Add(new(nameof(Entry.Directory),ListSortDirection.Descending));view.SortDescriptions.Add(new(order.Property,order.Direction));
            if(order.Property!="Name")view.SortDescriptions.Add(new(nameof(Entry.Name),ListSortDirection.Ascending));
        }
        if(list.View is GridView grid)foreach(var column in grid.Columns){var title=(column.Header as string??"").TrimEnd(' ','▲','▼');var property=column.DisplayMemberBinding is Binding binding?binding.Path.Path:"Name";column.Header=title+(property==order.Property?(order.Direction==ListSortDirection.Ascending?" ▲":" ▼"):"");}
    }
}
