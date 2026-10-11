import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:provider/provider.dart';
import 'package:sqflite/sqflite.dart';
import 'package:monolith_harness_app_flutter/main.dart';
import 'package:monolith_harness_app_flutter/app_state.dart';
import 'package:monolith_harness_app_flutter/data/store.dart';
import 'package:monolith_harness_app_flutter/domain/models.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  binding.framePolicy = LiveTestWidgetsFlutterBindingFramePolicy.fullyLive;
  testWidgets('Responsive native chat, sheets, forms, keyboard and rotation', (
    tester,
  ) async {
    expect(
      (await PackageInfo.fromPlatform()).packageName,
      'com.monolithharness.flutter.qa',
    );
    FlutterSecureStorage.setMockInitialValues({});
    final store = await Store.open(inMemoryDatabasePath);
    final dir = await Directory.systemTemp.createTemp('monolith-responsive-');
    final state = AppState(store, dir.path);
    Size viewSize() => tester.view.physicalSize / tester.view.devicePixelRatio;
    Future<void> until(bool Function() predicate) async {
      final deadline = DateTime.now().add(const Duration(seconds: 25));
      while (!predicate() && DateTime.now().isBefore(deadline)) {
        await tester.pump(const Duration(milliseconds: 100));
        await Future<void>.delayed(const Duration(milliseconds: 50));
      }
      expect(predicate(), isTrue);
      await tester.pumpAndSettle();
    }

    Future<void> capture(String name) async {
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      debugPrint(
        'RESPONSIVE_VIEW_$name:${viewSize().width.toStringAsFixed(1)}x${viewSize().height.toStringAsFixed(1)}',
      );
      debugPrint('RESPONSIVE_CAPTURE_$name');
      await Future<void>.delayed(const Duration(seconds: 4));
    }

    Future<void> rotate(bool landscape) async {
      debugPrint('RESPONSIVE_ROTATE_${landscape ? 'landscape' : 'portrait'}');
      await until(
        () => landscape
            ? viewSize().width > viewSize().height
            : viewSize().height > viewSize().width,
      );
    }

    try {
      await state.load();
      await state.saveProvider(
        ProviderConfig(
          name: 'Fournisseur de démonstration',
          url: 'https://example.org/v1',
          model: 'monolith-modele-demonstration',
          models: ['monolith-modele-demonstration', 'monolith-raisonnement'],
        ),
        'fixture-only',
      );
      await tester.pumpWidget(
        ChangeNotifierProvider.value(value: state, child: const MonolithApp()),
      );
      await capture('welcome_portrait');
      await state.newProject('Projet créatif');
      for (final title in [
        'Idées pour une interface moderne',
        'Documents et recherches',
        'Planification de la semaine',
      ]) {
        await state.newChat();
        await store.rename('chats', state.chat!.id, title);
      }
      await state.refresh();
      await state.select(state.chats[state.project!.id]!.first);
      final chat = state.chat!;
      await store.saveMessage(
        ChatMessage(
          chatId: chat.id,
          role: 'user',
          text: 'Comment présenter mes projets sur une tablette et un téléphone pliable ?',
        ),
      );
      await store.saveMessage(
        ChatMessage(
          chatId: chat.id,
          role: 'assistant',
          text: '''## Un espace de travail clair

Le contenu reste **lisible et confortable** dans chaque orientation.

- Retrouvez les projets et discussions dans la navigation.
- Gardez vos documents près du message.
- Ouvrez les paramètres et choisissez un modèle.

| Format | Usage |
| --- | --- |
| Téléphone | Écrire une idée |
| Pliable ouvert | Lire et comparer |
| Tablette | Organiser un projet |

```dart
final interface = 'Material 3';
```

Vous pouvez continuer à écrire tout en consultant la réponse.''',
        ),
      );
      await state.select(chat);
      await state.importDocument(
        'notes-du-projet.txt',
        utf8.encode('Contenu fictif pour la revue responsive.'),
      );
      await capture('chat_portrait');
      await tester.tap(find.byIcon(Icons.menu));
      await capture('sidebar_portrait');
      Navigator.of(tester.element(find.byType(Drawer))).pop();
      await tester.pumpAndSettle();
      await tester.tap(find.text('monolith-modele-demonstration').first);
      await capture('models_portrait');
      Navigator.of(tester.element(find.text('Choisir le modèle'))).pop();
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Paramètres'));
      await capture('settings_portrait');
      await tester.tap(find.text('Général'));
      await capture('general_portrait');
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Fournisseurs'));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Ajouter un fournisseur'));
      await capture('provider_portrait');
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Ajouter une pièce jointe'));
      await capture('attachments_portrait');
      final documentAction = find.widgetWithText(
        ListTile,
        'Voir et exporter les documents',
      );
      expect(documentAction.hitTestable(), findsOneWidget);
      expect(
        tester.getBottomRight(documentAction).dy,
        lessThanOrEqualTo(
          viewSize().height -
              tester.view.padding.bottom / tester.view.devicePixelRatio +
              0.5,
        ),
      );
      await tester.tap(documentAction);
      await tester.pumpAndSettle();
      expect(find.text('Documents de la discussion'), findsOneWidget);
      Navigator.of(tester.element(find.text('Documents de la discussion')))
          .pop();
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byType(TextField),
        'Un message sur plusieurs lignes\nAvec des pièces jointes et un clavier ouvert.',
      );
      await until(() => tester.view.viewInsets.bottom > 0);
      await capture('keyboard_portrait');
      await SystemChannels.textInput.invokeMethod<void>('TextInput.hide');
      FocusManager.instance.primaryFocus?.unfocus();
      await until(() => tester.view.viewInsets.bottom == 0);
      await rotate(true);
      await capture('chat_landscape');
      await tester.tap(find.byTooltip('Paramètres'));
      await capture('settings_landscape');
      await tester.tap(find.byType(BackButton).first);
      await tester.pumpAndSettle();
      await rotate(false);
      if (const bool.fromEnvironment('QA_IS_FOLD')) {
        debugPrint('RESPONSIVE_FOLD_closed');
        await until(() => viewSize().width < 600);
        await capture('chat_fold_closed');
        await tester.tap(find.byTooltip('Paramètres'));
        await capture('settings_fold_closed');
        await tester.tap(find.byType(BackButton).first);
        await tester.pumpAndSettle();
        debugPrint('RESPONSIVE_FOLD_opened');
        await until(() => viewSize().width >= 600);
        expect(state.chat!.id, chat.id);
        expect(state.draft, contains('clavier ouvert'));
        await capture('chat_fold_reopened');
      }
      expect(find.byTooltip('Envoyer'), findsOneWidget);
      expect(tester.takeException(), isNull);
    } finally {
      await SystemChannels.textInput.invokeMethod<void>('TextInput.hide');
      await tester.pumpWidget(const SizedBox());
      state.dispose();
      await store.db.close();
      await dir.delete(recursive: true);
    }
  });
}
