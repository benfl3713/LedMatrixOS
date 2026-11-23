import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/led_api.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/api/schedule_models.dart';
import 'package:led_matrix_controller/core/connection.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/apps/app_settings_provider.dart';
import 'package:led_matrix_controller/features/schedule/rule_editor.dart';
import 'package:led_matrix_controller/features/schedule/schedule_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

/// Requests that finish only when the test says so.
class _GatedApi extends FakeApi {
  final gates = <Completer<void>>[];
  Completer<void>? healthGate;
  Completer<void>? saveGate;

  @override
  Future<Result<void>> setPower(bool enabled) async {
    final gate = Completer<void>();
    gates.add(gate);
    await gate.future;
    return super.setPower(enabled);
  }

  @override
  Future<Result<Health>> getHealth() async {
    final gate = healthGate;
    if (gate != null) await gate.future;
    return super.getHealth();
  }

  @override
  Future<Result<ScheduleDocument>> saveSchedule(ScheduleDocument doc) async {
    final gate = saveGate;
    if (gate != null) await gate.future;
    return super.saveSchedule(doc);
  }
}

class _ApiHolder extends Notifier<LedApi> {
  _ApiHolder(this.initial);
  final LedApi initial;

  @override
  LedApi build() => initial;
  void set(LedApi api) => state = api;
}

Future<void> _pump() => Future<void>.delayed(Duration.zero);

ProviderContainer _container(LedApi api) {
  final c = ProviderContainer(retry: (a, b) => null, overrides: [
    apiProvider.overrideWithValue(api),
    pollIntervalProvider.overrideWithValue(null),
  ]);
  addTearDown(c.dispose);
  return c;
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  group('power toggles', () {
    test('rapid toggles are serialised and the latest wins', () async {
      final api = _GatedApi();
      final c = _container(api);
      await c.read(deviceSettingsProvider.future);
      final n = c.read(deviceSettingsProvider.notifier);
      final first = n.setPower(false);
      await _pump();
      final second = n.setPower(true);
      await _pump();
      expect(api.gates, hasLength(1)); // only one request in flight
      api.gates[0].complete();
      await _pump();
      await _pump();
      api.gates[1].complete();
      await Future.wait([first, second]);
      expect(api.calls.where((x) => x.startsWith('power')), ['power:false', 'power:true']);
      expect(c.read(deviceSettingsProvider).value!.isEnabled, true);
      expect(api.power, true);
    });

    test('a failure rolls back to the last confirmed value, not the previous tap', () async {
      final api = _GatedApi();
      final c = _container(api);
      await c.read(deviceSettingsProvider.future);
      final n = c.read(deviceSettingsProvider.notifier);
      final f = n.setPower(false); // true -> false
      await _pump();
      api.failPower = true;
      unawaited(n.setPower(true)); // queued; the in-flight request now fails
      await _pump();
      api.gates[0].complete();
      await _pump();
      await _pump();
      api.gates[1].complete();
      await f;
      // The server never changed, so the confirmed value is still on.
      expect(c.read(deviceSettingsProvider).value!.isEnabled, true);
      expect(c.read(errorBusProvider), isNotNull);
    });

    test('a background sync cannot revert a toggle that is still in flight', () async {
      final api = _GatedApi();
      final c = _container(api);
      await c.read(deviceSettingsProvider.future);
      final n = c.read(deviceSettingsProvider.notifier);
      final f = n.setPower(false);
      await _pump();
      await n.syncSilently(); // the server still says on
      expect(c.read(deviceSettingsProvider).value!.isEnabled, false);
      api.gates[0].complete();
      await f;
      expect(c.read(deviceSettingsProvider).value!.isEnabled, false);
    });
  });

  group('pending brightness', () {
    test('reload (pull to refresh) sends the pending slider change first', () async {
      final api = FakeApi();
      final c = _container(api);
      await c.read(deviceSettingsProvider.future);
      final n = c.read(deviceSettingsProvider.notifier);
      n.setBrightnessPercent(80);
      await n.reload();
      expect(api.brightness, isNot(50));
      expect(c.read(deviceSettingsProvider).value!.brightness, api.brightness);
    });

    test('disposing the provider still sends the pending change', () async {
      final api = FakeApi();
      final c = _container(api);
      await c.read(deviceSettingsProvider.future);
      c.read(deviceSettingsProvider.notifier).setBrightnessPercent(80);
      c.invalidate(deviceSettingsProvider);
      await _pump();
      expect(api.calls.where((x) => x.startsWith('brightness')), hasLength(1));
    });
  });

  test('closing the app settings sheet inside the debounce window still saves', () async {
    final api = FakeApi();
    final c = _container(api);
    final sub = c.listen(appSettingsProvider('clock'), (_, __) {});
    await c.read(appSettingsProvider('clock').future);
    c.read(appSettingsProvider('clock').notifier).edit('size', 5);
    sub.close();
    await _pump();
    await _pump();
    expect(api.settingUpdates, [
      {'size': 5}
    ]);
  });

  test('a health check in flight across a URL change does not touch the new state', () async {
    final a = _GatedApi()
      ..failHealth = true
      ..healthGate = Completer<void>();
    final b = FakeApi();
    final holder = NotifierProvider<_ApiHolder, LedApi>(() => _ApiHolder(a));
    final c = ProviderContainer(retry: (x, y) => null, overrides: [
      apiProvider.overrideWith((ref) => ref.watch(holder)),
      pollIntervalProvider.overrideWithValue(null),
    ]);
    addTearDown(c.dispose);
    c.listen(connectionProvider, (_, __) {});
    unawaited(c.read(connectionProvider.notifier).check());
    await _pump();
    c.read(holder.notifier).set(b);
    expect(c.read(connectionProvider).status, ConnectionStatus.connecting);
    a.healthGate!.complete();
    await _pump();
    await _pump();
    // The old address's failure was ignored...
    expect(c.read(connectionProvider).status, ConnectionStatus.connecting);
    expect(c.read(connectionProvider).failures, 0);
    // ...and the new address can be checked right away.
    await c.read(connectionProvider.notifier).check();
    expect(c.read(connectionProvider).status, ConnectionStatus.online);
  });

  test('saving the schedule keeps edits made while the request was in flight', () async {
    final api = _GatedApi()..saveGate = Completer<void>();
    final c = _container(api);
    c.listen(scheduleEditorProvider, (_, __) {});
    await c.read(scheduleEditorProvider.future);
    final n = c.read(scheduleEditorProvider.notifier);
    n.upsertPlaylist(const PlaylistDoc(name: 'p', entries: [EntryDoc(appId: 'clock', durationMs: 1000)]));
    final saving = n.save();
    await _pump();
    n.upsertRule(const RuleDoc(playlistId: 'p'));
    api.saveGate!.complete();
    await saving;
    final s = c.read(scheduleEditorProvider).value!;
    expect(s.draft.rules, hasLength(s.saved.rules.length + 1));
    expect(s.dirty, true);
    expect(s.saving, false);
  });

  testWidgets('the day chips fit a 320px screen at a large text scale', (tester) async {
    tester.view.physicalSize = const Size(320, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(ProviderScope(
      retry: (a, b) => null,
      overrides: [
        apiProvider.overrideWithValue(FakeApi()),
        pollIntervalProvider.overrideWithValue(null),
      ],
      child: MaterialApp(
        builder: (context, child) =>
            MediaQuery(data: MediaQuery.of(context).copyWith(textScaler: const TextScaler.linear(2)), child: child!),
        home: const RuleEditor(initial: RuleDoc(playlistId: 'p'), isNew: true, playlists: [PlaylistDoc(name: 'p')]),
      ),
    ));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('day-chip-1')), findsOneWidget);
  });
}
