import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:permission_handler/permission_handler.dart';
import 'package:record/record.dart';

/// Service to stream microphone audio data to the LED Matrix for equalizer visualization
class AudioStreamService {
  final String baseUrl;
  final AudioRecorder _recorder = AudioRecorder();
  Timer? _streamTimer;
  bool _isStreaming = false;

  static const int sampleRate = 44100;
  static const int chunkDurationMs = 100; // Send audio every 100ms
  static const int sampleCount = 512; // Number of samples to send

  AudioStreamService({required this.baseUrl});

  /// Check if microphone permission is granted
  Future<bool> checkPermission() async {
    final status = await Permission.microphone.status;
    if (status.isGranted) {
      return true;
    }

    final result = await Permission.microphone.request();
    return result.isGranted;
  }

  /// Start streaming audio to the LED Matrix
  Future<bool> startStreaming() async {
    if (_isStreaming) return true;

    // Check microphone permission
    if (!await checkPermission()) {
      throw Exception('Microphone permission denied');
    }

    try {
      // Start recording
      if (await _recorder.hasPermission()) {
        await _recorder.startStream(
          const RecordConfig(
            encoder: AudioEncoder.pcm16bits,
            sampleRate: sampleRate,
            numChannels: 1,
          ),
        );

        _isStreaming = true;

        // Start periodic streaming using amplitude data
        _streamTimer = Timer.periodic(
          Duration(milliseconds: chunkDurationMs),
          (_) => _captureAndSendAudio(),
        );

        return true;
      }
      return false;
    } catch (e) {
      print('Error starting audio stream: $e');
      return false;
    }
  }

  /// Stop streaming audio
  Future<void> stopStreaming() async {
    _streamTimer?.cancel();
    _streamTimer = null;
    _isStreaming = false;

    await _recorder.stop();
  }

  /// Capture audio and send to server
  Future<void> _captureAndSendAudio() async {
    try {
      // Get the current amplitude
      final amplitude = await _recorder.getAmplitude();

      // Convert amplitude to normalized samples
      // Amplitude comes in as decibels (typically -160 to 0)
      final normalizedAmplitude = _normalizeAmplitude(amplitude.current);

      // Generate frequency-band-like samples from amplitude
      // This simulates what real FFT would produce
      final samples = _generateFrequencyBands(normalizedAmplitude);

      print(samples);

      // Send to server
      await _sendAudioData(samples);
    } catch (e) {
      print('Error capturing/sending audio: $e');
    }
  }

  /// Normalize amplitude from dB to 0-1 range
  double _normalizeAmplitude(double amplitudeDb) {
    // Amplitude typically ranges from -160 (silence) to 0 (max)
    // Normalize to 0-1 range with some scaling for better visualization
    const minDb = -60.0; // Ignore very quiet sounds
    const maxDb = 0.0;

    final clamped = amplitudeDb.clamp(minDb, maxDb);
    final normalized = (clamped - minDb) / (maxDb - minDb);

    // Apply some boost for better visualization
    return (normalized * 1.5).clamp(0.0, 1.0);
  }

  /// Generate frequency band samples from amplitude
  /// This creates a spectrum-like effect even though we only have amplitude
  List<double> _generateFrequencyBands(double amplitude) {
    final samples = <double>[];
    final now = DateTime.now().millisecondsSinceEpoch;

    // Generate samples with frequency-like variation
    for (int i = 0; i < sampleCount; i++) {
      // Create variation across frequency bands
      final bandPosition = i / sampleCount;

      // Add some time-based variation for animation
      final timeVariation = ((now / 50) % 100) / 100;

      // Lower frequencies get more energy (typical for music)
      final frequencyBias = 1.0 - (bandPosition * 0.7);

      // Add some randomness
      final random = ((i * 123 + now) % 100) / 100.0;

      // Combine all factors
      var sample = amplitude * frequencyBias * (0.7 + random * 0.3);

      // Add some variation based on time
      sample *= (0.8 + timeVariation * 0.4);

      samples.add(sample.clamp(0.0, 1.0));
    }

    return samples;
  }

  /// Send audio data to the server
  Future<void> _sendAudioData(List<double> samples) async {
    try {
      final response = await http
          .post(
            Uri.parse('$baseUrl/api/audio/stream'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({
              'Samples': samples,
              'SampleRate': sampleRate,
            }),
          )
          .timeout(const Duration(seconds: 2));

      if (response.statusCode != 200) {
        print(
            'Failed to send audio data: ${response.statusCode} - ${response.body}');
      }
    } catch (e) {
      print('Error sending audio data: $e');
      // Don't throw - just log and continue streaming
    }
  }

  /// Check if audio streaming is active
  bool get isStreaming => _isStreaming;

  /// Check the status on the server
  Future<Map<String, dynamic>?> checkServerStatus() async {
    try {
      final response = await http.get(
        Uri.parse('$baseUrl/api/audio/status'),
      );

      if (response.statusCode == 200) {
        return jsonDecode(response.body);
      }
    } catch (e) {
      print('Error checking server status: $e');
    }
    return null;
  }

  /// Clean up resources
  void dispose() {
    stopStreaming();
    _recorder.dispose();
  }
}
