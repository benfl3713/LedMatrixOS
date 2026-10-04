import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/preview_frame.dart';
import '../../core/providers.dart';

/// The hero of the Now screen: the live matrix, pixel-perfect, in its true aspect ratio.
class PreviewCard extends ConsumerStatefulWidget {
  const PreviewCard({super.key});

  @override
  ConsumerState<PreviewCard> createState() => _PreviewCardState();
}

class _PreviewCardState extends ConsumerState<PreviewCard> {
  ui.Image? _image;
  bool _decoding = false;

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
    } finally {
      _decoding = false;
    }
  }

  @override
  void dispose() {
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
      }
    });

    final feed = ref.watch(previewFeedProvider);
    final settings = ref.watch(deviceSettingsProvider).value;
    final scheme = Theme.of(context).colorScheme;
    final connected = feed.value?.connected ?? false;
    final aspect = _image != null
        ? _image!.width / _image!.height
        : (settings != null && settings.width > 0 && settings.height > 0 ? settings.width / settings.height : 4.0);

    Widget content;
    if (_image != null) {
      content = RawImage(image: _image, fit: BoxFit.fill, filterQuality: FilterQuality.none);
    } else if (!connected && feed.hasValue) {
      content = _PngFallback(key: ValueKey(ref.watch(apiProvider).baseUrl));
    } else if (connected) {
      content = const Center(child: Icon(Icons.grid_on_rounded, color: Colors.white24, size: 32));
    } else {
      content = const Center(child: CircularProgressIndicator(strokeWidth: 2));
    }

    return Card(
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          AspectRatio(
            aspectRatio: aspect,
            child: ColoredBox(color: Colors.black, child: content),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
            child: Row(
              children: [
                Icon(Icons.circle, size: 10, color: _image != null ? Colors.redAccent : scheme.outline),
                const SizedBox(width: 8),
                Text(
                  _image != null ? 'Live' : (connected ? 'Waiting for frames' : 'Reconnecting (polling preview)'),
                  style: Theme.of(context).textTheme.labelMedium,
                ),
                const Spacer(),
                if (settings != null)
                  Text(
                    '${settings.width}x${settings.height}${settings.fps > 0 ? '  ${settings.fps} fps' : ''}',
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
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
