using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace WinOCP;
public partial class MainWindow {
    ICollectionView? transferView;
    void SetupTransferFilters() {
        transferView=CollectionViewSource.GetDefaultView(transfers);
        transferView.Filter=value=>value is TransferItem item && ((TransferFilter.SelectedItem as ComboBoxItem)?.Content as string) switch {
            "Running"=>item.State is "Queued" or "Transferring",
            "Completed"=>item.State=="Completed",
            "Cancelled"=>item.State=="Cancelled",
            _=>true
        };
        TransferList.ItemsSource=transferView;
        transfers.CollectionChanged+=(_,e)=>{
            if(e.OldItems!=null)foreach(TransferItem item in e.OldItems)item.PropertyChanged-=TransferStateChanged;
            if(e.NewItems!=null)foreach(TransferItem item in e.NewItems)item.PropertyChanged+=TransferStateChanged;
        };
    }
    void TransferStateChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(TransferItem.State))transferView?.Refresh();}
    void TransferFilterChanged(object sender,SelectionChangedEventArgs e)=>transferView?.Refresh();
    void ClearTransferHistory(object sender,RoutedEventArgs e){foreach(var item in transfers.Where(x=>x.State is "Completed" or "Cancelled").ToArray())transfers.Remove(item);}
}
