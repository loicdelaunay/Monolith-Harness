import '../domain/models.dart';
import '../data/store.dart';
import '../data/documents.dart';
import 'public_web.dart';

class Tools {
  final AppSettings settings;
  final Store store;
  final Documents documents;
  final int chat, project;
  final Future<bool> Function(String, Json) approve;
  final Future<Json> Function() information;
  final PublicWeb web;
  bool cancelled = false;
  Tools(
    this.settings,
    this.store,
    this.documents,
    this.project,
    this.chat,
    this.approve,
    this.information, {
    PublicWeb? web,
  }) : web = web ?? PublicWeb();
  final _schemas = <String, Json>{};
  List<Json> get definitions {
    _schemas.clear();
    void add(
      String name,
      String description,
      Map<String, String> fields, [
      Set<String> optional = const {},
    ]) {
      _schemas[name] = {
        'type': 'function',
        'function': {
          'name': name,
          'description': description,
          'parameters': {
            'type': 'object',
            'properties': {
              for (final f in fields.entries) f.key: {'type': f.value},
            },
            'required': fields.keys
                .where((k) => !optional.contains(k))
                .toList(),
            'additionalProperties': false,
          },
        },
      };
    }

    if (settings.skills.contains('informations')) {
      add('device_information', 'Current enabled device information', {});
    }
    if (settings.skills.contains('web') && settings.network) {
      add('web_search', 'Find public HTTPS pages', {'query': 'string'});
      add(
        'web_read',
        'Read public page text with line ranges',
        {'url': 'string', 'start_line': 'integer', 'line_count': 'integer'},
        {'start_line', 'line_count'},
      );
    }
    if (settings.skills.contains('sources')) {
      add('list_sources', 'List private conversation documents', {});
      add(
        'read_source',
        'Read a private file with numbered lines',
        {'path': 'string', 'start_line': 'integer', 'line_count': 'integer'},
        {'start_line', 'line_count'},
      );
      add('grep_sources', 'Find literal text in private documents', {
        'query': 'string',
      });
      if (settings.fileWrites) {
        add(
          'write_source',
          'Create or replace a private document; Android original is unchanged',
          {'path': 'string', 'content': 'string'},
        );
        add(
          'edit_source',
          'Replace exact text occurring once in a private document',
          {'path': 'string', 'old_text': 'string', 'new_text': 'string'},
        );
      }
    }
    if (settings.skills.contains('memory')) {
      add(
        'memory_search',
        'Find visible durable facts',
        {'query': 'string'},
        {'query'},
      );
      add(
        'memory_save',
        'Save approved durable fact to conversation/project/shared scope',
        {'scope': 'string', 'content': 'string'},
      );
      add('memory_delete', 'Delete a visible memory by ID', {'id': 'integer'});
    }
    return _schemas.values.toList();
  }

  void check() {
    if (cancelled) throw const FormatException('Action interrompue.');
  }

  Future<Json> execute(String name, Json args) async {
    check();
    if (!_schemas.containsKey(name)) {
      throw const FormatException('Outil indisponible ou désactivé.');
    }
    if (settings.approval == ApprovalMode.deny) {
      throw const FormatException(
        'Action refusée : Refuser tout. Ne pas contourner ce refus.',
      );
    }
    if (settings.approval == ApprovalMode.ask && !await approve(name, args)) {
      throw const FormatException(
        'Action refusée par l’utilisateur. Ne pas contourner ce refus.',
      );
    }
    check();
    String text(String field) => args[field] is String
        ? args[field] as String
        : throw FormatException('Argument $field requis.');
    int number(String field, int fallback) =>
        (args[field] as num?)?.toInt() ?? fallback;
    switch (name) {
      case 'device_information':
        return information();
      case 'web_search':
        return web.search(text('query'));
      case 'web_read':
        return web.read(
          text('url'),
          number('start_line', 1).clamp(1, 1 << 30),
          number('line_count', 60),
        );
      case 'list_sources':
        return {'files': await documents.list()};
      case 'read_source':
        final value = await documents.read(text('path'));
        final lines = value.split('\n');
        final start = number('start_line', 1).clamp(1, 1 << 30);
        final count = number('line_count', 80).clamp(1, 120);
        final output = lines.skip(start - 1).take(count).toList();
        final result = [
          for (var i = 0; i < output.length; i++) '${start + i}: ${output[i]}',
        ].join('\n');
        return {
          'path': text('path'),
          'line_count': lines.length,
          'text': result.substring(0, result.length.clamp(0, 18000)),
          'truncated': result.length > 18000,
        };
      case 'grep_sources':
        final query = text('query');
        if (query.isEmpty || query.length > 500) {
          throw const FormatException(
            'Recherche de 1 à 500 caractères requise.',
          );
        }
        final hits = <Json>[];
        for (final file in await documents.list()) {
          check();
          final lines = (await documents.read(file['path'])).split('\n');
          for (var i = 0; i < lines.length && hits.length < 40; i++) {
            if (lines[i].toLowerCase().contains(query.toLowerCase())) {
              hits.add({
                'path': file['path'],
                'line': i + 1,
                'text': lines[i].substring(0, lines[i].length.clamp(0, 400)),
              });
            }
          }
          if (hits.length >= 40) break;
        }
        return {'matches': hits, 'truncated': hits.length >= 40};
      case 'write_source':
        await documents.write(text('path'), text('content'));
        return {
          'saved': true,
          'path': text('path'),
          'original_modified': false,
        };
      case 'edit_source':
        final value = await documents.read(text('path'));
        final old = text('old_text');
        final index = value.indexOf(old);
        if (old.isEmpty ||
            index < 0 ||
            value.indexOf(old, index + old.length) >= 0) {
          throw const FormatException(
            'Le texte doit correspondre exactement une fois.',
          );
        }
        await documents.write(
          text('path'),
          value.replaceRange(index, index + old.length, text('new_text')),
        );
        return {
          'saved': true,
          'path': text('path'),
          'original_modified': false,
        };
      case 'memory_search':
        return {
          'memories': await store.memories(
            project,
            chat,
            settings.sharedMemory,
            args['query'] as String? ?? '',
          ),
        };
      case 'memory_save':
        return {
          'id': await store.saveMemory(
            project,
            chat,
            settings.sharedMemory,
            text('scope'),
            text('content'),
          ),
          'saved': true,
        };
      case 'memory_delete':
        await store.deleteMemory(
          project,
          chat,
          settings.sharedMemory,
          number('id', 0),
        );
        return {'deleted': true};
      default:
        throw const FormatException('Outil indisponible.');
    }
  }

  void close() {
    cancelled = true;
    web.close();
  }
}
