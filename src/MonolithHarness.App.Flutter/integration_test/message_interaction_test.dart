import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:sqflite/sqflite.dart';
import 'package:monolith_harness_app_flutter/main.dart';
import 'package:monolith_harness_app_flutter/app_state.dart';
import 'package:monolith_harness_app_flutter/data/store.dart';
import 'package:monolith_harness_app_flutter/domain/models.dart';
import 'package:monolith_harness_app_flutter/services/api.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  binding.framePolicy = LiveTestWidgetsFlutterBindingFramePolicy.fullyLive;
  testWidgets('Pixel: long press copy and live reasoning activity', (
    tester,
  ) async {
    expect(
      (await PackageInfo.fromPlatform()).packageName,
      'com.monolithharness.flutter.qa',
    );
    FlutterSecureStorage.setMockInitialValues({});
    final store = await Store.open(inMemoryDatabasePath);
    final dir = await Directory.systemTemp.createTemp(
      'monolith-message-device-',
    );
    final advance = Completer<void>(), finish = Completer<void>();
    final state = AppState(
      store,
      dir.path,
      createApi: () => ApiClient(
        MockClient.streaming((request, body) async {
          String frame(Json delta) =>
              'data: ${jsonEncode({
                'choices': [
                  {'delta': delta},
                ],
              })}\n\n';
          Stream<List<int>> response() async* {
            yield utf8.encode(
              frame({
                'reasoning_content': '### Lecture des fichiers du projet',
              }),
            );
            await advance.future;
            yield utf8.encode(
              frame({
                'reasoning_content': '\n### Vérification des modifications',
              }),
            );
            await finish.future;
            yield utf8.encode(
              frame({'content': 'Les modifications sont prêtes.'}),
            );
            yield utf8.encode('data: [DONE]\n\n');
          }

          return http.StreamedResponse(
            response(),
            200,
            headers: {'content-type': 'text/event-stream'},
          );
        }),
      ),
    );
    Future<void>? sending;
    Future<void> until(bool Function() predicate) async {
      final limit = DateTime.now().add(const Duration(seconds: 15));
      while (!predicate() && DateTime.now().isBefore(limit)) {
        await tester.pump(const Duration(milliseconds: 100));
        await Future<void>.delayed(const Duration(milliseconds: 30));
      }
      expect(predicate(), isTrue);
      await tester.pump();
    }

    Future<void> capture(String name) async {
      debugPrint('MESSAGE_QA_$name');
      await Future<void>.delayed(const Duration(seconds: 5));
    }

    try {
      await state.load();
      await state.saveProvider(
        ProviderConfig(
          name: 'Démonstration',
          url: 'https://example.org/v1',
          model: 'monolith-demo',
        ),
        'fixture-only',
      );
      await state.newChat();
      state.settings.automaticInfo = false;
      state.settings.reasoningVisible = true;
      const body =
          'Une réponse à copier.\n\n**Le message complet reste disponible.**';
      final reply = ChatMessage(
        id: 1,
        chatId: state.chat!.id,
        role: 'assistant',
        text: body,
      );
      state.messages = [reply];
      await tester.pumpWidget(
        ChangeNotifierProvider.value(value: state, child: const MonolithApp()),
      );
      await tester.pumpAndSettle();
      expect(find.byTooltip('Copier le message'), findsNothing);
      expect(find.byIcon(Icons.copy_outlined), findsNothing);
      await tester.longPress(find.text('Monolith'));
      await tester.pumpAndSettle();
      expect(find.text('Options du message'), findsOneWidget);
      await capture('options');
      await tester.tap(find.text('Copier le message'));
      await tester.pumpAndSettle();
      expect((await Clipboard.getData(Clipboard.kTextPlain))?.text, body);
      await tester.longPress(
        find.text('Une réponse à copier.', findRichText: true),
      );
      await tester.pumpAndSettle();
      expect(find.text('Copier le message'), findsOneWidget);
      expect(find.text('Copier la sélection'), findsOneWidget);
      await tester.tap(find.text('Copier le message'));
      await tester.pumpAndSettle();
      state.editDraft('Vérifie le projet.');
      sending = state.send();
      await until(() => state.status == 'Lecture des fichiers du projet');
      expect(find.text('Lecture des fichiers du projet'), findsNWidgets(2));
      expect(find.text('Raisonnement'), findsNothing);
      await capture('reading');
      advance.complete();
      await until(() => state.status == 'Vérification des modifications');
      expect(find.text('Vérification des modifications'), findsNWidgets(2));
      expect(find.text('Lecture des fichiers du projet'), findsNothing);
      await capture('verifying');
      finish.complete();
      await until(() => !state.busy);
      await sending;
      expect(state.messages.last.text, 'Les modifications sont prêtes.');
      expect(tester.takeException(), isNull);
    } finally {
      if (!advance.isCompleted) advance.complete();
      if (!finish.isCompleted) finish.complete();
      state.stop();
      if (sending != null) await sending;
      await tester.pumpWidget(const SizedBox());
      state.dispose();
      await store.db.close();
      await dir.delete(recursive: true);
    }
  });
}
