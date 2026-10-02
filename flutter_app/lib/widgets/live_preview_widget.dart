import 'dart:async';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import '../api_service.dart';

/// Shows the matrix. Frames stream over `/ws/preview` (binary: width u16, height u16, then RGB);
/// if the socket cannot connect, it falls back to polling the PNG preview.
class LivePreviewWidget extends StatefulWidget {
  final LedMatrixApi api;
  final String previewImageKey;

  const LivePreviewWidget({
    super.key,
    required this.api,
    required this.previewImageKey,
  });

  @override
  State<LivePreviewWidget> createState() => _LivePreviewWidgetState();
}

class _LivePreviewWidgetState extends State<LivePreviewWidget> {
  WebSocketChannel? _channel;
  StreamSubscription? _subscription;
  Timer? _retry;
  ui.Image? _frame;
  bool _decoding = false;

  LedMatrixApi get api => widget.api;
  String get previewImageKey => widget.previewImageKey;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void didUpdateWidget(LivePreviewWidget oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.api.baseUrl != widget.api.baseUrl) {
      _disconnect();
      _connect();
    }
  }

  void _connect() {
    try {
      final channel = WebSocketChannel.connect(api.getPreviewSocketUri());
      _channel = channel;
      _subscription = channel.stream.listen(
        _onMessage,
        onError: (_) => _onClosed(),
        onDone: _onClosed,
        cancelOnError: true,
      );
    } catch (_) {
      _onClosed();
    }
  }

  void _disconnect() {
    _retry?.cancel();
    _subscription?.cancel();
    _channel?.sink.close();
    _subscription = null;
    _channel = null;
    _frame?.dispose();
    _frame = null;
  }

  void _onClosed() {
    if (!mounted) return;
    setState(() {
      _frame?.dispose();
      _frame = null; // fall back to the polled image
    });
    _retry?.cancel();
    _retry = Timer(const Duration(seconds: 5), () {
      if (mounted) _connect();
    });
  }

  void _onMessage(dynamic message) {
    if (_decoding || message is! List<int>) return; // drop frames while one is still being decoded
    final bytes = message is Uint8List ? message : Uint8List.fromList(message);
    if (bytes.length < 4) return;
    final width = bytes[0] | (bytes[1] << 8);
    final height = bytes[2] | (bytes[3] << 8);
    if (width == 0 || height == 0 || bytes.length < 4 + width * height * 3) return;

    final rgba = Uint8List(width * height * 4);
    for (int i = 0, src = 4, dst = 0; i < width * height; i++) {
      rgba[dst++] = bytes[src++];
      rgba[dst++] = bytes[src++];
      rgba[dst++] = bytes[src++];
      rgba[dst++] = 255;
    }

    _decoding = true;
    ui.decodeImageFromPixels(rgba, width, height, ui.PixelFormat.rgba8888, (image) {
      _decoding = false;
      if (!mounted) {
        image.dispose();
        return;
      }
      setState(() {
        _frame?.dispose();
        _frame = image;
      });
    });
  }

  @override
  void dispose() {
    _disconnect();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Card(
      clipBehavior: Clip.hardEdge,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 12, 12),
            child: Row(
              children: [
                Icon(Icons.monitor_rounded, color: colorScheme.primary, size: 18),
                const SizedBox(width: 8),
                Text(
                  'Live Preview',
                  style: Theme.of(context).textTheme.titleSmall?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                ),
                const Spacer(),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: colorScheme.errorContainer,
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Container(
                        width: 5,
                        height: 5,
                        decoration: BoxDecoration(
                          color: colorScheme.error,
                          shape: BoxShape.circle,
                        ),
                      ),
                      const SizedBox(width: 4),
                      Text(
                        'LIVE',
                        style: TextStyle(
                          fontSize: 10,
                          color: colorScheme.onErrorContainer,
                          fontWeight: FontWeight.w700,
                          letterSpacing: 0.8,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          Container(
            width: double.infinity,
            color: Colors.black,
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 180, minHeight: 80),
              child: _frame != null
                  ? RawImage(
                      image: _frame,
                      fit: BoxFit.contain,
                      filterQuality: FilterQuality.none,
                    )
                  : Image.network(
                '${api.getPreviewUrl()}&key=$previewImageKey',
                gaplessPlayback: true,
                fit: BoxFit.contain,
                filterQuality: FilterQuality.none,
                errorBuilder: (context, error, stackTrace) {
                  return const SizedBox(
                    height: 100,
                    child: Center(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(Icons.tv_off_rounded,
                              size: 36, color: Colors.white24),
                          SizedBox(height: 8),
                          Text(
                            'Preview unavailable',
                            style:
                                TextStyle(color: Colors.white38, fontSize: 12),
                          ),
                          Text(
                            'Simulator mode only',
                            style:
                                TextStyle(color: Colors.white24, fontSize: 11),
                          ),
                        ],
                      ),
                    ),
                  );
                },
                loadingBuilder: (context, child, loadingProgress) {
                  if (loadingProgress == null) return child;
                  return const SizedBox(
                    height: 100,
                    child: Center(
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  );
                },
              ),
            ),
          ),
        ],
      ),
    );
  }
}
