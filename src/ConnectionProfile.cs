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
    public static string FilePath => Path.Combine(AppContext.BaseDirectory,"connections.vault");
    public static CredentialVault Vault {get;}=new(FilePath);
    public static ConnectionStore Load()=>JsonSerializer.Deserialize<ConnectionStore>(Vault.Read())??throw new IOException("Invalid saved connections.");
    public void Save()=>Vault.Save(JsonSerializer.Serialize(this));
    public static string Protect(string value){if(!Vault.IsUnlocked)throw new IOException("Unlock WinOCP first.");return value;}
    public static string Unprotect(string value){if(!Vault.IsUnlocked)throw new IOException("Unlock WinOCP first.");return value;}
    public static string CreateVault(string password){
        var legacy=Path.Combine(AppContext.BaseDirectory,"connections.json");var store=new ConnectionStore();
        if(File.Exists(legacy)){
            store=JsonSerializer.Deserialize<ConnectionStore>(File.ReadAllText(legacy))??throw new IOException("Invalid legacy connections file. It has not been changed.");
            foreach(var site in store.Sites)if(site.EncryptedSecret!=null){try{var data=ProtectedData.Unprotect(Convert.FromBase64String(site.EncryptedSecret),null,DataProtectionScope.CurrentUser);try{site.EncryptedSecret=Encoding.UTF8.GetString(data);}finally{CryptographicOperations.ZeroMemory(data);}}catch{throw new IOException("An existing saved secret could not be migrated. Run this upgrade with the original Windows account. Existing files have not been changed.");}}
        }
        Vault.Create(password,JsonSerializer.Serialize(store));
        if(File.Exists(legacy)){try{File.Delete(legacy);}catch{return "Vault created. The old connections.json could not be removed; remove that old file manually after verifying your saved connections.";}}
        return "";
    }
}