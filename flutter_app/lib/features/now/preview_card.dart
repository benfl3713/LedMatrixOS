import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import 'live_matrix.dart';

/// The hero of the Now screen: the live matrix, pixel-perfect, in its true aspect ratio.
class PreviewCard extends ConsumerStatefulWidget {
  const PreviewCard({super.key});

  @override
  ConsumerState<PreviewCard> createState() => _PreviewCardState();
}

class _PreviewCardState extends ConsumerState<PreviewCard> {
  bool _live = false;

  @override
  Widget build(BuildContext context) {
    final feed = ref.watch(previewFeedProvider);
    final settings = ref.watch(deviceSettingsProvider).value;
    final scheme = Theme.of(context).colorScheme;
    final connected = feed.value?.connected ?? false;
    final aspect = settings != null && settings.width > 0 && settings.height > 0 ? settings.width / settings.height : 4.0;

    return Card(
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          AspectRatio(
            aspectRatio: aspect,
            child: GestureDetector(
              onLongPress: () => ref.invalidate(previewFeedProvider),
              child: LiveMatrix(onLiveChanged: (live) => setState(() => _live = live)),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 4, 4),
            child: Row(
              children: [
                Icon(Icons.circle, size: 10, color: _live ? Colors.redAccent : scheme.outline),
                const SizedBox(width: 8),
                Text(
                  _live ? 'Live' : (connected ? 'Waiting for frames' : 'Reconnecting (polling preview)'),
                  style: Theme.of(context).textTheme.labelMedium,
                ),
                if (!_live && !connected) ...[
                  const SizedBox(width: 8),
                  TextButton(
                    onPressed: () => ref.invalidate(previewFeedProvider),
                    style: TextButton.styleFrom(visualDensity: VisualDensity.compact),
                    child: const Text('Retry now'),
                  ),
                ],
                const Spacer(),
                if (settings != null)
                  Text(
                    '${settings.width}x${settings.height}${settings.fps > 0 ? '  ${settings.fps} fps' : ''}',
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant),
                  ),
                IconButton(
                  key: const Key('preview-fullscreen'),
                  tooltip: 'Full screen display',
                  icon: const Icon(Icons.fullscreen_rounded),
                  onPressed: () => context.push('/display'),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
