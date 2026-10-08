using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WinOCP;
public sealed class CredentialVault(string path) : IDisposable {
    const int Iterations=600000;
    static readonly byte[] Aad=Encoding.UTF8.GetBytes("WinOCP connections vault v1");
    byte[]? key,salt;
    public bool Exists=>File.Exists(path);
    public bool IsUnlocked=>key!=null;
    public sealed record Envelope(int Version,int Iterations,string Salt,string Nonce,string Tag,string Data);
    static byte[] Derive(string password,byte[] salt)=>Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
    Envelope Load(){var value=JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path))??throw new IOException("Invalid credential vault.");if(value.Version!=1||value.Iterations!=Iterations)throw new IOException("Unsupported credential vault version.");return value;}
    static byte[] Decrypt(Envelope e,byte[] key){var nonce=Convert.FromBase64String(e.Nonce);var tag=Convert.FromBase64String(e.Tag);var data=Convert.FromBase64String(e.Data);if(nonce.Length!=12||tag.Length!=16)throw new IOException("Invalid credential vault.");var plain=new byte[data.Length];try{using var aes=new AesGcm(key,16);aes.Decrypt(nonce,data,tag,plain,Aad.Concat(Convert.FromBase64String(e.Salt)).ToArray());return plain;}catch{CryptographicOperations.ZeroMemory(plain);throw;}}
    public void Unlock(string password){var e=Load();var newSalt=Convert.FromBase64String(e.Salt);if(newSalt.Length!=32)throw new IOException("Invalid credential vault.");var candidate=Derive(password,newSalt);try{var plain=Decrypt(e,candidate);CryptographicOperations.ZeroMemory(plain);Dispose();key=candidate;salt=newSalt;candidate=null!;}catch(AuthenticationTagMismatchException){throw new IOException("Incorrect master password or damaged credential vault.");}finally{if(candidate!=null)CryptographicOperations.ZeroMemory(candidate);}}
    public string Read(){if(key==null)throw new IOException("Unlock WinOCP first.");var plain=Decrypt(Load(),key);try{return Encoding.UTF8.GetString(plain);}finally{CryptographicOperations.ZeroMemory(plain);}}
    void Write(string value,byte[] writeKey,byte[] writeSalt,bool replace=true){var nonce=RandomNumberGenerator.GetBytes(12);var plain=Encoding.UTF8.GetBytes(value);var cipher=new byte[plain.Length];var tag=new byte[16];var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using var aes=new AesGcm(writeKey,16);aes.Encrypt(nonce,plain,cipher,tag,Aad.Concat(writeSalt).ToArray());var e=new Envelope(1,Iterations,Convert.ToBase64String(writeSalt),Convert.ToBase64String(nonce),Convert.ToBase64String(tag),Convert.ToBase64String(cipher));File.WriteAllText(temp,JsonSerializer.Serialize(e));File.Move(temp,path,replace);}finally{CryptographicOperations.ZeroMemory(plain);if(File.Exists(temp))File.Delete(temp);}}
    public static void ValidatePassword(string password){if(password.Length<12)throw new IOException("Use a master password of at least 12 characters.");}
    public void Create(string password,string value){if(Exists)throw new IOException("A vault already exists. Unlock it instead.");ValidatePassword(password);var newSalt=RandomNumberGenerator.GetBytes(32);var candidate=Derive(password,newSalt);try{Write(value,candidate,newSalt,false);Dispose();key=candidate;salt=newSalt;candidate=null!;}finally{if(candidate!=null)CryptographicOperations.ZeroMemory(candidate);}}
    public void Save(string value){if(key==null||salt==null)throw new IOException("Unlock WinOCP first.");var previous=Decrypt(Load(),key);CryptographicOperations.ZeroMemory(previous);Write(value,key,salt);}
    public void ChangePassword(string current,string replacement){ValidatePassword(replacement);Unlock(current);var value=Read();var newSalt=RandomNumberGenerator.GetBytes(32);var candidate=Derive(replacement,newSalt);try{Write(value,candidate,newSalt);Dispose();key=candidate;salt=newSalt;candidate=null!;}finally{if(candidate!=null)CryptographicOperations.ZeroMemory(candidate);}}
    public void Dispose(){if(key!=null)CryptographicOperations.ZeroMemory(key);key=null;salt=null;}
}