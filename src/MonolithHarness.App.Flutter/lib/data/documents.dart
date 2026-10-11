import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:path/path.dart' as p;

class Documents {
  final Directory root, owner;
  Documents(String directory, int chat)
    : root = Directory(p.join(directory, 'documents', 'chat-$chat')),
      owner = Directory(directory);
  Future<void> clear() async {
    if (!await root.exists()) return;
    final base = await owner.resolveSymbolicLinks();
    final target = await root.resolveSymbolicLinks();
    final expected = p.join(base, 'documents', p.basename(root.path));
    if (!p.equals(expected, target) ||
        !p.isWithin(base, target) ||
        await FileSystemEntity.isLink(root.path)) {
      throw const FormatException('Suppression hors du dossier privé refusée.');
    }
    await root.delete(recursive: true);
  }

  Future<File> resolve(String name) async {
    final clean = name.replaceAll('\\', '/');
    if (clean.isEmpty ||
        p.isAbsolute(clean) ||
        clean.contains(':') ||
        clean.contains('\x00') ||
        clean.split('/').any((s) => s == '..' || s.isEmpty)) {
      throw const FormatException('Chemin relatif à la discussion requis.');
    }
    final path = p.normalize(p.join(root.path, clean));
    if (!p.isWithin(root.path, path)) {
      throw const FormatException('Chemin hors discussion.');
    }
    var at = root.path;
    if (await FileSystemEntity.isLink(at)) {
      throw const FormatException('Lien symbolique refusé.');
    }
    for (final part in clean.split('/')) {
      at = p.join(at, part);
      if (await FileSystemEntity.isLink(at)) {
        throw const FormatException('Lien symbolique refusé.');
      }
    }
    return File(path);
  }

  Future<List<Map<String, dynamic>>> list() async {
    if (!await root.exists()) return [];
    final entries = <Map<String, dynamic>>[];
    await for (final item in root.list(recursive: true, followLinks: false)) {
      if (item is File) {
        final name = p.relative(item.path, from: root.path);
        await resolve(name);
        entries.add({'path': name, 'bytes': await item.length()});
      }
    }
    entries.sort(
      (a, b) => (a['path'] as String).compareTo(b['path'] as String),
    );
    return entries;
  }

  Future<String> read(String path) async {
    final file = await resolve(path);
    if (await file.length() > 1024 * 1024) {
      throw const FormatException('Document trop volumineux (1 Mo).');
    }
    return utf8.decode(await file.readAsBytes());
  }

  Future<void> write(String path, String content) async {
    final bytes = utf8.encode(content);
    if (bytes.length > 1024 * 1024) {
      throw const FormatException('Document limité à 1 Mo.');
    }
    final file = await resolve(path);
    await file.parent.create(recursive: true);
    final temp = File('${file.path}.tmp');
    await temp.writeAsBytes(bytes, flush: true);
    await temp.rename(file.path);
  }

  Future<String> import(String name, Uint8List bytes) async {
    if (bytes.length > 1024 * 1024) {
      throw const FormatException('Document limité à 1 Mo.');
    }
    var clean = p
        .basename(name.replaceAll('\\', '/'))
        .replaceAll(RegExp(r'[^\w.\- ]'), '_');
    if (clean.isEmpty || clean == '.' || clean == '..') clean = 'document.txt';
    if (await (await resolve(clean)).exists()) {
      clean =
          '${p.basenameWithoutExtension(clean)}-${DateTime.now().millisecondsSinceEpoch}${p.extension(clean)}';
    }
    String text;
    if (bytes.length >= 2 &&
        ((bytes[0] == 255 && bytes[1] == 254) ||
            (bytes[0] == 254 && bytes[1] == 255))) {
      if (bytes.length.isOdd) throw const FormatException('UTF-16 invalide.');
      final data = ByteData.sublistView(bytes);
      text = String.fromCharCodes([
        for (var i = 2; i < bytes.length; i += 2)
          data.getUint16(i, bytes[0] == 255 ? Endian.little : Endian.big),
      ]);
    } else {
      text = utf8.decode(bytes);
      if (text.startsWith('\ufeff')) text = text.substring(1);
    }
    if (text.contains('\x00')) {
      throw const FormatException('Document binaire non pris en charge.');
    }
    await write(clean, text);
    return clean;
  }
}
