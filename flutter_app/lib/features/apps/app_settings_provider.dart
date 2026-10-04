import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/providers.dart';

/// Settings of one app (active or not). Edits apply locally at once and are saved
/// 500 ms after the last change; the list also refreshes itself while it is on screen.
class AppSettingsNotifier extends AsyncNotifier<List<AppSetting>> {
  AppSettingsNotifier(this.appId);

  final String appId;

  static const saveDelay = Duration(milliseconds: 500);

  final Map<String, Object?> _pending = {};
  Timer? _debounce;
  bool _saving = false;

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
    if (_pending.isNotEmpty || _saving) return; // never overwrite what the user is editing
    final result = await ref.read(apiProvider).getAppSettings(appId);
    if (!ref.mounted || _pending.isNotEmpty || _saving) return;
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
    _debounce?.cancel();
    _debounce = Timer(saveDelay, _flush);
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
      ref.read(errorBusProvider.notifier).report(error);
      await reload(); // show what the device actually has
    } else if (_pending.isEmpty) {
      await reload(); // refresh labels and dependent values
    }
  }
}

final appSettingsProvider =
    AsyncNotifierProvider.autoDispose.family<AppSettingsNotifier, List<AppSetting>, String>(AppSettingsNotifier.new);
