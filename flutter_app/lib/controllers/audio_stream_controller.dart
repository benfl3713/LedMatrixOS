import 'package:flutter/foundation.dart';
import '../services/audio_stream_service.dart';

/// Controller for managing audio streaming to the LED Matrix equalizer
class AudioStreamController extends ChangeNotifier {
  AudioStreamService? _audioService;
  bool _isStreaming = false;
  String? _error;

  bool get isStreaming => _isStreaming;
  String? get error => _error;

  /// Initialize with the API base URL
  void initialize(String baseUrl) {
    _audioService?.dispose();
    _audioService = AudioStreamService(baseUrl: baseUrl);
  }

  /// Start streaming audio from microphone
  Future<void> startStreaming() async {
    if (_audioService == null) {
      _error = 'Audio service not initialized';
      notifyListeners();
      return;
    }

    try {
      _error = null;
      notifyListeners();

      final success = await _audioService!.startStreaming();
      
      _isStreaming = success;
      if (!success) {
        _error = 'Failed to start audio streaming - check microphone permissions';
      }
      notifyListeners();
    } catch (e) {
      _isStreaming = false;
      _error = e.toString();
      notifyListeners();
    }
  }

  /// Stop streaming audio
  Future<void> stopStreaming() async {
    if (_audioService == null) return;

    try {
      await _audioService!.stopStreaming();
      _isStreaming = false;
      _error = null;
      notifyListeners();
    } catch (e) {
      _error = e.toString();
      notifyListeners();
    }
  }

  /// Toggle streaming on/off
  Future<void> toggleStreaming() async {
    if (_isStreaming) {
      await stopStreaming();
    } else {
      await startStreaming();
    }
  }

  /// Check server status
  Future<Map<String, dynamic>?> checkServerStatus() async {
    if (_audioService == null) return null;
    return await _audioService!.checkServerStatus();
  }

  @override
  void dispose() {
    _audioService?.dispose();
    super.dispose();
  }
}

