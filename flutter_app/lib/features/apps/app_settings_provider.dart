import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../api/result.dart';
import '../../core/providers.dart';

enum SaveState { idle, saving, saved, failed }

/// Whether the last edits reached the device, and what the server said when they did not.
class SaveStatus {
  const SaveStatus(this.state, {this.message, this.fieldErrors = const {}});

  static const idle = SaveStatus(SaveState.idle);

  final SaveState state;
  final String? message;

  /// Server validation messages by setting key.
  final Map<String, String> fieldErrors;
}

class SaveStatusNotifier extends Notifier<SaveStatus> {
  SaveStatusNotifier(this.appId);

  final String appId;

  @override
  SaveStatus build() => SaveStatus.idle;

  void set(SaveStatus status) => state = status;
}

final settingsSaveStatusProvider =
    NotifierProvider.family<SaveStatusNotifier, SaveStatus, String>(SaveStatusNotifier.new);

/// Settings of one app (active or not). Edits apply locally at once and are saved
/// 500 ms after the last change; the list also refreshes itself while it is on screen.
class AppSettingsNotifier extends AsyncNotifier<List<AppSetting>> {
  AppSettingsNotifier(this.appId);

  final String appId;

  static const saveDelay = Duration(milliseconds: 500);

  final Map<String, Object?> _pending = {};
  Timer? _debounce;
  bool _saving = false;
  Map<String, Object?> _failed = {};

  void _status(SaveStatus status) {
    if (ref.mounted) ref.read(settingsSaveStatusProvider(appId).notifier).set(status);
  }

  @override
  Future<List<AppSetting>> build() async {
    final api = ref.watch(apiProvider);
    final interval = ref.watch(pollIntervalProvider);
    if (interval != null) {
      final timer = Timer.periodic(interval, (_) => _liveRefresh());
      ref.onDispose(timer.cancel);
    }
    ref.onDispose(() => _debounce?.cancel());
    return (await api.getAppSettings(appId)).getOrThrow();
  }

  Future<void> _liveRefresh() async {
    if (_pending.isNotEmpty || _saving || _failed.isNotEmpty) return; // never overwrite what the user is editing
    final result = await ref.read(apiProvider).getAppSettings(appId);
    if (!ref.mounted || _pending.isNotEmpty || _saving || _failed.isNotEmpty) return;
    final value = result.valueOrNull;
    if (value != null) state = AsyncData(value);
  }

  Future<void> reload() async {
    final result = await ref.read(apiProvider).getAppSettings(appId);
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  void edit(String key, Object value, {String? label, List<String>? labels}) {
    final current = state.value;
    if (current == null) return;
    state = AsyncData([
      for (final s in current)
        s.key == key ? s.copyWith(currentValue: value, currentLabel: label, currentLabels: labels) : s
    ]);
    _pending[key] = value;
    _failed.remove(key);
    _status(SaveStatus(SaveState.saving, fieldErrors: _withoutKey(key)));
    _debounce?.cancel();
    _debounce = Timer(saveDelay, _flush);
  }

  Map<String, String> _withoutKey(String key) =>
      Map.of(ref.read(settingsSaveStatusProvider(appId)).fieldErrors)..remove(key);

  /// Puts one setting back to its default (saved like any edit). Search settings also clear their label.
  void reset(String key) {
    final s = state.value?.where((e) => e.key == key).firstOrNull;
    if (s == null || s.defaultValue == null) return;
    final search = s.type == AppSettingType.search || s.type == AppSettingType.multiSearch;
    edit(key, s.defaultValue!, label: search ? '' : null, labels: search ? const [] : null);
  }

  /// Puts every changed setting back to its default.
  void resetAll() {
    for (final s in state.value ?? const <AppSetting>[]) {
      if (s.isModified) reset(s.key);
    }
  }

  /// Sends the edits that failed to save again.
  void retry() {
    if (_failed.isEmpty) return;
    _pending.addAll(_failed);
    _failed = {};
    _status(const SaveStatus(SaveState.saving));
    _debounce?.cancel();
    _flush();
  }

  Future<void> _flush() async {
    if (_pending.isEmpty) return;
    final batch = Map<String, Object?>.of(_pending);
    _pending.clear();
    _saving = true;
    final result = await ref.read(apiProvider).updateAppSettings(appId, batch);
    _saving = false;
    if (!ref.mounted) return;
    final error = result.errorOrNull;
    if (error != null) {
      // Keep what the user typed on screen, with the server's message, until they fix it or retry
      _failed = {...batch, ..._failed};
      _status(SaveStatus(SaveState.failed, message: error.message, fieldErrors: _fieldMessages(error, batch.keys)));
    } else {
      final errors = ref.read(settingsSaveStatusProvider(appId)).fieldErrors;
      if (_pending.isEmpty) {
        _status(SaveStatus(_failed.isEmpty ? SaveState.saved : SaveState.failed, fieldErrors: errors));
        if (_failed.isEmpty) await reload();
      } // refresh labels and dependent values
    }
  }

  /// Per-setting messages from a 400 body: `{path,message}` errors whose path is (or ends with) a setting key.
  static Map<String, String> _fieldMessages(ApiError error, Iterable<String> keys) {
    final result = <String, String>{};
    for (final e in error.fieldErrors) {
      final leaf = e.path.split('.').last;
      for (final k in keys) {
        if (leaf.toLowerCase() == k.toLowerCase()) result[k] = e.message;
      }
    }
    return result;
  }
}

final appSettingsProvider =
    AsyncNotifierProvider.autoDispose.family<AppSettingsNotifier, List<AppSetting>, String>(AppSettingsNotifier.new);
