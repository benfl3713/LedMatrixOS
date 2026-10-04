import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/app_icons.dart';
import '../../core/providers.dart';
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
          padding: const EdgeInsets.all(16),
          children: [
            const PreviewCard(),
            const SizedBox(height: 12),
            apps.when(
              data: (list) => _CurrentAppCard(list: list),
              loading: () => const SectionCard(
                child: SizedBox(height: 48, child: Center(child: CircularProgressIndicator(strokeWidth: 2))),
              ),
              error: (e, _) => SectionCard(
                child: ErrorView(error: e, onRetry: () => ref.invalidate(appListProvider)),
              ),
            ),
            const SizedBox(height: 12),
            const _DeviceControls(),
            const SizedBox(height: 12),
            const _TransitionPicker(),
            const SizedBox(height: 12),
            if (apps.hasValue) _QuickSwitch(list: apps.requireValue),
          ],
        ),
      ),
    );
  }
}

class _CurrentAppCard extends StatelessWidget {
  const _CurrentAppCard({required this.list});

  final AppList list;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final active = list.active;
    return SectionCard(
      child: Row(
        children: [
          CircleAvatar(
            backgroundColor: scheme.primaryContainer,
            foregroundColor: scheme.onPrimaryContainer,
            child: Icon(iconForApp(list.activeApp ?? '')),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Now showing', style: Theme.of(context).textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant)),
                Text(active?.name ?? list.activeApp ?? 'Nothing', style: Theme.of(context).textTheme.titleMedium),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _DeviceControls extends ConsumerWidget {
  const _DeviceControls();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final settings = ref.watch(deviceSettingsProvider);
    return settings.when(
      loading: () => const SectionCard(
        child: SizedBox(height: 96, child: Center(child: CircularProgressIndicator(strokeWidth: 2))),
      ),
      error: (e, _) => SectionCard(
        child: ErrorView(error: e, onRetry: () => ref.invalidate(deviceSettingsProvider)),
      ),
      data: (s) {
        final notifier = ref.read(deviceSettingsProvider.notifier);
        return SectionCard(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Column(
            children: [
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                secondary: const Icon(Icons.power_settings_new_rounded),
                title: const Text('Display power'),
                value: s.isEnabled,
                onChanged: notifier.setPower,
              ),
              Row(
                children: [
                  const Icon(Icons.brightness_6_rounded),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Slider(
                      value: s.brightnessPercent.toDouble(),
                      min: 0,
                      max: 100,
                      divisions: 100,
                      label: '${s.brightnessPercent}%',
                      onChanged: (v) => notifier.setBrightnessPercent(v.round()),
                    ),
                  ),
                  SizedBox(
                    width: 44,
                    child: Text('${s.brightnessPercent}%', textAlign: TextAlign.end),
                  ),
                ],
              ),
            ],
          ),
        );
      },
    );
  }
}

class _TransitionPicker extends ConsumerWidget {
  const _TransitionPicker();

  static String _label(String name) =>
      name.isEmpty ? name : name[0].toUpperCase() + name.substring(1).replaceAll('-', ' ').replaceAll('_', ' ');

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final transitions = ref.watch(transitionsProvider);
    return transitions.when(
      loading: () => const SizedBox.shrink(),
      error: (e, _) => SectionCard(
        child: ErrorView(error: e, onRetry: () => ref.invalidate(transitionsProvider)),
      ),
      data: (t) {
        final selected = t.transitions.contains(t.current) ? t.current : null;
        return SectionCard(
          child: Row(
            children: [
              const Icon(Icons.animation_rounded),
              const SizedBox(width: 16),
              Expanded(
                child: DropdownButtonFormField<String>(
                  key: const Key('transition-picker'),
                  initialValue: selected,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Transition', border: OutlineInputBorder()),
                  items: [
                    for (final name in t.transitions) DropdownMenuItem(value: name, child: Text(_label(name))),
                  ],
                  onChanged: (v) {
                    if (v != null) ref.read(transitionsProvider.notifier).select(v);
                  },
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _QuickSwitch extends ConsumerWidget {
  const _QuickSwitch({required this.list});

  final AppList list;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 4, bottom: 8),
          child: Text('Quick switch', style: Theme.of(context).textTheme.titleSmall),
        ),
        SizedBox(
          height: 88,
          child: ListView.separated(
            key: const Key('quick-switch'),
            scrollDirection: Axis.horizontal,
            itemCount: list.apps.length,
            separatorBuilder: (_, __) => const SizedBox(width: 8),
            itemBuilder: (context, i) {
              final app = list.apps[i];
              final isActive = app.id == list.activeApp;
              return SizedBox(
                width: 84,
                child: Material(
                  color: isActive ? scheme.primaryContainer : scheme.surfaceContainerHigh,
                  borderRadius: BorderRadius.circular(16),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(16),
                    onTap: isActive ? null : () => ref.read(appListProvider.notifier).activate(app.id),
                    child: Padding(
                      padding: const EdgeInsets.all(8),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(iconForApp(app.id),
                              color: isActive ? scheme.onPrimaryContainer : scheme.onSurfaceVariant),
                          const SizedBox(height: 6),
                          Text(app.name, maxLines: 2, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center,
                              style: Theme.of(context).textTheme.labelSmall),
                        ],
                      ),
                    ),
                  ),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}
