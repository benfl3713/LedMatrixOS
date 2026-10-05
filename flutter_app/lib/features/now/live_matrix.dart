import 'dart:async';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/preview_frame.dart';
import '../../core/providers.dart';

/// The live matrix from `/ws/preview`, falling back to polling the PNG while the socket is down.
/// Fills its parent; callers size it to the matrix aspect ratio.
class LiveMatrix extends ConsumerStatefulWidget {
  const LiveMatrix({super.key, this.ledDots = false, this.onLiveChanged});

  /// Draw each pixel as a round LED with a dark gap around it, like the real panel.
  final bool ledDots;

  /// Called when the first frame arrives (true) or the feed drops (false).
  final ValueChanged<bool>? onLiveChanged;

  @override
  ConsumerState<LiveMatrix> createState() => _LiveMatrixState();
}

class _LiveMatrixState extends ConsumerState<LiveMatrix> with WidgetsBindingObserver {
  ui.Image? _image;
  bool _decoding = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  /// Sockets silently die while the app is backgrounded; reconnect as soon as it is visible again.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) ref.invalidate(previewFeedProvider);
  }

  Future<void> _onFrame(PreviewFrame frame) async {
    if (_decoding) return; // drop frames while one is still decoding
    _decoding = true;
    try {
      final image = await decodeFrame(frame);
      if (!mounted) {
        image.dispose();
        return;
      }
      final old = _image;
      setState(() => _image = image);
      old?.dispose();
      if (old == null) widget.onLiveChanged?.call(true);
    } finally {
      _decoding = false;
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _image?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(previewFeedProvider, (previous, next) {
      final update = next.value;
      if (update == null) return;
      final frame = update.frame;
      if (frame != null) {
        _onFrame(frame);
      } else if (!update.connected && _image != null) {
        setState(() {
          _image?.dispose();
          _image = null;
        });
        widget.onLiveChanged?.call(false);
      }
    });

    final feed = ref.watch(previewFeedProvider);
    final connected = feed.value?.connected ?? false;

    Widget content;
    if (_image != null) {
      content = widget.ledDots
          ? CustomPaint(painter: _LedDotsPainter(_image!), size: Size.infinite)
          : RawImage(image: _image, fit: BoxFit.fill, filterQuality: FilterQuality.none);
    } else if (!connected && feed.hasValue) {
      content = _PngFallback(key: ValueKey(ref.watch(apiProvider).baseUrl));
    } else if (connected) {
      content = const Center(child: Icon(Icons.grid_on_rounded, color: Colors.white24, size: 32));
    } else {
      content = const Center(child: CircularProgressIndicator(strokeWidth: 2));
    }
    return ColoredBox(color: Colors.black, child: content);
  }
}

/// Nearest-neighbour scaled frame with a mask of round holes on top, one hole per pixel.
/// The mask is a single tile repeated by a shader, so the cost per frame is one image draw and one rect fill.
class _LedDotsPainter extends CustomPainter {
  _LedDotsPainter(this.image);

  final ui.Image image;

  static const _tile = 32.0;
  static ui.Image? _mask;

  /// Black square with a transparent circle covering 80% of it.
  static ui.Image _maskTile() => _mask ??= () {
        final recorder = ui.PictureRecorder();
        final canvas = Canvas(recorder);
        canvas.drawRect(const Rect.fromLTWH(0, 0, _tile, _tile), Paint()..color = Colors.black);
        canvas.drawCircle(const Offset(_tile / 2, _tile / 2), _tile * 0.4, Paint()..blendMode = BlendMode.clear);
        return recorder.endRecording().toImageSync(_tile.toInt(), _tile.toInt());
      }();

  @override
  void paint(Canvas canvas, Size size) {
    final dst = Offset.zero & size;
    final src = Rect.fromLTWH(0, 0, image.width.toDouble(), image.height.toDouble());
    canvas.drawImageRect(image, src, dst, Paint()..filterQuality = FilterQuality.none);

    final cellW = size.width / image.width;
    final cellH = size.height / image.height;
    final transform = Matrix4.diagonal3Values(cellW / _tile, cellH / _tile, 1).storage;
    final shader = ui.ImageShader(_maskTile(), TileMode.repeated, TileMode.repeated, transform,
        filterQuality: math.min(cellW, cellH) < 4 ? FilterQuality.none : FilterQuality.medium);
    canvas.drawRect(dst, Paint()..shader = shader);
  }

  @override
  bool shouldRepaint(_LedDotsPainter oldDelegate) => oldDelegate.image != image;
}

/// Used only while the websocket is down: re-fetches the PNG `/preview` once a second.
class _PngFallback extends ConsumerStatefulWidget {
  const _PngFallback({super.key});

  @override
  ConsumerState<_PngFallback> createState() => _PngFallbackState();
}

class _PngFallbackState extends ConsumerState<_PngFallback> {
  Timer? _timer;
  int _tick = DateTime.now().millisecondsSinceEpoch;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() => _tick = DateTime.now().millisecondsSinceEpoch);
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final api = ref.watch(apiProvider);
    return Image.network(
      api.previewUrl(cacheBust: _tick),
      gaplessPlayback: true,
      fit: BoxFit.fill,
      filterQuality: FilterQuality.none,
      errorBuilder: (context, error, stack) => Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.tv_off_rounded, color: Colors.white38),
            const SizedBox(height: 6),
            Text('Preview unavailable: $error',
                maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Colors.white54, fontSize: 11)),
          ],
        ),
      ),
    );
  }
}
