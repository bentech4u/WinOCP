using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
namespace WinOCP;
public static class FileMetadata {
    public static Entry Local(FileSystemInfo info) {
        bool directory=(info.Attributes & FileAttributes.Directory)!=0;
        string rights="Unavailable",owner="Unavailable";
        try {
            FileSystemSecurity acl = info is DirectoryInfo d ? d.GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access) : ((FileInfo)info).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
            owner=acl.GetOwner(typeof(NTAccount))?.Value??"Unavailable";
            rights=string.Join("; ",acl.GetAccessRules(true,true,typeof(NTAccount)).Cast<FileSystemAccessRule>().Select(r=>$"{r.IdentityReference.Value}: {r.FileSystemRights} ({r.AccessControlType})"));
            if(rights.Length==0)rights="No ACL entries";
        }catch(SystemException){}
        return new(info.Name,info.FullName,directory,directory?null:((FileInfo)info).Length,info.LastWriteTime,rights,owner);
    }
    public static Entry[] ParseRemote(string path,string result) {
        var parts=result.Split('\0');var entries=new List<Entry>();
        for(int i=0;i+2<parts.Length;i+=3) {
            var name=parts[i+1];if(name.StartsWith("./"))name=name[2..];
            var metadata=parts[i+2].Split('|');long? size=null;DateTime? modified=null;string rights="Unavailable",owner="Unavailable";
            if(metadata.Length==5) {
                if(parts[i]!="d" && long.TryParse(metadata[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out var bytes))size=bytes;
                if(long.TryParse(metadata[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out var seconds)){try{modified=DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;}catch(ArgumentOutOfRangeException){}}
                rights=metadata[2].Length>1?metadata[2][1..]:metadata[2];owner=metadata[3] is "UNKNOWN" or ""?metadata[4]:metadata[3];
            }
            entries.Add(new(name,path.TrimEnd('/')+"/"+name,parts[i]=="d",size,modified,rights,owner));
        }
        return entries.ToArray();
    }
}
