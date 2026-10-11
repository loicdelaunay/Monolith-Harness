import 'dart:convert';

import 'package:sqflite/sqflite.dart';

import '../domain/models.dart';

class Store {
  final Database db;
  Store(this.db);
  static Future<Store> open(String path, {DatabaseFactory? factory}) async {
    final db = await (factory ?? databaseFactory).openDatabase(
      path,
      options: OpenDatabaseOptions(
        version: 2,
        onConfigure: (d) => d.execute('PRAGMA foreign_keys=ON'),
        onCreate: (d, _) async {
          await _create(d);
          await d.insert('projects', {
            'name': 'Mes discussions',
            'position': 0,
          });
        },
        onUpgrade: (d, old, _) async {
          if (old < 2) {
            const names = [
              'projects',
              'chats',
              'messages',
              'providers',
              'memories',
            ];
            for (final name in names) {
              await d.execute(
                'CREATE TEMP TABLE migration_$name AS SELECT * FROM $name',
              );
            }
            for (final name in [
              'messages',
              'chats',
              'projects',
              'providers',
              'memories',
            ]) {
              await d.execute('DROP TABLE $name');
            }
            await _create(d, preferences: false);
            for (final name in names) {
              await d.execute(
                'INSERT INTO $name SELECT * FROM migration_$name',
              );
              await d.execute('DROP TABLE migration_$name');
            }
          }
        },
      ),
    );
    await db.update(
      'messages',
      {'status': 'interrupted'},
      where: 'status=?',
      whereArgs: ['generating'],
    );
    return Store(db);
  }

  static Future<void> _create(
    DatabaseExecutor d, {
    bool preferences = true,
  }) async {
    const tables = {
      'projects': 'id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,position INTEGER NOT NULL DEFAULT 0',
      'chats': "id INTEGER PRIMARY KEY AUTOINCREMENT,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,title TEXT NOT NULL,draft TEXT NOT NULL DEFAULT '',provider_id INTEGER,position INTEGER NOT NULL DEFAULT 0,updated TEXT NOT NULL",
      'messages': 'id INTEGER PRIMARY KEY AUTOINCREMENT,chat_id INTEGER NOT NULL REFERENCES chats(id) ON DELETE CASCADE,role TEXT NOT NULL,text TEXT NOT NULL,reasoning TEXT NOT NULL,status TEXT NOT NULL,wire TEXT',
      'providers': 'id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,url TEXT NOT NULL,model TEXT NOT NULL,models TEXT NOT NULL,images INTEGER NOT NULL',
      'preferences': 'key TEXT PRIMARY KEY,value TEXT NOT NULL',
      'memories': 'id INTEGER PRIMARY KEY AUTOINCREMENT,scope TEXT NOT NULL,project_id INTEGER NOT NULL,chat_id INTEGER NOT NULL,content TEXT NOT NULL,updated TEXT NOT NULL',
    };
    for (final entry in tables.entries) {
      if (entry.key == 'preferences' && !preferences) continue;
      await d.execute('CREATE TABLE ${entry.key}(${entry.value})');
    }
  }

  Future<AppSettings> settings() async {
    final rows = await db.query(
      'preferences',
      where: 'key=?',
      whereArgs: ['settings'],
    );
    return rows.isEmpty
        ? AppSettings()
        : AppSettings.fromJson(
            Map<String, dynamic>.from(
              jsonDecode(rows.first['value'] as String),
            ),
          );
  }

  Future<void> saveSettings(AppSettings s) => db
      .insert('preferences', {
        'key': 'settings',
        'value': jsonEncode(s.toJson()),
      }, conflictAlgorithm: ConflictAlgorithm.replace)
      .then((_) {});
  Future<List<Project>> projects() async => (await db.query(
    'projects',
    orderBy: 'position,id',
  )).map(Project.fromJson).toList();
  Future<List<ChatThread>> chats(int project) async => (await db.query(
    'chats',
    where: 'project_id=?',
    whereArgs: [project],
    orderBy: 'position,updated DESC',
  )).map(ChatThread.fromJson).toList();
  Future<List<ProviderConfig>> providers() async => (await db.query(
    'providers',
    orderBy: 'id',
  )).map(ProviderConfig.fromJson).toList();
  Future<List<ChatMessage>> messages(int chat) async => (await db.query(
    'messages',
    where: 'chat_id=?',
    whereArgs: [chat],
    orderBy: 'id',
  )).map(ChatMessage.fromJson).toList();
  Future<int> createProject(String name) async {
    if (name.trim().isEmpty) throw const FormatException('Nom requis.');
    return db.insert('projects', {
      'name': name.trim(),
      'position': DateTime.now().millisecondsSinceEpoch,
    });
  }

  Future<int> createChat(int project, {int? provider}) => db.insert('chats', {
    'project_id': project,
    'title': 'Nouvelle discussion',
    'provider_id': provider,
    'updated': DateTime.now().toUtc().toIso8601String(),
    'position': DateTime.now().millisecondsSinceEpoch,
  });
  Future<void> rename(String table, int id, String text) async {
    if (!['projects', 'chats'].contains(table) || text.trim().isEmpty) {
      throw const FormatException('Nom requis.');
    }
    await db.update(
      table,
      {table == 'projects' ? 'name' : 'title': text.trim()},
      where: 'id=?',
      whereArgs: [id],
    );
  }

  Future<void> remove(String table, int id) async {
    if (!['projects', 'chats', 'providers'].contains(table)) {
      throw ArgumentError('Table invalide');
    }
    await db.transaction((txn) async {
      if (table == 'chats') {
        await txn.delete(
          'memories',
          where: "scope='conversation' AND chat_id=?",
          whereArgs: [id],
        );
      } else if (table == 'projects') {
        await txn.delete(
          'memories',
          where: "(scope='project' AND project_id=?) OR (scope='conversation' AND chat_id IN (SELECT id FROM chats WHERE project_id=?))",
          whereArgs: [id, id],
        );
      }
      await txn.delete(table, where: 'id=?', whereArgs: [id]);
    });
  }

  Future<void> order(String table, List<int> ids) async {
    if (!['projects', 'chats'].contains(table)) {
      throw ArgumentError('Table invalide');
    }
    await db.transaction((txn) async {
      for (var i = 0; i < ids.length; i++) {
        await txn.update(
          table,
          {'position': i},
          where: 'id=?',
          whereArgs: [ids[i]],
        );
      }
    });
  }

  Future<void> draft(int chat, String value) => db
      .update('chats', {'draft': value}, where: 'id=?', whereArgs: [chat])
      .then((_) {});
  Future<void> chatProvider(int chat, int? value) => db
      .update('chats', {'provider_id': value}, where: 'id=?', whereArgs: [chat])
      .then((_) {});
  Future<int> saveProvider(ProviderConfig p) => p.id == 0
      ? db.insert('providers', p.toJson())
      : db
            .update('providers', p.toJson(), where: 'id=?', whereArgs: [p.id])
            .then((_) => p.id);
  Future<void> saveMessage(ChatMessage m) async {
    if (m.id == 0) {
      m.id = await db.insert('messages', m.toJson());
    } else {
      await db.update('messages', m.toJson(), where: 'id=?', whereArgs: [m.id]);
    }
    await db.update(
      'chats',
      {'updated': DateTime.now().toUtc().toIso8601String()},
      where: 'id=?',
      whereArgs: [m.chatId],
    );
    if (m.role == 'user' && m.text.trim().isNotEmpty) {
      await db.update(
        'chats',
        {
          'title': m.text
              .replaceAll('\n', ' ')
              .substring(0, m.text.length.clamp(0, 55)),
        },
        where: 'id=? AND title=?',
        whereArgs: [m.chatId, 'Nouvelle discussion'],
      );
    }
  }

  String _scope(bool shared) =>
      "(scope='conversation' AND chat_id=?) OR (scope='project' AND project_id=?)${shared ? " OR scope='shared'" : ''}";
  Future<List<Json>> memories(
    int project,
    int chat,
    bool shared, [
    String query = '',
  ]) => db.query(
    'memories',
    where: '(${_scope(shared)}) AND instr(lower(content),lower(?))>0',
    whereArgs: [chat, project, query],
    orderBy: 'updated DESC',
    limit: 60,
  );
  Future<int> saveMemory(
    int project,
    int chat,
    bool shared,
    String scope,
    String content,
  ) async {
    if (!['conversation', 'project', if (shared) 'shared'].contains(scope)) {
      throw const FormatException('Portée non autorisée.');
    }
    if (content.trim().isEmpty || content.length > 4000) {
      throw const FormatException('Souvenir entre 1 et 4000 caractères.');
    }
    return db.insert('memories', {
      'scope': scope,
      'project_id': project,
      'chat_id': chat,
      'content': content.trim(),
      'updated': DateTime.now().toUtc().toIso8601String(),
    });
  }

  Future<void> deleteMemory(int project, int chat, bool shared, int id) async {
    if (await db.delete(
          'memories',
          where: 'id=? AND (${_scope(shared)})',
          whereArgs: [id, chat, project],
        ) ==
        0) {
      throw const FormatException('Souvenir hors des portées visibles.');
    }
  }
}
