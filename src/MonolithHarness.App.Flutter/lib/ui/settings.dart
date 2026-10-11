import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:package_info_plus/package_info_plus.dart';

import 'brand_logo.dart';

import '../app_state.dart';
import '../domain/models.dart';

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key});
  @override
  Widget build(BuildContext context) {
    final state = context.watch<AppState>();
    const items = [
      ('Général', Icons.tune, 'Langue, thème et taille du texte'),
      (
        'Chat',
        Icons.chat_bubble_outline,
        'Réponses, raisonnement et bibliothèques HTML',
      ),
      ('Fournisseurs', Icons.hub_outlined, 'Modèles API et clés sécurisées'),
      (
        'Skills',
        Icons.auto_awesome_outlined,
        'Synthèse, fichiers, web et informations',
      ),
      (
        'Autorisations',
        Icons.shield_outlined,
        'Approbation et limites des outils',
      ),
      (
        'Mémoire',
        Icons.psychology_outlined,
        'Souvenirs de conversation et de projet',
      ),
      ('À propos', Icons.info_outline, 'Version et licences'),
    ];
    return Scaffold(
      appBar: AppBar(title: const Text('Paramètres')),
      body: ListView(
        padding: const EdgeInsets.all(12),
        children: [
          for (final item in items)
            Card.filled(
              child: ListTile(
                leading: Icon(item.$2),
                title: Text(item.$1),
                subtitle: Text(item.$3),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => Navigator.of(context).push(
                  MaterialPageRoute(
                    builder: (_) => item.$1 == 'Fournisseurs'
                        ? const ProvidersPage()
                        : item.$1 == 'Mémoire'
                        ? const MemoryPage()
                        : item.$1 == 'À propos'
                        ? const AboutPage()
                        : SettingsSection(item.$1),
                  ),
                ),
              ),
            ),
          if (state.busy)
            const Padding(
              padding: EdgeInsets.all(16),
              child: Text(
                'Les paramètres modifiés seront utilisés au prochain message.',
              ),
            ),
        ],
      ),
    );
  }
}

class SettingsSection extends StatelessWidget {
  final String section;
  const SettingsSection(this.section, {super.key});
  @override
  Widget build(BuildContext context) {
    final state = context.watch<AppState>(), s = state.settings;
    void change(void Function(AppSettings) f) {
      final copy = s.copy();
      f(copy);
      state.updateSettings(copy);
    }

    Widget toggle(
      String title,
      String help,
      bool value,
      void Function(AppSettings, bool) f,
    ) => SwitchListTile(
      title: Text(title),
      subtitle: Text(help),
      value: value,
      onChanged: (v) => change((s) => f(s, v)),
    );
    final content = <Widget>[];
    if (section == 'Général') {
      content.addAll([
        ListTile(
          title: const Text('Langue des réponses'),
          subtitle: DropdownButton<String>(
            isExpanded: true,
            value: s.language,
            items: const [
              DropdownMenuItem(value: 'auto', child: Text('Automatique')),
              DropdownMenuItem(value: 'fr', child: Text('Français')),
              DropdownMenuItem(value: 'en', child: Text('English')),
            ],
            onChanged: (v) => change((s) => s.language = v!),
          ),
        ),
        ListTile(
          title: const Text('Thème'),
          subtitle: DropdownButton<String>(
            isExpanded: true,
            value: s.theme,
            items: const [
              DropdownMenuItem(value: 'system', child: Text('Suivre Android')),
              DropdownMenuItem(value: 'light', child: Text('Clair')),
              DropdownMenuItem(value: 'dark', child: Text('Sombre')),
            ],
            onChanged: (v) => change((s) => s.theme = v!),
          ),
        ),
        ListTile(
          title: Text('Taille du texte · ${s.textSize.round()}'),
          subtitle: Slider(
            min: 14,
            max: 22,
            divisions: 8,
            value: s.textSize,
            onChanged: (v) => change((s) => s.textSize = v),
          ),
        ),
      ]);
    } else if (section == 'Chat') {
      content.addAll([
        toggle(
          'Afficher les outils',
          'Une bulle compacte et dépliable par tour.',
          s.toolsVisible,
          (s, v) => s.toolsVisible = v,
        ),
        toggle(
          'Détails du raisonnement',
          'Affiche le raisonnement fourni par le modèle, dans une section dépliable.',
          s.reasoningVisible,
          (s, v) => s.reasoningVisible = v,
        ),
        toggle(
          'Tailwind CSS',
          'Copie locale pour les réponses HTML, sans CDN.',
          s.tailwind,
          (s, v) => s.tailwind = v,
        ),
        toggle(
          'Chart.js',
          'Graphiques interactifs dans les aperçus plein écran.',
          s.chartJs,
          (s, v) => s.chartJs = v,
        ),
        ListTile(
          title: Text('Tours d’outils maximum · ${s.maxRounds}'),
          subtitle: Slider(
            min: 1,
            max: 16,
            divisions: 15,
            value: s.maxRounds.toDouble(),
            onChanged: (v) => change((s) => s.maxRounds = v.round()),
          ),
        ),
      ]);
    } else if (section == 'Skills') {
      content.add(
        const Padding(
          padding: EdgeInsets.all(16),
          child: Text(
            'Insérez /nom-du-skill dans votre message. La sélection complète le texte ; le skill est demandé uniquement à l’envoi.',
          ),
        ),
      );
      for (final k in skills) {
        content.add(
          toggle(k.label, k.description, s.skills.contains(k.id), (s, v) {
            if (v) {
              s.skills.add(k.id);
            } else {
              s.skills.remove(k.id);
            }
          }),
        );
      }
      content.add(const Divider());
      content.add(const ListTile(title: Text('Informations · catégories')));
      content.addAll([
        toggle(
          'Informations automatiques',
          'Ajoute les catégories choisies à chaque message.',
          s.automaticInfo,
          (s, v) => s.automaticInfo = v,
        ),
        toggle(
          'Date et heure',
          'Date et heures locale/UTC actualisées.',
          s.infoDate,
          (s, v) => s.infoDate = v,
        ),
        toggle(
          'Fuseau horaire',
          'Nom et décalage UTC.',
          s.infoZone,
          (s, v) => s.infoZone = v,
        ),
        toggle(
          'Système Android',
          'Version Android et niveau API, sans identifiant.',
          s.infoSystem,
          (s, v) => s.infoSystem = v,
        ),
        toggle(
          'Langue et région',
          'Langue et formats régionaux.',
          s.infoLocale,
          (s, v) => s.infoLocale = v,
        ),
        ListTile(
          leading: const Icon(Icons.visibility_outlined),
          title: const Text('Voir les informations envoyées'),
          onTap: () async {
            final info = await state.information();
            if (context.mounted) {
              showDialog(
                context: context,
                builder: (_) => AlertDialog(
                  title: const Text('Informations'),
                  content: SingleChildScrollView(
                    child: SelectableText(
                      const JsonEncoder.withIndent('  ').convert(info),
                    ),
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.pop(context),
                      child: const Text('Fermer'),
                    ),
                  ],
                ),
              );
            }
          },
        ),
      ]);
    } else {
      content.addAll([
        ListTile(
          title: const Text('Approbation des outils'),
          subtitle: DropdownButton<ApprovalMode>(
            isExpanded: true,
            value: s.approval,
            items: [
              const DropdownMenuItem(
                value: ApprovalMode.deny,
                child: Text('Refuser tout'),
              ),
              const DropdownMenuItem(
                value: ApprovalMode.ask,
                child: Text('Demander tout'),
              ),
              DropdownMenuItem(
                value: ApprovalMode.allow,
                child: Text(
                  'Autoriser tout',
                  style: TextStyle(color: Colors.orange.shade700),
                ),
              ),
            ],
            onChanged: (v) => change((s) => s.approval = v!),
          ),
        ),
        Padding(
          padding: const EdgeInsets.all(16),
          child: Text(
            s.approval == ApprovalMode.allow
                ? 'Les outils activés peuvent rechercher, lire et modifier leurs données sans confirmation. Les restrictions ci-dessous restent appliquées.'
                : s.approval == ApprovalMode.deny
                ? 'Tous les appels d’outils sont refusés. Le chat reste disponible.'
                : 'Demande votre accord avant chaque appel d’outil. Mode par défaut.',
          ),
        ),
        toggle(
          'Autoriser le web',
          'Pages HTTPS publiques et images distantes. Les pages lues sont envoyées au modèle.',
          s.network,
          (s, v) => s.network = v,
        ),
        toggle(
          'Modifier les documents',
          'Uniquement les copies privées de cette discussion. Export manuel vers Android.',
          s.fileWrites,
          (s, v) => s.fileWrites = v,
        ),
        toggle(
          'Mémoire partagée',
          'Autorise les souvenirs visibles entre les projets.',
          s.sharedMemory,
          (s, v) => s.sharedMemory = v,
        ),
      ]);
    }
    return Scaffold(
      appBar: AppBar(title: Text(section)),
      body: ListView(
        padding: const EdgeInsets.symmetric(vertical: 12),
        children: content,
      ),
    );
  }
}

class ProvidersPage extends StatelessWidget {
  const ProvidersPage({super.key});
  @override
  Widget build(BuildContext context) {
    final s = context.watch<AppState>();
    return Scaffold(
      appBar: AppBar(
        title: const Text('Fournisseurs'),
        actions: [
          IconButton(
            tooltip: 'Ajouter un fournisseur',
            onPressed: s.busy ? null : () => editProvider(context),
            icon: const Icon(Icons.add),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const Text(
            'Routes OpenAI compatibles (HTTPS). Les clés sont conservées dans le stockage sécurisé Android et ne figurent ni dans l’historique ni dans les exports.',
          ),
          const SizedBox(height: 16),
          for (final p in s.providers)
            Card.filled(
              child: ListTile(
                leading: const Icon(Icons.hub_outlined),
                title: Text(p.name),
                subtitle: Text('${p.model}\n${p.url}'),
                isThreeLine: true,
                trailing: const Icon(Icons.edit_outlined),
                onTap: s.busy ? null : () => editProvider(context, p),
              ),
            ),
          if (s.providers.isEmpty)
            FilledButton.icon(
              onPressed: () => editProvider(context),
              icon: const Icon(Icons.add),
              label: const Text('Ajouter un fournisseur'),
            ),
        ],
      ),
    );
  }
}

Future<void> editProvider(BuildContext context, [ProviderConfig? initial]) =>
    Navigator.of(context)
        .push(MaterialPageRoute(builder: (_) => ProviderEditor(initial)))
        .then((_) {});

class ProviderEditor extends StatefulWidget {
  final ProviderConfig? initial;
  const ProviderEditor(this.initial, {super.key});
  @override
  State<ProviderEditor> createState() => _ProviderEditorState();
}

class _ProviderEditorState extends State<ProviderEditor> {
  final form = GlobalKey<FormState>();
  late TextEditingController name, url, model;
  final keyInput = TextEditingController();
  List<String> models = [];
  bool working = false, images = true;
  String error = '';
  @override
  void initState() {
    super.initState();
    name = TextEditingController(text: widget.initial?.name ?? '');
    url = TextEditingController(
      text: widget.initial?.url ?? 'https://api.deepseek.com/v1',
    );
    model = TextEditingController(
      text: widget.initial?.model ?? 'deepseek-chat',
    );
    models = widget.initial?.models ?? [];
    images = widget.initial?.images ?? true;
  }

  ProviderConfig value() => ProviderConfig(
    id: widget.initial?.id ?? 0,
    name: name.text.trim(),
    url: url.text.trim(),
    model: model.text.trim(),
    models: models,
    images: images,
  );
  @override
  void dispose() {
    name.dispose();
    url.dispose();
    model.dispose();
    keyInput.dispose();
    super.dispose();
  }

  Future<void> detect() async {
    setState(() => working = true);
    try {
      final list = await context.read<AppState>().detectModels(
        value(),
        keyInput.text.trim(),
      );
      if (mounted) setState(() => models = list);
    } catch (ex) {
      if (mounted) {
        setState(
          () => error =
              'Chargement impossible : ${ex.toString().replaceAll(keyInput.text.isEmpty ? '\x00' : keyInput.text, '[clé masquée]')}',
        );
      }
    } finally {
      if (mounted) setState(() => working = false);
    }
  }

  Future<void> save() async {
    if (!form.currentState!.validate()) return;
    setState(() => working = true);
    try {
      await context.read<AppState>().saveProvider(
        value(),
        keyInput.text.trim(),
      );
      if (mounted) Navigator.pop(context);
    } catch (ex) {
      if (mounted) setState(() => error = ex.toString());
    } finally {
      if (mounted) setState(() => working = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        widget.initial == null
            ? 'Ajouter un fournisseur'
            : 'Modifier le fournisseur',
      ),
    ),
    body: Form(
      key: form,
      child: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          TextFormField(
            controller: name,
            decoration: const InputDecoration(
              labelText: 'Nom du fournisseur',
              hintText: 'DeepSeek',
            ),
            validator: (v) => (v?.trim().isEmpty ?? true) ? 'Nom requis' : null,
          ),
          const SizedBox(height: 18),
          TextFormField(
            controller: url,
            keyboardType: TextInputType.url,
            autocorrect: false,
            decoration: const InputDecoration(
              labelText: 'URL API HTTPS',
              helperText: 'Inclure la route /v1 si nécessaire',
            ),
          ),
          const SizedBox(height: 18),
          TextFormField(
            controller: keyInput,
            obscureText: true,
            enableSuggestions: false,
            autocorrect: false,
            decoration: InputDecoration(
              labelText: 'Clé API',
              helperText: widget.initial == null
                  ? 'Chiffrée par Android Keystore'
                  : 'Vide : conserver la clé actuelle',
            ),
          ),
          const SizedBox(height: 18),
          OutlinedButton.icon(
            onPressed: working ? null : detect,
            icon: const Icon(Icons.cloud_download_outlined),
            label: const Text('Charger les modèles'),
          ),
          const SizedBox(height: 18),
          if (models.isNotEmpty)
            DropdownButtonFormField<String>(
              initialValue: models.contains(model.text) ? model.text : null,
              isExpanded: true,
              decoration: const InputDecoration(
                labelText: 'Modèles disponibles',
              ),
              items: models
                  .map(
                    (m) => DropdownMenuItem(
                      value: m,
                      child: Text(m, overflow: TextOverflow.ellipsis),
                    ),
                  )
                  .toList(),
              onChanged: (v) {
                if (v != null) model.text = v;
              },
            ),
          const SizedBox(height: 18),
          TextFormField(
            controller: model,
            autocorrect: false,
            decoration: const InputDecoration(
              labelText: 'Identifiant du modèle',
            ),
            validator: (v) =>
                (v?.trim().isEmpty ?? true) ? 'Modèle requis' : null,
          ),
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            title: const Text('Ce fournisseur accepte les images'),
            value: images,
            onChanged: (v) => setState(() => images = v),
          ),
          if (error.isNotEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 12),
              child: Text(
                error,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          if (working) const LinearProgressIndicator(),
          FilledButton.icon(
            onPressed: working ? null : save,
            icon: const Icon(Icons.check),
            label: const Text('Enregistrer'),
          ),
          if (widget.initial != null)
            TextButton.icon(
              onPressed: working
                  ? null
                  : () async {
                      final ok = await showDialog<bool>(
                        context: context,
                        builder: (c) => AlertDialog(
                          title: const Text('Supprimer le fournisseur ?'),
                          content: const Text(
                            'La clé sera aussi supprimée. Les discussions sont conservées.',
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
                      if (ok == true && context.mounted) {
                        await context.read<AppState>().remove(
                          'providers',
                          widget.initial!.id,
                        );
                        if (context.mounted) Navigator.pop(context);
                      }
                    },
              icon: const Icon(Icons.delete_outline),
              label: const Text('Supprimer ce fournisseur'),
            ),
        ],
      ),
    ),
  );
}

class MemoryPage extends StatefulWidget {
  const MemoryPage({super.key});
  @override
  State<MemoryPage> createState() => _MemoryPageState();
}

class _MemoryPageState extends State<MemoryPage> {
  final query = TextEditingController(), content = TextEditingController();
  String scope = 'conversation', error = '';
  List<Json> rows = [];
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    query.dispose();
    content.dispose();
    super.dispose();
  }

  Future<void> load() async {
    final s = context.read<AppState>();
    if (s.chat == null) return;
    final result = await s.store.memories(
      s.chat!.projectId,
      s.chat!.id,
      s.settings.sharedMemory,
      query.text,
    );
    if (mounted) setState(() => rows = result);
  }

  Future<void> save() async {
    final s = context.read<AppState>();
    try {
      await s.store.saveMemory(
        s.chat!.projectId,
        s.chat!.id,
        s.settings.sharedMemory,
        scope,
        content.text,
      );
      content.clear();
      await load();
    } catch (ex) {
      if (mounted) setState(() => error = ex.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = context.watch<AppState>();
    return Scaffold(
      appBar: AppBar(title: const Text('Mémoire')),
      body: s.chat == null
          ? const Center(
              child: Text('Créez une discussion pour gérer sa mémoire.'),
            )
          : ListView(
              padding: const EdgeInsets.all(20),
              children: [
                const Text(
                  'Souvenirs locaux visibles dans cette discussion et son projet. Ne mémorisez pas de clés ou de mots de passe.',
                ),
                const SizedBox(height: 16),
                TextField(
                  controller: query,
                  decoration: InputDecoration(
                    labelText: 'Rechercher',
                    suffixIcon: IconButton(
                      onPressed: load,
                      icon: const Icon(Icons.search),
                    ),
                  ),
                  onSubmitted: (_) => load(),
                ),
                const SizedBox(height: 16),
                for (final row in rows)
                  Card.filled(
                    child: ListTile(
                      title: Text(row['content']),
                      subtitle: Text('${row['scope']} · #${row['id']}'),
                      trailing: IconButton(
                        tooltip: 'Supprimer',
                        icon: const Icon(Icons.delete_outline),
                        onPressed: () async {
                          await s.store.deleteMemory(
                            s.chat!.projectId,
                            s.chat!.id,
                            s.settings.sharedMemory,
                            row['id'],
                          );
                          await load();
                        },
                      ),
                    ),
                  ),
                const SizedBox(height: 16),
                DropdownButtonFormField<String>(
                  initialValue: scope,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Portée'),
                  items: [
                    const DropdownMenuItem(
                      value: 'conversation',
                      child: Text('Conversation'),
                    ),
                    const DropdownMenuItem(
                      value: 'project',
                      child: Text('Projet'),
                    ),
                    if (s.settings.sharedMemory)
                      const DropdownMenuItem(
                        value: 'shared',
                        child: Text('Partagée'),
                      ),
                  ],
                  onChanged: (v) => setState(() => scope = v!),
                ),
                const SizedBox(height: 16),
                TextField(
                  controller: content,
                  minLines: 2,
                  maxLines: 5,
                  maxLength: 4000,
                  decoration: const InputDecoration(
                    labelText: 'Nouveau souvenir',
                  ),
                ),
                if (error.isNotEmpty) Text(error),
                FilledButton.icon(
                  onPressed: save,
                  icon: const Icon(Icons.add),
                  label: const Text('Enregistrer le souvenir'),
                ),
              ],
            ),
    );
  }
}

class AboutPage extends StatelessWidget {
  const AboutPage({super.key});
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('À propos')),
    body: ListView(
      padding: const EdgeInsets.all(24),
      children: [
        const Center(child: BrandLogo(size: 96)),
        const SizedBox(height: 16),
        Text(
          'Monolith',
          style: Theme.of(context).textTheme.headlineMedium,
          textAlign: TextAlign.center,
        ),
        FutureBuilder(
          future: PackageInfo.fromPlatform(),
          builder: (c, s) => Text(
            'Flutter · ${s.data?.version ?? '1.89.0'}',
            textAlign: TextAlign.center,
          ),
        ),
        const SizedBox(height: 24),
        const Text(
          'Chat API, projets et discussions, fichiers privés, mémoire, skills, images, Markdown et applications web plein écran. Les aperçus web fonctionnent sans réseau, fichiers ni accès natifs. Leur état est réinitialisé à la fermeture.\n\nLes documents texte peuvent être modifiés dans leur copie privée puis exportés. Les PDF/Office, commandes système, modèles locaux, MCP, Mermaid et synchronisation desktop/mobile ne sont pas inclus, comme dans la version Android initiale.',
        ),
        const SizedBox(height: 24),
        OutlinedButton.icon(
          onPressed: () =>
              showLicensePage(context: context, applicationName: 'Monolith'),
          icon: const Icon(Icons.description_outlined),
          label: const Text('Licences des composants'),
        ),
      ],
    ),
  );
}
