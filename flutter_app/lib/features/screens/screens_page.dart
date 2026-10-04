import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/screen_models.dart';
import '../../core/providers.dart';
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
    return Scaffold(
      appBar: AppBar(title: const Text('Screens')),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('new-screen'),
        onPressed: () => _newScreen(context, ref, screens.value ?? const []),
        icon: const Icon(Icons.add),
        label: const Text('New screen'),
      ),
      body: screens.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(screenListProvider)),
        data: (list) => RefreshIndicator(
          onRefresh: () => ref.read(screenListProvider.notifier).refresh(),
          child: list.isEmpty
              ? ListView(children: const [
                  Padding(
                    padding: EdgeInsets.all(48),
                    child: Center(child: Text('No screens yet. Create one with New screen.', textAlign: TextAlign.center)),
                  ),
                ])
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  children: [
                    for (final s in list)
                      Card(
                        child: ListTile(
                          key: Key('screen-tile-${s.id}'),
                          leading: const Icon(Icons.dashboard_customize_rounded),
                          title: Text(s.name),
                          subtitle: Text(active == 'screen:${s.id}' ? '${s.id} - Active' : s.id),
                          onTap: () => context.push(screenEditorPath(s.id)),
                          trailing: IconButton(
                            key: Key('activate-screen-${s.id}'),
                            tooltip: 'Activate ${s.name}',
                            onPressed: active == 'screen:${s.id}'
                                ? null
                                : () => ref.read(screenListProvider.notifier).activate(s.id),
                            icon: const Icon(Icons.play_circle_rounded),
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
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 0, 16, 16 + MediaQuery.of(context).viewInsets.bottom),
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
              decoration: const InputDecoration(labelText: 'Name', border: OutlineInputBorder()),
            ),
            const SizedBox(height: 12),
            Text('Start from', style: Theme.of(context).textTheme.labelLarge),
            for (final t in screenTemplates)
              ListTile(
                key: Key('template-${t.key}'),
                contentPadding: EdgeInsets.zero,
                title: Text(t.label),
                subtitle: Text(t.description),
                trailing: const Icon(Icons.chevron_right),
                onTap: () {
                  final name = _name.text.trim();
                  Navigator.pop(context, (name.isEmpty ? t.label : name, t));
                },
              ),
          ],
        ),
      ),
    );
  }
}
