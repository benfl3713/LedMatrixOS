import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/app_icons.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'preview_card.dart';

class NowPage extends ConsumerWidget {
  const NowPage({super.key});

  Future<void> _refresh(WidgetRef ref) async {
    ref.invalidate(appListProvider);
    ref.invalidate(deviceSettingsProvider);
    ref.invalidate(transitionsProvider);
    await Future.wait([
      ref.read(appListProvider.future).then<void>((_) {}, onError: (_) {}),
      ref.read(deviceSettingsProvider.future).then<void>((_) {}, onError: (_) {}),
    ]);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final apps = ref.watch(appListProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Now')),
      body: RefreshIndicator(
        onRefresh: () => _refresh(ref),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.xs, Space.lg, Space.xl),
          children: [
            const PreviewCard(),
            const SizedBox(height: Space.lg),
            apps.when(
              data: (list) => _NowShowing(list: list),
              loading: () => const _NowShowingSkeleton(),
              error: (e, _) => SectionCard(
                child: ErrorView(error: e, onRetry: () => ref.invalidate(appListProvider)),
              ),
            ),
            const SizedBox(height: Space.lg),
            _DeviceControls(hideErrors: apps.hasError),
            const SizedBox(height: Space.sm),
            const _TransitionRow(),
            const SizedBox(height: Space.xl),
            if (apps.hasValue) _QuickSwitch(list: apps.requireValue),
            if (apps.isLoading && !apps.hasValue) const _QuickSwitchSkeleton(),
          ],
        ),
      ),
    );
  }
}

class _NowShowing extends StatelessWidget {
  const _NowShowing({required this.list});

  final AppList list;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final name = list.active?.name ?? list.activeApp ?? 'Nothing';
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Row(
        children: [
          Container(
            width: 52,
            height: 52,
            decoration: BoxDecoration(
              color: scheme.primaryContainer,
              borderRadius: BorderRadius.circular(Radii.md),
              border: Border.all(color: scheme.primary.withValues(alpha: 0.5)),
            ),
            child: Icon(iconForApp(list.activeApp ?? ''), color: scheme.primary, size: 28),
          ),
          const SizedBox(width: Space.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Now showing', style: text.labelMedium?.copyWith(color: scheme.onSurfaceVariant, letterSpacing: 1)),
                const SizedBox(height: 2),
                Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, style: text.headlineSmall),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _NowShowingSkeleton extends StatelessWidget {
  const _NowShowingSkeleton();

  @override
  Widget build(BuildContext context) => const Padding(
        padding: EdgeInsets.symmetric(horizontal: 4),
        child: Row(
          children: [
            Skeleton(height: 52, width: 52, radius: Radii.md),
            SizedBox(width: Space.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Skeleton(height: 12, width: 80),
                  SizedBox(height: 8),
                  Skeleton(height: 22, width: 160),
                ],
              ),
            ),
          ],
        ),
      );
}

class _DeviceControls extends ConsumerWidget {
  const _DeviceControls({required this.hideErrors});

  /// The apps request already shows an offline card; do not stack a second one.
  final bool hideErrors;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final settings = ref.watch(deviceSettingsProvider);
    return settings.when(
      loading: () => const _ControlsSkeleton(),
      error: (e, _) => hideErrors
          ? const SizedBox.shrink()
          : SectionCard(child: ErrorView(error: e, onRetry: () => ref.invalidate(deviceSettingsProvider))),
      data: (s) {
        final notifier = ref.read(deviceSettingsProvider.notifier);
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _PowerTile(on: s.isEnabled, onChanged: notifier.setPower),
            const SizedBox(height: Space.md),
            _BrightnessCard(percent: s.brightnessPercent, onChanged: notifier.setBrightnessPercent, enabled: s.isEnabled),
          ],
        );
      },
    );
  }
}

class _ControlsSkeleton extends StatelessWidget {
  const _ControlsSkeleton();

  @override
  Widget build(BuildContext context) => const Column(
        children: [
          Skeleton(height: 72, radius: Radii.lg),
          SizedBox(height: Space.md),
          Skeleton(height: 112, radius: Radii.lg),
        ],
      );
}

/// A large, whole-row power toggle. The inner [Switch] is visual; the row handles the tap.
class _PowerTile extends StatelessWidget {
  const _PowerTile({required this.on, required this.onChanged});

  final bool on;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Semantics(
      container: true,
      toggled: on,
      label: 'Display power',
      onTap: () => onChanged(!on),
      child: ExcludeSemantics(
        child: Material(
          color: on ? scheme.primaryContainer : scheme.surfaceContainerLow,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(Radii.lg),
            side: BorderSide(color: on ? scheme.primary.withValues(alpha: 0.55) : scheme.outlineVariant),
          ),
          child: InkWell(
            key: const Key('power-tile'),
            borderRadius: BorderRadius.circular(Radii.lg),
            onTap: () => onChanged(!on),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: Space.lg, vertical: Space.md),
              child: Row(
                children: [
                  Icon(Icons.power_settings_new_rounded,
                      size: 32, color: on ? scheme.primary : scheme.onSurfaceVariant),
                  const SizedBox(width: Space.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Display power', style: text.titleMedium),
                        Text(on ? 'On' : 'Off',
                            style: text.bodyMedium?.copyWith(color: on ? scheme.onPrimaryContainer : scheme.onSurfaceVariant)),
                      ],
                    ),
                  ),
                  IgnorePointer(
                    child: Transform.scale(scale: 1.2, child: Switch(value: on, onChanged: onChanged)),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _BrightnessCard extends StatelessWidget {
  const _BrightnessCard({required this.percent, required this.onChanged, required this.enabled});

  final int percent;
  final ValueChanged<int> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return SectionCard(
      padding: const EdgeInsets.fromLTRB(Space.lg, Space.md, Space.lg, Space.sm),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(child: Text('Brightness', style: text.titleMedium)),
              Text('$percent%', style: numericStyle(text.headlineMedium?.copyWith(color: enabled ? scheme.primary : scheme.onSurfaceVariant))),
            ],
          ),
          Row(
            children: [
              Icon(Icons.light_mode_outlined, size: 20, color: scheme.onSurfaceVariant),
              Expanded(
                child: Slider(
                  semanticFormatterCallback: (v) => '${v.round()} percent',
                  value: percent.toDouble(),
                  min: 0,
                  max: 100,
                  divisions: 100,
                  label: '$percent%',
                  onChanged: (v) => onChanged(v.round()),
                ),
              ),
              Icon(Icons.light_mode_rounded, size: 26, color: scheme.onSurfaceVariant),
            ],
          ),
        ],
      ),
    );
  }
}

String _transitionLabel(String name) =>
    name.isEmpty ? name : name[0].toUpperCase() + name.substring(1).replaceAll('-', ' ').replaceAll('_', ' ');

/// A compact row showing the current transition; tapping it opens a picker sheet.
class _TransitionRow extends ConsumerWidget {
  const _TransitionRow();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final transitions = ref.watch(transitionsProvider);
    final scheme = Theme.of(context).colorScheme;
    return transitions.when(
      loading: () => const SizedBox.shrink(),
      error: (e, _) => const SizedBox.shrink(),
      data: (t) {
        final current = t.transitions.contains(t.current) ? t.current : null;
        return Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            key: const Key('transition-picker'),
            onPressed: () => _showSheet(context, ref, t),
            icon: Icon(Icons.animation_rounded, color: scheme.onSurfaceVariant),
            label: Text.rich(TextSpan(children: [
              TextSpan(text: 'Transition  ', style: TextStyle(color: scheme.onSurfaceVariant)),
              TextSpan(text: current == null ? 'None' : _transitionLabel(current), style: TextStyle(color: scheme.onSurface)),
            ])),
            iconAlignment: IconAlignment.start,
          ),
        );
      },
    );
  }

  void _showSheet(BuildContext context, WidgetRef ref, TransitionList t) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Consumer(
        builder: (ctx, ref, _) {
          final current = ref.watch(transitionsProvider).value?.current ?? t.current;
          return SafeArea(
            child: ConstrainedBox(
              constraints: BoxConstraints(maxHeight: MediaQuery.of(ctx).size.height * 0.7),
              child: ListView(
                shrinkWrap: true,
                padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.lg),
                children: [
                  const SectionHeader('TRANSITION'),
                  for (final name in t.transitions)
                    ListTile(
                      key: Key('transition-$name'),
                      title: Text(_transitionLabel(name)),
                      trailing: name == current ? Icon(Icons.check_rounded, color: Theme.of(ctx).colorScheme.primary) : null,
                      selected: name == current,
                      onTap: () {
                        ref.read(transitionsProvider.notifier).select(name);
                        Navigator.of(ctx).pop();
                      },
                    ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}

class _QuickSwitch extends ConsumerWidget {
  const _QuickSwitch({required this.list});

  final AppList list;

  static const _tileWidth = 96.0;
  static const _tileHeight = 78.0;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final rows = list.apps.length > 4 ? 2 : 1;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SectionHeader('Quick switch'),
        SizedBox(
          height: rows * _tileHeight + (rows - 1) * Space.sm,
          child: GridView.builder(
            key: const Key('quick-switch'),
            scrollDirection: Axis.horizontal,
            padding: EdgeInsets.zero,
            itemCount: list.apps.length,
            gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: rows,
              mainAxisSpacing: Space.sm,
              crossAxisSpacing: Space.sm,
              mainAxisExtent: _tileWidth,
            ),
            itemBuilder: (context, i) {
              final app = list.apps[i];
              return _AppChip(
                name: app.name,
                icon: iconForApp(app.id),
                active: app.id == list.activeApp,
                onTap: () => ref.read(appListProvider.notifier).activate(app.id),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _AppChip extends StatelessWidget {
  const _AppChip({required this.name, required this.icon, required this.active, required this.onTap});

  final String name;
  final IconData icon;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final fg = active ? scheme.primary : scheme.onSurfaceVariant;
    return Semantics(
      button: true,
      selected: active,
      label: active ? '$name, showing now' : name,
      child: ExcludeSemantics(
        child: Material(
          color: active ? scheme.primaryContainer : scheme.surfaceContainerLow,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(Radii.md),
            side: BorderSide(color: active ? scheme.primary : scheme.outlineVariant, width: active ? 1.5 : 1),
          ),
          child: InkWell(
            borderRadius: BorderRadius.circular(Radii.md),
            onTap: active ? null : onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 8),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(icon, color: fg, size: 26),
                  const SizedBox(height: 6),
                  Text(
                    name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(
                          color: active ? scheme.onPrimaryContainer : scheme.onSurface,
                        ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _QuickSwitchSkeleton extends StatelessWidget {
  const _QuickSwitchSkeleton();

  @override
  Widget build(BuildContext context) => const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionHeader('Quick switch'),
          Row(
            children: [
              Skeleton(height: 78, width: 96, radius: Radii.md),
              SizedBox(width: Space.sm),
              Skeleton(height: 78, width: 96, radius: Radii.md),
              SizedBox(width: Space.sm),
              Skeleton(height: 78, width: 96, radius: Radii.md),
            ],
          ),
        ],
      );
}
