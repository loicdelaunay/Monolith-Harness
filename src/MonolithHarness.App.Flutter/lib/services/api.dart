import 'dart:convert';

import 'package:http/http.dart' as http;

import '../domain/models.dart';

Uri endpoint(String base, String resource) {
  final uri = Uri.tryParse('${base.trim().replaceAll(RegExp(r'/+$'), '')}/');
  if (uri == null ||
      uri.scheme != 'https' ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty ||
      uri.hasQuery ||
      uri.hasFragment) {
    throw const FormatException(
      'URL API HTTPS sans identifiants, requête ni fragment requise.',
    );
  }
  return uri.resolve(resource);
}

String safeError(Object error, [String secret = '']) {
  var value = error.toString();
  if (secret.isNotEmpty) value = value.replaceAll(secret, '[clé masquée]');
  value = value.replaceAll(
    RegExp(r'(?:sk-[\w-]{12,}|Bearer\s+\S+)', caseSensitive: false),
    '[clé masquée]',
  );
  return value.substring(0, value.length.clamp(0, 1800));
}

class ApiResult {
  final Json message;
  final String reasoning;
  final int? inputTokens, outputTokens;
  ApiResult(this.message, this.reasoning, this.inputTokens, this.outputTokens);
}

class ApiClient {
  final http.Client client;
  ApiClient(this.client);
  Future<List<String>> models(ProviderConfig provider, String key) async {
    final request = http.Request('GET', endpoint(provider.url, 'models'))
      ..followRedirects = false;
    request.headers['Authorization'] = 'Bearer $key';
    final response = await client
        .send(request)
        .timeout(const Duration(seconds: 20));
    final text = await bounded(response.stream, 2 * 1024 * 1024);
    if (response.statusCode != 200) {
      throw Exception(
        'Liste des modèles : HTTP ${response.statusCode}. ${safeError(text, key)}',
      );
    }
    final value = jsonDecode(text) as Json;
    return ((value['data'] ?? []) as List)
        .whereType<Json>()
        .where((m) => m['capabilities']?['completion_chat'] != false)
        .map((m) => m['id'])
        .whereType<String>()
        .toSet()
        .toList()
      ..sort();
  }

  Future<ApiResult> complete(
    ProviderConfig provider,
    String key,
    List<Json> history,
    List<Json> tools,
    void Function(String, String) update,
  ) async {
    final payload = <String, dynamic>{
      'model': provider.model,
      'messages': history,
      'stream': true,
      'stream_options': {'include_usage': true},
      if (tools.isNotEmpty) 'tools': tools,
    };
    http.StreamedResponse? response;
    for (var attempt = 0; attempt < 2; attempt++) {
      final request = http.Request(
        'POST',
        endpoint(provider.url, 'chat/completions'),
      )..followRedirects = false;
      request.headers.addAll({
        'Authorization': 'Bearer $key',
        'Content-Type': 'application/json',
        'Accept': 'text/event-stream',
      });
      request.body = jsonEncode(payload);
      response = await client
          .send(request)
          .timeout(const Duration(seconds: 60));
      if (response.statusCode >= 200 && response.statusCode < 300) break;
      final text = await bounded(response.stream, 32 * 1024);
      if (attempt == 0 &&
          (response.statusCode == 400 || response.statusCode == 422) &&
          text.toLowerCase().contains('stream_options')) {
        payload.remove('stream_options');
        continue;
      }
      throw Exception(
        'Fournisseur : HTTP ${response.statusCode}. ${safeError(text, key)}',
      );
    }
    final result = response!;
    var content = '', reasoning = '';
    int? input, output;
    final calls = <int, Json>{};
    void chunk(Json value) {
      if (value['error'] != null) {
        throw Exception(safeError(jsonEncode(value['error']), key));
      }
      if (value['usage'] is Json) {
        input = (value['usage']['prompt_tokens'] as num?)?.toInt();
        output = (value['usage']['completion_tokens'] as num?)?.toInt();
      }
      final choices = value['choices'] as List? ?? [];
      if (choices.isEmpty) return;
      final choice = choices.first as Json;
      final delta = (choice['delta'] ?? choice['message'] ?? {}) as Json;
      if (delta['content'] is String) content += delta['content'] as String;
      if (delta['reasoning_content'] is String) {
        reasoning += delta['reasoning_content'] as String;
      } else if (delta['reasoning'] is String) {
        reasoning += delta['reasoning'] as String;
      }
      if (content.length + reasoning.length > 2 * 1024 * 1024) {
        throw const FormatException('Réponse trop volumineuse.');
      }
      for (final item in (delta['tool_calls'] as List? ?? [])) {
        final c = item as Json;
        final index = (c['index'] as num?)?.toInt() ?? 0;
        if (index < 0 || index > 31) {
          throw const FormatException('Trop d’appels d’outils.');
        }
        final target = calls.putIfAbsent(
          index,
          () => {
            'id': '',
            'type': 'function',
            'function': {'name': '', 'arguments': ''},
          },
        );
        if (c['id'] is String) target['id'] += c['id'];
        final function = c['function'] as Json? ?? {};
        for (final field in ['name', 'arguments']) {
          if (function[field] is String) {
            target['function'][field] += function[field];
          }
        }
        if ((target['function']['arguments'] as String).length > 1024 * 1024) {
          throw const FormatException('Arguments d’outil trop volumineux.');
        }
      }
      update(content, reasoning);
    }

    if (result.headers['content-type']?.contains('application/json') ?? false) {
      chunk(
        Map<String, dynamic>.from(
          jsonDecode(await bounded(result.stream, 2 * 1024 * 1024)),
        ),
      );
    } else {
      var data = <String>[];
      void flush() {
        if (data.isEmpty) return;
        final raw = data.join('\n');
        data = [];
        if (raw == '[DONE]') return;
        chunk(Map<String, dynamic>.from(jsonDecode(raw)));
      }

      await for (final line
          in result.stream
              .timeout(const Duration(seconds: 90))
              .transform(utf8.decoder)
              .transform(const LineSplitter())) {
        if (line.length > 2 * 1024 * 1024) {
          throw const FormatException('Fragment de réponse trop volumineux.');
        }
        if (line.isEmpty) {
          flush();
        } else if (line.startsWith('data:')) {
          data.add(line.substring(5).trimLeft());
        }
      }
      flush();
    }
    final ordered = calls.keys.toList()..sort();
    return ApiResult(
      {
        'role': 'assistant',
        'content': content,
        if (calls.isNotEmpty) 'tool_calls': [for (final i in ordered) calls[i]],
      },
      reasoning,
      input,
      output,
    );
  }

  void close() => client.close();
}

Future<String> bounded(Stream<List<int>> stream, int max) async {
  final bytes = <int>[];
  await for (final block in stream.timeout(const Duration(seconds: 30))) {
    bytes.addAll(block);
    if (bytes.length > max) {
      throw const FormatException('Réponse réseau trop volumineuse.');
    }
  }
  return utf8.decode(bytes, allowMalformed: true);
}

/// Excludes unfinished tool exchanges after cancellation or process interruption.
List<Json> completeHistory(Iterable<Json> messages) {
  final source = messages.toList(), result = <Json>[];
  for (var i = 0; i < source.length; i++) {
    final message = source[i], calls = source[i]['tool_calls'];
    if (message['role'] == 'tool') continue;
    if (message['role'] != 'assistant' || calls is! List || calls.isEmpty) {
      result.add(message);
      continue;
    }
    final ids = calls
        .map((c) => c is Map ? c['id'] : null)
        .whereType<String>()
        .toSet();
    final replies = <Json>[];
    var end = i + 1;
    while (end < source.length && source[end]['role'] == 'tool') {
      replies.add(source[end++]);
    }
    final received = replies.map((m) => m['tool_call_id']).toSet();
    if (ids.length == calls.length &&
        replies.length == ids.length &&
        received.length == ids.length &&
        received.containsAll(ids)) {
      result.add(message);
      result.addAll(replies);
    } else if (message['content'] is String &&
        (message['content'] as String).isNotEmpty) {
      result.add({'role': 'assistant', 'content': message['content']});
    }
    i = end - 1;
  }
  return result;
}
