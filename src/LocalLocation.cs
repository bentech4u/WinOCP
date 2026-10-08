using System.IO;
using System.Runtime.InteropServices;
namespace WinOCP;
public record LocalLocation(string Label, string Path)
{
    public override string ToString() => Label;
    public static LocalLocation[] GetLocations() {
        var items = new List<LocalLocation> { new("Locations…", "") };
        void Add(string label, string path) { if (!string.IsNullOrWhiteSpace(path)) items.Add(new(label, path)); }
        Add("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Downloads", DownloadsPath());
        foreach (var drive in DriveInfo.GetDrives().OrderBy(x => x.Name)) {
            var label = drive.Name;
            try {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady && !string.IsNullOrEmpty(drive.VolumeLabel)) label += "  " + drive.VolumeLabel;
                else label += "  (" + drive.DriveType + ")";
            } catch { /* An unavailable drive remains selectable; browsing reports its error. */ }
            Add(label, drive.Name);
        }
        return items.ToArray();
    }
    static string DownloadsPath() {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        IntPtr path = IntPtr.Zero;
        try { return SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path) == 0 ? Marshal.PtrToStringUni(path) ?? "" : ""; }
        finally { if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path); }
    }
    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
}
