using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
namespace WinOCP;
public partial class ConnectionManager : Window
{
    readonly MainWindow host;
    ConnectionStore store = new();
    Guid editingId = Guid.NewGuid();
    bool working, changed, storeReadable = true;
    public ConnectionManager(MainWindow host) {
        this.host = host; InitializeComponent();
        string? loadError = null;
        try { store = ConnectionStore.Load(); } catch (Exception ex) { loadError = "Cannot read saved sites: " + ex.Message; storeReadable = false; SaveButton.IsEnabled = DeleteButton.IsEnabled = ShowStartup.IsEnabled = false; }
        ShowStartup.IsChecked = store.ShowAtStartup;
        RefreshSites(); if (store.Sites.Count > 0) Sites.SelectedItem = store.Sites[0]; else ResetEditor();
        if (loadError != null) Feedback.Text = loadError;
        foreach (var box in new TextBox[] { SiteName, GroupName, ApiUrl, Description, DefaultProject, Username, ConfigPath, Notes }) box.TextChanged += (_, _) => changed = true;
        Credential.PasswordChanged += (_, _) => changed = true;
        RememberSecret.Click += (_, _) => changed = true; SkipCertificate.Click += (_, _) => changed = true;
        Closing += OnClosing;
    }
    void RefreshSites() {
        var list = store.Sites.Where(x => (x.Name + " " + x.Server + " " + x.Group).Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Group).ThenBy(x => x.Name).ToList();
        var view = new ListCollectionView(list); view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConnectionProfile.Group))); Sites.ItemsSource = view;
    }
    void SearchChanged(object s, TextChangedEventArgs e) { if (Sites != null && !working) RefreshSites(); }
    bool DiscardChanges(){if(!changed)return true;var choice=UnsavedChangesDialog.Ask(this,SiteName.Text);if(choice==UnsavedChoice.Cancel)return false;if(choice==UnsavedChoice.Save){SaveSite(this,new RoutedEventArgs());return !changed;}changed=false;return true;}
    void ResetEditor() { editingId = Guid.NewGuid(); SiteName.Text = "New connection"; GroupName.Text = "My connections"; ApiUrl.Text = "https://api.example.com:6443"; Description.Clear(); Notes.Clear(); DefaultProject.Clear(); Method.SelectedIndex = 0; Username.Clear(); Credential.Clear(); ConfigPath.Clear(); RememberSecret.IsChecked = false; SkipCertificate.IsChecked = false; changed = false; }
    void NewSite(object s, RoutedEventArgs e) { if (working || !DiscardChanges()) return; Sites.SelectedItem = null; ResetEditor(); Editor.SelectedIndex = 0; SiteName.Focus(); SiteName.SelectAll(); }
    void SiteSelected(object s, SelectionChangedEventArgs e) {
        if (Sites.SelectedItem is not ConnectionProfile p || working) return;
        if (!DiscardChanges()) { Sites.SelectionChanged -= SiteSelected; Sites.SelectedItem = store.Sites.Find(x => x.Id == editingId); Sites.SelectionChanged += SiteSelected; return; }
        editingId = p.Id; SiteName.Text = p.Name; GroupName.Text = p.Group; ApiUrl.Text = p.Server; Description.Text = p.Description; Notes.Text = p.Notes; DefaultProject.Text = p.DefaultProject;
        Method.SelectedIndex = Math.Clamp(p.AuthMethod, 0, 2); Username.Text = p.Username; ConfigPath.Text = p.KubeconfigPath; SkipCertificate.IsChecked = p.SkipTls;
        Credential.Clear(); RememberSecret.IsChecked = p.EncryptedSecret != null;
        Feedback.Text = "Ready to log in.";
        if (p.EncryptedSecret != null) try { Credential.Password = ConnectionStore.Unprotect(p.EncryptedSecret); } catch { Feedback.Text = "Saved secret cannot be decrypted here. Enter it again."; }
        changed = false;
        Sites.SelectionChanged -= SiteSelected; try { Sites.SelectedItem = p; } finally { Sites.SelectionChanged += SiteSelected; }
    }
    void MethodChanged(object s, SelectionChangedEventArgs e) {
        if (CredentialLabel == null || ConfigPanel == null) return;
        Credential.Clear(); RememberSecret.IsChecked = false; changed = true;
        UsernamePanel.Visibility = Method.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SecretPanel.Visibility = Method.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        ConfigPanel.Visibility = Method.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        CredentialLabel.Content = Method.SelectedIndex == 1 ? "Password" : "Token"; RememberSecret.Visibility = Method.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible; ApiUrl.IsEnabled = Method.SelectedIndex != 2;
    }
    ConnectionProfile ReadEditor(bool saveSecret) {
        if (string.IsNullOrWhiteSpace(SiteName.Text)) throw new Exception("Enter a site name.");
        if (Method.SelectedIndex != 2 && (!Uri.TryCreate(ApiUrl.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https")) throw new Exception("Enter an HTTPS cluster API URL.");
        return new ConnectionProfile { Id = editingId, Name = SiteName.Text.Trim(), Group = string.IsNullOrWhiteSpace(GroupName.Text) ? "My connections" : GroupName.Text.Trim(), Server = ApiUrl.Text.Trim(), Description = Description.Text, Notes = Notes.Text, DefaultProject = DefaultProject.Text.Trim(), AuthMethod = Method.SelectedIndex, Username = Username.Text.Trim(), KubeconfigPath = ConfigPath.Text.Trim(), SkipTls = SkipCertificate.IsChecked == true, EncryptedSecret = saveSecret && Method.SelectedIndex != 2 && RememberSecret.IsChecked == true && Credential.Password.Length > 0 ? ConnectionStore.Protect(Credential.Password) : null };
    }
    void SaveSite(object s, RoutedEventArgs e) {
        if (working || !storeReadable) return;
        try { var p = ReadEditor(true); var old = store.Sites.ToList(); store.Sites.RemoveAll(x => x.Id == p.Id); store.Sites.Add(p); try { store.Save(); } catch { store.Sites = old; throw; } changed = false; RefreshSites(); Sites.SelectedItem = p; Feedback.Text = "Site saved."; } catch (Exception ex) { Feedback.Text = ex.Message; }
    }
    void DeleteSite(object s, RoutedEventArgs e) {
        if (working || !storeReadable || !store.Sites.Any(x => x.Id == editingId)) return;
        if (MessageBox.Show(this, "Delete this saved site?", "Delete connection", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { var old = store.Sites.ToList(); store.Sites.RemoveAll(x => x.Id == editingId); try { store.Save(); } catch { store.Sites = old; throw; } changed = false; RefreshSites(); ResetEditor(); Feedback.Text = "Site deleted."; } catch (Exception ex) { Feedback.Text = ex.Message; }
    }
    void BrowseConfig(object s, RoutedEventArgs e) { var dialog = new OpenFileDialog { Title = "Choose kubeconfig" }; if (dialog.ShowDialog(this) == true) ConfigPath.Text = dialog.FileName; }
    async void Login(object s, RoutedEventArgs e) {
        if (working) return;
        try {
            var p = ReadEditor(false); working = true; Editor.IsEnabled = Sites.IsEnabled = Search.IsEnabled = SaveButton.IsEnabled = DeleteButton.IsEnabled = LoginButton.IsEnabled = ShowStartup.IsEnabled = false;
            Feedback.Text = "Connecting…";
            var result = await host.LoginFromManager(p, Credential.Password);
            if (result) { Credential.Clear(); changed = false; working = false; DialogResult = true; }
            else { Feedback.Text = host.LastStatus; Editor.SelectedIndex = 0; }
        } catch (Exception ex) { Feedback.Text = ex.Message; }
        finally { working = false; Editor.IsEnabled = Sites.IsEnabled = Search.IsEnabled = LoginButton.IsEnabled = true; SaveButton.IsEnabled = DeleteButton.IsEnabled = ShowStartup.IsEnabled = storeReadable; }
    }
    void SiteDoubleClick(object s, MouseButtonEventArgs e) { if (Sites.SelectedItem != null) Login(s, e); }
    void StartupChanged(object s, RoutedEventArgs e) { if (!storeReadable) return; try { store.ShowAtStartup = ShowStartup.IsChecked == true; store.Save(); } catch (Exception ex) { Feedback.Text = ex.Message; } }
    void CloseManager(object s, RoutedEventArgs e) { if (working) { host.CancelLogin(); Feedback.Text = "Cancelling…"; } else Close(); }
    void OnClosing(object? s, CancelEventArgs e) { if (working) { host.CancelLogin(); e.Cancel = true; } else if (!DiscardChanges()) e.Cancel = true; else Credential.Clear(); }
}
