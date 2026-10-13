using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MonolithHarness.App;

/// <summary>Bounded native fallback for common flowcharts and sequence diagrams; never runs scripts.</summary>
internal static class NativeMermaidDiagram
{
    sealed class Node(string id)
    {
        public string Id = id, Label = id, Shape = "rectangle";
    }
    sealed record Edge(string From, string To, string Label, bool Arrow = true, bool Dashed = false, bool ReverseArrow = false);
    static readonly Regex connector = new(@"^\s*(?<arrow><-->|-->|---|==>|-\.->|--\s+(?<text>.*?)\s+-->)(?:\s*\|(?<label>[^|]*)\|)?\s*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(75));
    static readonly Regex participant = new(@"^(?:participant|actor)\s+(?<id>[\w-]+)(?:\s+as\s+(?<label>.+))?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(75));
    static readonly Regex message = new(@"^(?<from>[\w-]+)\s*(?<arrow>--?>>|--?>)\s*[+-]?(?<to>[\w-]+)\s*:\s*(?<label>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(75));
    const double NodeWidth = 180, NodeHeight = 72, Gap = 64, Padding = 28;

    internal static FrameworkElement? TryCreate(string source, Func<FrameworkElement> sourceView)
    {
        if (source.Length > 20000) return null;
        try
        {
            var statements = Statements(source).Where(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal)).ToArray();
            if (statements.Length == 0) return null;
            var nodes = new Dictionary<string, Node>(StringComparer.Ordinal); var edges = new List<Edge>();
            var header = statements[0]; Canvas? canvas;
            if (header.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) || header.StartsWith("graph ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in statements.Skip(1))
                {
                    if (line.StartsWith("subgraph ", StringComparison.Ordinal) || line == "end" ||
                        new[] { "classDef ", "class ", "style ", "linkStyle ", "direction ", "click " }.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal))) continue;
                    int position = 0;
                    var from = ReadNode(line, ref position, nodes); if (from == null) return null;
                    while (position < line.Length)
                    {
                        var tail = line[position..].Trim();
                        if (tail.Length == 0 || tail.StartsWith(":::", StringComparison.Ordinal)) break;
                        var match = connector.Match(line[position..]); if (!match.Success) return null;
                        position += match.Length;
                        var to = ReadNode(line, ref position, nodes); if (to == null) return null;
                        var arrow = match.Groups["arrow"].Value;
                        edges.Add(new(from.Id, to.Id, Clean(match.Groups["label"].Success ? match.Groups["label"].Value : match.Groups["text"].Value), arrow.Contains('>'), arrow.Contains('.'), arrow.Contains('<')));
                        from = to;
                    }
                    if (nodes.Count > 48 || edges.Count > 96) return null;
                }
                canvas = Flow(nodes, edges, header.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "TD");
            }
            else if (header.Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in statements.Skip(1))
                {
                    if (line == "autonumber" || line.StartsWith("activate ", StringComparison.Ordinal) || line.StartsWith("deactivate ", StringComparison.Ordinal)) continue;
                    var declared = participant.Match(line);
                    if (declared.Success)
                    {
                        var item = Get(nodes, declared.Groups["id"].Value);
                        if (declared.Groups["label"].Success) item.Label = Clean(declared.Groups["label"].Value);
                        continue;
                    }
                    var call = message.Match(line); if (!call.Success) return null;
                    var from = Get(nodes, call.Groups["from"].Value); var to = Get(nodes, call.Groups["to"].Value);
                    edges.Add(new(from.Id, to.Id, Clean(call.Groups["label"].Value), true, call.Groups["arrow"].Value.StartsWith("--", StringComparison.Ordinal)));
                    if (nodes.Count > 16 || edges.Count > 96) return null;
                }
                canvas = Sequence(nodes, edges);
            }
            else return null;
            if (canvas == null || nodes.Count == 0) return null;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = UiText.Resolve("Diagramme Mermaid · aperçu natif simplifié", "Mermaid diagram · simplified native preview"), Foreground = FluentDesign.Secondary, FontSize = 11 });
            panel.Children.Add(new ScrollViewer { Content = canvas, MaxHeight = 600, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalAlignment = HorizontalAlignment.Stretch });
            panel.Children.Add(new Expander { Header = UiText.Resolve("Voir la source Mermaid", "View Mermaid source"), Content = sourceView(), HorizontalAlignment = HorizontalAlignment.Stretch });
            return FluentDesign.Surface(panel, 12);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException or OverflowException) { return null; }
    }
    static IEnumerable<string> Statements(string source)
    {
        int start = 0, depth = 0; bool quoted = false;
        for (int index = 0; index < source.Length; index++)
        {
            var value = source[index];
            if (value == '"' && (index == 0 || source[index - 1] != '\\')) quoted = !quoted;
            if (!quoted)
            {
                if (value is '[' or '(' or '{') depth++;
                else if (value is ']' or ')' or '}') depth = Math.Max(0, depth - 1);
                if ((value == ';' && depth == 0) || value is '\r' or '\n')
                { yield return source[start..index].Trim(); start = index + 1; }
            }
        }
        if (start < source.Length) yield return source[start..].Trim();
    }
    static string Clean(string value)
    {
        value = value.Trim().Trim('"', '`');
        value = Regex.Replace(value, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(75));
        return WebUtility.HtmlDecode(Regex.Replace(value, @"<[^>]{0,200}>", "", RegexOptions.None, TimeSpan.FromMilliseconds(75)));
    }
    static Node Get(Dictionary<string, Node> nodes, string id)
    {
        if (!nodes.TryGetValue(id, out var item)) nodes[id] = item = new(id);
        return item;
    }
    static Node? ReadNode(string text, ref int position, Dictionary<string, Node> nodes)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        int beginning = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '-'))
        {
            if (text[position] == '-' && position + 1 < text.Length && text[position + 1] is '-' or '>') break;
            position++;
        }
        if (position == beginning) return null;
        var node = Get(nodes, text[beginning..position]);
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        if (position < text.Length && text[position] is '[' or '(' or '{')
        {
            var opening = text[position++]; var closing = opening == '[' ? ']' : opening == '(' ? ')' : '}';
            int start = position, depth = 1; bool quoted = false;
            while (position < text.Length && depth > 0)
            {
                var value = text[position++];
                if (value == '"' && (position < 2 || text[position - 2] != '\\')) quoted = !quoted;
                if (quoted) continue;
                if (value == opening) depth++;
                else if (value == closing) depth--;
            }
            if (depth != 0) return null;
            var label = text[start..(position - 1)];
            while (label.Length >= 2 && label[0] == opening && label[^1] == closing) label = label[1..^1];
            node.Label = Clean(label);
            node.Shape = opening == '{' ? "diamond" : opening == '(' ? "round" : "rectangle";
        }
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        if (text.AsSpan(position).StartsWith(":::"))
        {
            position += 3;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '-')) position++;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
        return node;
    }
    static Canvas? Flow(Dictionary<string, Node> nodes, List<Edge> edges, string direction)
    {
        if (nodes.Count == 0) return null;
        var incoming = nodes.Keys.ToDictionary(id => id, id => edges.Count(edge => edge.To == id && edge.From != id));
        var rank = nodes.Keys.ToDictionary(id => id, _ => 0);
        var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = new HashSet<string>();
        while (queue.TryDequeue(out var id))
        {
            if (!visited.Add(id)) continue;
            foreach (var edge in edges.Where(edge => edge.From == id && edge.To != id))
            {
                rank[edge.To] = Math.Max(rank[edge.To], rank[id] + 1);
                if (--incoming[edge.To] == 0) queue.Enqueue(edge.To);
            }
        }
        // Cycles are kept visible, without repeatedly growing the diagram.
        foreach (var id in nodes.Keys.Where(id => !visited.Contains(id))) rank[id] = 0;
        bool horizontal = direction.Equals("LR", StringComparison.OrdinalIgnoreCase) || direction.Equals("RL", StringComparison.OrdinalIgnoreCase);
        bool reverse = direction.Equals("BT", StringComparison.OrdinalIgnoreCase) || direction.Equals("RL", StringComparison.OrdinalIgnoreCase);
        var rows = nodes.Values.GroupBy(node => rank[node.Id]).OrderBy(group => group.Key).ToArray();
        var maximum = rows.Max(row => row.Count()); var levels = rank.Values.Max() + 1;
        var canvas = new Canvas { Width = Padding * 2 + (horizontal ? levels * (NodeWidth + Gap) : maximum * (NodeWidth + Gap)),
            Height = Padding * 2 + (horizontal ? maximum * (NodeHeight + Gap) : levels * (NodeHeight + Gap)) };
        var positions = new Dictionary<string, Point>();
        foreach (var row in rows)
        {
            int index = 0;
            foreach (var node in row)
            {
                var level = reverse ? levels - 1 - row.Key : row.Key;
                positions[node.Id] = horizontal ? new(Padding + level * (NodeWidth + Gap), Padding + index * (NodeHeight + Gap))
                    : new(Padding + index * (NodeWidth + Gap), Padding + level * (NodeHeight + Gap));
                index++;
            }
        }
        foreach (var edge in edges)
        {
            var a = positions[edge.From]; var b = positions[edge.To];
            var start = horizontal ? new Point(a.X + (reverse ? 0 : NodeWidth), a.Y + NodeHeight / 2) : new Point(a.X + NodeWidth / 2, a.Y + (reverse ? 0 : NodeHeight));
            var end = horizontal ? new Point(b.X + (reverse ? NodeWidth : 0), b.Y + NodeHeight / 2) : new Point(b.X + NodeWidth / 2, b.Y + (reverse ? NodeHeight : 0));
            var points = new List<Point> { start };
            if (edge.From == edge.To)
            { points.Add(new(a.X + NodeWidth + 24, start.Y)); points.Add(new(a.X + NodeWidth + 24, a.Y - 16)); points.Add(new(a.X + NodeWidth / 2, a.Y - 16)); end = new(a.X + NodeWidth / 2, a.Y); }
            else if (horizontal) { var x = (start.X + end.X) / 2; points.Add(new(x, start.Y)); points.Add(new(x, end.Y)); }
            else { var y = (start.Y + end.Y) / 2; points.Add(new(start.X, y)); points.Add(new(end.X, y)); }
            points.Add(end); DrawEdge(canvas, points, edge.Arrow, edge.Dashed, edge.ReverseArrow);
            if (edge.Label.Length > 0) Caption(canvas, edge.Label, (start.X + end.X) / 2 - 64, (start.Y + end.Y) / 2 - 18, 128);
        }
        foreach (var node in nodes.Values) DrawNode(canvas, node, positions[node.Id]);
        return canvas;
    }
    static Canvas? Sequence(Dictionary<string, Node> nodes, List<Edge> messages)
    {
        if (nodes.Count == 0) return null;
        var indexes = nodes.Keys.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        var canvas = new Canvas { Width = Padding * 2 + nodes.Count * (NodeWidth + 40), Height = 120 + messages.Count * 64 };
        foreach (var node in nodes.Values)
        {
            var x = Padding + indexes[node.Id] * (NodeWidth + 40);
            canvas.Children.Add(new Line { X1 = x + NodeWidth / 2, X2 = x + NodeWidth / 2, Y1 = NodeHeight, Y2 = canvas.Height - 12,
                Stroke = FluentDesign.Secondary, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 4 } });
            DrawNode(canvas, node, new(x, 4));
        }
        int index = 0;
        foreach (var call in messages)
        {
            var from = Padding + indexes[call.From] * (NodeWidth + 40) + NodeWidth / 2;
            var to = Padding + indexes[call.To] * (NodeWidth + 40) + NodeWidth / 2;
            var y = 108 + index++ * 64;
            var points = call.From == call.To ? new List<Point> { new(from, y), new(from + 50, y), new(from + 50, y + 24), new(to, y + 24) }
                : new List<Point> { new(from, y), new(to, y) };
            DrawEdge(canvas, points, true, call.Dashed);
            Caption(canvas, call.Label, Math.Min(from, to) + 4, y - 28, Math.Max(160, Math.Abs(to - from) - 8));
        }
        return canvas;
    }
    static void DrawNode(Canvas canvas, Node node, Point at)
    {
        if (node.Shape == "diamond")
        {
            var diamond = new Polygon { Points = new PointCollection { new(0, NodeHeight / 2), new(NodeWidth / 2, 0), new(NodeWidth, NodeHeight / 2), new(NodeWidth / 2, NodeHeight) },
                Fill = FluentDesign.Card, Stroke = FluentDesign.Resource("AccentTextFillColorPrimaryBrush"), StrokeThickness = 1.5 };
            Canvas.SetLeft(diamond, at.X); Canvas.SetTop(diamond, at.Y); canvas.Children.Add(diamond);
        }
        var label = new TextBlock { Text = node.Label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5, Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
        var box = new Border { Child = label, Width = NodeWidth, Height = NodeHeight, Padding = new(node.Shape == "diamond" ? 34 : 10, 6, node.Shape == "diamond" ? 34 : 10, 6),
            Background = node.Shape == "diamond" ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : FluentDesign.Card,
            BorderBrush = FluentDesign.Resource("AccentTextFillColorPrimaryBrush"), BorderThickness = node.Shape == "diamond" ? new(0) : new(1.5), CornerRadius = new(node.Shape == "round" ? 28 : 8) };
        ToolTipService.SetToolTip(box, node.Label); Canvas.SetLeft(box, at.X); Canvas.SetTop(box, at.Y); canvas.Children.Add(box);
    }
    static void DrawEdge(Canvas canvas, List<Point> points, bool arrow, bool dashed, bool reverseArrow = false)
    {
        var line = new Polyline { Points = new PointCollection(), Stroke = FluentDesign.Secondary, StrokeThickness = 1.5 };
        foreach (var point in points) line.Points.Add(point);
        if (dashed) line.StrokeDashArray = new DoubleCollection { 3, 3 };
        canvas.Children.Add(line);
        if (points.Count < 2) return;
        void Head(Point end, Point before)
        {
            var angle = Math.Atan2(end.Y - before.Y, end.X - before.X);
            canvas.Children.Add(new Polygon { Fill = FluentDesign.Secondary, Points = new PointCollection { end,
                new(end.X - 9 * Math.Cos(angle - .45), end.Y - 9 * Math.Sin(angle - .45)), new(end.X - 9 * Math.Cos(angle + .45), end.Y - 9 * Math.Sin(angle + .45)) } });
        }
        if (arrow) Head(points[^1], points[^2]);
        if (reverseArrow) Head(points[0], points[1]);
    }
    static void Caption(Canvas canvas, string text, double x, double y, double width)
    {
        var caption = new Border { Background = FluentDesign.Card, Padding = new(4, 1, 4, 1), MaxWidth = width,
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxLines = 2, FontSize = 11, Foreground = FluentDesign.Primary } };
        Canvas.SetLeft(caption, Math.Max(0, x)); Canvas.SetTop(caption, Math.Max(0, y)); canvas.Children.Add(caption);
    }
}