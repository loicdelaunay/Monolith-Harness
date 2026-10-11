import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';
import 'package:monolith_harness_app_flutter/app_state.dart';
import 'package:monolith_harness_app_flutter/data/store.dart';
import 'package:monolith_harness_app_flutter/domain/models.dart';
import 'package:monolith_harness_app_flutter/services/api.dart';
import 'package:monolith_harness_app_flutter/services/reasoning_activity.dart';
import 'package:monolith_harness_app_flutter/ui/home.dart';
import 'package:monolith_harness_app_flutter/ui/settings.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  sqfliteFfiInit();
  late Store store;
  late Directory directory;
  late AppState state;
  setUp(() async {
    FlutterSecureStorage.setMockInitialValues({});
    store = await Store.open(
      inMemoryDatabasePath,
      factory: databaseFactoryFfiNoIsolate,
    );
    directory = await Directory.systemTemp.createTemp('monolith-ui-');
    state = AppState(store, directory.path);
    await state.load();
  });
  tearDown(() async {
    state.dispose();
    await store.db.close();
    await directory.delete(recursive: true);
  });
  Future<void> show(
    WidgetTester tester, {
    Widget? child,
    double scale = 1,
  }) async {
    await tester.pumpWidget(
      ChangeNotifierProvider.value(
        value: state,
        child: MaterialApp(
          theme: ThemeData(useMaterial3: true),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: TextScaler.linear(scale)),
            child: child!,
          ),
          home: child ?? const HomePage(),
        ),
      ),
    );
    if (state.busy) {
      await tester.pump(const Duration(milliseconds: 400));
    } else {
      await tester.pumpAndSettle();
    }
  }

  for (final size in [
    const Size(320, 480),
    const Size(360, 640),
    const Size(412, 892),
    const Size(892, 412),
    const Size(768, 1024),
    const Size(443, 995), // Fold outer display.
    const Size(852, 883), // Fold inner display.
    const Size(883, 852),
    const Size(800, 1232), // Lenovo portrait app viewport.
    const Size(1280, 752), // Lenovo landscape app viewport.
  ]) {
    testWidgets('Responsive chat ${size.width} x ${size.height}', (
      tester,
    ) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await show(tester);
      expect(find.byTooltip('Ajouter une pièce jointe'), findsOneWidget);
      expect(find.byTooltip('Envoyer'), findsOneWidget);
      expect(tester.takeException(), isNull);
      final input = find.byType(TextField);
      await tester.enterText(
        input,
        'Un message sur plusieurs lignes\nUne deuxième ligne',
      );
      await tester.pump();
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets('Small phone with keyboard, attachments and large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 270);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    state.documents = [
      {'path': 'document-avec-un-nom-très-long.txt', 'bytes': 123},
    ];
    await show(tester, scale: 1.8);
    expect(tester.takeException(), isNull);
    await tester.ensureVisible(find.text('Pièces jointes (1)'));
    await tester.tap(find.text('Pièces jointes (1)'));
    await tester.pumpAndSettle();
    expect(find.text('Voir pièces jointes (1)'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
  for (final size in [const Size(443, 995), const Size(852, 883)]) {
    testWidgets('Attachment actions avoid Android navigation $size', (
      tester,
    ) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      tester.view.padding = const FakeViewPadding(bottom: 56);
      tester.view.viewPadding = const FakeViewPadding(bottom: 56);
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPadding);
      addTearDown(tester.view.resetViewPadding);
      state.documents = [
        {'path': 'notes.txt', 'bytes': 12},
      ];
      await show(tester, scale: 1.5);
      await tester.tap(find.byTooltip('Ajouter une pièce jointe'));
      await tester.pumpAndSettle();
      final action = find.widgetWithText(
        ListTile,
        'Voir et exporter les documents',
      );
      expect(action.hitTestable(), findsOneWidget);
      expect(
        tester.getBottomRight(action).dy,
        lessThanOrEqualTo(size.height - 56),
      );
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets('Attachment actions scroll on a short screen with large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(640, 360);
    tester.view.devicePixelRatio = 1;
    tester.view.padding = const FakeViewPadding(bottom: 34);
    tester.view.viewPadding = const FakeViewPadding(bottom: 34);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPadding);
    addTearDown(tester.view.resetViewPadding);
    state.documents = [
      {'path': 'notes.txt', 'bytes': 12},
    ];
    await show(tester, scale: 1.8);
    await tester.tap(find.byTooltip('Ajouter une pièce jointe'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    final action = find.widgetWithText(
      ListTile,
      'Voir et exporter les documents',
    );
    await tester.ensureVisible(action);
    await tester.pumpAndSettle();
    expect(action.hitTestable(), findsOneWidget);
    expect(tester.getBottomRight(action).dy, lessThanOrEqualTo(326));
    expect(tester.takeException(), isNull);
  });
  testWidgets('Fold resize keeps the discussion, draft and attachment state', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(852, 883);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.runAsync(() => state.newChat());
    final chatId = state.chat!.id;
    state.documents = [
      {'path': 'notes.txt', 'bytes': 12},
    ];
    await show(tester, scale: 1.5);
    await tester.enterText(
      find.byType(TextField),
      'Un brouillon avant de plier',
    );
    await tester.tap(find.text('Pièces jointes (1)'));
    await tester.pumpAndSettle();
    for (final size in [
      const Size(443, 995),
      const Size(995, 443),
      const Size(852, 883),
    ]) {
      tester.view.physicalSize = size;
      await tester.pumpAndSettle();
      expect(state.chat!.id, chatId);
      expect(state.draft, 'Un brouillon avant de plier');
      expect(
        tester.widget<TextField>(find.byType(TextField)).controller!.text,
        'Un brouillon avant de plier',
      );
      expect(find.text('Voir pièces jointes (1)'), findsOneWidget);
      expect(find.byTooltip('Envoyer').hitTestable(), findsOneWidget);
      expect(tester.takeException(), isNull);
    }
  });
  testWidgets('Inline slash completion inserts a space and never sends', (
    tester,
  ) async {
    await show(tester);
    await tester.enterText(find.byType(TextField), 'Résume le document /sum');
    await tester.pumpAndSettle();
    expect(find.text('/summary'), findsOneWidget);
    await tester.tap(find.text('/summary'));
    await tester.pumpAndSettle();
    final field = tester.widget<TextField>(find.byType(TextField));
    expect(field.controller!.text, 'Résume le document /summary ');
    expect(state.messages, isEmpty);
    expect(state.busy, isFalse);
    expect(state.draft, 'Résume le document /summary ');
  });
  testWidgets(
    'Bare slash has suggestions and Enter completes without newline',
    (tester) async {
      await show(tester);
      await tester.enterText(find.byType(TextField), 'Test /');
      await tester.pumpAndSettle();
      expect(find.text('/summary'), findsOneWidget);
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.pump();
      expect(
        tester.widget<TextField>(find.byType(TextField)).controller!.text,
        'Test /summary ',
      );
      expect(state.messages, isEmpty);
    },
  );
  for (final name in ['Général', 'Chat', 'Skills', 'Autorisations']) {
    testWidgets('Settings $name fit a small phone', (tester) async {
      tester.view.physicalSize = const Size(320, 560);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await show(tester, child: SettingsSection(name), scale: 1.5);
      expect(tester.takeException(), isNull);
      await tester.drag(find.byType(ListView).first, const Offset(0, -500));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets('Settings can be opened from the chat', (tester) async {
    await show(tester);
    await tester.tap(find.byTooltip('Paramètres'));
    await tester.pumpAndSettle();
    expect(find.text('Autorisations'), findsOneWidget);
    await tester.tap(find.text('Autorisations'));
    await tester.pumpAndSettle();
    expect(find.text('Demander tout'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Long model names and status fit a narrow header', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    state.provider = ProviderConfig(
      name: 'Un fournisseur avec un nom très long',
      url: 'https://example.org/v1',
      model: 'un-nom-de-modèle-particulièrement-long',
    );
    state.status =
        'Un statut long qui doit rester lisible au-dessus de la zone de saisie';
    await show(tester, scale: 1.6);
    expect(tester.takeException(), isNull);
  });
  testWidgets('Conversation drag handles work on successive moves', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(420, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.runAsync(() async {
      await state.newChat();
      await state.newChat();
      await state.newChat();
    });
    final project = state.project!.id;
    final before = state.chats[project]!.map((c) => c.id).toList();
    await show(tester);
    await tester.tap(find.byIcon(Icons.menu));
    await tester.pumpAndSettle();
    Future<void> move(int id, double distance) async {
      final handle = find.descendant(
        of: find.byKey(ValueKey('chat-$id')),
        matching: find.byType(ReorderableDelayedDragStartListener),
      );
      final gesture = await tester.startGesture(tester.getCenter(handle));
      await tester.pump(const Duration(milliseconds: 600));
      await gesture.moveBy(Offset(0, distance));
      await tester.pump(const Duration(milliseconds: 600));
      await gesture.up();
      await tester.pumpAndSettle();
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 200)),
      );
      await tester.pumpAndSettle();
    }

    await move(before.first, 140);
    final moved = state.chats[project]!.map((c) => c.id).toList();
    expect(moved, isNot(equals(before)));
    await move(before.first, -140);
    expect(
      state.chats[project]!.map((c) => c.id).toList(),
      isNot(equals(moved)),
    );
    expect(tester.takeException(), isNull);
  });
  testWidgets('Reasoning does not add an empty assistant bubble', (
    tester,
  ) async {
    state.messages = [
      ChatMessage(
        id: 1,
        chatId: 1,
        role: 'assistant',
        reasoning: 'Thinking',
        status: 'generating',
      ),
    ];
    state.busy = true;
    state.status = 'Réflexion en cours…';
    await show(tester);
    expect(find.text('Réflexion en cours…'), findsOneWidget);
    expect(find.byTooltip('Copier le message'), findsNothing);
    expect(
      tester.getTopLeft(find.text('Réflexion en cours…')).dy,
      lessThan(tester.getTopLeft(find.byType(TextField)).dy),
    );
    state.busy = false;
  });
  testWidgets('Message copy is only offered after long pressing a bubble', (
    tester,
  ) async {
    String? copied;
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async {
        if (call.method == 'Clipboard.setData') {
          copied = call.arguments['text'] as String;
        }
        return null;
      },
    );
    addTearDown(
      () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        null,
      ),
    );
    state.messages = [
      ChatMessage(
        id: 1,
        chatId: 1,
        role: 'assistant',
        text: '**Réponse complète**\n\nUne autre ligne.',
      ),
    ];
    await show(tester);
    expect(find.byTooltip('Copier le message'), findsNothing);
    expect(find.byIcon(Icons.copy_outlined), findsNothing);
    await tester.longPress(find.text('Monolith'));
    await tester.pumpAndSettle();
    expect(find.text('Options du message'), findsOneWidget);
    await tester.tap(find.text('Copier le message'));
    await tester.pumpAndSettle();
    expect(copied, state.messages.single.text);
    expect(find.text('Options du message'), findsNothing);
    expect(find.text('Message copié'), findsOneWidget);
  });
  testWidgets('Message options stay above Android navigation buttons', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    tester.view.padding = const FakeViewPadding(bottom: 34);
    tester.view.viewPadding = const FakeViewPadding(bottom: 34);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPadding);
    addTearDown(tester.view.resetViewPadding);
    state.messages = [
      ChatMessage(id: 1, chatId: 1, role: 'assistant', text: 'Une réponse'),
    ];
    await show(tester);
    await tester.longPress(find.text('Monolith'));
    await tester.pumpAndSettle();
    expect(
      tester
          .getBottomRight(find.widgetWithText(ListTile, 'Copier le message'))
          .dy,
      lessThanOrEqualTo(606),
    );
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'Long pressing message text keeps selection and whole-message copy',
    (tester) async {
      state.messages = [
        ChatMessage(
          id: 1,
          chatId: 1,
          role: 'user',
          text: 'Un message à sélectionner',
        ),
      ];
      await show(tester);
      await tester.longPress(
        find.text('Un message à sélectionner', findRichText: true),
      );
      await tester.pumpAndSettle();
      expect(find.text('Copier le message'), findsOneWidget);
      expect(find.text('Copier la sélection'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Reasoning heading and composer status follow the latest activity',
    (tester) async {
      final reply = ChatMessage(
        id: 1,
        chatId: 1,
        role: 'assistant',
        reasoning: '### Lecture du projet',
        status: 'generating',
      );
      state.messages = [reply];
      state.settings.reasoningVisible = true;
      state.busy = true;
      state.status = reasoningActivity(reply.reasoning);
      await show(tester);
      expect(find.text('Lecture du projet'), findsNWidgets(2));
      expect(find.text('Raisonnement'), findsNothing);
      reply.reasoning += '\n### Vérification des fichiers';
      state.status = reasoningActivity(reply.reasoning);
      state.repaint();
      await tester.pump(const Duration(milliseconds: 100));
      expect(find.text('Vérification des fichiers'), findsNWidgets(2));
      expect(find.text('Lecture du projet'), findsNothing);
      expect(tester.takeException(), isNull);
      state.busy = false;
    },
  );
  testWidgets('Tools share one compact bubble per turn', (tester) async {
    state.messages = [
      ChatMessage(
        id: 1,
        chatId: 1,
        role: 'user',
        text: 'Consulte mes fichiers',
      ),
      ChatMessage(
        id: 2,
        chatId: 1,
        role: 'assistant',
        wire: {
          'tool_calls': [
            {
              'id': 'a',
              'function': {'name': 'list_sources', 'arguments': '{}'},
            },
            {
              'id': 'b',
              'function': {
                'name': 'read_source',
                'arguments': '{"path":"test.txt"}',
              },
            },
          ],
        },
      ),
      ChatMessage(
        id: 3,
        chatId: 1,
        role: 'tool',
        text: '{}',
        wire: {'tool_call_id': 'a'},
      ),
      ChatMessage(
        id: 4,
        chatId: 1,
        role: 'tool',
        text: '{}',
        wire: {'tool_call_id': 'b'},
      ),
    ];
    await show(tester);
    expect(find.byType(ToolBubble), findsOneWidget);
    expect(find.text('2 outils utilisés'), findsOneWidget);
  });
  test(
    'First message retains pending images and explicitly requested skill',
    () async {
      Json? received;
      final api = ApiClient(
        MockClient((request) async {
          received = jsonDecode(request.body) as Json;
          return http.Response(
            jsonEncode({
              'choices': [
                {
                  'message': {'role': 'assistant', 'content': '**OK**'},
                },
              ],
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );
      state.dispose();
      state = AppState(store, directory.path, createApi: () => api);
      await state.load();
      await state.saveProvider(
        ProviderConfig(
          name: 'Test',
          url: 'https://example.org/v1',
          model: 'test',
        ),
        'test-fixture',
      );
      state.settings.automaticInfo = false;
      state.editDraft('Un résumé /summary');
      state.addImage(
        'pixel.png',
        base64Decode(
          'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aI1cAAAAASUVORK5CYII=',
        ),
      );
      await state.send();
      expect(state.error, isEmpty);
      expect(state.images, isEmpty);
      expect(state.messages.where((m) => m.role == 'user').length, 1);
      final content = (received!['messages'] as List).last['content'] as List;
      expect(content.last['type'], 'image_url');
      expect(
        received!['messages'][0]['content'],
        contains('EXPLICIT USER SKILL /summary'),
      );
      expect((await store.messages(state.chat!.id)).last.text, '**OK**');
    },
  );

  test('Importing the first document keeps the draft and images', () async {
    state.editDraft('Un brouillon à conserver');
    state.addImage(
      'pixel.png',
      base64Decode(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aI1cAAAAASUVORK5CYII=',
      ),
    );
    await state.importDocument('notes.txt', utf8.encode('Notes'));
    expect(state.draft, 'Un brouillon à conserver');
    expect(state.images.length, 1);
    expect(state.documents.single['path'], 'notes.txt');
    expect(state.chat, isNotNull);
  });
  test('Incomplete tool history cannot be submitted after interruption', () {
    final history = completeHistory([
      {'role': 'user', 'content': 'First'},
      {
        'role': 'assistant',
        'content': 'Checking',
        'tool_calls': [
          {'id': 'a'},
          {'id': 'b'},
        ],
      },
      {'role': 'tool', 'tool_call_id': 'a', 'content': 'partial'},
      {'role': 'user', 'content': 'Continue'},
    ]);
    expect(history.any((m) => m.containsKey('tool_calls')), isFalse);
    expect(history.any((m) => m['role'] == 'tool'), isFalse);
    expect(history[1]['content'], 'Checking');
  });
  test('Completed tool exchanges remain valid', () {
    final history = completeHistory([
      {
        'role': 'assistant',
        'content': '',
        'tool_calls': [
          {'id': 'a'},
        ],
      },
      {'role': 'tool', 'tool_call_id': 'a', 'content': 'Done'},
      {'role': 'tool', 'tool_call_id': 'orphan', 'content': 'Orphan'},
      {'role': 'user', 'content': 'Next'},
    ]);
    // Unexpected extra tool output makes the exchange invalid rather than sending an invalid protocol.
    expect(history.where((m) => m['role'] == 'tool'), isEmpty);
    final valid = completeHistory([
      {
        'role': 'assistant',
        'content': '',
        'tool_calls': [
          {'id': 'a'},
        ],
      },
      {'role': 'tool', 'tool_call_id': 'a', 'content': 'Done'},
    ]);
    expect(valid.length, 2);
  });
}
