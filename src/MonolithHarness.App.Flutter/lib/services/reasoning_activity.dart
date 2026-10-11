/// Latest activity reported in the provider's reasoning stream, bounded for UI.
String reasoningActivity(String reasoning) {
  var start = reasoning.length > 1000 ? reasoning.length - 1000 : 0;
  if (start > 0 &&
      reasoning.codeUnitAt(start) >= 0xdc00 &&
      reasoning.codeUnitAt(start) <= 0xdfff) {
    start++;
  }
  final lines = reasoning.substring(start).trimRight().split('\n');
  for (final raw in lines.reversed) {
    var line = raw
        .trim()
        .replaceFirst(RegExp(r'^(?:#{1,6}|[-*+>]|\d+[.)])\s+'), '')
        .replaceAll(RegExp(r'\*\*|__|`{1,3}'), '')
        .trim();
    if (line.isEmpty || RegExp(r'^[#*_>-]+$').hasMatch(line)) continue;
    final sentences = RegExp(r'[.!?。！？]\s+(?=\S)').allMatches(line);
    if (sentences.isNotEmpty) line = line.substring(sentences.last.end);
    final points = line.runes.take(121).toList();
    return points.length > 120
        ? '${String.fromCharCodes(points.take(119))}…'
        : line;
  }
  return 'Réflexion en cours…';
}
