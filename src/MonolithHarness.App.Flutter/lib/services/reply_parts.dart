import 'package:html/parser.dart' as html;

class ReplyPart {
  final String text;
  final bool isHtml;
  const ReplyPart(this.text, {this.isHtml = false});
}

List<ReplyPart> replyParts(String text) {
  final result = <ReplyPart>[];
  final lines = text.split('\n');
  var plain = <String>[];
  void flush() {
    if (plain.isNotEmpty) {
      result.addAll(_rawParts(plain.join('\n')));
      plain = [];
    }
  }

  for (var i = 0; i < lines.length; i++) {
    final start = RegExp(r'^ {0,3}(`{3,}|~{3,})([^\r]*)$').firstMatch(lines[i]);
    if (start == null) {
      plain.add(lines[i]);
      continue;
    }
    final marker = start[1]!;
    final lang = start[2]!.trim().toLowerCase();
    var end = i + 1;
    final closing = RegExp(
      '^ {0,3}${RegExp.escape(marker[0])}{${marker.length},}\\s*\$',
    );
    while (end < lines.length && !closing.hasMatch(lines[end])) {
      end++;
    }
    if (lang == 'html' || lang == 'htm') {
      flush();
      result.add(ReplyPart(lines.sublist(i + 1, end).join('\n'), isHtml: true));
    } else {
      flush();
      result.add(
        ReplyPart(
          lines.sublist(i, (end + 1).clamp(0, lines.length)).join('\n'),
        ),
      );
    }
    i = end;
  }
  flush();
  return result;
}

List<ReplyPart> _rawParts(String text) {
  final parts = <ReplyPart>[];
  var at = 0;
  while (at < text.length) {
    final open = RegExp(
      r'<!doctype\s+html\b|<html(?:\s|>)',
      caseSensitive: false,
    ).firstMatch(text.substring(at));
    if (open == null) {
      parts.add(ReplyPart(text.substring(at)));
      break;
    }
    final begin = at + open.start;
    if (begin > at) {
      parts.add(ReplyPart(text.substring(at, begin)));
    }
    var pos = begin;
    int? end;
    final token = RegExp(
      r'<!--[\s\S]*?-->|<(script|style|textarea|title)\b[^>]*>[\s\S]*?</\1\s*>|</html\s*>',
      caseSensitive: false,
    );
    for (final match in token.allMatches(text, pos)) {
      if (match[0]!.toLowerCase().startsWith('</html')) {
        end = match.end;
        break;
      }
    }
    end ??= text.length;
    parts.add(ReplyPart(text.substring(begin, end), isHtml: true));
    at = end;
  }
  return parts;
}

String previewText(String source) {
  final doc = html.parse(
    source.length > 450000 ? source.substring(0, 450000) : source,
  );
  for (final node in doc.querySelectorAll(
    'script,style,iframe,object,embed,link,meta,base,img,input,canvas',
  )) {
    node.remove();
  }
  return (doc.body?.outerHtml ?? '<p>Aperçu HTML</p>').substring(
    0,
    (doc.body?.outerHtml.length ?? 20).clamp(0, 20000),
  );
}

String sandboxDocument(
  String source, {
  required bool dark,
  required bool tailwind,
  required bool charts,
  String tailwindScript = '',
  String chartScript = '',
}) {
  if (source.length > 450000) {
    throw const FormatException('Page HTML limitée à 450 000 caractères.');
  }
  final doc = html.parse(
    source.replaceAll('<![CDATA[', '').replaceAll(']]>', ''),
  );
  for (final node in doc.querySelectorAll(
    'iframe,object,embed,base,link,meta[http-equiv],script[src]',
  )) {
    node.remove();
  }
  for (final img in doc.querySelectorAll('img')) {
    if (!(img.attributes['src'] ?? '').startsWith('data:image/')) {
      img.attributes.remove('src');
    }
    img.attributes.remove('srcset');
  }
  final head = doc.head!;
  final policy = html.parseFragment(
    '''<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline' 'unsafe-eval'; style-src 'unsafe-inline'; img-src data:; font-src data:; connect-src 'none'; frame-src 'none'; object-src 'none'; form-action 'none'; base-uri 'none'"><meta name="viewport" content="width=device-width, initial-scale=1"><style>html{color-scheme:${dark ? 'dark' : 'light'};background:${dark ? '#111713' : '#f7fbf7'};color:${dark ? '#e3ebe2' : '#192019'}}body{margin:0;min-height:100vh;font-family:system-ui,sans-serif}*,*::before,*::after{box-sizing:border-box}</style>''',
  );
  head.nodes.insertAll(0, policy.nodes.toList());
  final scripts = html.parseFragment('<script></script><script></script>');
  if (charts && chartScript.isNotEmpty) {
    final script = scripts.nodes[0];
    script.text = chartScript.replaceAll(
      RegExp('</script', caseSensitive: false),
      r'<\/script',
    );
    head.nodes.insert(1, script);
  }
  if (tailwind && tailwindScript.isNotEmpty) {
    final script = html.parseFragment('<script></script>').nodes.first;
    script.text = tailwindScript.replaceAll(
      RegExp('</script', caseSensitive: false),
      r'<\/script',
    );
    head.nodes.insert(1, script);
  }
  final root = doc.documentElement!;
  if (dark) {
    root.classes.add('dark');
  } else {
    root.classes.remove('dark');
  }
  return '<!doctype html>${root.outerHtml}';
}
