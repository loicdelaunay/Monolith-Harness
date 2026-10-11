import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';
import 'package:monolith_harness_app_flutter/domain/models.dart';
import 'package:monolith_harness_app_flutter/data/store.dart';
import 'package:monolith_harness_app_flutter/data/documents.dart';
import 'package:monolith_harness_app_flutter/services/api.dart';
import 'package:monolith_harness_app_flutter/services/tools.dart';
import 'package:monolith_harness_app_flutter/services/public_web.dart';
import 'package:monolith_harness_app_flutter/services/reply_parts.dart';
import 'package:monolith_harness_app_flutter/services/reasoning_activity.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  group('Live reasoning activity', () {
    test('Uses the latest nonempty Markdown step', () {
      expect(
        reasoningActivity(
          'Lecture des fichiers\n\n### **Analyse des dépendances**\n```\n',
        ),
        'Analyse des dépendances',
      );
    });
    test('Follows the latest sentence in a growing paragraph', () {
      expect(
        reasoningActivity('Je lis les fichiers. Je vérifie les modifications.'),
        'Je vérifie les modifications.',
      );
    });
    test('Empty reasoning has an honest fallback', () {
      expect(reasoningActivity(' \n```'), 'Réflexion en cours…');
    });
    test('Bounds large streams and preserves Unicode code points', () {
      final result = reasoningActivity('${'a' * 5000}\n${'💭' * 140}');
      expect(result.runes.length, 120);
      expect(result.endsWith('…'), isTrue);
      expect(result.contains('\ufffd'), isFalse);
    });
  });
  group('Settings', () {
    test(
      'Default asks for every tool',
      () => expect(AppSettings().approval, ApprovalMode.ask),
    );
    test(
      'Unknown approval modes fail closed',
      () => expect(
        AppSettings.fromJson({'approval': 'invalid'}).approval,
        ApprovalMode.deny,
      ),
    );
    test('Limits are bounded', () {
      final s = AppSettings.fromJson({'textSize': 100, 'maxRounds': 100});
      expect(s.textSize, 22);
      expect(s.maxRounds, 16);
    });
    test('Copies are independent', () {
      final s = AppSettings();
      final copy = s.copy();
      copy.skills.clear();
      expect(s.skills, isNotEmpty);
    });
  });
  group('HTML grouping and sandbox', () {
    test('One card for a fenced document with blank lines', () {
      final parts = replyParts(
        'Before\n```html\n<!doctype html><html>\n\n<body>test</body></html>\n```\nAfter',
      );
      expect(parts.where((p) => p.isHtml).length, 1);
      expect(parts.first.text, 'Before');
      expect(parts.last.text, 'After');
    });
    test('Keeps complete raw HTML together', () {
      expect(
        replyParts(
          'Before\n<!doctype html><html><body>\n\nHello</body></html>\nAfter',
        ).where((p) => p.isHtml).length,
        1,
      );
    });
    test('Does not cut on a fake closing HTML in JavaScript', () {
      final p = replyParts(
        '<html><script>let x="</html>";</script><body>OK</body></html>',
      );
      expect(p.length, 1);
      expect(p.single.text, contains('OK'));
    });
    test('Fenced non-HTML examples remain code', () {
      expect(
        replyParts('```text\n<!doctype html><html>example</html>\n```')
            .where((p) => p.isHtml),
        isEmpty,
      );
    });
    test('Partial streamed document stays in one card', () {
      expect(
        replyParts('<!doctype html><html><body>\npartial')
            .where((p) => p.isHtml)
            .length,
        1,
      );
    });
    test('Strips imports, refresh, frames and remote images', () {
      final value = sandboxDocument(
        '<html><head><script src="https://bad.test/x.js"></script><meta http-equiv="refresh" content="1;url=https://bad.test"><link href="x"></head><body><iframe src="x"></iframe><img src="https://bad.test/i"><script>let safe=1;</script></body></html>',
        dark: true,
        tailwind: false,
        charts: false,
      );
      expect(value, contains("connect-src 'none'"));
      expect(value, contains("class=\"dark\""));
      expect(value, contains('let safe=1'));
      expect(value, isNot(contains('<iframe')));
      expect(value, isNot(contains('bad.test')));
    });
    test('Local libraries toggle separately', () {
      final v = sandboxDocument(
        '<p>Hello</p>',
        dark: false,
        tailwind: true,
        charts: false,
        tailwindScript: 'let TW=1;',
        chartScript: 'let CHART=1;',
      );
      expect(v, contains('TW=1'));
      expect(v, isNot(contains('CHART=1')));
    });
    test('Native HTML preview removes scripts and resource requests', () {
      expect(
        previewText(
          '<script>alert(1)</script><img src="private"><h1>Hello</h1>',
        ),
        isNot(contains('private')),
      );
    });
    test(
      'Large HTML is rejected',
      () => expect(
        () => sandboxDocument(
          'x' * 450001,
          dark: false,
          tailwind: false,
          charts: false,
        ),
        throwsFormatException,
      ),
    );
  });
  group('Network and provider endpoint', () {
    for (final bad in [
      'http://example.com',
      'https://localhost',
      'https://127.0.0.1',
      'https://10.0.0.1',
      'https://[::1]',
      'https://example.com:8443',
      'https://user:pass@example.com',
    ]) {
      test(
        'Reject $bad',
        () => expect(() => publicUri(bad), throwsFormatException),
      );
    }
    for (final bad in [
      '127.0.0.1',
      '10.2.3.4',
      '172.16.0.1',
      '192.168.0.1',
      '169.254.1.1',
      '100.64.1.1',
      '::1',
      'fc00::1',
      '::ffff:127.0.0.1',
      '2001:db8::1',
      '2002:7f00:1::',
    ]) {
      test(
        'Private/reserved address $bad',
        () => expect(publicAddress(InternetAddress(bad)), false),
      );
    }
    test('Public IPv4 and IPv6', () {
      expect(publicAddress(InternetAddress('8.8.8.8')), true);
      expect(publicAddress(InternetAddress('2606:4700:4700::1111')), true);
      expect(publicAddress(InternetAddress('172.64.1.1')), true);
    });
    test(
      'API v1 routes',
      () => expect(
        endpoint('https://api.example.com/v1/', 'models').toString(),
        'https://api.example.com/v1/models',
      ),
    );
    test(
      'Reject credential URLs',
      () => expect(
        () => endpoint('https://key@example.com/v1', 'models'),
        throwsFormatException,
      ),
    );
    test(
      'Secrets are redacted in errors',
      () => expect(
        safeError('secret sk-abcdefghijklmnopqrstuvwxyz', 'secret'),
        isNot(contains('abcdefghijklmnopqrstuvwxyz')),
      ),
    );
  });

  group('Pinned HTTPS transport', () {
    test(
      'TCP is upgraded to TLS with the original host and cancellation',
      () async {
        final transport = CapturedClient();
        final raw = StubSocket(), secured = StubSocket();
        var tlsCalls = 0, cancelled = false;
        final web = PublicWeb(
          transport: transport,
          resolveHost: (host) async => [InternetAddress('8.8.8.8')],
          connectSocket: (address, port) async {
            expect(address.address, '8.8.8.8');
            expect(port, 443);
            return ConnectionTask.fromSocket(
              Future.value(raw),
              () => cancelled = true,
            );
          },
          secureSocket: (socket, host) async {
            expect(socket, same(raw));
            expect(host, 'example.org');
            tlsCalls++;
            return secured;
          },
        );
        final task = await transport.factory(
          Uri.parse('https://example.org/test'),
          null,
          null,
        );
        expect(await task.socket, same(secured));
        expect(tlsCalls, 1);
        task.cancel();
        expect(cancelled, isTrue);
        web.close();
      },
    );
    test('Mixed public/private DNS fails before connecting', () async {
      final transport = CapturedClient();
      var connected = false;
      final web = PublicWeb(
        transport: transport,
        resolveHost: (host) async => [
          InternetAddress('8.8.8.8'),
          InternetAddress('10.0.0.1'),
        ],
        connectSocket: (address, port) async {
          connected = true;
          throw StateError('Must not connect');
        },
      );
      await expectLater(
        transport.factory(Uri.parse('https://example.org'), null, null),
        throwsFormatException,
      );
      expect(connected, isFalse);
      web.close();
    });
    test('TLS errors propagate without a plaintext fallback', () async {
      final transport = CapturedClient();
      final web = PublicWeb(
        transport: transport,
        resolveHost: (host) async => [InternetAddress('8.8.8.8')],
        connectSocket: (address, port) async =>
            ConnectionTask.fromSocket(Future.value(StubSocket()), () {}),
        secureSocket: (socket, host) async =>
            throw const HandshakeException('Untrusted certificate'),
      );
      final task = await transport.factory(
        Uri.parse('https://example.org'),
        null,
        null,
      );
      await expectLater(task.socket, throwsA(isA<HandshakeException>()));
      web.close();
    });
  });
  group('Streaming', () {
    final p = ProviderConfig(
      name: 'Fixture',
      url: 'https://api.example.com/v1',
      model: 'fixture',
    );
    test('SSE tolerates fragmented Unicode, reasoning and usage', () async {
      final body =
          'data: ${jsonEncode({
            'choices': [
              {
                'delta': {'reasoning_content': 'Think'},
              },
            ],
          })}\n\ndata: ${jsonEncode({
            'choices': [
              {
                'delta': {'content': 'Réponse'},
              },
            ],
          })}\n\ndata: ${jsonEncode({
            'choices': [],
            'usage': {'prompt_tokens': 10, 'completion_tokens': 3},
          })}\n\ndata: [DONE]\n\n';
      final bytes = utf8.encode(body);
      final client = MockClient.streaming(
        (r, _) async => http.StreamedResponse(
          Stream.fromIterable([
            for (var i = 0; i < bytes.length; i += 3)
              bytes.sublist(i, (i + 3).clamp(0, bytes.length)),
          ]),
          200,
          headers: {'content-type': 'text/event-stream'},
        ),
      );
      final api = ApiClient(client);
      final result = await api.complete(p, 'fixture', [], [], (_, _) {});
      expect(result.message['content'], 'Réponse');
      expect(result.reasoning, 'Think');
      expect(result.outputTokens, 3);
      api.close();
    });
    test('Accumulates tool arguments', () async {
      final body = [
        {
          'choices': [
            {
              'delta': {
                'tool_calls': [
                  {
                    'index': 0,
                    'id': 'call1',
                    'function': {
                      'name': 'read_source',
                      'arguments': '{"path":',
                    },
                  },
                ],
              },
            },
          ],
        },
        {
          'choices': [
            {
              'delta': {
                'tool_calls': [
                  {
                    'index': 0,
                    'function': {'arguments': '"file.txt"}'},
                  },
                ],
              },
            },
          ],
        },
      ].map((v) => 'data: ${jsonEncode(v)}\n\n').join();
      final api = ApiClient(
        MockClient(
          (r) async => http.Response(
            body,
            200,
            headers: {'content-type': 'text/event-stream'},
          ),
        ),
      );
      final result = await api.complete(p, 'fixture', [], [], (_, _) {});
      expect(
        result.message['tool_calls'][0]['function']['arguments'],
        '{"path":"file.txt"}',
      );
      api.close();
    });
    test('Retries without unsupported stream_options', () async {
      var n = 0;
      final api = ApiClient(
        MockClient((r) async {
          n++;
          if (n == 1) return http.Response('unsupported stream_options', 400);
          expect(jsonDecode(r.body), isNot(contains('stream_options')));
          return http.Response(
            jsonEncode({
              'choices': [
                {
                  'message': {'content': 'OK'},
                },
              ],
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );
      expect(
        (await api.complete(
          p,
          'fixture',
          [],
          [],
          (_, _) {},
        )).message['content'],
        'OK',
      );
      expect(n, 2);
      api.close();
    });
  });
  group('Private documents, memory and approvals', () {
    late Directory temp;
    late Store store;
    late int project, chat;
    setUp(() async {
      temp = await Directory.systemTemp.createTemp('monolith-test-');
      store = await Store.open(
        inMemoryDatabasePath,
        factory: databaseFactoryFfi,
      );
      project = (await store.projects()).first.id;
      chat = await store.createChat(project);
    });
    tearDown(() async {
      await store.db.close();
      await temp.delete(recursive: true);
    });
    test('Rejects traversal and absolute document paths', () async {
      final docs = Documents(temp.path, chat);
      for (final path in [
        '../bad',
        '/absolute',
        'C:/bad',
        'x/../../bad',
        'x\\..\\bad',
      ]) {
        await expectLater(docs.resolve(path), throwsFormatException);
      }
    });
    test('Round trips UTF-8 and UTF-16 imports', () async {
      final d = Documents(temp.path, chat);
      await d.import('test.txt', Uint8List.fromList(utf8.encode('Bonjour é')));
      expect(await d.read('test.txt'), 'Bonjour é');
      await d.import('utf16.txt', Uint8List.fromList([255, 254, 65, 0, 66, 0]));
      expect(await d.read('utf16.txt'), 'AB');
    });
    test('Conversation files stay isolated', () async {
      await Documents(temp.path, chat).write('a.txt', 'private');
      expect(await Documents(temp.path, chat + 1).list(), isEmpty);
    });
    test('Ask can refuse before writes', () async {
      final s = AppSettings();
      var asked = 0;
      final tools = Tools(s, store, Documents(temp.path, chat), project, chat, (
        n,
        a,
      ) async {
        asked++;
        return false;
      }, () async => {});
      tools.definitions;
      await expectLater(
        tools.execute('write_source', {'path': 'a.txt', 'content': 'x'}),
        throwsFormatException,
      );
      expect(asked, 1);
      expect(await Documents(temp.path, chat).list(), isEmpty);
      tools.close();
    });
    test('Deny never requests approval or executes', () async {
      final s = AppSettings()..approval = ApprovalMode.deny;
      final tools = Tools(
        s,
        store,
        Documents(temp.path, chat),
        project,
        chat,
        (n, a) async => throw Exception('should not ask'),
        () async => {},
      );
      tools.definitions;
      await expectLater(
        tools.execute('device_information', {}),
        throwsFormatException,
      );
      tools.close();
    });
    test('Allow writes without prompts but obeys path bounds', () async {
      final s = AppSettings()..approval = ApprovalMode.allow;
      final tools = Tools(
        s,
        store,
        Documents(temp.path, chat),
        project,
        chat,
        (n, a) async => throw Exception('should not ask'),
        () async => {},
      );
      tools.definitions;
      await tools.execute('write_source', {'path': 'a.txt', 'content': 'old'});
      await tools.execute('edit_source', {
        'path': 'a.txt',
        'old_text': 'old',
        'new_text': 'new',
      });
      expect(await Documents(temp.path, chat).read('a.txt'), 'new');
      await expectLater(
        tools.execute('write_source', {'path': '../bad', 'content': 'x'}),
        throwsFormatException,
      );
      tools.close();
    });
    test('File writes and web can be disabled independently', () {
      final s = AppSettings()
        ..fileWrites = false
        ..network = false;
      final tools = Tools(
        s,
        store,
        Documents(temp.path, chat),
        project,
        chat,
        (n, a) async => true,
        () async => {},
      );
      final names = tools.definitions.map((d) => d['function']['name']);
      expect(names, isNot(contains('write_source')));
      expect(names, isNot(contains('web_search')));
      expect(names, contains('read_source'));
      tools.close();
    });
    test('Memories obey conversation/project/shared boundaries', () async {
      await store.saveMemory(project, chat, false, 'conversation', 'private');
      await store.saveMemory(project, chat, false, 'project', 'project');
      await store.saveMemory(project, chat, true, 'shared', 'shared');
      expect((await store.memories(project, chat, false)).length, 2);
      expect((await store.memories(project, chat + 1, false)).length, 1);
      expect((await store.memories(project + 1, chat + 1, false)), isEmpty);
      expect((await store.memories(project + 1, chat + 1, true)).length, 1);
      await expectLater(
        store.deleteMemory(project + 1, chat + 1, false, 1),
        throwsFormatException,
      );
    });

    test('Deleted chat IDs are never reused for private documents', () async {
      await store.remove('chats', chat);
      final next = await store.createChat(project);
      expect(next, greaterThan(chat));
    });
    test(
      'Deleted providers cannot lend a stale vault ID to a new provider',
      () async {
        final old = await store.saveProvider(
          ProviderConfig(
            name: 'Old',
            url: 'https://example.org/v1',
            model: 'test',
          ),
        );
        await store.remove('providers', old);
        final next = await store.saveProvider(
          ProviderConfig(
            name: 'Next',
            url: 'https://example.org/v1',
            model: 'test',
          ),
        );
        expect(next, greaterThan(old));
      },
    );
    test(
      'Deleting conversations and projects clears their memory scope',
      () async {
        await store.saveMemory(
          project,
          chat,
          true,
          'conversation',
          'Private chat',
        );
        await store.saveMemory(
          project,
          chat,
          true,
          'project',
          'Private project',
        );
        await store.saveMemory(project, chat, true, 'shared', 'Shared');
        await store.remove('chats', chat);
        expect(
          (await store.memories(project, chat, true)).map((m) => m['content']),
          unorderedEquals(['Private project', 'Shared']),
        );
        await store.remove('projects', project);
        final next = await store.createProject('Next');
        expect(next, greaterThan(project));
        expect((await store.memories(next, 0, true)).map((m) => m['content']), [
          'Shared',
        ]);
      },
    );
    test('Document cleanup only removes the owning discussion', () async {
      await Documents(temp.path, chat).write('one.txt', 'one');
      await Documents(temp.path, chat + 1).write('two.txt', 'two');
      await Documents(temp.path, chat).clear();
      expect(await Documents(temp.path, chat).list(), isEmpty);
      expect(await Documents(temp.path, chat + 1).read('two.txt'), 'two');
    });
    test(
      'Schema migration preserves IDs, drafts, messages and preferences',
      () async {
        final file = '${temp.path}/migration.sqlite';
        final original = await Store.open(file, factory: databaseFactoryFfi);
        final owner = (await original.projects()).first.id;
        final id = await original.createChat(owner);
        await original.draft(id, 'Saved draft');
        await original.saveMessage(
          ChatMessage(
            chatId: id,
            role: 'user',
            text: 'Saved message',
            wire: {'role': 'user', 'content': 'Saved message'},
          ),
        );
        await original.saveSettings(AppSettings()..theme = 'dark');
        await original.db.execute('PRAGMA user_version=1');
        await original.db.close();
        final migrated = await Store.open(file, factory: databaseFactoryFfi);
        try {
          expect((await migrated.chats(owner)).single.id, id);
          expect((await migrated.chats(owner)).single.draft, 'Saved draft');
          expect((await migrated.messages(id)).single.text, 'Saved message');
          expect((await migrated.settings()).theme, 'dark');
          await migrated.remove('chats', id);
          expect(await migrated.createChat(owner), greaterThan(id));
        } finally {
          await migrated.db.close();
        }
      },
    );
    test('Ordering supports repeated moves', () async {
      final b = await store.createChat(project);
      await store.order('chats', [b, chat]);
      expect((await store.chats(project)).first.id, b);
      await store.order('chats', [chat, b]);
      expect((await store.chats(project)).first.id, chat);
    });
    test('Provider SQL contains no key field', () async {
      final cols = await store.db.rawQuery('PRAGMA table_info(providers)');
      expect(cols.map((r) => r['name']), isNot(contains('key')));
    });
  });
}

class StubSocket extends Fake implements Socket {}

class CapturedClient extends Fake implements HttpClient {
  late Future<ConnectionTask<Socket>> Function(Uri, String?, int?) factory;
  @override
  set connectionFactory(
    Future<ConnectionTask<Socket>> Function(Uri, String?, int?)? value,
  ) {
    factory = value!;
  }

  @override
  set connectionTimeout(Duration? value) {}
  @override
  set findProxy(String Function(Uri)? value) {}
  @override
  void close({bool force = false}) {}
}
