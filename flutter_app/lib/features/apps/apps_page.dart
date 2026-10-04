import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/models.dart';
import '../../core/app_icons.dart';
import '../../core/providers.dart';
import '../../core/widgets.dart';
import 'app_settings_sheet.dart';

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
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(appListProvider)),
        data: (list) {
          final q = _query.trim().toLowerCase();
          final shown = q.isEmpty
              ? list.apps
              : list.apps.where((a) => a.name.toLowerCase().contains(q) || a.id.toLowerCase().contains(q)).toList();
          return Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                child: SearchBar(
                  hintText: 'Search apps',
                  leading: const Icon(Icons.search),
                  onChanged: (v) => setState(() => _query = v),
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                child: Card(
                  margin: EdgeInsets.zero,
                  child: ListTile(
                    key: const Key('screens-entry'),
                    leading: const Icon(Icons.dashboard_customize_rounded),
                    title: const Text('Screens'),
                    subtitle: const Text('Build your own screens'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push('/apps/screens'),
                  ),
                ),
              ),
              Expanded(
                child: RefreshIndicator(
                  onRefresh: () => ref.read(appListProvider.notifier).refresh(),
                  child: shown.isEmpty
                      ? ListView(children: const [Padding(padding: EdgeInsets.all(48), child: Center(child: Text('No apps match.')))])
                      : GridView.builder(
                          padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                          gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                            maxCrossAxisExtent: 200,
                            mainAxisExtent: 148,
                            crossAxisSpacing: 12,
                            mainAxisSpacing: 12,
                          ),
                          itemCount: shown.length,
                          itemBuilder: (context, i) => _AppTile(app: shown[i], isActive: shown[i].id == list.activeApp),
                        ),
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}

class _AppTile extends ConsumerWidget {
  const _AppTile({required this.app, required this.isActive});

  final MatrixApp app;
  final bool isActive;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      color: isActive ? scheme.primaryContainer : null,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        key: Key('app-tile-${app.id}'),
        // Tapping configures; the play button activates.
        onTap: () => showAppSettingsSheet(context, app),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(12, 12, 4, 4),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(iconForApp(app.id), size: 32, color: isActive ? scheme.onPrimaryContainer : scheme.primary),
              const Spacer(),
              Text(app.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.titleSmall),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      isActive ? 'Active' : (app.hasSettings ? 'Tap to configure' : 'No settings'),
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(color: scheme.onSurfaceVariant),
                    ),
                  ),
                  IconButton(
                    key: Key('activate-${app.id}'),
                    tooltip: 'Activate ${app.name}',
                    onPressed: isActive ? null : () => ref.read(appListProvider.notifier).activate(app.id),
                    icon: const Icon(Icons.play_circle_rounded),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
