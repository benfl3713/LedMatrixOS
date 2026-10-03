import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../api/schedule_models.dart';
import '../../core/providers.dart';

enum NotifyMode { toast, badge, alert }

/// A saved composer state: mode plus its field values (text, colours, seconds, ...).
class NotifyPreset {
  const NotifyPreset({required this.name, required this.mode, required this.fields});

  final String name;
  final NotifyMode mode;
  final Map<String, Object?> fields;

  factory NotifyPreset.fromJson(Map<String, dynamic> json) => NotifyPreset(
        name: (json['name'] ?? '') as String,
        mode: NotifyMode.values.firstWhere((m) => m.name == json['mode'], orElse: () => NotifyMode.toast),
        fields: ((json['fields'] ?? const {}) as Map).cast<String, Object?>(),
      );

  Map<String, Object?> toJson() => {'name': name, 'mode': mode.name, 'fields': fields};
}

const _presetsKey = 'notify_presets';

class PresetsNotifier extends AsyncNotifier<List<NotifyPreset>> {
  @override
  Future<List<NotifyPreset>> build() async {
    final prefs = await SharedPreferences.getInstance();
    final text = prefs.getString(_presetsKey);
    if (text == null) return const [];
    return (jsonDecode(text) as List).map((e) => NotifyPreset.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<void> _persist(List<NotifyPreset> presets) async {
    final previous = state.value ?? const <NotifyPreset>[];
    state = AsyncData(presets);
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_presetsKey, jsonEncode([for (final p in presets) p.toJson()]));
    } catch (e) {
      if (!ref.mounted) return;
      state = AsyncData(previous);
      ref.read(errorBusProvider.notifier).report('Could not save presets: $e');
    }
  }

  /// Saves a preset, replacing any existing one with the same name.
  Future<void> save(NotifyPreset preset) {
    final current = [...(state.value ?? const <NotifyPreset>[])];
    final i = current.indexWhere((p) => p.name == preset.name);
    if (i >= 0) {
      current[i] = preset;
    } else {
      current.add(preset);
    }
    return _persist(current);
  }

  Future<void> delete(String name) =>
      _persist([for (final p in state.value ?? const <NotifyPreset>[]) if (p.name != name) p]);
}

final presetsProvider = AsyncNotifierProvider<PresetsNotifier, List<NotifyPreset>>(PresetsNotifier.new);

class OverlaysNotifier extends AsyncNotifier<List<OverlayInfo>> {
  @override
  Future<List<OverlayInfo>> build() async {
    final api = ref.watch(apiProvider);
    final interval = ref.watch(pollIntervalProvider);
    if (interval != null) {
      final timer = Timer.periodic(interval, (_) => _silentRefresh());
      ref.onDispose(timer.cancel);
    }
    return (await api.getOverlays()).getOrThrow();
  }

  Future<void> _silentRefresh() async {
    final result = await ref.read(apiProvider).getOverlays();
    if (!ref.mounted) return;
    final value = result.valueOrNull;
    if (value != null) state = AsyncData(value);
  }

  Future<void> refresh() async {
    final result = await ref.read(apiProvider).getOverlays();
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  Future<void> dismiss(String id) async {
    final result = await ref.read(apiProvider).dismissOverlay(id);
    if (!ref.mounted) return;
    final error = result.errorOrNull;
    if (error != null) ref.read(errorBusProvider.notifier).report(error);
    await refresh();
  }

  Future<void> clearAll() async {
    final result = await ref.read(apiProvider).clearOverlays();
    if (!ref.mounted) return;
    final error = result.errorOrNull;
    if (error != null) ref.read(errorBusProvider.notifier).report(error);
    await refresh();
  }
}

final overlaysProvider = AsyncNotifierProvider<OverlaysNotifier, List<OverlayInfo>>(OverlaysNotifier.new);
