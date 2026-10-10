using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace MonolithHarness.Core.Mobile;

public sealed class MobileWeb : IDisposable
{
    readonly HttpClient http;
    readonly Dictionary<string,(DateTime At,string Text,string Title,string Url)> cache=[];
    public MobileWeb()
    {
        // Resolve and pin a public address at connect time, including on every redirect.
        var handler=new SocketsHttpHandler {AllowAutoRedirect=false,UseProxy=false,AutomaticDecompression=DecompressionMethods.All,
            ConnectCallback=async(context,ct)=> {
                var host=context.DnsEndPoint.Host;
                var addresses=await Dns.GetHostAddressesAsync(host,ct);
                if(addresses.Length==0 || addresses.Any(x=>!PublicAddress(x)))throw new UnauthorizedAccessException("Le web mobile est limité aux serveurs Internet publics.");
                Exception? last=null;
                foreach(var address in addresses) {
                    var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp) {NoDelay=true};
                    try {await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),ct);return new NetworkStream(socket,true);}
                    catch(Exception ex){socket.Dispose();last=ex;ct.ThrowIfCancellationRequested();}
                }
                throw new IOException("Serveur web inaccessible.",last);
            }};
        http=new(handler) {Timeout=TimeSpan.FromSeconds(25)};http.DefaultRequestHeaders.UserAgent.ParseAdd("Monolith/1.79.0");
    }
    static bool PublicAddress(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();var b=address.GetAddressBytes();
        if(address.AddressFamily==AddressFamily.InterNetwork)return !(b[0] is 0 or 10 or 127 || b[0]>=224 || b[0]==169 && b[1]==254 || b[0]==172 && b[1]>=16 && b[1]<=31 || b[0]==192 && b[1]==168 || b[0]==100 && b[1]>=64 && b[1]<=127 || b[0]==198 && b[1] is 18 or 19);
        // Standard DNS64/NAT64 synthesis can represent a public IPv4 server on IPv6-only mobile networks.
        if(b.Length==16 && b[0]==0 && b[1]==0x64 && b[2]==0xff && b[3]==0x9b && b.Skip(4).Take(8).All(x=>x==0))return PublicAddress(new IPAddress(b[12..]));
        // Only global unicast IPv6, excluding embedded/translated IPv4 local routes.
        return address.AddressFamily==AddressFamily.InterNetworkV6 && (b[0]&0xe0)==0x20 && !(b[0]==0x20 && b[1]==0x02) && !(b[0]==0x20 && b[1]==0x01 && b[2]==0 && b[3]==0);
    }
    public static Uri Validate(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0 || uri.Port!=443 || uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("URL HTTPS publique requise (port 443, sans identifiants).");
        return uri;
    }
    static string Replace(string input,string pattern,string value)=>Regex.Replace(input,pattern,value,RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(1));
    static string Plain(string html)
    {
        html=Replace(html,@"<(script|style|noscript)\b[^>]*>.*?</\1\s*>","");
        html=Replace(html,@"</?(p|div|br|li|h[1-6]|tr|section|article)\b[^>]*>","\n");html=Replace(html,@"<[^>]+>"," ");
        html=WebUtility.HtmlDecode(html);return string.Join('\n',html.Split('\n').Select(x=>Regex.Replace(x,@"\s+"," ",RegexOptions.None,TimeSpan.FromSeconds(1)).Trim()).Where(x=>x.Length>0));
    }
    async Task<(string Html,string Url,string Mime)> FetchAsync(Uri url,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));ct=deadline.Token;
        for(int redirect=0;redirect<6;redirect++) {
            Validate(url.AbsoluteUri);using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is >=300 and <=399 && response.Headers.Location is {} location) {url=new Uri(url,location);continue;}
            response.EnsureSuccessStatusCode();var mime=response.Content.Headers.ContentType?.MediaType??"text/plain";
            if(!(mime.StartsWith("text/",StringComparison.OrdinalIgnoreCase) || mime.Contains("json",StringComparison.OrdinalIgnoreCase) || mime.Contains("xml",StringComparison.OrdinalIgnoreCase)))throw new IOException("Cette page ne contient pas de texte lisible.");
            if(response.Content.Headers.ContentLength>2*1024*1024)throw new IOException("Page trop volumineuse (maximum 2 Mo).");
            using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[16384];int count;
            while((count=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+count>2*1024*1024)throw new IOException("Page trop volumineuse.");await buffer.WriteAsync(chunk.AsMemory(0,count),ct);}
            Encoding encoding=Encoding.UTF8;var charset=response.Content.Headers.ContentType?.CharSet?.Trim('"');
            if(!string.IsNullOrWhiteSpace(charset)) {try{encoding=Encoding.GetEncoding(charset);}catch(ArgumentException){}}
            return (encoding.GetString(buffer.ToArray()),url.AbsoluteUri,mime);
        }
        throw new IOException("Trop de redirections web.");
    }
    public async Task<JsonObject> ReadAsync(string url,int start,int count,CancellationToken ct)
    {
        Validate(url);
        if(!cache.TryGetValue(url,out var page) || DateTime.UtcNow-page.At>TimeSpan.FromMinutes(10)) {
            var fetched=await FetchAsync(new Uri(url),ct);var text=fetched.Mime.Contains("html")?Plain(fetched.Html):fetched.Html;
            if(text.Length>250000)text=text[..250000]+"\n[Page tronquée à 250000 caractères]";
            var title=Regex.Match(fetched.Html,@"<title\b[^>]*>(.*?)</title>",RegexOptions.Singleline|RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
            page=(DateTime.UtcNow,text,title.Success?Plain(title.Groups[1].Value):fetched.Url,fetched.Url);
            if(cache.Count>=8)cache.Remove(cache.OrderBy(x=>x.Value.At).First().Key);cache[url]=page;
        }
        var lines=page.Text.Replace("\r","").Split('\n');start=Math.Max(1,start);count=Math.Clamp(count,1,120);
        var excerpt=string.Join('\n',lines.Skip(start-1).Take(count).Select((line,i)=>$"{start+i}: {line}"));var truncated=excerpt.Length>18000;if(truncated)excerpt=excerpt[..18000];
        return new() {["url"]=page.Url,["title"]=page.Title,["line_count"]=lines.Length,["start_line"]=start,["truncated"]=truncated,["text"]=excerpt};
    }
    public async Task<JsonObject> SearchAsync(string query,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(query) || query.Length>500)throw new ArgumentException("Recherche entre 1 et 500 caractères requise.");
        var page=await FetchAsync(new Uri("https://html.duckduckgo.com/html/?q="+Uri.EscapeDataString(query)),ct);
        var results=new JsonArray();
        foreach(Match match in Regex.Matches(page.Html,@"<a\b(?=[^>]*class\s*=\s*[""'][^""']*result__a)[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(1))) {
            var href=WebUtility.HtmlDecode(match.Groups[1].Value);var decoded=Regex.Match(href,@"[?&]uddg=([^&]+)");if(decoded.Success)href=Uri.UnescapeDataString(decoded.Groups[1].Value);
            else if(href.StartsWith("//"))href="https:"+href;
            if(!Uri.TryCreate(href,UriKind.Absolute,out var uri) || uri.Scheme!="https")continue;
            results.Add(new JsonObject {["title"]=Plain(match.Groups[2].Value),["url"]=href});if(results.Count>=8)break;
        }
        if(results.Count==0)throw new IOException("Le moteur de recherche n’a fourni aucun résultat exploitable (absence de résultats ou protection anti-robot). Utilisez web_read avec une URL connue.");
        return new() {["query"]=query,["source"]=page.Url,["results"]=results};
    }
    public void Dispose()=>http.Dispose();
}
