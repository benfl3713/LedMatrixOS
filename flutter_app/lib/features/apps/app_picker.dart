import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/models.dart';
import '../../core/app_categories.dart';
import '../../core/app_prefs.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'apps_logic.dart';
import 'apps_page.dart';

/// Opens the app picker: a tall bottom sheet with search, category chips, favourites and recents, using the same
/// rows as the Apps tab.
///
/// Single mode returns a one-element list as soon as a row is tapped. Multiple mode shows checkboxes and an
/// "Add N apps" button, and returns the ticked ids in the order they were ticked. Null means dismissed.
Future<List<String>?> showAppPicker(BuildContext context, {String? selected, bool multiple = false, String? title}) {
  return showModalBottomSheet<List<String>>(
    context: context,
    useRootNavigator: true,
    isScrollControlled: true,
    useSafeArea: true,
    showDragHandle: false,
    backgroundColor: Colors.transparent,
    builder: (_) => _AppPickerSheet(
      selected: selected,
      multiple: multiple,
      title: title ?? (multiple ? 'Add apps' : 'Choose an app'),
    ),
  );
}

/// Single-select convenience: the chosen app id, or null when dismissed.
Future<String?> pickOneApp(BuildContext context, {String? selected, String? title}) async {
  final r = await showAppPicker(context, selected: selected, title: title);
  return r == null || r.isEmpty ? null : r.first;
}

class _AppPickerSheet extends ConsumerStatefulWidget {
  const _AppPickerSheet({required this.selected, required this.multiple, required this.title});

  final String? selected;
  final bool multiple;
  final String title;

  @override
  ConsumerState<_AppPickerSheet> createState() => _AppPickerSheetState();
}

class _AppPickerSheetState extends ConsumerState<_AppPickerSheet> {
  final _search = TextEditingController();
  String _query = '';
  String _filter = filterAll;
  final List<String> _ticked = [];

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  void _tap(String id) {
    if (!widget.multiple) {
      Navigator.pop(context, [id]);
      return;
    }
    setState(() => _ticked.contains(id) ? _ticked.remove(id) : _ticked.add(id));
  }

  Widget _row(MatrixApp app, String prefix, {bool showCategory = false}) {
    final scheme = Theme.of(context).colorScheme;
    final on = widget.multiple ? _ticked.contains(app.id) : app.id == widget.selected;
    final IconData icon = widget.multiple
        ? (on ? Icons.check_box_rounded : Icons.check_box_outline_blank_rounded)
        : (on ? Icons.radio_button_checked_rounded : Icons.radio_button_unchecked_rounded);
    return AppRow(
      app: app,
      isActive: false,
      showCategory: showCategory,
      keyPrefix: prefix,
      selected: on,
      onTap: () => _tap(app.id),
      trailing: Padding(
        padding: const EdgeInsets.only(right: Space.md),
        child: ExcludeSemantics(child: Icon(icon, color: on ? scheme.primary : scheme.onSurfaceVariant)),
      ),
    );
  }

  Widget _header(String text) =>
      Padding(padding: const EdgeInsets.fromLTRB(Space.lg, Space.md, Space.lg, 0), child: SectionHeader(text));

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final list = ref.watch(appListProvider);
    final prefs = ref.watch(appPrefsProvider);
    final all = list.value?.apps ?? const <MatrixApp>[];
    final byId = {for (final a in all) a.id: a};
    final groups = groupByCategory(all);
    final filter = _filter == filterAll || groups.any((g) => g.$1.key == _filter) ? _filter : filterAll;
    final searching = _query.trim().isNotEmpty;

    final slivers = <Widget>[];
    void rows(List<MatrixApp> apps, String prefix, {bool showCategory = false}) {
      slivers.add(
        SliverList.builder(
          itemCount: apps.length,
          itemBuilder: (context, i) => _row(apps[i], prefix, showCategory: showCategory),
        ),
      );
    }

    if (list.isLoading && all.isEmpty) {
      slivers.add(
        const SliverToBoxAdapter(
          child: Padding(padding: EdgeInsets.all(Space.lg), child: SizedBox(height: 400, child: ListSkeleton(rows: 6, rowHeight: 64))),
        ),
      );
    } else if (all.isEmpty) {
      slivers.add(
        const SliverToBoxAdapter(
          child: EmptyState(
            icon: Icons.apps_rounded,
            title: 'No apps to choose from',
            message: 'The app list could not be loaded.',
          ),
        ),
      );
    } else if (searching) {
      final pool = filter == filterAll ? all : all.where((a) => categoryOf(a).key == filter).toList();
      final results = searchApps(pool, _query);
      slivers.add(SliverToBoxAdapter(child: _header('Results (${results.length})')));
      if (results.isEmpty) {
        slivers.add(
          SliverToBoxAdapter(
            child: EmptyState(
              key: const Key('picker-no-results'),
              icon: Icons.search_off_rounded,
              title: 'No apps match "${_query.trim()}"',
              message: 'Try a name, a category like Travel, or a word like "train".',
            ),
          ),
        );
      } else {
        rows(results, 'pick', showCategory: true);
      }
    } else if (filter == filterAll) {
      final favs = [
        for (final id in prefs.favourites)
          if (byId[id] != null) byId[id]!,
      ];
      final recents = [
        for (final id in prefs.recents)
          if (byId[id] != null) byId[id]!,
      ];
      if (favs.isNotEmpty) {
        slivers.add(SliverToBoxAdapter(child: _header('FAVOURITES')));
        rows(favs, 'pick-fav');
      }
      if (recents.isNotEmpty) {
        slivers.add(SliverToBoxAdapter(child: _header('RECENT')));
        rows(recents, 'pick-recent');
      }
      for (final (cat, items) in groups) {
        slivers.add(SliverToBoxAdapter(child: _header('${cat.label.toUpperCase()}  ${items.length}')));
        rows(items, 'pick');
      }
    } else {
      final cat = categoryStyle(filter);
      final items = all.where((a) => categoryOf(a).key == filter).toList();
      slivers.add(SliverToBoxAdapter(child: _header('${cat.label.toUpperCase()}  ${items.length}')));
      rows(items, 'pick');
    }
    slivers.add(const SliverToBoxAdapter(child: SizedBox(height: Space.lg)));

    final n = _ticked.length;
    return AnimatedPadding(
      duration: const Duration(milliseconds: 100),
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: DraggableScrollableSheet(
        initialChildSize: 0.9,
        maxChildSize: 0.9,
        minChildSize: 0.5,
        expand: false,
        snap: true,
        builder: (context, controller) => Material(
          color: scheme.surface,
          clipBehavior: Clip.antiAlias,
          borderRadius: const BorderRadius.vertical(top: Radius.circular(Radii.lg)),
          child: Column(
            children: [
              const SizedBox(height: Space.sm),
              ExcludeSemantics(
                child: Container(
                  width: 32,
                  height: 4,
                  decoration: BoxDecoration(
                    color: scheme.onSurfaceVariant.withValues(alpha: 0.4),
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.sm, 0),
                child: Row(
                  children: [
                    Expanded(
                      child: Semantics(header: true, child: Text(widget.title, style: text.titleMedium)),
                    ),
                    IconButton(
                      key: const Key('picker-close'),
                      tooltip: 'Close',
                      constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
                      icon: const Icon(Icons.close_rounded),
                      onPressed: () => Navigator.pop(context),
                    ),
                  ],
                ),
              ),
              AppSearchField(
                fieldKey: const Key('picker-search'),
                controller: _search,
                onChanged: (v) => setState(() => _query = v),
              ),
              AppFilterChips(
                keyPrefix: 'picker-filter',
                showFavourites: false,
                selected: filter,
                total: all.length,
                favourites: 0,
                groups: groups,
                onSelected: (v) => setState(() => _filter = v),
              ),
              Expanded(
                child: CustomScrollView(
                  key: const Key('picker-scroll'),
                  controller: controller,
                  keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
                  slivers: slivers,
                ),
              ),
              if (widget.multiple)
                SafeArea(
                  top: false,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.sm),
                    child: SizedBox(
                      width: double.infinity,
                      child: FilledButton(
                        key: const Key('picker-add'),
                        onPressed: n == 0 ? null : () => Navigator.pop(context, List<String>.of(_ticked)),
                        child: Text(n == 0 ? 'Choose apps to add' : (n == 1 ? 'Add 1 app' : 'Add $n apps')),
                      ),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A tappable form field showing the chosen app's icon and name (or "Choose an app"); tapping opens the picker.
class AppPickerField extends ConsumerWidget {
  const AppPickerField({
    super.key,
    required this.appId,
    required this.onChanged,
    this.label = 'App',
    this.title,
    this.errorText,
  });

  final String? appId;
  final ValueChanged<String> onChanged;
  final String label;
  final String? title;
  final String? errorText;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    final apps = ref.watch(appListProvider).value?.apps ?? const <MatrixApp>[];
    final id = appId ?? '';
    MatrixApp? app;
    for (final a in apps) {
      if (a.id == id) app = a;
    }
    if (app == null && id.isNotEmpty) app = MatrixApp(id: id, name: id, hasSettings: false);
    final name = app?.name ?? 'Choose an app';
    return Semantics(
      button: true,
      label: '$label: $name',
      hint: 'Opens the app picker',
      excludeSemantics: true,
      child: Material(
        color: scheme.surfaceContainerLow,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(Radii.md),
          side: BorderSide(color: errorText != null ? scheme.error : scheme.outlineVariant),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () async {
            final picked = await pickOneApp(context, selected: id.isEmpty ? null : id, title: title);
            if (picked != null) onChanged(picked);
          },
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 56),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: Space.md, vertical: Space.sm),
              child: Row(
                children: [
                  if (app != null)
                    AppIconTile(app: app, size: 36)
                  else
                    Container(
                      width: 36,
                      height: 36,
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerHighest,
                        borderRadius: BorderRadius.circular(11),
                      ),
                      child: Icon(Icons.apps_rounded, size: 20, color: scheme.onSurfaceVariant),
                    ),
                  const SizedBox(width: Space.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(label, style: text.labelSmall?.copyWith(color: scheme.onSurfaceVariant)),
                        Text(
                          name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: text.titleSmall?.copyWith(color: app == null ? scheme.onSurfaceVariant : null),
                        ),
                        if (errorText != null) Text(errorText!, style: text.bodySmall?.copyWith(color: scheme.error)),
                      ],
                    ),
                  ),
                  Icon(Icons.unfold_more_rounded, color: scheme.onSurfaceVariant),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
