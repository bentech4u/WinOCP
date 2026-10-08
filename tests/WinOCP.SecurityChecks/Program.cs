using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinOCP;
static class Checks {
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();}catch(Exception e)when(e is IOException or CryptographicException or JsonException or FormatException){return;}throw new Exception(message);}
    static void Main(){
        var scratch=Path.Combine(AppContext.BaseDirectory,"WinOCP-vault-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);var path=Path.Combine(scratch,"test.vault");
        const string password="test-only-master-password";const string replacement="test-only-replacement-password";const string payload="{\"Username\":\"dummy-user\",\"Token\":\"dummy-token-for-tests\"}";
        using(var vault=new CredentialVault(path)){
            Reject(()=>vault.Save(payload),"Locked save accepted");Reject(()=>vault.Read(),"Locked read accepted");Reject(()=>vault.Create("short",payload),"Weak password accepted");Assert(!File.Exists(path),"Failed setup wrote a vault");
            vault.Create(password,payload);Assert(vault.Read()==payload,"Vault round trip failed");var first=File.ReadAllText(path);Assert(!first.Contains("dummy-user")&&!first.Contains("dummy-token")&&!first.Contains(password),"Sensitive plaintext on disk");
            vault.Save(payload);var second=File.ReadAllText(path);Assert(first!=second,"Save reused nonce");Reject(()=>vault.Create(password,payload),"Existing vault overwritten");
            vault.Dispose();Reject(()=>vault.Unlock("incorrect"),"Wrong password accepted");Assert(!vault.IsUnlocked,"Wrong password unlocked vault");vault.Unlock(password);Assert(vault.Read()==payload,"Restart unlock failed");
            Reject(()=>vault.ChangePassword("wrong",replacement),"Password change accepted wrong old password");Assert(File.ReadAllText(path)==second,"Failed password change altered vault");
            vault.ChangePassword(password,replacement);vault.Dispose();Reject(()=>vault.Unlock(password),"Old password still accepted");vault.Unlock(replacement);Assert(vault.Read()==payload,"Password change lost data");
            var good=File.ReadAllText(path);var envelope=JsonSerializer.Deserialize<CredentialVault.Envelope>(good)!;var bytes=Convert.FromBase64String(envelope.Data);bytes[0]^=1;File.WriteAllText(path,JsonSerializer.Serialize(envelope with {Data=Convert.ToBase64String(bytes)}));Reject(()=>vault.Save(payload),"Save overwrote a tampered vault");vault.Dispose();Reject(()=>vault.Unlock(replacement),"Tampered ciphertext accepted");File.WriteAllText(path,good);
            using var portable=new CredentialVault(path);portable.Unlock(replacement);Assert(portable.Read()==payload,"Independent instance portability failed");
            File.WriteAllText(path,JsonSerializer.Serialize(envelope with {Version=99}));Reject(()=>portable.Read(),"Unknown format accepted");File.WriteAllText(path,"{");Reject(()=>portable.Read(),"Truncated vault accepted");
        }
        var legacy=Path.Combine(AppContext.BaseDirectory,"connections.json");var destination=ConnectionStore.FilePath;
        if(File.Exists(legacy)||File.Exists(destination))throw new Exception("Migration test requires a clean test output directory; existing files were not changed.");
        try{
            var store=new ConnectionStore{Sites=[new(){Name="Dummy profile",Username="dummy-user",EncryptedSecret="invalid-dpapi"}]};File.WriteAllText(legacy,JsonSerializer.Serialize(store));var original=File.ReadAllText(legacy);Reject(()=>ConnectionStore.CreateVault(password),"Unreadable legacy secret migrated");Assert(!File.Exists(destination)&&File.ReadAllText(legacy)==original,"Failed migration changed existing data");
            store.Sites[0].EncryptedSecret=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes("dummy-token-for-tests"),null,DataProtectionScope.CurrentUser));File.WriteAllText(legacy,JsonSerializer.Serialize(store));ConnectionStore.CreateVault(password);Assert(!File.Exists(legacy),"Legacy file remains after migration");var migrated=ConnectionStore.Load();Assert(migrated.Sites[0].Username=="dummy-user"&&ConnectionStore.Unprotect(migrated.Sites[0].EncryptedSecret!)=="dummy-token-for-tests","Migration lost profile or secret");Assert(!File.ReadAllText(destination).Contains("dummy"),"Migration wrote plaintext");ConnectionStore.Vault.Dispose();ConnectionStore.Vault.Unlock(password);Assert(ConnectionStore.Load().Sites.Count==1,"Migrated profiles did not survive restart");
        }finally{ConnectionStore.Vault.Dispose();if(File.Exists(legacy))File.Delete(legacy);if(File.Exists(destination))File.Delete(destination);}
        Console.WriteLine("Vault checks passed: encryption, locked access, password validation, restart, password change, nonce freshness, tampering, portability, and migration success/failure.");
    }
}