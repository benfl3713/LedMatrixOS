import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/models.dart';
import '../../core/app_categories.dart';
import '../../core/app_icons.dart';
import '../../core/app_prefs.dart';
import '../../core/connection.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'app_settings_sheet.dart';
import 'apps_logic.dart';
import 'sheet_parts.dart';

const _filterAll = 'all';
const _filterFavourites = 'favourites';

class AppsPage extends ConsumerStatefulWidget {
  const AppsPage({super.key});

  @override
  ConsumerState<AppsPage> createState() => _AppsPageState();
}

class _AppsPageState extends ConsumerState<AppsPage> {
  final _search = TextEditingController();
  String _query = '';
  String _filter = _filterAll;
  bool _developerOpen = false;

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final apps = ref.watch(appListProvider);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Apps'),
        actions: [
          IconButton(
            key: const Key('screens-entry'),
            tooltip: 'My screens',
            icon: const Icon(Icons.dashboard_customize_rounded),
            onPressed: () => context.push('/apps/screens'),
          ),
          IconButton(
            key: const Key('media-entry'),
            tooltip: 'Media library',
            icon: const Icon(Icons.photo_library_rounded),
            onPressed: () => context.push('/apps/media'),
          ),
          IconButton(
            key: const Key('gamepad-entry'),
            tooltip: 'Gamepad',
            icon: const Icon(Icons.sports_esports_rounded),
            onPressed: () => context.push('/gamepad'),
          ),
        ],
      ),
      body: apps.when(
        loading: () => const _ListSkeleton(),
        error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(appListProvider)),
        data: (list) => RefreshIndicator(
          onRefresh: () => ref.read(appListProvider.notifier).refresh(),
          child: _body(context, list),
        ),
      ),
    );
  }

  Widget _body(BuildContext context, AppList list) {
    final prefs = ref.watch(appPrefsProvider);
    final favApps = [
      for (final id in prefs.favourites)
        for (final a in list.apps)
          if (a.id == id) a,
    ];
    final groups = groupByCategory(list.apps);
    // A chip for a category that has no apps (anymore) falls back to All.
    final filter = _filter == _filterAll || _filter == _filterFavourites || groups.any((g) => g.$1.key == _filter)
        ? _filter
        : _filterAll;
    final searching = _query.trim().isNotEmpty;
    final active = list.apps.where((a) => a.id == list.activeApp).firstOrNull;

    final pool = switch (filter) {
      _filterAll => list.apps,
      _filterFavourites => favApps,
      _ => list.apps.where((a) => categoryOf(a).key == filter).toList(),
    };

    final slivers = <Widget>[
      SliverToBoxAdapter(child: _SearchField(controller: _search, onChanged: (v) => setState(() => _query = v))),
      SliverToBoxAdapter(
        child: _FilterChips(
          selected: filter,
          total: list.apps.length,
          favourites: favApps.length,
          groups: groups,
          onSelected: (v) => setState(() => _filter = v),
        ),
      ),
    ];

    if (searching) {
      final results = searchApps(pool, _query);
      slivers.add(SliverToBoxAdapter(child: _padded(SectionHeader('Results (${results.length})'))));
      slivers.add(results.isEmpty
          ? SliverToBoxAdapter(
              child: EmptyState(
                icon: Icons.search_off_rounded,
                title: 'No apps match "${_query.trim()}"',
                message: 'Try a name, a category like Travel, or a word from the description.',
              ),
            )
          : _rows(results, list, showCategory: true));
    } else if (filter == _filterFavourites) {
      slivers.add(favApps.isEmpty
          ? const SliverToBoxAdapter(
              child: EmptyState(
                icon: Icons.star_border_rounded,
                title: 'No favourites yet',
                message: 'Tap the star in an app\'s settings, or long-press an app, to pin it here.',
              ),
            )
          : _rows(favApps, list));
    } else if (filter == _filterAll) {
      if (active != null) slivers.add(SliverToBoxAdapter(child: _OnDisplayStrip(app: active)));
      if (favApps.isNotEmpty) {
        slivers.add(SliverToBoxAdapter(child: _padded(const SectionHeader('Favourites'))));
        slivers.add(SliverToBoxAdapter(child: _FavouritesRow(apps: favApps, activeId: list.activeApp)));
      }
      for (final (cat, items) in groups) {
        if (cat.key == 'developer') {
          slivers.add(SliverToBoxAdapter(
            child: _DeveloperSection(
              apps: items,
              expanded: _developerOpen,
              onToggle: (v) => setState(() => _developerOpen = v),
              list: list,
            ),
          ));
          continue;
        }
        slivers.add(SliverToBoxAdapter(child: _padded(_categoryHeader(context, cat, items.length))));
        slivers.add(_rows(items, list));
      }
    } else {
      final cat = categoryStyle(filter);
      slivers.add(SliverToBoxAdapter(child: _padded(_categoryHeader(context, cat, pool.length))));
      slivers.add(_rows(pool, list));
    }
    slivers.add(const SliverToBoxAdapter(child: SizedBox(height: Space.xl)));

    return CustomScrollView(
      key: const Key('apps-scroll'),
      physics: const AlwaysScrollableScrollPhysics(),
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      slivers: slivers,
    );
  }

  Widget _padded(Widget child) =>
      Padding(padding: const EdgeInsets.fromLTRB(Space.lg, Space.md, Space.lg, 0), child: child);

  Widget _categoryHeader(BuildContext context, AppCategoryStyle cat, int count) {
    final (label, key, route) = switch (cat.key) {
      'screens' => ('Manage', 'screens-manage', '/apps/screens'),
      'media' => ('Library', 'media-library', '/apps/media'),
      'games' => ('Gamepad', 'games-gamepad', '/gamepad'),
      _ => (null, null, null),
    };
    return SectionHeader(
      '${cat.label.toUpperCase()}  $count',
      trailing:
          route == null ? null : TextButton(key: Key(key!), onPressed: () => context.push(route), child: Text(label!)),
    );
  }

  SliverList _rows(List<MatrixApp> apps, AppList list, {bool showCategory = false}) => SliverList.builder(
        itemCount: apps.length,
        itemBuilder: (context, i) =>
            AppRow(app: apps[i], isActive: apps[i].id == list.activeApp, showCategory: showCategory),
      );
}

class _SearchField extends StatelessWidget {
  const _SearchField({required this.controller, required this.onChanged});

  final TextEditingController controller;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.sm),
      child: SearchBar(
        key: const Key('apps-search'),
        controller: controller,
        hintText: 'Search apps',
        leading: const Icon(Icons.search),
        onChanged: onChanged,
        trailing: [
          if (controller.text.isNotEmpty)
            IconButton(
              key: const Key('apps-search-clear'),
              tooltip: 'Clear search',
              icon: const Icon(Icons.close_rounded),
              onPressed: () {
                controller.clear();
                onChanged('');
              },
            ),
        ],
      ),
    );
  }
}

class _FilterChips extends StatelessWidget {
  const _FilterChips({
    required this.selected,
    required this.total,
    required this.favourites,
    required this.groups,
    required this.onSelected,
  });

  final String selected;
  final int total;
  final int favourites;
  final List<(AppCategoryStyle, List<MatrixApp>)> groups;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    Widget chip(String key, Widget label) => Padding(
          padding: const EdgeInsets.only(right: Space.sm),
          child: FilterChip(
            key: Key('filter-$key'),
            label: label,
            selected: selected == key,
            showCheckmark: false,
            onSelected: (_) => onSelected(key),
          ),
        );
    return SizedBox(
      height: 48,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: Space.lg),
        children: [
          chip(_filterAll, Text('All $total')),
          chip(
            _filterFavourites,
            Row(mainAxisSize: MainAxisSize.min, children: [
              const Icon(Icons.star_rounded, size: 16, color: Brand.amber),
              const SizedBox(width: 4),
              Text('Favourites $favourites'),
            ]),
          ),
          for (final (cat, items) in groups) chip(cat.key, Text('${cat.label} ${items.length}')),
        ],
      ),
    );
  }
}

/// The app on the display right now, with a shortcut to its settings.
class _OnDisplayStrip extends StatelessWidget {
  const _OnDisplayStrip({required this.app});

  final MatrixApp app;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, 0),
      child: Semantics(
        container: true,
        label: 'On display: ${app.name}',
        child: Container(
          key: const Key('on-display-strip'),
          padding: const EdgeInsets.fromLTRB(Space.md, Space.sm, Space.sm, Space.sm),
          decoration: BoxDecoration(
            color: scheme.primaryContainer.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(Radii.md),
            border: Border.all(color: scheme.primary.withValues(alpha: 0.6)),
          ),
          child: Row(
            children: [
              AppIconTile(app: app, size: 40),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Showing now', style: Theme.of(context).textTheme.labelSmall?.copyWith(color: scheme.primary)),
                    Text(app.name,
                        maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.titleSmall),
                  ],
                ),
              ),
              TextButton.icon(
                key: const Key('on-display-settings'),
                onPressed: () => showAppSettingsSheet(context, app),
                icon: const Icon(Icons.tune_rounded, size: 18),
                label: const Text('Settings'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FavouritesRow extends ConsumerWidget {
  const _FavouritesRow({required this.apps, required this.activeId});

  final List<MatrixApp> apps;
  final String? activeId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return SizedBox(
      height: 96,
      child: ListView.separated(
        key: const Key('favourites-row'),
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: Space.lg),
        itemCount: apps.length,
        separatorBuilder: (_, __) => const SizedBox(width: Space.sm),
        itemBuilder: (context, i) {
          final app = apps[i];
          final isActive = app.id == activeId;
          final scheme = Theme.of(context).colorScheme;
          return Semantics(
            button: true,
            selected: isActive,
            label: isActive ? '${app.name}, on display' : 'Show ${app.name} on display',
            child: ExcludeSemantics(
              child: SizedBox(
                width: 80,
                child: Material(
                  color: isActive ? scheme.primaryContainer : scheme.surfaceContainerLow,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(Radii.md),
                    side: BorderSide(color: isActive ? scheme.primary : scheme.outlineVariant),
                  ),
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    key: Key('fav-${app.id}'),
                    onTap: () => ref.read(appListProvider.notifier).activate(app.id),
                    onLongPress: () => showAppMenu(context, ref, app, isActive),
                    child: Padding(
                      padding: const EdgeInsets.all(Space.sm),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          AppIconTile(app: app, size: 36),
                          const SizedBox(height: Space.xs),
                          Text(app.name,
                              maxLines: 2,
                              textAlign: TextAlign.center,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.labelSmall),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class _DeveloperSection extends StatelessWidget {
  const _DeveloperSection({required this.apps, required this.expanded, required this.onToggle, required this.list});

  final List<MatrixApp> apps;
  final bool expanded;
  final ValueChanged<bool> onToggle;
  final AppList list;

  @override
  Widget build(BuildContext context) {
    return Theme(
      data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
      child: ExpansionTile(
        key: const Key('developer-section'),
        initiallyExpanded: expanded,
        onExpansionChanged: onToggle,
        tilePadding: const EdgeInsets.symmetric(horizontal: Space.lg),
        shape: const Border(),
        collapsedShape: const Border(),
        title: Text('DEVELOPER  ${apps.length}',
            style: Theme.of(context)
                .textTheme
                .labelMedium
                ?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant, letterSpacing: 1)),
        children: [
          for (final a in apps) AppRow(app: a, isActive: a.id == list.activeApp),
        ],
      ),
    );
  }
}

/// A rounded tile in the app's category colour holding its icon.
class AppIconTile extends StatelessWidget {
  const AppIconTile({super.key, required this.app, this.size = 44});

  final MatrixApp app;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tint = categoryOf(app).color;
    final fg = tintForeground(tint, Theme.of(context).brightness);
    return ExcludeSemantics(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(color: tint.withValues(alpha: 0.18), borderRadius: BorderRadius.circular(size * 0.3)),
        child: Icon(iconForApp(app.id), size: size * 0.55, color: fg),
      ),
    );
  }
}

/// Opens the small per-app menu: show on display, favourite, settings.
Future<void> showAppMenu(BuildContext context, WidgetRef ref, MatrixApp app, bool isActive) {
  HapticFeedback.mediumImpact();
  final stale = ref.read(connectionStaleProvider);
  return showModalBottomSheet<void>(
    context: context,
    showDragHandle: true,
    builder: (sheetContext) => Consumer(
      builder: (context, ref, _) {
        final fav = ref.watch(appPrefsProvider).isFavourite(app.id);
        return SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: AppIconTile(app: app, size: 40),
                title: Text(app.name),
                subtitle: app.description.isEmpty
                    ? null
                    : Text(app.description, maxLines: 1, overflow: TextOverflow.ellipsis),
              ),
              ListTile(
                key: const Key('menu-show'),
                leading: const Icon(Icons.play_arrow_rounded),
                title: Text(isActive ? 'On display' : 'Show on display'),
                enabled: !isActive && !stale,
                onTap: () {
                  Navigator.pop(sheetContext);
                  ref.read(appListProvider.notifier).activate(app.id);
                },
              ),
              ListTile(
                key: const Key('menu-favourite'),
                leading: Icon(fav ? Icons.star_rounded : Icons.star_border_rounded),
                title: Text(fav ? 'Remove from favourites' : 'Add to favourites'),
                onTap: () {
                  ref.read(appPrefsProvider.notifier).toggleFavourite(app.id);
                  Navigator.pop(sheetContext);
                },
              ),
              ListTile(
                key: const Key('menu-settings'),
                leading: const Icon(Icons.tune_rounded),
                title: const Text('Settings'),
                onTap: () {
                  Navigator.pop(sheetContext);
                  showAppSettingsSheet(context, app);
                },
              ),
            ],
          ),
        );
      },
    ),
  );
}

/// One dense (64px) app row: tinted icon tile, name and description, then either the On display pill or a play button.
/// Tap opens settings, long press the quick menu.
class AppRow extends ConsumerWidget {
  const AppRow({super.key, required this.app, required this.isActive, this.showCategory = false});

  final MatrixApp app;
  final bool isActive;
  final bool showCategory;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final stale = ref.watch(connectionStaleProvider);
    final cat = categoryOf(app);
    final sub = [if (showCategory) cat.label, if (app.description.isNotEmpty) app.description].join(' · ');
    return Semantics(
      container: true,
      selected: isActive,
      label: [app.name, if (app.description.isNotEmpty) app.description, if (isActive) 'on display'].join(', '),
      hint: 'Opens settings. Long press for more.',
      child: ExcludeSemantics(
        excluding: false,
        child: InkWell(
          key: Key('app-tile-${app.id}'),
          onTap: () => showAppSettingsSheet(context, app),
          onLongPress: () => showAppMenu(context, ref, app, isActive),
          child: Container(
            height: 64,
            padding: const EdgeInsets.only(left: Space.lg, right: Space.sm),
            color: isActive ? scheme.primary.withValues(alpha: 0.06) : null,
            child: Row(
              children: [
                AppIconTile(app: app),
                const SizedBox(width: Space.md),
                Expanded(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(app.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: text.titleSmall),
                      if (sub.isNotEmpty)
                        Text(sub,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: text.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
                    ],
                  ),
                ),
                if (isActive)
                  const Padding(padding: EdgeInsets.only(right: Space.sm), child: OnDisplayBadge())
                else
                  IconButton(
                    key: Key('activate-${app.id}'),
                    tooltip: 'Show ${app.name} on display',
                    constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
                    onPressed: stale ? null : () => ref.read(appListProvider.notifier).activate(app.id),
                    icon: const Icon(Icons.play_circle_outline_rounded),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _ListSkeleton extends StatelessWidget {
  const _ListSkeleton();

  @override
  Widget build(BuildContext context) => const Padding(
        padding: EdgeInsets.all(Space.lg),
        child: ListSkeleton(rows: 8, rowHeight: 64),
      );
}
