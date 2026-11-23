import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'connection.dart';
import 'tokens.dart';

/// Shown when a screen's data cannot be loaded. While the connection layer says the display is unreachable it is a
/// calm "offline" state (the global banner explains the rest); otherwise a short message with the technical detail
/// kept small underneath. Always offers a retry.
class ErrorView extends ConsumerWidget {
  const ErrorView({super.key, required this.error, this.onRetry});

  final Object error;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final connection = ref.watch(connectionProvider);
    final offline = connection.isOffline;
    final retrying = connection.status == ConnectionStatus.connecting && connection.health != null;
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(Space.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: offline ? scheme.surfaceContainerHigh : scheme.errorContainer,
              ),
              child: Icon(
                offline ? Icons.cloud_off_rounded : Icons.error_outline_rounded,
                size: 30,
                color: offline ? scheme.onSurfaceVariant : scheme.onErrorContainer,
              ),
            ),
            const SizedBox(height: Space.lg),
            Text(
              offline ? 'Display offline' : "Couldn't load this",
              key: const Key('error-title'),
              style: text.titleMedium,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: Space.xs),
            Text(
              offline
                  ? "We can't reach the display right now. This page fills in as soon as it is back."
                  : 'Something went wrong talking to the display.',
              textAlign: TextAlign.center,
              style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant),
            ),
            if (!offline) ...[
              const SizedBox(height: Space.sm),
              Text(
                error.toString(),
                textAlign: TextAlign.center,
                maxLines: 3,
                overflow: TextOverflow.ellipsis,
                style: text.bodySmall?.copyWith(color: scheme.outline),
              ),
            ],
            if (onRetry != null || offline) ...[
              const SizedBox(height: Space.lg),
              FilledButton.tonalIcon(
                key: const Key('error-retry'),
                onPressed: retrying
                    ? null
                    : () {
                        if (offline) ref.read(connectionProvider.notifier).check();
                        onRetry?.call();
                      },
                icon: const Icon(Icons.refresh_rounded),
                label: Text(retrying ? 'Reconnecting...' : 'Retry'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// A friendly "nothing here yet" panel: icon, title, an explanation and an optional primary action.
class EmptyState extends StatelessWidget {
  const EmptyState({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.actionLabel,
    this.actionIcon = Icons.add_rounded,
    this.onAction,
    this.actionKey,
  });

  final IconData icon;
  final String title;
  final String message;
  final String? actionLabel;
  final IconData actionIcon;
  final VoidCallback? onAction;
  final Key? actionKey;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.all(Space.xl),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 72,
            height: 72,
            decoration: BoxDecoration(shape: BoxShape.circle, color: scheme.primaryContainer),
            child: Icon(icon, size: 32, color: scheme.onPrimaryContainer),
          ),
          const SizedBox(height: Space.lg),
          Semantics(header: true, child: Text(title, style: text.titleMedium, textAlign: TextAlign.center)),
          const SizedBox(height: Space.xs),
          Text(message, textAlign: TextAlign.center, style: text.bodyMedium?.copyWith(color: scheme.onSurfaceVariant)),
          if (actionLabel != null && onAction != null) ...[
            const SizedBox(height: Space.lg),
            FilledButton.icon(key: actionKey, onPressed: onAction, icon: Icon(actionIcon), label: Text(actionLabel!)),
          ],
        ],
      ),
    );
  }
}

/// Placeholder rows shown while a list loads.
class ListSkeleton extends StatelessWidget {
  const ListSkeleton({super.key, this.rows = 4, this.rowHeight = 72});

  final int rows;
  final double rowHeight;

  @override
  Widget build(BuildContext context) => Semantics(
        label: 'Loading',
        child: ListView(
          physics: const NeverScrollableScrollPhysics(),
          padding: const EdgeInsets.all(Space.lg),
          children: [
            for (var i = 0; i < rows; i++)
              Padding(
                padding: const EdgeInsets.only(bottom: Space.md),
                child: Skeleton(height: rowHeight, radius: Radii.md),
              ),
          ],
        ),
      );
}

/// Asks before a destructive action. Resolves true only when the user confirms.
Future<bool> confirmDestructive(
  BuildContext context, {
  required String title,
  required String message,
  String confirmLabel = 'Delete',
  Key? confirmKey,
}) async {
  final scheme = Theme.of(context).colorScheme;
  final ok = await showDialog<bool>(
    context: context,
    useRootNavigator: true,
    builder: (dialogContext) => AlertDialog(
      title: Text(title),
      content: Text(message),
      actions: [
        TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Cancel')),
        FilledButton(
          key: confirmKey,
          style: FilledButton.styleFrom(backgroundColor: scheme.error, foregroundColor: scheme.onError),
          onPressed: () => Navigator.pop(dialogContext, true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
  return ok == true;
}

class SectionCard extends StatelessWidget {
  const SectionCard({super.key, required this.child, this.padding = const EdgeInsets.all(16)});

  final Widget child;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) => Card(child: Padding(padding: padding, child: child));
}

/// Small-caps style heading above a group of controls.
class SectionHeader extends StatelessWidget {
  const SectionHeader(this.title, {super.key, this.trailing});

  final String title;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 0, 4, Space.sm),
      child: Row(
        children: [
          Expanded(
            child: Semantics(
              header: true,
              child: Text(
                title,
                style: Theme.of(context).textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant, letterSpacing: 1),
              ),
            ),
          ),
          if (trailing != null) trailing!,
        ],
      ),
    );
  }
}

/// A pulsing placeholder block shown while data loads. Static when animations are disabled.
class Skeleton extends StatefulWidget {
  const Skeleton({super.key, this.height = 16, this.width, this.radius = Radii.sm});

  final double height;
  final double? width;
  final double radius;

  @override
  State<Skeleton> createState() => _SkeletonState();
}

class _SkeletonState extends State<Skeleton> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 1100));

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.of(context).disableAnimations) {
      _c.stop();
    } else if (!_c.isAnimating) {
      _c.repeat(reverse: true);
    }
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return ExcludeSemantics(
      child: AnimatedBuilder(
        animation: _c,
        builder: (context, _) => Container(
          height: widget.height,
          width: widget.width,
          decoration: BoxDecoration(
            color: Color.lerp(scheme.surfaceContainerHigh, scheme.surfaceContainerHighest, Curves.easeInOut.transform(_c.value)),
            borderRadius: BorderRadius.circular(widget.radius),
          ),
        ),
      ),
    );
  }
}

/// A matrix "bezel": near-black frame with a soft accent glow, like a powered LED panel.
class GlowPreview extends StatelessWidget {
  const GlowPreview({super.key, required this.child, this.glow = true});

  final Widget child;
  final bool glow;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return DecoratedBox(
      decoration: BoxDecoration(
        color: Brand.panel,
        borderRadius: BorderRadius.circular(Radii.bezel + 6),
        border: Border.all(color: scheme.outlineVariant),
        boxShadow: [
          if (glow) BoxShadow(color: scheme.primary.withValues(alpha: 0.16), blurRadius: 28, spreadRadius: -2),
        ],
      ),
      child: Padding(
        padding: const EdgeInsets.all(6),
        child: ClipRRect(borderRadius: BorderRadius.circular(Radii.bezel), child: child),
      ),
    );
  }
}
