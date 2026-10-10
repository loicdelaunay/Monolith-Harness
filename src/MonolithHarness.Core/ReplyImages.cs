using System.Net;
using System.Net.Sockets;
namespace MonolithHarness.Core;

/// <summary>Bounded raster images. No cookies, credentials, local URLs or private network requests.</summary>
public static class ReplyImages
{
    public const int MaxBytes=8*1024*1024;
    static readonly SemaphoreSlim requests=new(3);
    static readonly HttpClient http=new(new SocketsHttpHandler {
        AllowAutoRedirect=false,UseProxy=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.All,
        ConnectCallback=async(context,ct)=> {
            var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,ct);
            if(addresses.Length==0 || addresses.Any(x=>!PublicAddress(x)))throw new IOException("Les images distantes doivent provenir d’un serveur Internet public.");
            Exception? last=null;
            foreach(var address in addresses) {
                var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
                try {await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),ct);return new NetworkStream(socket,true);}
                catch(Exception ex){socket.Dispose();last=ex;ct.ThrowIfCancellationRequested();}
            }
            throw new IOException("Image inaccessible.",last);
        }
    }) {Timeout=TimeSpan.FromSeconds(20)};
    static bool PublicAddress(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();var b=address.GetAddressBytes();
        if(address.AddressFamily==AddressFamily.InterNetwork)return !(b[0] is 0 or 10 or 127 || b[0]>=224 || b[0]==169 && b[1]==254 || b[0]==172 && b[1] is >=16 and <=31 || b[0]==192 && b[1]==168 || b[0]==100 && b[1] is >=64 and <=127 || b[0]==198 && b[1] is 18 or 19);
        if(b.Length==16 && b[0]==0 && b[1]==0x64 && b[2]==0xff && b[3]==0x9b && b.Skip(4).Take(8).All(x=>x==0))return PublicAddress(new IPAddress(b[12..]));
        return b.Length==16 && (b[0]&0xe0)==0x20;
    }
    public static bool IsRaster(ReadOnlySpan<byte> b)=>b.Length>=12 &&
        (b[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) || b[0]==255 && b[1]==216 && b[2]==255 || b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8) || b[..4].SequenceEqual("RIFF"u8) && b.Slice(8,4).SequenceEqual("WEBP"u8));
    public static async Task<byte[]> LoadAsync(string source,bool allowNetwork,CancellationToken ct)
    {
        if(source.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase)) {
            var comma=source.IndexOf(',');
            if(comma<0 || comma>80 || !source[..comma].EndsWith(";base64",StringComparison.OrdinalIgnoreCase) || source.Length>MaxBytes*4/3+100)throw new IOException("Image intégrée invalide ou trop volumineuse.");
            var bytes=Convert.FromBase64String(source[(comma+1)..]);Validate(bytes);return bytes;
        }
        if(!allowNetwork)throw new IOException("Les images distantes sont désactivées par l’autorisation Web.");
        if(!Uri.TryCreate(source,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0 || uri.Port!=443)throw new IOException("Image : utilisez une URL HTTPS publique ou une image intégrée PNG, JPEG, GIF ou WebP.");
        await requests.WaitAsync(ct);
        try {
            for(int i=0;i<5;i++) {
                using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
                if((int)response.StatusCode is >=300 and <=399) {
                    var location=response.Headers.Location ?? throw new IOException("Redirection de l’image invalide.");uri=new Uri(uri,location);
                    if(uri.Scheme!="https" || uri.UserInfo.Length>0 || uri.Port!=443)throw new IOException("Redirection de l’image interdite.");continue;
                }
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>MaxBytes)throw new IOException("Image trop volumineuse (8 Mo maximum).");
                using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[32768];int read;
                while((read=await stream.ReadAsync(chunk,ct))>0) {if(buffer.Length+read>MaxBytes)throw new IOException("Image trop volumineuse (8 Mo maximum).");await buffer.WriteAsync(chunk.AsMemory(0,read),ct);}
                var bytes=buffer.ToArray();Validate(bytes);return bytes;
            }
            throw new IOException("Trop de redirections pour l’image.");
        }finally {requests.Release();}
    }
    public static void Validate(byte[] bytes) {if(bytes.Length>MaxBytes || !IsRaster(bytes))throw new IOException("Format image non pris en charge : PNG, JPEG, GIF ou WebP, 8 Mo maximum.");}
}
