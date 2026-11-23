import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import '../now/live_matrix.dart';

/// "On display" pill shown for the app that is showing right now.
class OnDisplayBadge extends StatelessWidget {
  const OnDisplayBadge({super.key});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Semantics(
      label: 'On display',
      child: ExcludeSemantics(
        child: Container(
          key: const Key('on-display-badge'),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(
            color: scheme.primary.withValues(alpha: 0.16),
            borderRadius: BorderRadius.circular(999),
            border: Border.all(color: scheme.primary.withValues(alpha: 0.6)),
          ),
          child: Row(mainAxisSize: MainAxisSize.min, children: [
            Icon(Icons.circle, size: 8, color: scheme.primary),
            const SizedBox(width: 6),
            Flexible(child: Text('On display', maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.labelMedium?.copyWith(color: scheme.primary))),
          ]),
        ),
      ),
    );
  }
}

/// Live matrix at the top of the sheet, so edits to the active app are visible at once.
class SheetPreview extends ConsumerWidget {
  const SheetPreview({super.key, this.compact = false});

  /// Height of the compact (pinned) preview.
  static const compactHeight = 80.0;

  /// A short preview that keeps the 4:1 shape, centred, for pinning above scrolling settings.
  final bool compact;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final settings = ref.watch(deviceSettingsProvider).value;
    final aspect = settings != null && settings.width > 0 && settings.height > 0 ? settings.width / settings.height : 4.0;
    final preview = GlowPreview(
      key: const Key('sheet-preview'),
      child: AspectRatio(aspectRatio: aspect, child: const LiveMatrix()),
    );
    return Semantics(
      label: 'Live preview of the display',
      child: compact
          ? SizedBox(
              height: compactHeight,
              child: Center(child: AspectRatio(aspectRatio: aspect, child: preview)),
            )
          : preview,
    );
  }
}

/// The prominent activate action for an app that is not on the display.
class ShowOnDisplayCard extends ConsumerWidget {
  const ShowOnDisplayCard({super.key, required this.app, required this.withPreviewHint});

  final MatrixApp app;
  final bool withPreviewHint;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(Space.md),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(Radii.md),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (withPreviewHint)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.sm),
              child: Text('Show it on the display to preview your changes live.',
                  textAlign: TextAlign.center, style: Theme.of(context).textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
            ),
          FilledButton.icon(
            key: const Key('sheet-activate'),
            style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48)),
            onPressed: () => ref.read(appListProvider.notifier).activate(app.id),
            icon: const Icon(Icons.play_arrow_rounded),
            label: const Text('Show on display'),
          ),
        ],
      ),
    );
  }
}
