import 'dart:convert';

/// Day bits in `daysMask`: Sun=1, Mon=2, ... Sat=64 (matches .NET DayOfWeek).
const allDaysMask = 127;

/// Supported rule conditions, shown as a hint in the rule editor.
const conditionHint = 'spotify_playing, line_disrupted:<id>, bus_due:<stop>, ha_state:<entity>=<value>, bin_day, road_disrupted:<corridor|any>';

int _int(Object? v, int fallback) => v is num ? v.round() : fallback;

/// "HH:mm" to minutes since midnight, or null when malformed.
int? parseHm(String? text) {
  if (text == null) return null;
  final m = RegExp(r'^(\d{1,2}):(\d{2})$').firstMatch(text.trim());
  if (m == null) return null;
  final h = int.parse(m.group(1)!), min = int.parse(m.group(2)!);
  if (h > 23 || min > 59) return null;
  return h * 60 + min;
}

String formatHm(int minutes) =>
    '${(minutes ~/ 60).toString().padLeft(2, '0')}:${(minutes % 60).toString().padLeft(2, '0')}';

class EntryDoc {
  const EntryDoc({required this.appId, this.durationMs = 30000, this.transition, this.settings});

  final String appId;
  final int durationMs;
  final String? transition;

  /// Per-entry setting overrides. Not editable in the app but preserved on save.
  final Map<String, Object?>? settings;

  factory EntryDoc.fromJson(Map<String, dynamic> json) {
    final transition = json['transition'] as String?;
    return EntryDoc(
      appId: (json['appId'] ?? '') as String,
      durationMs: _int(json['durationMs'], 30000),
      transition: transition == null || transition.isEmpty ? null : transition,
      settings: (json['settings'] as Map?)?.cast<String, Object?>(),
    );
  }

  Map<String, Object?> toJson() => {
        'appId': appId,
        'durationMs': durationMs,
        if (transition != null) 'transition': transition,
        if (settings != null) 'settings': settings,
      };

  EntryDoc copyWith({String? appId, int? durationMs, String? transition, bool clearTransition = false}) => EntryDoc(
        appId: appId ?? this.appId,
        durationMs: durationMs ?? this.durationMs,
        transition: clearTransition ? null : (transition ?? this.transition),
        settings: settings,
      );
}

class PlaylistDoc {
  const PlaylistDoc({required this.name, this.skipUnavailable = false, this.entries = const []});

  final String name;
  final bool skipUnavailable;
  final List<EntryDoc> entries;

  factory PlaylistDoc.fromJson(Map<String, dynamic> json) => PlaylistDoc(
        name: (json['name'] ?? '') as String,
        skipUnavailable: (json['skipUnavailable'] ?? false) as bool,
        entries: ((json['entries'] ?? const []) as List).map((e) => EntryDoc.fromJson(e as Map<String, dynamic>)).toList(),
      );

  Map<String, Object?> toJson() => {
        'name': name,
        'skipUnavailable': skipUnavailable,
        'entries': [for (final e in entries) e.toJson()],
      };

  PlaylistDoc copyWith({String? name, bool? skipUnavailable, List<EntryDoc>? entries}) => PlaylistDoc(
        name: name ?? this.name,
        skipUnavailable: skipUnavailable ?? this.skipUnavailable,
        entries: entries ?? this.entries,
      );
}

class RuleDoc {
  const RuleDoc({
    required this.playlistId,
    this.priority = 50,
    this.startTime,
    this.endTime,
    this.daysMask = allDaysMask,
    this.brightnessOverride,
    this.condition,
  });

  final String playlistId;
  final int priority;

  /// "HH:mm", or null for midnight / end of day.
  final String? startTime;
  final String? endTime;
  final int daysMask;

  /// 0-255 as the server stores it.
  final int? brightnessOverride;
  final String? condition;

  factory RuleDoc.fromJson(Map<String, dynamic> json) => RuleDoc(
        playlistId: (json['playlistId'] ?? '') as String,
        priority: _int(json['priority'], 50),
        startTime: json['startTime'] as String?,
        endTime: json['endTime'] as String?,
        daysMask: _int(json['daysMask'], allDaysMask),
        brightnessOverride: json['brightnessOverride'] is num ? (json['brightnessOverride'] as num).round() : null,
        condition: json['condition'] as String?,
      );

  Map<String, Object?> toJson() => {
        'playlistId': playlistId,
        'priority': priority,
        if (startTime != null) 'startTime': startTime,
        if (endTime != null) 'endTime': endTime,
        'daysMask': daysMask,
        if (brightnessOverride != null) 'brightnessOverride': brightnessOverride,
        if (condition != null && condition!.trim().isNotEmpty) 'condition': condition!.trim(),
      };

  bool get isAllDay => startTime == null && endTime == null;

  /// Whether the rule runs past midnight into the next day.
  bool get wraps {
    final s = parseHm(startTime), e = parseHm(endTime);
    return s != null && e != null && e < s;
  }

  /// [dayIndex] 0 = Sunday.
  bool appliesOn(int dayIndex) => daysMask & (1 << dayIndex) != 0;

  RuleDoc copyWith({
    String? playlistId,
    int? priority,
    String? startTime,
    String? endTime,
    int? daysMask,
    int? brightnessOverride,
    String? condition,
    bool clearStart = false,
    bool clearEnd = false,
    bool clearBrightness = false,
    bool clearCondition = false,
  }) =>
      RuleDoc(
        playlistId: playlistId ?? this.playlistId,
        priority: priority ?? this.priority,
        startTime: clearStart ? null : (startTime ?? this.startTime),
        endTime: clearEnd ? null : (endTime ?? this.endTime),
        daysMask: daysMask ?? this.daysMask,
        brightnessOverride: clearBrightness ? null : (brightnessOverride ?? this.brightnessOverride),
        condition: clearCondition ? null : (condition ?? this.condition),
      );
}

/// The whole schedule: the body of `PUT /api/schedule`.
class ScheduleDocument {
  const ScheduleDocument({this.playlists = const [], this.rules = const []});

  final List<PlaylistDoc> playlists;
  final List<RuleDoc> rules;

  static const empty = ScheduleDocument();

  /// True when [json] looks like a schedule document (has `playlists` or `rules`).
  static bool looksLikeDocument(Map<String, dynamic> json) => json['playlists'] is List || json['rules'] is List;

  factory ScheduleDocument.fromJson(Map<String, dynamic> json) => ScheduleDocument(
        playlists: ((json['playlists'] ?? const []) as List).map((e) => PlaylistDoc.fromJson(e as Map<String, dynamic>)).toList(),
        rules: ((json['rules'] ?? const []) as List).map((e) => RuleDoc.fromJson(e as Map<String, dynamic>)).toList(),
      );

  Map<String, Object?> toJson() => {
        'playlists': [for (final p in playlists) p.toJson()],
        'rules': [for (final r in rules) r.toJson()],
      };

  String encode() => jsonEncode(toJson());

  /// Structural equality via the wire form (used for the unsaved-changes indicator).
  bool sameAs(ScheduleDocument other) => encode() == other.encode();

  ScheduleDocument copyWith({List<PlaylistDoc>? playlists, List<RuleDoc>? rules}) =>
      ScheduleDocument(playlists: playlists ?? this.playlists, rules: rules ?? this.rules);
}

class ActiveRuleInfo {
  const ActiveRuleInfo({required this.index, required this.playlistId, required this.priority, this.condition});

  final int index;
  final String playlistId;
  final int priority;
  final String? condition;

  factory ActiveRuleInfo.fromJson(Map<String, dynamic> json) => ActiveRuleInfo(
        index: _int(json['index'], -1),
        playlistId: (json['playlistId'] ?? '') as String,
        priority: _int(json['priority'], 0),
        condition: json['condition'] as String?,
      );
}

/// `GET /api/schedule/status`.
class ScheduleStatus {
  const ScheduleStatus({
    this.activeRule,
    this.playlist,
    this.entryIndex,
    this.entryCount,
    this.appId,
    this.nextChange,
    this.nextChangeReason,
  });

  final ActiveRuleInfo? activeRule;
  final String? playlist;
  final int? entryIndex;
  final int? entryCount;
  final String? appId;
  final DateTime? nextChange;
  final String? nextChangeReason;

  factory ScheduleStatus.fromJson(Map<String, dynamic> json) => ScheduleStatus(
        activeRule: json['activeRule'] is Map ? ActiveRuleInfo.fromJson(json['activeRule'] as Map<String, dynamic>) : null,
        playlist: json['playlist'] as String?,
        entryIndex: json['entryIndex'] is num ? (json['entryIndex'] as num).round() : null,
        entryCount: json['entryCount'] is num ? (json['entryCount'] as num).round() : null,
        appId: json['appId'] as String?,
        nextChange: json['nextChange'] is String ? DateTime.tryParse(json['nextChange'] as String)?.toLocal() : null,
        nextChangeReason: json['nextChangeReason'] as String?,
      );
}

/// One live overlay from `GET /api/overlays`.
class OverlayInfo {
  const OverlayInfo({
    required this.id,
    required this.kind,
    this.text,
    this.priority = 0,
    this.remainingSeconds,
    this.exiting = false,
  });

  final String id;
  final String kind;
  final String? text;
  final int priority;
  final double? remainingSeconds;
  final bool exiting;

  factory OverlayInfo.fromJson(Map<String, dynamic> json) => OverlayInfo(
        id: (json['id'] ?? '') as String,
        kind: (json['kind'] ?? 'overlay') as String,
        text: json['text'] as String?,
        priority: _int(json['priority'], 0),
        remainingSeconds: json['remainingSeconds'] is num ? (json['remainingSeconds'] as num).toDouble() : null,
        exiting: (json['exiting'] ?? false) as bool,
      );
}
