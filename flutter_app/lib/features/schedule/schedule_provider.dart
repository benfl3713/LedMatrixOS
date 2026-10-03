import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../api/schedule_models.dart';
import '../../core/providers.dart';

// ---------------------------------------------------------------------------
// Active now
// ---------------------------------------------------------------------------

class ScheduleStatusNotifier extends AsyncNotifier<ScheduleStatus> {
  @override
  Future<ScheduleStatus> build() async {
    final api = ref.watch(apiProvider);
    final interval = ref.watch(pollIntervalProvider);
    if (interval != null) {
      final timer = Timer.periodic(interval, (_) => _silentRefresh());
      ref.onDispose(timer.cancel);
    }
    return (await api.getScheduleStatus()).getOrThrow();
  }

  Future<void> _silentRefresh() async {
    final result = await ref.read(apiProvider).getScheduleStatus();
    if (!ref.mounted) return;
    // Background refresh failures are not worth a snackbar every 10s.
    final value = result.valueOrNull;
    if (value != null) state = AsyncData(value);
  }

  Future<void> refresh() async {
    final result = await ref.read(apiProvider).getScheduleStatus();
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }
}

final scheduleStatusProvider = AsyncNotifierProvider<ScheduleStatusNotifier, ScheduleStatus>(ScheduleStatusNotifier.new);

// ---------------------------------------------------------------------------
// Editing the document
// ---------------------------------------------------------------------------

class ScheduleEditState {
  const ScheduleEditState({
    required this.saved,
    required this.draft,
    this.errors = const [],
    this.saving = false,
    this.fromDevice = true,
  });

  /// What the device (or the last local save) has.
  final ScheduleDocument saved;

  /// What the user is editing.
  final ScheduleDocument draft;

  /// Server validation problems from the last failed save.
  final List<String> errors;
  final bool saving;

  /// False when the device does not expose the stored document, so [saved] is only the last copy saved from this app.
  final bool fromDevice;

  bool get dirty => !draft.sameAs(saved);

  ScheduleEditState copyWith({
    ScheduleDocument? saved,
    ScheduleDocument? draft,
    List<String>? errors,
    bool? saving,
    bool? fromDevice,
  }) =>
      ScheduleEditState(
        saved: saved ?? this.saved,
        draft: draft ?? this.draft,
        errors: errors ?? this.errors,
        saving: saving ?? this.saving,
        fromDevice: fromDevice ?? this.fromDevice,
      );
}

const _lastSavedKey = 'schedule_last_saved';

class ScheduleEditor extends AsyncNotifier<ScheduleEditState> {
  @override
  Future<ScheduleEditState> build() async {
    final result = await ref.watch(apiProvider).getSchedule();
    final fetched = result.getOrThrow();
    if (fetched != null) return ScheduleEditState(saved: fetched, draft: fetched);
    final cached = await _loadCached();
    return ScheduleEditState(saved: cached, draft: cached, fromDevice: false);
  }

  Future<ScheduleDocument> _loadCached() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final text = prefs.getString(_lastSavedKey);
      if (text != null) return ScheduleDocument.fromJson(jsonDecode(text) as Map<String, dynamic>);
    } catch (e) {
      if (ref.mounted) ref.read(errorBusProvider.notifier).report('Could not load the cached schedule: $e');
    }
    return ScheduleDocument.empty;
  }

  Future<void> _cache(ScheduleDocument doc) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_lastSavedKey, doc.encode());
    } catch (e) {
      if (ref.mounted) ref.read(errorBusProvider.notifier).report('Could not cache the schedule: $e');
    }
  }

  ScheduleEditState? get _current => state.value;

  void _edit(ScheduleDocument Function(ScheduleDocument d) change) {
    final s = _current;
    if (s == null) return;
    state = AsyncData(s.copyWith(draft: change(s.draft)));
  }

  void reload() => ref.invalidateSelf();

  void discard() {
    final s = _current;
    if (s == null) return;
    state = AsyncData(s.copyWith(draft: s.saved, errors: const []));
  }

  // Rules -------------------------------------------------------------------

  /// Adds a rule, or replaces the one at [index].
  void upsertRule(RuleDoc rule, {int? index}) => _edit((d) {
        final rules = [...d.rules];
        if (index == null || index < 0 || index >= rules.length) {
          rules.add(rule);
        } else {
          rules[index] = rule;
        }
        return d.copyWith(rules: rules);
      });

  void deleteRule(int index) => _edit((d) {
        if (index < 0 || index >= d.rules.length) return d;
        return d.copyWith(rules: [...d.rules]..removeAt(index));
      });

  // Playlists ---------------------------------------------------------------

  /// Adds a playlist, or replaces the one at [index] (renaming it in the rules that use it).
  void upsertPlaylist(PlaylistDoc playlist, {int? index}) => _edit((d) {
        final playlists = [...d.playlists];
        var rules = d.rules;
        if (index == null || index < 0 || index >= playlists.length) {
          playlists.add(playlist);
        } else {
          final oldName = playlists[index].name;
          playlists[index] = playlist;
          if (oldName != playlist.name) {
            rules = [for (final r in rules) r.playlistId == oldName ? r.copyWith(playlistId: playlist.name) : r];
          }
        }
        return d.copyWith(playlists: playlists, rules: rules);
      });

  void deletePlaylist(int index) => _edit((d) {
        if (index < 0 || index >= d.playlists.length) return d;
        return d.copyWith(playlists: [...d.playlists]..removeAt(index));
      });

  /// Ensures a one-entry playlist exists for [appId] and returns its name.
  String ensureSingleAppPlaylist(String appId, {int durationMs = 60000}) {
    final s = _current;
    if (s != null && !s.draft.playlists.any((p) => p.name == appId)) {
      upsertPlaylist(PlaylistDoc(name: appId, entries: [EntryDoc(appId: appId, durationMs: durationMs)]));
    }
    return appId;
  }

  // Save --------------------------------------------------------------------

  /// Sends the whole document. Returns true on success; validation errors stay in the state.
  Future<bool> save() async {
    final s = _current;
    if (s == null || s.saving) return false;
    state = AsyncData(s.copyWith(saving: true, errors: const []));
    final result = await ref.read(apiProvider).saveSchedule(s.draft);
    if (!ref.mounted) return false;
    final error = result.errorOrNull;
    if (error != null) {
      state = AsyncData(s.copyWith(saving: false, errors: error.errors.isNotEmpty ? error.errors : [error.message]));
      ref.read(errorBusProvider.notifier).report(error.errors.isNotEmpty ? 'Schedule not saved: ${error.errors.length} problem(s)' : error);
      return false;
    }
    // The device answers with the stored document.
    final doc = result.valueOrNull!;
    state = AsyncData(s.copyWith(saved: doc, draft: doc, saving: false, errors: const [], fromDevice: true));
    await _cache(doc);
    ref.invalidate(scheduleStatusProvider);
    return true;
  }
}

final scheduleEditorProvider = AsyncNotifierProvider<ScheduleEditor, ScheduleEditState>(ScheduleEditor.new);
