import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:file_picker/file_picker.dart';
import 'package:provider/provider.dart';

import '../app_state.dart';
import '../domain/models.dart';
import '../data/documents.dart';
import '../services/reasoning_activity.dart';
import 'brand_logo.dart';
import 'reply_view.dart';
import 'settings.dart';

class HomePage extends StatefulWidget {
  const HomePage({super.key});
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> with WidgetsBindingObserver {
  final input = TextEditingController(),
      focus = FocusNode(),
      scaffold = GlobalKey<ScaffoldState>();
  int? editingChat;
  bool attachmentsOpen = true;
  String filter = '';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    input.dispose();
    focus.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState value) {
    if (value == AppLifecycleState.paused) {
      final s = context.read<AppState>();
      s.saveDraft();
      s.stop();
    }
  }

  Future<T?> protect<T>(Future<T> Function() action) async {
    try {
      return await action();
    } catch (ex) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(ex.toString())));
      }
      return null;
    }
  }

  Future<bool> approval(String name, Json args) async {
    final s = context.read<AppState>();
    if (!mounted) return false;
    return await showDialog<bool>(
          context: context,
          barrierDismissible: false,
          builder: (dialog) {
            s.dismissApproval = () {
              if (dialog.mounted) Navigator.of(dialog).pop(false);
            };
            return AlertDialog(
              title: Row(
                children: [
                  const Icon(Icons.shield_outlined),
                  const SizedBox(width: 12),
                  Expanded(child: Text('Autoriser $name ?')),
                ],
              ),
              content: ConstrainedBox(
                constraints: const BoxConstraints(maxHeight: 380),
                child: SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Text(
                        'L’outil utilise les accès activés dans les paramètres. Les résultats sont transmis au modèle de cette discussion.',
                      ),
                      const SizedBox(height: 16),
                      SelectableText(
                        const JsonEncoder.withIndent('  ').convert(args),
                        style: const TextStyle(
                          fontFamily: 'monospace',
                          fontSize: 13,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(dialog, false),
                  child: const Text('Refuser'),
                ),
                FilledButton(
                  onPressed: () => Navigator.pop(dialog, true),
                  child: const Text('Autoriser'),
                ),
              ],
            );
          },
        ).whenComplete(() => s.dismissApproval = null) ??
        false;
  }

  List<Skill> suggestions(AppState s) {
    final end = input.selection.baseOffset;
    if (end < 0 || end > input.text.length) return [];
    final match = RegExp(r'(?:^|\s)/([\w-]*)$')
        .firstMatch(input.text.substring(0, end));
    if (match == null) return [];
    return skills.where((k) => k.id.startsWith(match[1]!)).take(5).toList();
  }

  void completeSkill(Skill skill) {
    final end = input.selection.baseOffset;
    final match = RegExp(r'(?:^|\s)/([\w-]*)$')
        .firstMatch(input.text.substring(0, end));
    if (match == null) return;
    final slash = input.text.lastIndexOf('/', end - 1),
        insert = '/${skill.id} ';
    input.value = TextEditingValue(
      text: input.text.replaceRange(slash, end, insert),
      selection: TextSelection.collapsed(offset: slash + insert.length),
    );
    context.read<AppState>().editDraft(input.text);
    setState(() {});
    focus.requestFocus();
  }

  Future<void> attach(AppState s) async {
    focus.unfocus();
    final kind = await showModalBottomSheet<String>(
      context: context,
      useSafeArea: true,
      builder: (c) => SafeArea(
        top: false,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: const Icon(Icons.image_outlined),
                title: const Text('Ajouter des images'),
                subtitle: const Text('PNG, JPEG, WebP ou GIF · 8 Mo maximum'),
                onTap: () => Navigator.pop(c, 'image'),
              ),
              ListTile(
                leading: const Icon(Icons.description_outlined),
                title: const Text('Importer un document texte'),
                subtitle: const Text(
                  'Copie privée de la discussion · 1 Mo maximum',
                ),
                onTap: () => Navigator.pop(c, 'text'),
              ),
              if (s.documents.isNotEmpty)
                ListTile(
                  leading: const Icon(Icons.file_open_outlined),
                  title: const Text('Voir et exporter les documents'),
                  onTap: () => Navigator.pop(c, 'documents'),
                ),
            ],
          ),
        ),
      ),
    );
    if (kind == 'documents' && mounted) {
      await documents(s);
      return;
    }
    if (kind == null) return;
    await protect(() async {
      final result = await FilePicker.pickFiles(
        type: kind == 'image' ? FileType.image : FileType.any,
      );
      if (result.isEmpty) return;
      for (final f in result) {
        final limit = kind == 'image' ? 8 * 1024 * 1024 : 1024 * 1024;
        final blocks = BytesBuilder(copy: false);
        await for (final block in f.readAsByteStream()) {
          blocks.add(block);
          if (blocks.length > limit) {
            throw const FormatException('Fichier trop volumineux.');
          }
        }
        final bytes = blocks.takeBytes();
        if (kind == 'image') {
          s.addImage(f.name, bytes);
        } else {
          await s.importDocument(f.name, bytes);
        }
      }
      if (mounted) setState(() => attachmentsOpen = true);
    });
  }

  Future<void> documents(AppState s) async {
    if (s.chat == null) return;
    await showModalBottomSheet(
      context: context,
      useSafeArea: true,
      isScrollControlled: true,
      builder: (c) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: .6,
        builder: (c, scroll) => ListView(
          controller: scroll,
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              'Documents de la discussion',
              style: Theme.of(c).textTheme.titleLarge,
            ),
            for (final d in s.documents)
              ListTile(
                leading: const Icon(Icons.description_outlined),
                title: Text(d['path']),
                subtitle: Text('${d['bytes']} octets'),
                trailing: IconButton(
                  tooltip: 'Exporter',
                  icon: const Icon(Icons.save_alt),
                  onPressed: () => protect(() async {
                    final text = await Documents(
                      s.directory,
                      s.chat!.id,
                    ).read(d['path']);
                    await FilePicker.saveFile(
                      dialogTitle: 'Exporter le document',
                      fileName: d['path'].toString().split('/').last,
                      bytes: Uint8List.fromList(utf8.encode(text)),
                    );
                  }),
                ),
                onTap: () async {
                  final text = await Documents(
                    s.directory,
                    s.chat!.id,
                  ).read(d['path']);
                  if (c.mounted) {
                    showDialog(
                      context: c,
                      builder: (c) => AlertDialog(
                        title: Text(d['path']),
                        content: SingleChildScrollView(
                          child: SelectableText(text),
                        ),
                        actions: [
                          TextButton(
                            onPressed: () => Navigator.pop(c),
                            child: const Text('Fermer'),
                          ),
                        ],
                      ),
                    );
                  }
                },
              ),
          ],
        ),
      ),
    );
  }

  Future<String?> nameDialog(String title, [String initial = '']) async {
    final edit = TextEditingController(text: initial);
    final result = await showDialog<String>(
      context: context,
      builder: (c) => AlertDialog(
        title: Text(title),
        content: TextField(
          controller: edit,
          autofocus: true,
          maxLength: 100,
          decoration: const InputDecoration(labelText: 'Nom'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c),
            child: const Text('Annuler'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, edit.text),
            child: const Text('Enregistrer'),
          ),
        ],
      ),
    );
    // Let the dialog finish its exit animation before disposing the input.
    Future.delayed(const Duration(milliseconds: 400), edit.dispose);
    return result;
  }

  Widget rowMenu(AppState s, String table, int id, String title) =>
      PopupMenuButton<String>(
        enabled: !s.busy,
        tooltip: 'Actions',
        itemBuilder: (_) => const [
          PopupMenuItem(value: 'rename', child: Text('Renommer')),
          PopupMenuItem(value: 'delete', child: Text('Supprimer')),
        ],
        onSelected: (action) async {
          if (action == 'rename') {
            final name = await nameDialog('Renommer', title);
            if (name != null && name.trim().isNotEmpty) {
              await protect(() => s.rename(table, id, name));
            }
          } else {
            final yes = await showDialog<bool>(
              context: context,
              builder: (c) => AlertDialog(
                title: const Text('Supprimer ?'),
                content: Text(
                  table == 'projects'
                      ? 'Le projet et ses discussions seront supprimés.'
                      : 'Cette discussion et son historique seront supprimés.',
                ),
                actions: [
                  TextButton(
                    onPressed: () => Navigator.pop(c, false),
                    child: const Text('Annuler'),
                  ),
                  FilledButton(
                    onPressed: () => Navigator.pop(c, true),
                    child: const Text('Supprimer'),
                  ),
                ],
              ),
            );
            if (yes == true) await protect(() => s.remove(table, id));
          }
        },
      );
  Widget sidebar(AppState s) => Drawer(
    child: SafeArea(
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              children: [
                const BrandLogo(size: 40),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'Monolith',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                IconButton(
                  tooltip: 'Nouveau projet',
                  onPressed: s.busy
                      ? null
                      : () async {
                          final name = await nameDialog('Nouveau projet');
                          if (name != null && name.trim().isNotEmpty) {
                            await protect(() => s.newProject(name));
                          }
                        },
                  icon: const Icon(Icons.create_new_folder_outlined),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: FilledButton.tonalIcon(
              onPressed: s.busy
                  ? null
                  : () {
                      Navigator.pop(context);
                      protect(() => s.newChat());
                    },
              icon: const Icon(Icons.add),
              label: const Text('Nouvelle discussion'),
            ),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: ReorderableListView.builder(
              buildDefaultDragHandles: false,
              padding: const EdgeInsets.all(8),
              itemCount: s.projects.length,
              onReorderItem: (a, b) {
                final ids = s.projects.map((p) => p.id).toList();
                ids.insert(b, ids.removeAt(a));
                protect(() => s.reorder('projects', ids));
              },
              itemBuilder: (c, i) {
                final p = s.projects[i];
                return ExpansionTile(
                  key: ValueKey('project-${p.id}'),
                  initiallyExpanded: p.id == s.project?.id,
                  maintainState: true,
                  leading: ReorderableDelayedDragStartListener(
                    index: i,
                    child: const Tooltip(
                      message: 'Déplacer le projet',
                      triggerMode: TooltipTriggerMode.tap,
                      child: Icon(Icons.drag_indicator),
                    ),
                  ),
                  title: Text(p.name, overflow: TextOverflow.ellipsis),
                  trailing: rowMenu(s, 'projects', p.id, p.name),
                  children: [
                    ReorderableListView.builder(
                      shrinkWrap: true,
                      physics: const NeverScrollableScrollPhysics(),
                      buildDefaultDragHandles: false,
                      itemCount: s.chats[p.id]?.length ?? 0,
                      onReorderItem: (a, b) {
                        final ids = s.chats[p.id]!.map((t) => t.id).toList();
                        ids.insert(b, ids.removeAt(a));
                        protect(() => s.reorder('chats', ids));
                      },
                      itemBuilder: (c, j) {
                        final t = s.chats[p.id]![j];
                        return ListTile(
                          key: ValueKey('chat-${t.id}'),
                          selected: t.id == s.chat?.id,
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(20),
                          ),
                          leading: ReorderableDelayedDragStartListener(
                            index: j,
                            child: const Tooltip(
                              message: 'Déplacer la discussion',
                              triggerMode: TooltipTriggerMode.tap,
                              child: Icon(Icons.drag_indicator),
                            ),
                          ),
                          title: Text(
                            t.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                          trailing: rowMenu(s, 'chats', t.id, t.title),
                          enabled: !s.busy,
                          onTap: () {
                            Navigator.pop(context);
                            protect(() => s.select(t));
                          },
                        );
                      },
                    ),
                    ListTile(
                      leading: const Icon(Icons.add),
                      title: const Text('Nouvelle discussion'),
                      enabled: !s.busy,
                      onTap: () {
                        Navigator.pop(context);
                        protect(() => s.newChat(p.id));
                      },
                    ),
                  ],
                );
              },
            ),
          ),
          const Divider(height: 1),
          ListTile(
            leading: const Icon(Icons.settings_outlined),
            title: const Text('Paramètres'),
            onTap: () {
              Navigator.pop(context);
              Navigator.push(
                context,
                MaterialPageRoute(builder: (_) => const SettingsPage()),
              );
            },
          ),
        ],
      ),
    ),
  );
  Future<void> models(AppState s) async {
    await showModalBottomSheet(
      context: context,
      useSafeArea: true,
      isScrollControlled: true,
      builder: (c) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: .65,
        builder: (c, scroll) => ListView(
          controller: scroll,
          padding: const EdgeInsets.all(16),
          children: [
            Text('Choisir le modèle', style: Theme.of(c).textTheme.titleLarge),
            const SizedBox(height: 12),
            for (final p in s.providers) ...[
              ListTile(
                leading: const Icon(Icons.hub_outlined),
                title: Text(p.name),
                subtitle: Text(p.model),
                selected: s.provider?.id == p.id,
                onTap: () async {
                  await s.chooseProvider(p);
                  if (c.mounted) Navigator.pop(c);
                },
              ),
              if (s.provider?.id == p.id)
                for (final model in p.models)
                  ListTile(
                    title: Text(model),
                    selected: model == p.model,
                    trailing: model == p.model ? const Icon(Icons.check) : null,
                    onTap: () async {
                      await s.chooseModel(model);
                      if (c.mounted) Navigator.pop(c);
                    },
                  ),
            ],
            ListTile(
              leading: const Icon(Icons.add),
              title: const Text('Configurer un fournisseur'),
              onTap: () {
                Navigator.pop(c);
                Navigator.push(
                  context,
                  MaterialPageRoute(builder: (_) => const ProvidersPage()),
                );
              },
            ),
          ],
        ),
      ),
    );
  }

  List<Widget> history(AppState s) {
    final list = <Widget>[];
    List<ToolActivity>? group;
    final known = <String, Json>{};
    int user = 0;
    for (final m in s.messages) {
      if (m.role == 'user') {
        group = null;
        known.clear();
        user = m.id;
      }
      for (final raw in m.wire?['tool_calls'] as List? ?? []) {
        known[raw['id']] = Map<String, dynamic>.from(raw);
      }
      if (m.role == 'tool') {
        if (group == null) {
          group = [];
          if (s.settings.toolsVisible) {
            list.add(ToolBubble(group, key: ValueKey('tools-$user')));
          }
        }
        final call = known[m.wire?['tool_call_id']];
        Json args = {};
        try {
          args = Map<String, dynamic>.from(
            jsonDecode(call?['function']?['arguments'] ?? '{}'),
          );
        } catch (_) {}
        final activity =
            ToolActivity(
                m.wire?['tool_call_id'] ?? '${m.id}',
                call?['function']?['name'] ?? 'Outil',
                args,
              )
              ..result = m.text
              ..status = m.text.contains('"error"')
                  ? 'Refusé ou en erreur'
                  : 'Terminé';
        group.add(activity);
        continue;
      }
      if (m.text.isEmpty &&
          (!s.settings.reasoningVisible || m.reasoning.isEmpty)) {
        continue;
      }
      list.add(message(s, m));
    }
    for (final activity in s.activities) {
      if (group == null) {
        group = [];
        if (s.settings.toolsVisible) {
          list.add(ToolBubble(group, key: ValueKey('tools-live-$user')));
        }
      }
      final index = group.indexWhere((g) => g.id == activity.id);
      if (index >= 0) {
        group[index] = activity;
      } else {
        group.add(activity);
      }
    }
    return list;
  }

  Future<void> copyMessage(ChatMessage message) async {
    await protect(() async {
      await Clipboard.setData(ClipboardData(text: message.text));
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Message copié')));
      }
    });
  }

  Future<void> messageOptions(ChatMessage message) async {
    focus.unfocus();
    await showModalBottomSheet<void>(
      context: context,
      useSafeArea: true,
      showDragHandle: true,
      builder: (c) => SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: const EdgeInsets.all(16),
              child: Text(
                'Options du message',
                style: Theme.of(c).textTheme.titleMedium,
              ),
            ),
            ListTile(
              leading: const Icon(Icons.copy_outlined),
              title: const Text('Copier le message'),
              enabled: message.text.isNotEmpty,
              onTap: message.text.isEmpty
                  ? null
                  : () {
                      Navigator.pop(c);
                      copyMessage(message);
                    },
            ),
            const SizedBox(height: 16),
          ],
        ),
      ),
    );
  }

  Widget message(AppState s, ChatMessage m) {
    final user = m.role == 'user';
    final theme = Theme.of(context);
    return Align(
      key: ValueKey('message-${m.id}'),
      alignment: user ? Alignment.centerRight : Alignment.centerLeft,
      child: FractionallySizedBox(
        widthFactor: user ? .92 : 1,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onLongPress: () => messageOptions(m),
          child: Card.filled(
            color: user
                ? theme.colorScheme.primaryContainer
                : theme.colorScheme.surfaceContainerLow,
            margin: const EdgeInsets.symmetric(vertical: 6),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    user ? 'Vous' : 'Monolith',
                    style: theme.textTheme.labelMedium,
                  ),
                  const SizedBox(height: 8),
                  if (!user &&
                      s.settings.reasoningVisible &&
                      m.reasoning.isNotEmpty)
                    ExpansionTile(
                      title: Text(
                        m.status == 'generating' && m.text.isEmpty
                            ? reasoningActivity(m.reasoning)
                            : 'Raisonnement',
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                      children: [
                        Padding(
                          padding: const EdgeInsets.all(12),
                          child: SelectableText(m.reasoning),
                        ),
                      ],
                    ),
                  SelectionArea(
                    contextMenuBuilder: (c, selection) =>
                        AdaptiveTextSelectionToolbar.buttonItems(
                          anchors: selection.contextMenuAnchors,
                          buttonItems: [
                            if (m.text.isNotEmpty)
                              ContextMenuButtonItem(
                                label: 'Copier le message',
                                onPressed: () {
                                  selection.hideToolbar();
                                  copyMessage(m);
                                },
                              ),
                            for (final item in selection.contextMenuButtonItems)
                              if (item.type == ContextMenuButtonType.copy)
                                ContextMenuButtonItem(
                                  label: 'Copier la sélection',
                                  onPressed: item.onPressed,
                                )
                              else
                                item,
                          ],
                        ),
                    child: ReplyView(
                      m.text,
                      settings: s.settings,
                      complete: m.status != 'generating',
                      selectable: false,
                    ),
                  ),
                  if (user && m.wire?['content'] is List)
                    for (final part in m.wire!['content'])
                      if (part['type'] == 'image_url')
                        Padding(
                          padding: const EdgeInsets.only(top: 8),
                          child: SafeImage(
                            part['image_url']['url'],
                            network: s.settings.network,
                          ),
                        ),
                  if (m.status == 'interrupted')
                    const Padding(
                      padding: EdgeInsets.only(top: 12),
                      child: Text(
                        'Réponse interrompue',
                        style: TextStyle(fontStyle: FontStyle.italic),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = context.watch<AppState>();
    s.approve = approval;
    s.dark = Theme.of(context).brightness == Brightness.dark;
    if (editingChat != s.chat?.id ||
        (!s.busy && s.draft.isEmpty && input.text.isNotEmpty)) {
      editingChat = s.chat?.id;
      input.value = TextEditingValue(
        text: s.draft,
        selection: TextSelection.collapsed(offset: s.draft.length),
      );
    }
    final items = history(s), available = suggestions(s);
    final attachments = s.images.length + s.documents.length;
    return Scaffold(
      key: scaffold,
      onDrawerChanged: (open) {
        if (open) focus.unfocus();
      },
      drawer: sidebar(s),
      appBar: AppBar(
        titleSpacing: 0,
        title: TextButton.icon(
          onPressed: s.busy ? null : () => models(s),
          icon: const Icon(Icons.expand_more),
          iconAlignment: IconAlignment.end,
          label: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                s.provider?.model ?? 'Choisir un modèle',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              Text(
                s.provider?.name ?? 'Fournisseur API',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.labelSmall,
              ),
            ],
          ),
        ),
        actions: [
          IconButton(
            tooltip: 'Paramètres',
            onPressed: () => Navigator.push(
              context,
              MaterialPageRoute(builder: (_) => const SettingsPage()),
            ),
            icon: const Icon(Icons.settings_outlined),
          ),
        ],
      ),
      body: SafeArea(
        top: false,
        child: LayoutBuilder(
          builder: (context, space) => Column(
            children: [
              if (s.error.isNotEmpty)
                MaterialBanner(
                  content: Text(
                    s.error,
                    maxLines: 4,
                    overflow: TextOverflow.ellipsis,
                  ),
                  leading: const Icon(Icons.error_outline),
                  actions: [
                    TextButton(
                      onPressed: () {
                        s.clearError();
                      },
                      child: const Text('Fermer'),
                    ),
                  ],
                ),
              Expanded(
                child: Align(
                  alignment: Alignment.topCenter,
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 880),
                    child: items.isEmpty
                        ? LayoutBuilder(
                            builder: (c, b) => SingleChildScrollView(
                              child: Padding(
                                padding: const EdgeInsets.all(28),
                                child: Column(
                                  children: [
                                    if (b.maxHeight > 300) ...[
                                      const BrandLogo(size: 100),
                                      const SizedBox(height: 24),
                                    ],
                                    Text(
                                      'Un espace pour vos idées.',
                                      style: Theme.of(c)
                                          .textTheme
                                          .headlineSmall,
                                      textAlign: TextAlign.center,
                                    ),
                                    const SizedBox(height: 12),
                                    const Text(
                                      'Discutez, explorez vos documents ou créez une application web.',
                                      textAlign: TextAlign.center,
                                    ),
                                    const SizedBox(height: 24),
                                    FilledButton.icon(
                                      onPressed: s.busy
                                          ? null
                                          : () => protect(() => s.newChat()),
                                      icon: const Icon(Icons.add),
                                      label: const Text('Nouvelle discussion'),
                                    ),
                                    if (s.providers.isEmpty)
                                      TextButton(
                                        onPressed: () => editProvider(context),
                                        child: const Text(
                                          'Configurer un fournisseur',
                                        ),
                                      ),
                                  ],
                                ),
                              ),
                            ),
                          )
                        : ListView.builder(
                            reverse: true,
                            padding: const EdgeInsets.symmetric(
                              horizontal: 12,
                              vertical: 8,
                            ),
                            itemCount: items.length,
                            itemBuilder: (c, i) => items[items.length - i - 1],
                          ),
                  ),
                ),
              ),
              if (s.status.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 20,
                    vertical: 6,
                  ),
                  child: Row(
                    children: [
                      if (s.busy) ...[
                        const SizedBox(
                          height: 14,
                          width: 14,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        ),
                        const SizedBox(width: 8),
                      ],
                      Expanded(
                        child: Text(
                          s.status,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.labelMedium,
                        ),
                      ),
                    ],
                  ),
                ),
              ConstrainedBox(
                constraints: BoxConstraints(
                  maxWidth: 880,
                  maxHeight: (space.maxHeight * .55).clamp(60.0, 340.0),
                ),
                child: SingleChildScrollView(
                  reverse: true,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(12, 4, 12, 8),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (available.isNotEmpty && !s.busy)
                          ConstrainedBox(
                            constraints: const BoxConstraints(maxHeight: 180),
                            child: Card.filled(
                              child: ListView(
                                shrinkWrap: true,
                                children: [
                                  for (final skill in available)
                                    ListTile(
                                      dense: true,
                                      leading: const Icon(
                                        Icons.auto_awesome,
                                        size: 18,
                                      ),
                                      title: Text('/${skill.id}'),
                                      subtitle: Text(skill.label),
                                      onTap: () => completeSkill(skill),
                                    ),
                                ],
                              ),
                            ),
                          ),
                        if (attachments > 0)
                          Align(
                            alignment: Alignment.centerLeft,
                            child: TextButton.icon(
                              onPressed: () => setState(
                                () => attachmentsOpen = !attachmentsOpen,
                              ),
                              icon: Icon(
                                attachmentsOpen
                                    ? Icons.expand_less
                                    : Icons.expand_more,
                              ),
                              label: Text(
                                attachmentsOpen
                                    ? 'Pièces jointes ($attachments)'
                                    : 'Voir pièces jointes ($attachments)',
                              ),
                            ),
                          ),
                        if (attachments > 0 && attachmentsOpen)
                          SizedBox(
                            height: 46,
                            child: ListView(
                              scrollDirection: Axis.horizontal,
                              children: [
                                for (var i = 0; i < s.images.length; i++)
                                  Padding(
                                    padding: const EdgeInsets.only(right: 8),
                                    child: InputChip(
                                      avatar: const Icon(Icons.image_outlined),
                                      label: Text(s.images[i]['name']),
                                      onDeleted: s.busy
                                          ? null
                                          : () => s.removeImage(i),
                                    ),
                                  ),
                                for (final d in s.documents)
                                  Padding(
                                    padding: const EdgeInsets.only(right: 8),
                                    child: ActionChip(
                                      avatar: const Icon(
                                        Icons.description_outlined,
                                      ),
                                      label: Text(d['path']),
                                      onPressed: () => documents(s),
                                    ),
                                  ),
                              ],
                            ),
                          ),
                        Row(
                          crossAxisAlignment: CrossAxisAlignment.end,
                          children: [
                            IconButton(
                              tooltip: 'Ajouter une pièce jointe',
                              onPressed: s.busy ? null : () => attach(s),
                              icon: const Icon(Icons.add),
                            ),
                            Expanded(
                              child: Focus(
                                onKeyEvent: (node, event) {
                                  if (event is KeyDownEvent &&
                                      available.isNotEmpty &&
                                      (event.logicalKey ==
                                              LogicalKeyboardKey.tab ||
                                          event.logicalKey ==
                                              LogicalKeyboardKey.enter)) {
                                    completeSkill(available.first);
                                    return KeyEventResult.handled;
                                  }
                                  return KeyEventResult.ignored;
                                },
                                child: TextField(
                                  controller: input,
                                  focusNode: focus,
                                  enabled: !s.busy,
                                  minLines: 1,
                                  maxLines: 6,
                                  keyboardType: TextInputType.multiline,
                                  textInputAction: TextInputAction.newline,
                                  decoration: const InputDecoration(
                                    hintText: 'Écrivez votre message…',
                                    filled: true,
                                    border: OutlineInputBorder(
                                      borderRadius: BorderRadius.all(
                                        Radius.circular(28),
                                      ),
                                      borderSide: BorderSide.none,
                                    ),
                                    contentPadding: EdgeInsets.symmetric(
                                      horizontal: 18,
                                      vertical: 14,
                                    ),
                                  ),
                                  onChanged: (v) {
                                    s.editDraft(v);
                                    setState(() {});
                                  },
                                ),
                              ),
                            ),
                            const SizedBox(width: 8),
                            IconButton.filled(
                              tooltip: s.busy ? 'Arrêter' : 'Envoyer',
                              onPressed: s.busy
                                  ? s.stop
                                  : (input.text.trim().isNotEmpty ||
                                        s.images.isNotEmpty)
                                  ? () => protect(s.send)
                                  : null,
                              icon: Icon(
                                s.busy ? Icons.stop : Icons.arrow_upward,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class ToolBubble extends StatelessWidget {
  final List<ToolActivity> items;
  const ToolBubble(this.items, {super.key});
  @override
  Widget build(BuildContext context) => Card.outlined(
    margin: const EdgeInsets.symmetric(vertical: 6),
    child: ExpansionTile(
      leading: const Icon(Icons.build_circle_outlined),
      title: Text(
        '${items.length} outil${items.length > 1 ? 's' : ''} utilisé${items.length > 1 ? 's' : ''}',
      ),
      subtitle: Text(
        items.isEmpty ? '' : '${items.last.name} · ${items.last.status}',
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      ),
      children: [
        for (final item in items)
          ExpansionTile(
            title: Text(item.name),
            subtitle: Text(item.status),
            children: [
              Padding(
                padding: const EdgeInsets.all(16),
                child: SelectableText(
                  'Arguments\n${const JsonEncoder.withIndent('  ').convert(item.args)}\n\nRésultat\n${item.result}',
                  style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
                ),
              ),
            ],
          ),
      ],
    ),
  );
}
