using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
namespace MonolithHarness.Core;

/// <summary>A response artifact, never a tool call or a bridge to the host.</summary>
public sealed record ReplyWebDocument(string Html, string Title)
{
    public const int MaxCharacters = 450_000;
    // Do not repeatedly execute a document while its streamed HTML is incomplete.
    public bool ReadyForInlinePreview => !(Html.TrimStart().StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
        Html.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase)) || ReplyHtmlGrouping.IsCompleteDocument(Html);
    public const string Instructions = "RESPONSE MEDIA: You may display images using Markdown ![description](https://public-url/image.png) or a supplied image path; never invent an image URL. For a visual HTML/CSS response, emit one complete fenced html block with CSS inside <style>. Use self-contained content and embedded data images. A preview is displayed by the application; no tool is required. Keep ordinary answers in Markdown. External scripts, styles, frames, network requests, native APIs and local file access are unavailable in HTML previews.";
    static readonly Regex fences = new(@"(?m)^[ \t]*(?<f>`{3,}|~{3,})(?<lang>html-preview|html|htm|webapp|css|javascript|js)(?=[ \t\r\n])[^\r\n]*\r?\n(?<body>[\s\S]*?)^[ \t]*\k<f>[`~]*[ \t]*\r?$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    public static string PrepareMarkdown(string markdown) => ReplyHtmlGrouping.Prepare(markdown);
    public static bool IsHtml(string? language) => language?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() is "html" or "htm" or "webapp" or "html-preview";
    public static string ExtraCss(string markdown)
    {
        if(markdown.Length>2_000_000)return "";
        var styles=fences.Matches(markdown).Cast<Match>().Where(x=>x.Groups["lang"].Value.Equals("css",StringComparison.OrdinalIgnoreCase)).Take(16).Select(x=>x.Groups["body"].Value);
        var css=string.Join("\n",styles);return css.Length<MaxCharacters?css:"";
    }
    public static IReadOnlyList<ReplyWebDocument> Extract(string markdown)
    {
        if(markdown.Length > 2_000_000)return [];
        markdown=PrepareMarkdown(markdown);
        var matches=fences.Matches(markdown).Cast<Match>().ToArray();
        var css=ExtraCss(markdown);
        var result=new List<ReplyWebDocument>();
        foreach(var match in matches.Where(x=>IsHtml(x.Groups["lang"].Value))) {
            var html=match.Groups["body"].Value;
            if(css.Length>0 && html.Length+css.Length<MaxCharacters)html+="<style>"+css+"</style>";
            if(html.Length<=MaxCharacters)result.Add(FromHtml(html));
            if(result.Count==8)break;
        }
        var raw=markdown.TrimStart();
        if(result.Count==0 && raw.Length<=MaxCharacters && new[]{"<!doctype html","<html","<body","<main","<div","<style"}.Any(tag=>raw.StartsWith(tag,StringComparison.OrdinalIgnoreCase)))result.Add(FromHtml(raw));
        return result;
    }
    public static bool IsEnvelopeFragment(string html)
    {
        if(html.Length>MaxCharacters)return false;
        using var document=new HtmlParser().ParseDocument(html);
        // A CDATA delimiter is not an app. Preserve legitimate elements containing that text.
        return document.Body is { Children.Length: 0 } body && body.TextContent.Trim()=="]]>";
    }
    public static ReplyWebDocument FromHtml(string html)
    {
        if(html.Length>MaxCharacters)throw new IOException("Aperçu HTML trop volumineux (450 000 caractères maximum).");
        using var document=new HtmlParser().ParseDocument(html);
        return new(html, string.IsNullOrWhiteSpace(document.Title)?"Aperçu HTML":document.Title[..Math.Min(100,document.Title.Length)]);
    }
    public string SandboxedPage(bool interactive, bool dark, ReplyRenderOptions? options = null) =>
        Shell(interactive, dark, WebUtility.HtmlEncode(Content(interactive, dark, options ?? ReplyRenderOptions.Default)));

    public static string SandboxedShell(bool interactive, bool dark, string? resizeChannel = null, string? background = null) =>
        Shell(interactive, dark, "", resizeChannel, background);

    /// <summary>Set srcdoc after the host navigation; avoids WebView2's 2 MB HTML navigation limit.</summary>
    public string PreviewLoadScript(bool interactive, bool dark, ReplyRenderOptions? options = null, string? resizeChannel = null) =>
        "document.getElementById('preview').srcdoc=" + System.Text.Json.JsonSerializer.Serialize(Content(interactive, dark, options ?? ReplyRenderOptions.Default, resizeChannel)) + ";";

    string Content(bool interactive, bool dark, ReplyRenderOptions options, string? resizeChannel = null)
    {
        var palette = options.FollowApplicationTheme && options.Palette is { IsValid: true } configured ? configured : null;
        dark = palette?.Dark ?? dark;
        using var document=new HtmlParser().ParseDocument(Html);
        // The model cannot replace policy/base URLs, embed another page or load an external library.
        foreach(var element in document.QuerySelectorAll("meta, base, iframe, frame, object, embed, link, script[src]").ToArray())element.Remove();
        if(!interactive) {
            foreach(var script in document.QuerySelectorAll("script").ToArray())script.Remove();
            foreach(var element in document.All)foreach(var attribute in element.Attributes.Where(x=>x.Name.StartsWith("on",StringComparison.OrdinalIgnoreCase)).ToArray())element.RemoveAttribute(attribute.Name);
        }
        var policy="default-src 'none'; img-src data: blob:; style-src 'unsafe-inline'; font-src data:; script-src "+(interactive?"'unsafe-inline'":"'none'")+"; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
        var meta=document.CreateElement("meta");meta.SetAttribute("http-equiv","Content-Security-Policy");meta.SetAttribute("content",policy);document.Head!.Prepend(meta);
        var viewport=document.CreateElement("meta");viewport.SetAttribute("name","viewport");viewport.SetAttribute("content","width=device-width, initial-scale=1");document.Head.Append(viewport);
        var defaults=document.CreateElement("style");
        var colors = options.FollowApplicationTheme
            ? ":root{--monolith-bg:"+(palette?.Background ?? (dark?"#16181d":"#ffffff"))+";--monolith-surface:"+(palette?.Surface ?? (dark?"#20232b":"#f5f5f5"))+";--monolith-text:"+(palette?.Foreground ?? (dark?"#e6e0e9":"#1c1b1f"))+";--monolith-muted:"+(palette?.Muted ?? (dark?"#b8b8b8":"#606060"))+";--monolith-accent:"+(palette?.Accent ?? "#22d3ee")+"}body{background:var(--monolith-bg);color:var(--monolith-text)}"
            : "";
        defaults.TextContent="html{color-scheme:"+(options.FollowApplicationTheme?(dark?"dark":"light"):"light dark")+"}body{margin:16px;font-family:system-ui,sans-serif;overflow-wrap:anywhere}img,video,canvas,svg{max-width:100%}*{box-sizing:border-box}"+colors;
        if (resizeChannel != null) defaults.TextContent += "html,body{height:auto!important;min-height:0!important}html{overflow-y:auto!important}body{overflow-y:visible!important}";
        document.Head.InsertBefore(defaults,meta.NextSibling);
        // Both libraries precede every model script (head and body), without eval or a network request.
        if(interactive) {
            if (options.FollowApplicationTheme) document.DocumentElement!.ClassList.Toggle("dark",dark);
            if(options.Tailwind && options.FollowApplicationTheme) {
                // Use the application's theme, rather than the operating system's color preference.
                var theme=document.CreateElement("style");theme.SetAttribute("type","text/tailwindcss");theme.TextContent="@custom-variant dark (&:where(.dark, .dark *));@theme inline{--color-monolith-bg:var(--monolith-bg);--color-monolith-surface:var(--monolith-surface);--color-monolith-text:var(--monolith-text);--color-monolith-muted:var(--monolith-muted);--color-monolith-accent:var(--monolith-accent)}";document.Head.InsertBefore(theme,defaults);
            }
            foreach(var library in ReplyWebLibraries.Enabled(options)) {
                var script=document.CreateElement("script");script.SetAttribute("data-monolith-library",library.Name);script.TextContent=library.Script;document.Head.InsertBefore(script,defaults);
            }
            if(options.ChartJs && options.FollowApplicationTheme) {
                var theme=document.CreateElement("script");theme.TextContent="Chart.defaults.color='"+(palette?.Foreground ?? (dark?"#E6E0E9":"#1C1B1F"))+"';Chart.defaults.borderColor='"+(dark?"rgba(255,255,255,0.14)":"rgba(0,0,0,0.12)")+"';Chart.defaults.font.family='system-ui, sans-serif';";document.Head.InsertBefore(theme,defaults);
            }
        }
        if (interactive && resizeChannel != null)
        {
            var sizing = document.CreateElement("script"); sizing.SetAttribute("data-monolith-sizing", "true");
            sizing.TextContent = ResizeObserverScript(resizeChannel); document.Body!.Append(sizing);
        }
        return "<!doctype html>"+document.DocumentElement!.OuterHtml;
    }

    static string ResizeObserverScript(string channel) => "(()=>{const monolithSizeChannel=" + System.Text.Json.JsonSerializer.Serialize(channel) + ";" + """
          let last=-1,timer=0,width=innerWidth,height=innerHeight,lastBody=0,contentChanged=true;
          const measure=()=>{
            timer=0;if(!document.body)return;
            const changedWidth=width!==innerWidth,changedHeight=height!==innerHeight;
            width=innerWidth;height=innerHeight;
            const body=document.body,rect=body.getBoundingClientRect(),style=getComputedStyle(body);
            const margins=(parseFloat(style.marginTop)||0)+(parseFloat(style.marginBottom)||0);
            const range=document.createRange();range.selectNodeContents(body);
            const bottom=Math.max(rect.bottom,range.getBoundingClientRect().bottom);
            const measured=Math.ceil(Math.max(rect.height,bottom-rect.top)+margins);
            // A host resize alone must not start a vh/percentage-height feedback loop.
            if(changedHeight&&!changedWidth&&!contentChanged){lastBody=rect.height;return;}
            if(!changedWidth&&!contentChanged&&Math.abs(rect.height-lastBody)<2)return;
            contentChanged=false;lastBody=rect.height;
            const size=Math.max(80,Math.min(2000,measured));
            if(Math.abs(size-last)>2){last=size;parent.postMessage({kind:'monolith-size',channel:monolithSizeChannel,height:size},'*');}
          };
          const schedule=()=>{if(!timer)timer=setTimeout(measure,100);};
          const start=()=>{
            new ResizeObserver(schedule).observe(document.body);
            new MutationObserver(()=>{contentChanged=true;schedule();}).observe(document.body,{childList:true,subtree:true,characterData:true});
            addEventListener('resize',schedule);
            document.addEventListener('load',()=>{contentChanged=true;schedule();},true);
            document.fonts?.ready.then(()=>{contentChanged=true;schedule();});
            schedule();
          };
          if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start,{once:true});else start();
        })();
        """;

    static string Shell(bool interactive, bool dark, string content, string? resizeChannel = null, string? background = null)
    {
        // Only bounded sizing messages from this opaque iframe are relayed; no host API is exposed.
        var color = background is { Length: 7 } && background[0] == '#' &&
            background.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0 ? background : dark ? "#16181d" : "#ffffff";
        var reveal = interactive ? "<script>const previewFrame=document.getElementById('preview');previewFrame.addEventListener('load',()=>{if(previewFrame.srcdoc)setTimeout(()=>requestAnimationFrame(()=>requestAnimationFrame(()=>{previewFrame.style.visibility='visible'})),60)});</script>" : "";
        var relay = resizeChannel == null ? "" : "<script>" + """
            (()=>{const channel=CHANNEL;addEventListener('message',event=>{
              const frame=document.getElementById('preview'),data=event.data;
              if(event.source!==frame.contentWindow||event.origin!=='null'||!data||data.kind!=='monolith-size'||data.channel!==channel||!Number.isFinite(data.height))return;
              const height=Math.max(80,Math.min(2000,data.height));
              window.chrome?.webview?.postMessage({kind:'monolith-size',channel,height});
            });})();
            """.Replace("CHANNEL", System.Text.Json.JsonSerializer.Serialize(resizeChannel), StringComparison.Ordinal) + "</script>";
        return "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; frame-src 'self' about:; img-src data: blob:; style-src 'unsafe-inline'; font-src data:; script-src "+(interactive?"'unsafe-inline'":"'none'")+"; connect-src 'none'; base-uri 'none'; form-action 'none'\"><style>html,body,iframe{width:100%;height:100%;border:0;margin:0}body{background:"+color+"}iframe{visibility:"+(interactive?"hidden":"visible")+"}</style></head><body><iframe id='preview' title='Aperçu de la réponse' sandbox='"+(interactive?"allow-scripts":"")+"' srcdoc='"+content+"'></iframe>"+reveal+relay+"</body></html>";
    }
}
