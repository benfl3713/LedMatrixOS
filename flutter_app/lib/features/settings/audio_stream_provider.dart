import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../services/audio_stream_service.dart';

class AudioStreamState {
  const AudioStreamState({this.isStreaming = false, this.error});

  final bool isStreaming;
  final String? error;
}

/// Streams the phone microphone to the equalizer app.
class AudioStreamNotifier extends Notifier<AudioStreamState> {
  AudioStreamService? _service;

  AudioStreamService _serviceFor(String baseUrl) => _service ??= AudioStreamService(
        baseUrl: baseUrl,
        onError: (message) {
          if (ref.mounted) state = AudioStreamState(isStreaming: state.isStreaming, error: message);
        },
      );

  @override
  AudioStreamState build() {
    // A new device URL means a new service.
    final baseUrl = ref.watch(apiUrlProvider);
    ref.onDispose(() {
      _service?.dispose();
      _service = null;
    });
    _service?.dispose();
    _service = null;
    _baseUrl = baseUrl;
    return const AudioStreamState();
  }

  String _baseUrl = defaultApiUrl;

  Future<void> toggle() => state.isStreaming ? stop() : start();

  Future<void> start() async {
    state = const AudioStreamState();
    try {
      final ok = await _serviceFor(_baseUrl).startStreaming();
      state = ok
          ? const AudioStreamState(isStreaming: true)
          : const AudioStreamState(error: 'Could not start streaming. Check microphone permission.');
    } catch (e) {
      state = AudioStreamState(error: e.toString());
    }
  }

  Future<void> stop() async {
    try {
      await _service?.stopStreaming();
      state = const AudioStreamState();
    } catch (e) {
      state = AudioStreamState(error: e.toString());
    }
  }
}

final audioStreamProvider = NotifierProvider<AudioStreamNotifier, AudioStreamState>(AudioStreamNotifier.new);
