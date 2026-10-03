import 'dart:async';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import '../api/led_api.dart';
import '../api/models.dart';
import '../api/preview_frame.dart';

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
    final trimmed = url.trim();
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

final apiProvider = Provider<LedApi>((ref) => HttpLedApi(baseUrl: ref.watch(apiUrlProvider)));

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
      final timer = Timer.periodic(interval, (_) => _silentRefresh());
      ref.onDispose(timer.cancel);
    }
    return (await api.getApps()).getOrThrow();
  }

  Future<void> _silentRefresh() async {
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

  @override
  Future<DeviceSettings> build() async {
    ref.onDispose(() => _brightnessDebounce?.cancel());
    return (await ref.watch(apiProvider).getSettings()).getOrThrow();
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
      state = AsyncData(previous);
      ref.read(errorBusProvider.notifier).report(error);
    }
  }

  /// Slider position in percent (0-100). Sent to the device as 0-255 after a short pause.
  void setBrightnessPercent(int percent) {
    final previous = state.value;
    if (previous == null) return;
    final raw = percentToBrightness(percent);
    state = AsyncData(previous.copyWith(brightness: raw));
    _brightnessDebounce?.cancel();
    _brightnessDebounce = Timer(const Duration(milliseconds: 250), () => _sendBrightness(raw));
  }

  Future<void> _sendBrightness(int raw) async {
    final result = await ref.read(apiProvider).setBrightness(raw);
    if (!ref.mounted) return;
    final error = result.errorOrNull;
    if (error != null) {
      ref.read(errorBusProvider.notifier).report(error);
      await reload();
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

Stream<PreviewUpdate> _previewStream(LedApi api) async* {
  var delay = const Duration(seconds: 1);
  while (true) {
    WebSocketChannel? channel;
    try {
      channel = WebSocketChannel.connect(api.previewSocketUri);
      await channel.ready.timeout(const Duration(seconds: 5));
      delay = const Duration(seconds: 1);
      yield const PreviewUpdate(connected: true);
      await for (final message in channel.stream) {
        if (message is List<int>) {
          final frame = parsePreviewFrame(message is Uint8List ? message : Uint8List.fromList(message));
          if (frame != null) yield PreviewUpdate(frame: frame, connected: true);
        }
      }
    } catch (_) {
      // Fall through to the reconnect path; the UI shows the polled PNG meanwhile.
    } finally {
      await channel?.sink.close();
    }
    yield const PreviewUpdate(connected: false);
    await Future<void>.delayed(delay);
    delay = Duration(seconds: (delay.inSeconds * 2).clamp(1, 15));
  }
}

/// Decodes RGBA pixels to a [ui.Image].
Future<ui.Image> decodeFrame(PreviewFrame frame) {
  final completer = Completer<ui.Image>();
  ui.decodeImageFromPixels(frame.rgba, frame.width, frame.height, ui.PixelFormat.rgba8888, completer.complete);
  return completer.future;
}
