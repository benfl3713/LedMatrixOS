import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:web_socket_channel/web_socket_channel.dart';

enum PadButton { up, down, left, right, a, b, start, select }

enum PadState { down, up, press }

/// Sends gamepad input to the device: WebSocket `/ws/input` when connected, POST `/api/input` otherwise.
/// Reconnects in the background. Never throws.
class InputService {
  InputService({
    required String baseUrl,
    http.Client? client,
    WebSocketChannel Function(Uri uri)? connect,
    this.reconnectDelay = const Duration(seconds: 2),
  })  : baseUrl = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl,
        _client = client ?? http.Client(),
        _connect = connect ?? WebSocketChannel.connect;

  final String baseUrl;
  final http.Client _client;
  final WebSocketChannel Function(Uri uri) _connect;
  final Duration reconnectDelay;

  WebSocketChannel? _channel;
  Timer? _retry;
  bool _disposed = false;
  bool _connected = false;
  final _status = StreamController<bool>.broadcast();

  /// True while the WebSocket is open.
  bool get connected => _connected;
  Stream<bool> get connectionChanges => _status.stream;

  Uri get socketUri {
    final uri = Uri.parse(baseUrl);
    return uri.replace(scheme: uri.scheme == 'https' ? 'wss' : 'ws', path: '/ws/input');
  }

  static Map<String, Object> payload(int player, PadButton button, PadState state) =>
      {'player': player, 'button': button.name, 'state': state.name};

  void _setConnected(bool value) {
    if (_connected == value) return;
    _connected = value;
    if (!_status.isClosed) _status.add(value);
  }

  /// Opens the socket (idempotent). Failures schedule a retry.
  Future<void> start() async {
    if (_disposed || _channel != null) return;
    final WebSocketChannel channel;
    try {
      channel = _connect(socketUri);
    } catch (_) {
      _scheduleRetry();
      return;
    }
    _channel = channel;
    channel.stream.listen(
      (_) {},
      onError: (_) => _lost(channel),
      onDone: () => _lost(channel),
      cancelOnError: true,
    );
    try {
      await channel.ready;
      if (_channel == channel && !_disposed) _setConnected(true);
    } catch (_) {
      _lost(channel);
    }
  }

  void _lost(WebSocketChannel channel) {
    if (_channel != channel) return;
    _channel = null;
    _setConnected(false);
    unawaited(channel.sink.close().timeout(const Duration(seconds: 1), onTimeout: () {}).catchError((_) {}));
    _scheduleRetry();
  }

  void _scheduleRetry() {
    if (_disposed) return;
    _retry?.cancel();
    _retry = Timer(reconnectDelay, start);
  }

  /// Sends one event. Returns false if neither transport could deliver it.
  Future<bool> send(int player, PadButton button, PadState state) async {
    final body = jsonEncode(payload(player, button, state));
    final channel = _channel;
    if (_connected && channel != null) {
      try {
        channel.sink.add(body);
        return true;
      } catch (_) {
        _lost(channel);
      }
    }
    try {
      final response = await _client
          .post(Uri.parse('$baseUrl/api/input'), headers: {'Content-Type': 'application/json'}, body: body)
          .timeout(const Duration(seconds: 2));
      return response.statusCode >= 200 && response.statusCode < 300;
    } catch (_) {
      return false;
    }
  }

  void dispose() {
    _disposed = true;
    _retry?.cancel();
    final channel = _channel;
    _channel = null;
    if (channel != null) unawaited(channel.sink.close().catchError((_) {}));
    _status.close();
    _client.close();
  }
}
