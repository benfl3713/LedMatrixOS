import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'live_matrix.dart';

/// The hero of the Now screen: the live matrix in a dark bezel, in its true aspect ratio.
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
    final text = Theme.of(context).textTheme;
    final connected = feed.value?.connected ?? false;
    final aspect = settings != null && settings.width > 0 && settings.height > 0 ? settings.width / settings.height : 4.0;
    final poweredOn = settings?.isEnabled ?? true;

    final status = _live ? 'Live' : (connected ? 'Waiting for frames' : 'Reconnecting (polling preview)');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Semantics(
          label: 'Live preview of the LED matrix. Double tap for full screen.',
          button: true,
          child: GestureDetector(
            key: const Key('preview-hero'),
            onTap: () => context.push('/display'),
            onLongPress: () => ref.invalidate(previewFeedProvider),
            child: GlowPreview(
              glow: _live && poweredOn,
              child: AspectRatio(
                aspectRatio: aspect,
                child: LiveMatrix(onLiveChanged: (live) => setState(() => _live = live)),
              ),
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.only(left: 6, top: Space.xs),
          child: Row(
            children: [
              _LiveDot(active: _live),
              const SizedBox(width: Space.sm),
              Flexible(
                child: Text(
                  status,
                  overflow: TextOverflow.ellipsis,
                  style: text.labelMedium?.copyWith(color: _live ? scheme.onSurface : scheme.onSurfaceVariant),
                ),
              ),
              if (!_live && !connected)
                TextButton(
                  onPressed: () => ref.invalidate(previewFeedProvider),
                  style: TextButton.styleFrom(visualDensity: VisualDensity.compact),
                  child: const Text('Retry now'),
                ),
              const Spacer(),
              if (settings != null)
                Text(
                  '${settings.width}x${settings.height}${settings.fps > 0 ? '  ${settings.fps} fps' : ''}',
                  style: numericStyle(text.labelMedium?.copyWith(color: scheme.onSurfaceVariant)),
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
    );
  }
}

class _LiveDot extends StatelessWidget {
  const _LiveDot({required this.active});

  final bool active;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      width: 10,
      height: 10,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: active ? Brand.live : scheme.outline,
        boxShadow: [if (active) BoxShadow(color: Brand.live.withValues(alpha: 0.6), blurRadius: 8)],
      ),
    );
  }
}
