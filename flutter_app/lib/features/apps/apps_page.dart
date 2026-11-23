import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/models.dart';
import '../../core/app_icons.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'app_settings_sheet.dart';
import 'sheet_parts.dart';

class AppsPage extends ConsumerStatefulWidget {
  const AppsPage({super.key});

  @override
  ConsumerState<AppsPage> createState() => _AppsPageState();
}

class _AppsPageState extends ConsumerState<AppsPage> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final apps = ref.watch(appListProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Apps')),
      body: apps.when(
        loading: () => const _GridSkeleton(),
        error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(appListProvider)),
        data: (list) {
          final q = _query.trim().toLowerCase();
          final shown = q.isEmpty
              ? list.apps
              : list.apps.where((a) => a.name.toLowerCase().contains(q) || a.id.toLowerCase().contains(q)).toList();
          return RefreshIndicator(
            onRefresh: () => ref.read(appListProvider.notifier).refresh(),
            child: CustomScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              slivers: [
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.md),
                  sliver: SliverToBoxAdapter(
                    child: SearchBar(
                      hintText: 'Search apps',
                      leading: const Icon(Icons.search),
                      onChanged: (v) => setState(() => _query = v),
                    ),
                  ),
                ),
                if (q.isEmpty) const SliverPadding(padding: EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.md), sliver: SliverToBoxAdapter(child: _FeatureRow())),
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, 0),
                  sliver: SliverToBoxAdapter(child: SectionHeader(q.isEmpty ? 'All apps' : 'Results')),
                ),
                if (shown.isEmpty)
                  const SliverFillRemaining(hasScrollBody: false, child: Padding(padding: EdgeInsets.all(48), child: Center(child: Text('No apps match.'))))
                else
                  SliverPadding(
                    padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.xl),
                    sliver: SliverGrid.builder(
                      gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                        maxCrossAxisExtent: 200,
                        mainAxisExtent: 132,
                        crossAxisSpacing: Space.md,
                        mainAxisSpacing: Space.md,
                      ),
                      itemCount: shown.length,
                      itemBuilder: (context, i) => _AppTile(app: shown[i], isActive: shown[i].id == list.activeApp),
                    ),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _GridSkeleton extends StatelessWidget {
  const _GridSkeleton();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.all(Space.lg),
        child: GridView.count(
          crossAxisCount: 2,
          mainAxisSpacing: Space.md,
          crossAxisSpacing: Space.md,
          childAspectRatio: 1.4,
          children: [for (var i = 0; i < 6; i++) const Skeleton(radius: Radii.md)],
        ),
      );
}

/// Screens, Media and Gamepad as one compact row of cards.
class _FeatureRow extends StatelessWidget {
  const _FeatureRow();

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(child: _FeatureCard(key: const Key('screens-entry'), icon: Icons.dashboard_customize_rounded, label: 'Screens', onTap: () => context.push('/apps/screens'))),
        const SizedBox(width: Space.sm),
        Expanded(child: _FeatureCard(key: const Key('media-entry'), icon: Icons.photo_library_rounded, label: 'Media', onTap: () => context.push('/apps/media'))),
        const SizedBox(width: Space.sm),
        Expanded(child: _FeatureCard(key: const Key('gamepad-entry'), icon: Icons.sports_esports_rounded, label: 'Gamepad', onTap: () => context.push('/gamepad'))),
      ],
    );
  }
}

class _FeatureCard extends StatelessWidget {
  const _FeatureCard({super.key, required this.icon, required this.label, required this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Semantics(
      button: true,
      label: label,
      child: ExcludeSemantics(
        child: Material(
          color: scheme.surfaceContainerLow,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(Radii.md), side: BorderSide(color: scheme.outlineVariant)),
          child: InkWell(
            borderRadius: BorderRadius.circular(Radii.md),
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: Space.md, horizontal: Space.xs),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(icon, color: scheme.tertiary, size: 26),
                  const SizedBox(height: Space.xs),
                  Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.labelLarge),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Tap opens the app's detail sheet, long press (or the Show button) puts it on the display.
class _AppTile extends ConsumerWidget {
  const _AppTile({required this.app, required this.isActive});

  final MatrixApp app;
  final bool isActive;

  void _activate(WidgetRef ref) => ref.read(appListProvider.notifier).activate(app.id);

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Semantics(
      button: true,
      selected: isActive,
      label: isActive ? '${app.name}, on display' : app.name,
      hint: 'Opens details. Long press to show on display.',
      child: ExcludeSemantics(
        excluding: false,
        child: DecoratedBox(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(Radii.md),
            boxShadow: [if (isActive) BoxShadow(color: scheme.primary.withValues(alpha: 0.22), blurRadius: 18, spreadRadius: -2)],
          ),
          child: Material(
            color: isActive ? scheme.primaryContainer : scheme.surfaceContainerLow,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(Radii.md),
              side: BorderSide(color: isActive ? scheme.primary : scheme.outlineVariant, width: isActive ? 1.5 : 1),
            ),
            clipBehavior: Clip.antiAlias,
            child: InkWell(
              key: Key('app-tile-${app.id}'),
              onTap: () => showAppSettingsSheet(context, app),
              onLongPress: isActive
                  ? null
                  : () {
                      HapticFeedback.mediumImpact();
                      _activate(ref);
                    },
              child: Padding(
                padding: const EdgeInsets.fromLTRB(Space.md, Space.md, Space.md, Space.sm),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(iconForApp(app.id), size: 28, color: isActive ? scheme.primary : scheme.onSurfaceVariant),
                        const Spacer(),
                        if (app.hasSettings) Icon(Icons.tune_rounded, size: 16, color: scheme.onSurfaceVariant),
                      ],
                    ),
                    const Spacer(),
                    Text(app.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: text.titleSmall),
                    const SizedBox(height: Space.xs),
                    if (isActive)
                      const Align(alignment: Alignment.centerLeft, child: OnDisplayBadge())
                    else
                      Align(
                        alignment: Alignment.centerLeft,
                        child: TextButton.icon(
                          key: Key('activate-${app.id}'),
                          onPressed: () => _activate(ref),
                          style: TextButton.styleFrom(
                            minimumSize: const Size(0, 32),
                            padding: const EdgeInsets.symmetric(horizontal: Space.sm),
                            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                          ),
                          icon: const Icon(Icons.play_arrow_rounded, size: 18),
                          label: const Text('Show'),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
