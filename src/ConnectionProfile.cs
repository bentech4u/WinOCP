using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WinOCP;
public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New connection";
    public string Group { get; set; } = "My connections";
    public string Server { get; set; } = "https://api.example.com:6443";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public int AuthMethod { get; set; }
    public string Username { get; set; } = "";
    public string KubeconfigPath { get; set; } = "";
    public string DefaultProject { get; set; } = "";
    public bool SkipTls { get; set; }
    public string? EncryptedSecret { get; set; }
}
public sealed class ConnectionStore
{
    public bool ShowAtStartup { get; set; } = true;
    public List<ConnectionProfile> Sites { get; set; } = [];
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "connections.json");
    public static ConnectionStore Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<ConnectionStore>(File.ReadAllText(FilePath)) ?? new() : new();
    public void Save() {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
}
