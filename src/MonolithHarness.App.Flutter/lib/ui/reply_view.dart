import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';
import 'package:flutter_widget_from_html_core/flutter_widget_from_html_core.dart';
import 'package:markdown/markdown.dart' as md;
import 'package:url_launcher/url_launcher.dart';

import '../app_state.dart';
import '../domain/models.dart';
import '../services/public_web.dart';
import '../services/reply_parts.dart';
import 'web_preview.dart';

Future<void> openLink(String? target) async {
  if (target == null) return;
  try {
    final uri = publicUri(target);
    await launchUrl(uri, mode: LaunchMode.externalApplication);
  } catch (_) {}
}

class SafeImage extends StatefulWidget {
  final String url;
  final bool network;
  const SafeImage(this.url, {super.key, required this.network});
  @override
  State<SafeImage> createState() => _SafeImageState();
}

class _SafeImageState extends State<SafeImage> {
  late Future<Uint8List> data;
  PublicWeb? web;
  @override
  void initState() {
    super.initState();
    data = load();
  }

  Future<Uint8List> load() async {
    Uint8List bytes;
    if (widget.url.startsWith('data:image/')) {
      final at = widget.url.indexOf(';base64,');
      if (at < 0 || widget.url.length > 12 * 1024 * 1024) {
        throw const FormatException('Image invalide');
      }
      bytes = base64Decode(widget.url.substring(at + 8));
    } else {
      if (!widget.network) {
        throw const FormatException('Images distantes désactivées');
      }
      web = PublicWeb();
      bytes = (await web!.download(widget.url, max: 8 * 1024 * 1024)).$1;
    }
    if (bytes.length > 8 * 1024 * 1024 || imageMime(bytes) == null) {
      throw const FormatException('Image raster non prise en charge');
    }
    return bytes;
  }

  @override
  void didUpdateWidget(SafeImage old) {
    super.didUpdateWidget(old);
    if (old.url != widget.url || old.network != widget.network) {
      web?.close();
      data = load();
    }
  }

  @override
  void dispose() {
    web?.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Uint8List>(
    future: data,
    builder: (c, s) {
      if (s.hasError) {
        return const Padding(
          padding: EdgeInsets.all(12),
          child: Text('Image indisponible'),
        );
      }
      if (!s.hasData) {
        return const SizedBox(
          height: 72,
          child: Center(child: CircularProgressIndicator()),
        );
      }
      return ClipRRect(
        borderRadius: BorderRadius.circular(16),
        child: Image.memory(
          s.data!,
          fit: BoxFit.contain,
          errorBuilder: (_, _, _) => const Text('Image invalide'),
        ),
      );
    },
  );
}

class ReplyView extends StatelessWidget {
  final String text;
  final AppSettings settings;
  final bool complete, selectable;
  const ReplyView(
    this.text, {
    super.key,
    required this.settings,
    this.complete = true,
    this.selectable = true,
  });
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = MarkdownStyleSheet.fromTheme(theme).copyWith(
      p: theme.textTheme.bodyLarge?.copyWith(
        fontSize: settings.textSize,
        height: 1.45,
      ),
      code: TextStyle(
        fontFamily: 'monospace',
        fontSize: settings.textSize - 2,
        color: theme.colorScheme.onSurface,
      ),
      codeblockDecoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHigh,
        borderRadius: BorderRadius.circular(12),
      ),
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final part in replyParts(text))
          if (part.isHtml)
            HtmlReplyCard(part.text, settings: settings, complete: complete)
          else if (part.text.trim().isNotEmpty)
            MarkdownBody(
              data: part.text,
              selectable: selectable,
              styleSheet: style,
              extensionSet: md.ExtensionSet.gitHubFlavored,
              onTapLink: (_, href, _) => openLink(href),
              imageBuilder: (uri, title, alt) =>
                  SafeImage(uri.toString(), network: settings.network),
            ),
      ],
    );
  }
}

class HtmlReplyCard extends StatelessWidget {
  final String source;
  final AppSettings settings;
  final bool complete;
  const HtmlReplyCard(
    this.source, {
    super.key,
    required this.settings,
    required this.complete,
  });
  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card.filled(
      margin: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ListTile(
            leading: const Icon(Icons.web),
            title: const Text('Application web'),
            subtitle: Text(
              complete
                  ? 'HTML · CSS · JavaScript'
                  : 'Document en cours de réception…',
            ),
          ),
          if (complete)
            ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 200),
              child: SingleChildScrollView(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: HtmlWidget(
                  previewText(source),
                  textStyle: TextStyle(color: scheme.onSurface),
                  onTapUrl: (_) => Future.value(true),
                ),
              ),
            ),
          Padding(
            padding: const EdgeInsets.all(12),
            child: FilledButton.icon(
              onPressed: complete
                  ? () => Navigator.of(context).push(
                      MaterialPageRoute(
                        builder: (_) => WebPreview(
                          html: source,
                          settings: settings,
                          dark: Theme.of(context).brightness == Brightness.dark,
                        ),
                      ),
                    )
                  : null,
              icon: const Icon(Icons.open_in_full),
              label: const Text('Ouvrir l’aperçu'),
            ),
          ),
          ExpansionTile(
            title: const Text('Voir le code HTML'),
            children: [
              Align(
                alignment: Alignment.centerRight,
                child: TextButton.icon(
                  onPressed: () =>
                      Clipboard.setData(ClipboardData(text: source)),
                  icon: const Icon(Icons.copy),
                  label: const Text('Copier'),
                ),
              ),
              SizedBox(
                height: 220,
                child: SingleChildScrollView(
                  padding: const EdgeInsets.all(16),
                  child: SelectableText(
                    source,
                    style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 12,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
