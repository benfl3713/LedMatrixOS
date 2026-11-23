import 'dart:async';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import '../api/led_api.dart';
import '../api/models.dart';
import '../api/preview_frame.dart';
import 'device_url.dart';

// ---------------------------------------------------------------------------
// Errors: every failure is reported here and shown as a snackbar by the shell.
// ---------------------------------------------------------------------------

/// One reportable failure. Deliberately has no value equality so repeated identical
/// errors still notify listeners.
class ErrorEvent {
  ErrorEvent(this.message);
  final String message;
}

class ErrorBus extends Notifier<ErrorEvent?> {
  @override
  ErrorEvent? build() => null;

  void report(Object error) => state = ErrorEvent(error.toString());
}

final errorBusProvider = NotifierProvider<ErrorBus, ErrorEvent?>(ErrorBus.new);

// ---------------------------------------------------------------------------
// Device URL and API client
// ---------------------------------------------------------------------------

const defaultApiUrl = 'http://localhost:5005';
const _apiUrlPrefsKey = 'api_url';

class ApiUrlNotifier extends Notifier<String> {
  @override
  String build() {
    _load();
    return defaultApiUrl;
  }

  Future<void> _load() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final saved = prefs.getString(_apiUrlPrefsKey);
      if (saved != null && ref.mounted) state = saved;
    } catch (e) {
      if (ref.mounted) ref.read(errorBusProvider.notifier).report('Could not load the saved device URL: $e');
    }
  }

  Future<void> save(String url) async {
    final trimmed = normalizeDeviceUrl(url).url ?? url.trim();
    if (trimmed == state) return;
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_apiUrlPrefsKey, trimmed);
      state = trimmed;
    } catch (e) {
      ref.read(errorBusProvider.notifier).report('Could not save the device URL: $e');
    }
  }

  Future<void> reset() => save(defaultApiUrl);
}

final apiUrlProvider = NotifierProvider<ApiUrlNotifier, String>(ApiUrlNotifier.new);

/// Builds an API client for an arbitrary URL (used to test a candidate address before saving). Tests override this.
final apiFactoryProvider = Provider<LedApi Function(String baseUrl)>((ref) => (url) => HttpLedApi(baseUrl: url));

final apiProvider = Provider<LedApi>((ref) => ref.watch(apiFactoryProvider)(ref.watch(apiUrlProvider)));

/// How often the lists refresh themselves in the background. Tests override this with null.
final pollIntervalProvider = Provider<Duration?>((ref) => const Duration(seconds: 10));

// ---------------------------------------------------------------------------
// Apps
// ---------------------------------------------------------------------------

class AppListNotifier extends AsyncNotifier<AppList> {
  @override
  Future<AppList> build() async {
    final api = ref.watch(apiProvider);
    final interval = ref.watch(pollIntervalProvider);
    if (interval != null) {
      final timer = Timer.periodic(interval, (_) => syncSilently());
      ref.onDispose(timer.cancel);
    }
    return (await api.getApps()).getOrThrow();
  }

  /// Refreshes without surfacing errors (background polling and connection recovery).
  Future<void> syncSilently() async {
    final result = await ref.read(apiProvider).getApps();
    if (!ref.mounted) return;
    // Background refresh failures are not worth a snackbar every 10s; the first load and
    // every user action do surface errors.
    final value = result.valueOrNull;
    if (value != null) state = AsyncData(value);
  }

  Future<void> refresh() async {
    final result = await ref.read(apiProvider).getApps();
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  Future<bool> activate(String id) async {
    final result = await ref.read(apiProvider).activateApp(id);
    if (!ref.mounted) return false;
    return result.when(
      ok: (_) {
        final current = state.value;
        if (current != null) state = AsyncData(current.copyWith(activeApp: id));
        ref.invalidate(healthProvider);
        return true;
      },
      err: (e) {
        ref.read(errorBusProvider.notifier).report(e);
        return false;
      },
    );
  }
}

final appListProvider = AsyncNotifierProvider<AppListNotifier, AppList>(AppListNotifier.new);

// ---------------------------------------------------------------------------
// Device settings (power, brightness)
// ---------------------------------------------------------------------------

class DeviceSettingsNotifier extends AsyncNotifier<DeviceSettings> {
  Timer? _brightnessDebounce;
  int? _pendingBrightness;
  bool _sending = false;

  /// True while a slider change has not reached the device yet; background syncs must not overwrite it.
  bool get _brightnessBusy => _pendingBrightness != null || _sending;

  @override
  Future<DeviceSettings> build() async {
    ref.onDispose(() => _brightnessDebounce?.cancel());
    final interval = ref.watch(pollIntervalProvider);
    if (interval != null) {
      final timer = Timer.periodic(interval, (_) => syncSilently());
      ref.onDispose(timer.cancel);
    }
    return (await ref.watch(apiProvider).getSettings()).getOrThrow();
  }

  /// Picks up changes made elsewhere (another phone, Home Assistant, the schedule) without surfacing errors.
  Future<void> syncSilently() async {
    if (_brightnessBusy) return;
    final result = await ref.read(apiProvider).getSettings();
    if (!ref.mounted || _brightnessBusy) return;
    final value = result.valueOrNull;
    if (value != null) state = AsyncData(value);
  }

  Future<void> reload() async {
    final result = await ref.read(apiProvider).getSettings();
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  Future<void> setPower(bool enabled) async {
    final previous = state.value;
    if (previous == null) return;
    state = AsyncData(previous.copyWith(isEnabled: enabled));
    final result = await ref.read(apiProvider).setPower(enabled);
    if (!ref.mounted) return;
    final error = result.errorOrNull;
    if (error != null) {
      // Roll back only the power flag so a brightness change made meanwhile is kept.
      final current = state.value;
      state = AsyncData((current ?? previous).copyWith(isEnabled: previous.isEnabled));
      ref.read(errorBusProvider.notifier).report(error);
    }
  }

  /// Slider position in percent (0-100), sent to the device after a short pause.
  void setBrightnessPercent(int percent) {
    final previous = state.value;
    if (previous == null) return;
    final value = percentToBrightness(percent);
    state = AsyncData(previous.copyWith(brightness: value));
    _pendingBrightness = value;
    _brightnessDebounce?.cancel();
    _brightnessDebounce = Timer(const Duration(milliseconds: 250), _flushBrightness);
  }

  /// Sends the newest value, one request at a time, so responses can never arrive out of order and the last value wins.
  Future<void> _flushBrightness() async {
    if (_sending) return; // The running loop picks up the newer value when it finishes.
    _sending = true;
    try {
      while (_pendingBrightness != null) {
        final value = _pendingBrightness!;
        _pendingBrightness = null;
        final result = await ref.read(apiProvider).setBrightness(value);
        if (!ref.mounted) return;
        final error = result.errorOrNull;
        if (error != null) {
          _pendingBrightness = null;
          ref.read(errorBusProvider.notifier).report(error);
          _sending = false;
          await reload();
          return;
        }
      }
    } finally {
      _sending = false;
    }
  }
}

final deviceSettingsProvider = AsyncNotifierProvider<DeviceSettingsNotifier, DeviceSettings>(
  DeviceSettingsNotifier.new,
);

// ---------------------------------------------------------------------------
// Transitions
// ---------------------------------------------------------------------------

class TransitionsNotifier extends AsyncNotifier<TransitionList> {
  @override
  Future<TransitionList> build() async => (await ref.watch(apiProvider).getTransitions()).getOrThrow();

  Future<void> select(String name) async {
    final previous = state.value;
    if (previous == null) return;
    state = AsyncData(previous.copyWith(current: name));
    final result = await ref.read(apiProvider).setTransition(name);
    if (!ref.mounted) return;
    final error = result.errorOrNull;
    if (error != null) {
      state = AsyncData(previous);
      ref.read(errorBusProvider.notifier).report(error);
    }
  }
}

final transitionsProvider = AsyncNotifierProvider<TransitionsNotifier, TransitionList>(TransitionsNotifier.new);

// ---------------------------------------------------------------------------
// Health
// ---------------------------------------------------------------------------

final healthProvider = FutureProvider<Health>((ref) async => (await ref.watch(apiProvider).getHealth()).getOrThrow());

// ---------------------------------------------------------------------------
// Live preview feed
// ---------------------------------------------------------------------------

class PreviewUpdate {
  const PreviewUpdate({this.frame, required this.connected});

  /// The newest frame, or null when none has arrived (yet).
  final PreviewFrame? frame;

  /// Whether the websocket is currently connected. When false the UI falls back to the PNG preview.
  final bool connected;
}

/// Streams `/ws/preview` with reconnect backoff. Tests override this provider.
final previewFeedProvider = StreamProvider.autoDispose<PreviewUpdate>((ref) {
  final api = ref.watch(apiProvider);
  return _previewStream(api);
});

/// The server pushes a frame ~30 times a second even when the picture is static, so this much silence means the
/// connection is dead (typical after the phone sleeps or changes network) and a reconnect is due.
const _previewIdleTimeout = Duration(seconds: 4);
const _previewConnectTimeout = Duration(seconds: 4);

Stream<PreviewUpdate> _previewStream(LedApi api) async* {
  var delay = const Duration(milliseconds: 500);
  while (true) {
    // Show the polled PNG straight away while (re)connecting instead of a spinner.
    yield const PreviewUpdate(connected: false);
    WebSocketChannel? channel;
    var gotFrame = false;
    final started = DateTime.now();
    try {
      channel = WebSocketChannel.connect(api.previewSocketUri);
      await channel.ready.timeout(_previewConnectTimeout);
      yield const PreviewUpdate(connected: true);
      final messages = channel.stream.timeout(_previewIdleTimeout, onTimeout: (sink) => sink.close());
      await for (final message in messages) {
        if (message is List<int>) {
          final frame = parsePreviewFrame(message is Uint8List ? message : Uint8List.fromList(message));
          if (frame != null) {
            gotFrame = true;
            yield PreviewUpdate(frame: frame, connected: true);
          }
        }
      }
    } catch (_) {
      // Fall through to the reconnect path; the UI shows the polled PNG meanwhile.
    } finally {
      // Never await the close: on a half-open socket the handshake can hang and would stall every later retry.
      final dead = channel;
      if (dead != null) unawaited(dead.sink.close().timeout(const Duration(seconds: 1), onTimeout: () {}).catchError((_) {}));
    }
    yield const PreviewUpdate(connected: false);
    // A connection that streamed for a while earns a fast retry; one that failed straight away backs off.
    if (gotFrame && DateTime.now().difference(started) > const Duration(seconds: 5)) {
      delay = const Duration(milliseconds: 500);
    }
    await Future<void>.delayed(delay);
    delay = Duration(milliseconds: (delay.inMilliseconds * 2).clamp(500, 8000));
  }
}

/// Decodes RGBA pixels to a [ui.Image].
Future<ui.Image> decodeFrame(PreviewFrame frame) {
  final completer = Completer<ui.Image>();
  ui.decodeImageFromPixels(frame.rgba, frame.width, frame.height, ui.PixelFormat.rgba8888, completer.complete);
  return completer.future;
}
