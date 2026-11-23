import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/led_api.dart';
import '../api/models.dart';
import 'providers.dart';

enum ConnectionStatus { connecting, online, offline }

class DeviceConnection {
  const DeviceConnection({
    this.status = ConnectionStatus.connecting,
    this.health,
    this.failures = 0,
    this.lastError,
  });

  final ConnectionStatus status;

  /// The latest successful health reading (kept while offline so the UI can still show the last known device).
  final Health? health;
  final int failures;
  final String? lastError;

  bool get isOffline => status == ConnectionStatus.offline;

  /// True while what the UI shows may be out of date: the display is unreachable, or we are retrying after losing it.
  /// Controls dim and refuse input meanwhile; they come back by themselves when this turns false.
  bool get isStale => isOffline || (status == ConnectionStatus.connecting && failures > 0);
}

/// Global reachability of the device, driven by a light `/api/health` poll. Polls every [pollIntervalProvider] while
/// online and backs off (2s, 4s, ... 15s) while offline. When the device comes back, every data provider refreshes.
/// With [pollIntervalProvider] null (tests) nothing is scheduled; call [check] manually.
class ConnectionNotifier extends Notifier<DeviceConnection> {
  Timer? _timer;
  bool _checking = false;
  Object? _generation;

  @override
  DeviceConnection build() {
    // A new device URL starts from scratch: nothing in flight for the old address may touch the new state.
    final api = ref.watch(apiProvider);
    _generation = api;
    _checking = false;
    _timer?.cancel();
    ref.onDispose(() => _timer?.cancel());
    if (ref.read(pollIntervalProvider) != null) Future.microtask(check);
    return const DeviceConnection();
  }

  /// Checks now (pull-to-retry, app resume, the banner's Retry button).
  Future<void> check() async {
    if (_checking) return;
    _checking = true;
    _timer?.cancel();
    final before = state;
    if (before.status == ConnectionStatus.offline) {
      state = DeviceConnection(
        status: ConnectionStatus.connecting,
        health: before.health,
        failures: before.failures,
        lastError: before.lastError,
      );
    }
    final api = ref.read(apiProvider);
    final result = await api.getHealth();
    if (!ref.mounted || !identical(api, _generation)) return; // Stale: the address changed while this was in flight.
    _checking = false;
    final health = result.valueOrNull;
    if (health != null) {
      state = DeviceConnection(status: ConnectionStatus.online, health: health);
      if (before.status == ConnectionStatus.offline) {
        _refreshEverything();
      } else {
        _reconcile(health);
      }
    } else {
      final failures = before.failures + 1;
      // A single dropped request while online is tolerated; the very first check and repeats are not.
      final offline = before.status != ConnectionStatus.online || failures >= 2;
      state = DeviceConnection(
        status: offline ? ConnectionStatus.offline : ConnectionStatus.online,
        health: before.health,
        failures: failures,
        lastError: result.errorOrNull?.toString(),
      );
    }
    _schedule();
  }

  void _schedule() {
    final interval = ref.read(pollIntervalProvider);
    if (interval == null) return;
    final wait = state.status == ConnectionStatus.online && state.failures == 0
        ? interval
        : Duration(
            seconds: (2 << (state.failures - 1).clamp(0, 3)).clamp(2, 15),
          );
    _timer?.cancel();
    _timer = Timer(wait, check);
  }

  /// The device changed under us (another controller switched app or power): pull the affected state.
  void _reconcile(Health health) {
    final apps = ref.read(appListProvider).value;
    if (apps != null && apps.activeApp != health.activeApp) ref.read(appListProvider.notifier).syncSilently();
    final device = ref.read(deviceSettingsProvider).value;
    if (device != null &&
        (device.isEnabled != health.isEnabled || device.width != health.width || device.height != health.height)) {
      ref.read(deviceSettingsProvider.notifier).syncSilently();
    }
  }

  void _refreshEverything() {
    ref.invalidate(appListProvider);
    ref.invalidate(deviceSettingsProvider);
    ref.invalidate(transitionsProvider);
    ref.invalidate(healthProvider);
  }

  /// Refresh when the app returns to the foreground: re-check reachability and pull fresh state.
  Future<void> onResume() async {
    await check();
    if (!ref.mounted || state.status != ConnectionStatus.online) return;
    ref.read(appListProvider.notifier).syncSilently();
    ref.read(deviceSettingsProvider.notifier).syncSilently();
    ref.invalidate(healthProvider);
  }
}

final connectionProvider = NotifierProvider<ConnectionNotifier, DeviceConnection>(
  ConnectionNotifier.new,
);

/// Whether the displayed device state is stale (see [DeviceConnection.isStale]).
final connectionStaleProvider = Provider<bool>(
  (ref) => ref.watch(connectionProvider.select((c) => c.isStale)),
);

/// The outcome of "Test connection" for a candidate address.
class ConnectionTestResult {
  const ConnectionTestResult.ok(Health this.health, this.settings) : error = null;
  const ConnectionTestResult.failed(String this.error)
      : health = null,
        settings = null;

  final Health? health;
  final DeviceSettings? settings;
  final String? error;
  bool get success => health != null;
}

/// Contacts the device behind [api] (built for a candidate URL) without touching the saved one.
Future<ConnectionTestResult> testDeviceConnection(LedApi api) async {
  final health = await api.getHealth();
  final h = health.valueOrNull;
  if (h == null) {
    return ConnectionTestResult.failed(
      health.errorOrNull?.toString() ?? 'Could not reach the device',
    );
  }
  final settings = await api.getSettings();
  return ConnectionTestResult.ok(h, settings.valueOrNull);
}
