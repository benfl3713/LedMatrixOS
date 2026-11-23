import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/screen_models.dart';
import '../../core/providers.dart';
import '../../core/tokens.dart';
import '../../core/widgets.dart';
import 'screens_provider.dart';

String screenEditorPath(String id) => '/apps/screens/${Uri.encodeComponent(id)}';

/// Lists the user's declarative screens and starts new ones from a template.
class ScreensPage extends ConsumerWidget {
  const ScreensPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final screens = ref.watch(screenListProvider);
    final active = ref.watch(appListProvider).value?.activeApp;
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Screens')),
      // The empty state already offers "Create your first screen".
      floatingActionButton: screens.value?.isEmpty ?? false
          ? null
          : FloatingActionButton.extended(
              key: const Key('new-screen'),
              onPressed: () => _newScreen(context, ref, screens.value ?? const []),
              icon: const Icon(Icons.add_rounded),
              label: const Text('New screen'),
            ),
      body: screens.when(
        loading: () => const ListSkeleton(rows: 4, rowHeight: 76),
        error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(screenListProvider)),
        data: (list) => RefreshIndicator(
          onRefresh: () => ref.read(screenListProvider.notifier).refresh(),
          child: list.isEmpty
              ? ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.only(top: Space.xl),
                  children: [
                    EmptyState(
                      icon: Icons.dashboard_customize_rounded,
                      title: 'Design your own screen',
                      message: 'A screen is a custom layout of clocks, text and live data that you build here and '
                          'show on the display like any other app.',
                      actionLabel: 'Create your first screen',
                      actionKey: const Key('empty-new-screen'),
                      onAction: () => _newScreen(context, ref, const []),
                    ),
                  ],
                )
              : ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  children: [
                    for (final s in list)
                      Padding(
                        padding: const EdgeInsets.only(bottom: Space.sm),
                        child: Card(
                          child: ListTile(
                            key: Key('screen-tile-${s.id}'),
                            contentPadding: const EdgeInsets.fromLTRB(Space.lg, Space.xs, Space.sm, Space.xs),
                            leading: Container(
                              width: 40,
                              height: 40,
                              decoration: BoxDecoration(
                                color: active == 'screen:${s.id}' ? scheme.primaryContainer : scheme.surfaceContainerHigh,
                                borderRadius: BorderRadius.circular(Radii.sm),
                              ),
                              child: Icon(Icons.dashboard_customize_rounded,
                                  color: active == 'screen:${s.id}' ? scheme.onPrimaryContainer : scheme.onSurfaceVariant),
                            ),
                            title: Text(s.name),
                            subtitle: Text(active == 'screen:${s.id}' ? '${s.id} - Active' : s.id),
                            onTap: () => context.push(screenEditorPath(s.id)),
                            trailing: IconButton(
                              key: Key('activate-screen-${s.id}'),
                              tooltip: active == 'screen:${s.id}' ? '${s.name} is showing' : 'Show ${s.name} on the display',
                              onPressed: active == 'screen:${s.id}'
                                  ? null
                                  : () => ref.read(screenListProvider.notifier).activate(s.id),
                              icon: Icon(active == 'screen:${s.id}' ? Icons.check_circle_rounded : Icons.play_circle_rounded),
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

  Future<void> _newScreen(BuildContext context, WidgetRef ref, List<ScreenSummary> existing) async {
    final result = await showModalBottomSheet<(String, ScreenTemplate)>(
      context: context,
      useRootNavigator: true,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _NewScreenSheet(),
    );
    if (result == null || !context.mounted) return;
    final (name, template) = result;
    final id = uniqueScreenId(name, existing.map((s) => s.id));
    ref.read(newScreenDraftsProvider.notifier).put(ScreenDefinition(id: id, name: name, root: template.buildRoot()));
    context.push(screenEditorPath(id));
  }
}

class _NewScreenSheet extends StatefulWidget {
  const _NewScreenSheet();

  @override
  State<_NewScreenSheet> createState() => _NewScreenSheetState();
}

class _NewScreenSheetState extends State<_NewScreenSheet> {
  final _name = TextEditingController(text: 'My screen');

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 0, 16, 16 + MediaQuery.of(context).viewInsets.bottom),
      child: SafeArea(
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('New screen', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 12),
              TextField(
                key: const Key('new-screen-name'),
                controller: _name,
                decoration: const InputDecoration(labelText: 'Name'),
              ),
              const SizedBox(height: 16),
              Text('START FROM',
                  style: Theme.of(context).textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant, letterSpacing: 1)),
              const SizedBox(height: 4),
              for (final t in screenTemplates)
                ListTile(
                  key: Key('template-${t.key}'),
                  contentPadding: EdgeInsets.zero,
                  title: Text(t.label),
                  subtitle: Text(t.description),
                  trailing: const Icon(Icons.chevron_right_rounded),
                  onTap: () {
                    final name = _name.text.trim();
                    Navigator.pop(context, (name.isEmpty ? t.label : name, t));
                  },
                ),
            ],
          ),
        ),
      ),
    );
  }
}
