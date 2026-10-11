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
import 'package:monolith_harness_app_flutter/ui/web_preview.dart';

const demoHtml =
    '''<!doctype html><html><head><title>Monolith demo</title></head><body>
<main class="min-h-screen bg-slate-950 text-white p-6 space-y-6">
<div class="rounded-3xl bg-emerald-600 p-6"><p class="text-sm opacity-80">MONOLITH · FLUTTER</p><h1 class="text-3xl font-bold mt-2">Votre espace créatif</h1><p class="mt-2">Aperçu local et interactif</p></div>
<section class="rounded-3xl bg-slate-800 p-5"><h2 class="text-xl font-semibold">Activité du projet</h2><div style="height:220px"><canvas id="activity"></canvas></div></section>
<button id="count" class="rounded-full bg-emerald-500 text-slate-950 font-semibold px-6 py-4" onclick="document.getElementById('counter').textContent=++window.clicks">Compter : <span id="counter">0</span></button>
<p class="text-sm text-slate-400">Tailwind CSS et Chart.js embarqués. Aucune connexion requise.</p>
</main><script>window.clicks=0;new Chart(document.getElementById('activity'),{type:'bar',data:{labels:['Lun','Mar','Mer','Jeu','Ven'],datasets:[{label:'Messages',data:[4,7,5,9,12],backgroundColor:'#34d399',borderRadius:8}]},options:{responsive:true,maintainAspectRatio:false,plugins:{legend:{display:false}},scales:{y:{ticks:{color:'#cbd5e1'},grid:{color:'#334155'}},x:{ticks:{color:'#cbd5e1'},grid:{display:false}}}}});</script>
</body></html>''';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  binding.framePolicy = LiveTestWidgetsFlutterBindingFramePolicy.fullyLive;
  testWidgets('Pixel: chat, tools, sidebar, permissions and interactive HTML', (
    tester,
  ) async {
    expect(
      (await PackageInfo.fromPlatform()).packageName,
      'com.monolithharness.flutter.qa',
    );
    FlutterSecureStorage.setMockInitialValues({});
    final store = await Store.open(inMemoryDatabasePath);
    final dir = await Directory.systemTemp.createTemp('monolith-device-');
    final requests = <Json>[];
    Stream<List<int>> chunks(String value) async* {
      final bytes = utf8.encode(value);
      for (var i = 0; i < bytes.length; i += 500) {
        await Future<void>.delayed(const Duration(milliseconds: 15));
        yield bytes.sublist(i, (i + 500).clamp(0, bytes.length));
      }
    }

    final state = AppState(
      store,
      dir.path,
      createApi: () => ApiClient(
        MockClient.streaming((req, body) async {
          final payload = jsonDecode(await utf8.decodeStream(body)) as Json;
          requests.add(payload);
          final last = payload['messages'].last as Map;
          final delta = last['role'] == 'user'
              ? {
                  'tool_calls': [
                    {
                      'index': 0,
                      'id': 'call-${requests.length}',
                      'type': 'function',
                      'function': {
                        'name': 'device_information',
                        'arguments': '{}',
                      },
                    },
                  ],
                }
              : {
                  'content':
                      '## Prêt à créer\n\nVotre dashboard est prêt. **Les outils restent soumis aux autorisations.**\n\n```html\n$demoHtml\n```',
                };
          final frame =
              'data: ${jsonEncode({
                'choices': [
                  {'delta': delta},
                ],
              })}\n\ndata: [DONE]\n\n';
          return http.StreamedResponse(
            chunks(frame),
            200,
            headers: {'content-type': 'text/event-stream'},
          );
        }),
      ),
    );
    Future<void> until(bool Function() predicate) async {
      final limit = DateTime.now().add(const Duration(seconds: 20));
      while (!predicate() && DateTime.now().isBefore(limit)) {
        await tester.pump(const Duration(milliseconds: 100));
        await Future<void>.delayed(const Duration(milliseconds: 30));
      }
      expect(predicate(), isTrue);
      await tester.pump();
    }

    Future<void> capture(String name) async {
      debugPrint('QA_CAPTURE_$name');
      await Future<void>.delayed(const Duration(seconds: 6));
    }

    try {
      await state.load();
      await state.saveProvider(
        ProviderConfig(
          name: 'Fournisseur de démonstration',
          url: 'https://example.org/v1',
          model: 'monolith-demo',
        ),
        'fixture-only',
      );
      state.settings.automaticInfo = false;
      await state.newChat();
      final original = state.chat!;
      await state.importDocument(
        'notes.txt',
        utf8.encode('Notes de test du Pixel.'),
      );
      await tester.pumpWidget(
        ChangeNotifierProvider.value(value: state, child: const MonolithApp()),
      );
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byType(TextField),
        'Crée un dashboard /create-web',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('/create-web-app'));
      await tester.pumpAndSettle();
      expect(state.messages, isEmpty);
      expect(state.draft.endsWith('/create-web-app '), isTrue);
      await tester.tap(find.byTooltip('Envoyer'));
      await until(
        () => find.text('Autoriser device_information ?').evaluate().isNotEmpty,
      );
      await capture('approval');
      await tester.tap(find.text('Refuser'));
      await until(() => !state.busy);
      expect(state.error, isEmpty);
      expect(state.activities.single.status, 'Refusé ou en erreur');
      expect(requests.last['messages'].last['content'], contains('error'));
      await SystemChannels.textInput.invokeMethod<void>('TextInput.hide');
      await tester.pumpAndSettle();
      await capture('chat');
      await state.newProject('Projet Pixel');
      await state.newChat();
      await state.newChat();
      await tester.pumpAndSettle();
      await tester.tap(find.byIcon(Icons.menu));
      await tester.pumpAndSettle();
      await capture('sidebar');
      await tester.tap(find.text('Paramètres'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Autorisations'));
      await tester.pumpAndSettle();
      await capture('permissions');
      expect(find.text('Demander tout'), findsOneWidget);
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await state.select(original);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Ouvrir l’aperçu'));
      await tester.tap(find.text('Ouvrir l’aperçu'));
      await tester.pumpAndSettle();
      expect(find.byType(WebPreview), findsOneWidget);
      await Future<void>.delayed(const Duration(seconds: 4));
      await capture('html');
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      expect(find.text('Ouvrir l’aperçu'), findsOneWidget);
      await SystemChrome.setPreferredOrientations([
        DeviceOrientation.landscapeLeft,
      ]);
      await Future<void>.delayed(const Duration(seconds: 2));
      await tester.pumpAndSettle();
      await capture('landscape');
      await SystemChrome.setPreferredOrientations([
        DeviceOrientation.portraitUp,
      ]);
      await Future<void>.delayed(const Duration(seconds: 1));
      await tester.pumpAndSettle();
      state.settings.approval = ApprovalMode.allow;
      state.editDraft('Continuer');
      final allowed = state.send();
      await until(() => !state.busy);
      await allowed;
      expect(state.error, isEmpty);
      expect(state.activities.single.status, 'Terminé');
      state.settings.approval = ApprovalMode.deny;
      state.editDraft('Refuser les outils');
      final denied = state.send();
      await until(() => !state.busy);
      await denied;
      expect(state.activities.single.status, 'Refusé ou en erreur');
      state.settings.approval = ApprovalMode.ask;
      state.editDraft('Tester l’arrêt');
      final interrupted = state.send();
      await until(
        () => find.text('Autoriser device_information ?').evaluate().isNotEmpty,
      );
      state.stop();
      await until(() => !state.busy);
      await interrupted;
      await tester.pumpAndSettle();
      expect(find.text('Autoriser device_information ?'), findsNothing);
      expect(tester.takeException(), isNull);
    } finally {
      await SystemChrome.setPreferredOrientations([]);
      await tester.pumpWidget(const SizedBox());
      state.dispose();
      await store.db.close();
      await dir.delete(recursive: true);
    }
  });
}
