import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:html/parser.dart' as html;

import '../domain/models.dart';

bool publicAddress(InternetAddress address) {
  final b = address.rawAddress;
  if (b.length == 4) {
    return b[0] != 0 &&
        b[0] != 10 &&
        b[0] != 127 &&
        b[0] < 224 &&
        !(b[0] == 169 && b[1] == 254) &&
        !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
        !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) &&
        !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
        !(b[0] == 198 && (b[1] == 18 || b[1] == 19));
  }
  if (b.length == 16) {
    if (b.take(10).every((v) => v == 0) && b[10] == 255 && b[11] == 255) {
      return publicAddress(
        InternetAddress.fromRawAddress(Uint8List.fromList(b.sublist(12))),
      );
    }
    if (b[0] == 0x20 && b[1] == 0x02) {
      return publicAddress(
        InternetAddress.fromRawAddress(Uint8List.fromList(b.sublist(2, 6))),
      );
    }
    if (b[0] == 0x20 &&
        b[1] == 0x01 &&
        ((b[2] == 0 && b[3] == 0) || (b[2] == 0x0d && b[3] == 0xb8))) {
      return false;
    }
    return (b[0] & 0xe0) == 0x20;
  }
  return false;
}

Uri publicUri(String value) {
  final u = Uri.tryParse(value);
  if (u == null ||
      u.scheme != 'https' ||
      u.host.isEmpty ||
      u.userInfo.isNotEmpty ||
      u.port != 443 ||
      u.host == 'localhost' ||
      u.host.endsWith('.local') ||
      u.host.endsWith('.internal')) {
    throw const FormatException('URL HTTPS publique requise.');
  }
  final literal = InternetAddress.tryParse(u.host);
  if (literal != null && !publicAddress(literal)) {
    throw const FormatException('Adresse privée refusée.');
  }
  return u;
}

class PublicWeb {
  final HttpClient client;
  PublicWeb({
    HttpClient? transport,
    Future<List<InternetAddress>> Function(String)? resolveHost,
    Future<ConnectionTask<Socket>> Function(InternetAddress, int)?
    connectSocket,
    Future<Socket> Function(Socket, String)? secureSocket,
  }) : client = transport ?? HttpClient() {
    client.connectionTimeout = const Duration(seconds: 20);
    client.findProxy = (_) => 'DIRECT';
    final lookup = resolveHost ?? ((name) => InternetAddress.lookup(name));
    final connect =
        connectSocket ??
        ((address, port) => Socket.startConnect(address, port));
    final secure =
        secureSocket ??
        ((socket, host) => SecureSocket.secure(socket, host: host));
    client.connectionFactory = (uri, proxyHost, proxyPort) async {
      publicUri(uri.toString());
      final addresses = await lookup(uri.host);
      if (addresses.isEmpty || addresses.any((a) => !publicAddress(a))) {
        throw const FormatException('Adresse réseau non publique refusée.');
      }
      final pending = await connect(addresses.first, uri.port);
      return ConnectionTask.fromSocket(
        pending.socket.then((socket) => secure(socket, uri.host)),
        pending.cancel,
      );
    };
  }
  Future<(Uint8List, String)> download(
    String url, {
    int max = 2 * 1024 * 1024,
  }) async {
    var uri = publicUri(url);
    for (var count = 0; count < 4; count++) {
      final request = await client
          .getUrl(uri)
          .timeout(const Duration(seconds: 20));
      request.followRedirects = false;
      request.headers.set('User-Agent', 'Monolith/1.89');
      final response = await request.close().timeout(
        const Duration(seconds: 30),
      );
      if ([301, 302, 303, 307, 308].contains(response.statusCode)) {
        final location = response.headers.value('location');
        await response.drain<void>().timeout(const Duration(seconds: 10));
        if (location == null) {
          throw const FormatException('Redirection invalide.');
        }
        uri = publicUri(uri.resolve(location).toString());
        continue;
      }
      if (response.statusCode != 200) {
        await response.drain<void>().timeout(const Duration(seconds: 10));
        throw HttpException('HTTP ${response.statusCode}');
      }
      final bytes = BytesBuilder(copy: false);
      await for (final block in response.timeout(const Duration(seconds: 30))) {
        bytes.add(block);
        if (bytes.length > max) {
          throw const FormatException('Réponse réseau trop volumineuse.');
        }
      }
      return (bytes.takeBytes(), response.headers.contentType?.mimeType ?? '');
    }
    throw const FormatException('Trop de redirections.');
  }

  Future<Json> search(String query) async {
    if (query.trim().isEmpty || query.length > 500) {
      throw const FormatException('Recherche de 1 à 500 caractères requise.');
    }
    final url = Uri.https('html.duckduckgo.com', '/html/', {
      'q': query,
    }).toString();
    final data = await download(url);
    final doc = html.parse(utf8.decode(data.$1, allowMalformed: true));
    final found = <Json>[];
    for (final link in doc.querySelectorAll('.result__a')) {
      final uri = Uri.tryParse(link.attributes['href'] ?? '');
      if (uri == null) continue;
      final target = uri.queryParameters['uddg'] ?? uri.toString();
      try {
        publicUri(target);
        found.add({'title': link.text, 'url': target});
      } catch (_) {}
      if (found.length == 8) break;
    }
    if (found.isEmpty) {
      throw const HttpException(
        'Recherche indisponible ou aucun résultat public (la page peut demander un CAPTCHA).',
      );
    }
    return {'query': query, 'results': found};
  }

  Future<Json> read(String url, int start, int count) async {
    final data = await download(url);
    var text = utf8.decode(data.$1, allowMalformed: true);
    if (data.$2.contains('html')) {
      final doc = html.parse(text);
      for (final el in doc.querySelectorAll(
        'script,style,nav,header,footer,noscript',
      )) {
        el.remove();
      }
      text = (doc.body ?? doc.documentElement)?.text ?? '';
    } else if (data.$2.contains('json')) {
      try {
        text = const JsonEncoder.withIndent('  ').convert(jsonDecode(text));
      } catch (_) {}
    } else if (!data.$2.startsWith('text/')) {
      throw const FormatException('Page texte, HTML ou JSON requise.');
    }
    final lines = text
        .replaceAll('\r', '')
        .split('\n')
        .map((s) => s.trim())
        .where((s) => s.isNotEmpty)
        .toList();
    final excerpt = lines
        .skip(start.clamp(1, 1 << 30) - 1)
        .take(count.clamp(1, 120))
        .toList();
    final selected = [
      for (var i = 0; i < excerpt.length; i++) '${start + i}: ${excerpt[i]}',
    ].join('\n');
    return {
      'url': url,
      'line_count': lines.length,
      'text': selected.substring(0, selected.length.clamp(0, 18000)),
      'truncated': selected.length > 18000,
    };
  }

  void close() => client.close(force: true);
}
