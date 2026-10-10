using System.Text;
namespace MonolithHarness.Core.Mobile;

public sealed record MobileDocument(string Path,long Bytes);
public sealed class MobileFiles(string directory,int chatId)
{
    readonly string root=Path.Combine(directory,"documents","chat-"+chatId);
    public string Resolve(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0'))throw new ArgumentException("Chemin relatif requis.");
        var segments=path.Replace('\\','/').Split('/');
        if(segments.Any(x=>x is "" or "." or ".."))throw new ArgumentException("Chemin non autorisé.");
        var full=Path.GetFullPath(Path.Combine(root,Path.Combine(segments)));
        if(!full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new UnauthorizedAccessException("Fichier hors de cette conversation.");
        var parent=full;while(parent!=root && parent!=null) {if((File.Exists(parent)||Directory.Exists(parent)) && (File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Liens symboliques non autorisés.");parent=Path.GetDirectoryName(parent);}
        return full;
    }
    public IReadOnlyList<MobileDocument> List()
    {
        if(!Directory.Exists(root))return [];
        return Directory.EnumerateFiles(root,"*",new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}).Take(60).Select(x=>new MobileDocument(Path.GetRelativePath(root,x).Replace('\\','/'),new FileInfo(Resolve(Path.GetRelativePath(root,x))).Length)).ToArray();
    }
    public static string Decode(byte[] data)
    {
        if(data.Length>2*1024*1024)throw new IOException("Le document texte doit faire moins de 2 Mo.");
        string text;
        if(data.AsSpan().StartsWith(new byte[]{255,254}))text=new UnicodeEncoding(false,true,true).GetString(data,2,data.Length-2);
        else if(data.AsSpan().StartsWith(new byte[]{254,255}))text=new UnicodeEncoding(true,true,true).GetString(data,2,data.Length-2);
        else {var start=data.AsSpan().StartsWith(new byte[]{239,187,191})?3:0;text=new UTF8Encoding(false,true).GetString(data,start,data.Length-start);}
        if(text.Contains('\0') || text.Count(x=>char.IsControl(x) && x is not ('\t' or '\r' or '\n'))>5)throw new IOException("Format binaire non pris en charge. Importez un document texte UTF-8 ou UTF-16.");
        return text;
    }
    public async Task<string> ImportAsync(string name,byte[] bytes,CancellationToken ct=default)
    {
        var text=Decode(bytes);name=Path.GetFileName(name.Replace('\\','/'));
        if(string.IsNullOrWhiteSpace(name) || name.Contains(':'))name="document.txt";
        var original=name;int suffix=1;while(File.Exists(Resolve(name)))name=Path.GetFileNameWithoutExtension(original)+"-"+(suffix++)+Path.GetExtension(original);
        await WriteAsync(name,text,ct);return name;
    }
    public async Task<string> ReadAsync(string path,CancellationToken ct=default)=>Decode(await File.ReadAllBytesAsync(Resolve(path),ct));
    public async Task WriteAsync(string path,string content,CancellationToken ct=default)
    {
        var destination=Resolve(path);var bytes=Encoding.UTF8.GetBytes(content);
        if(bytes.Length>2*1024*1024)throw new IOException("Le document doit faire moins de 2 Mo.");
        var list=List();if(list.Count>=60 && !File.Exists(destination))throw new IOException("Maximum 60 fichiers par conversation.");
        if(list.Sum(x=>x.Bytes)+bytes.Length-(File.Exists(destination)?new FileInfo(destination).Length:0)>32*1024*1024)throw new IOException("Espace documentaire de cette conversation plein (32 Mo).");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);var temp=destination+".monolith-"+Guid.NewGuid().ToString("N")+".tmp";
        try {await File.WriteAllBytesAsync(temp,bytes,ct);File.Move(temp,destination,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
