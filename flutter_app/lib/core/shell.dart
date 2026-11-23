import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'connection.dart';
import 'providers.dart';
import 'router.dart';

/// Bottom navigation, the app-wide error snackbar, the offline banner, and foreground refresh.
class AppShell extends ConsumerStatefulWidget {
  const AppShell({super.key, required this.navigationShell, required this.tabs});

  final StatefulNavigationShell navigationShell;
  final List<AppTab> tabs;

  @override
  ConsumerState<AppShell> createState() => _AppShellState();
}

class _AppShellState extends ConsumerState<AppShell> {
  late final AppLifecycleListener _lifecycle;

  @override
  void initState() {
    super.initState();
    _lifecycle = AppLifecycleListener(
      onResume: () => ref.read(connectionProvider.notifier).onResume(),
    );
    // Start the health poll as soon as the shell exists.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) ref.read(connectionProvider);
    });
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(errorBusProvider, (previous, next) {
      if (next == null) return;
      final messenger = ScaffoldMessenger.of(context);
      messenger
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(
          content: Text(next.message),
          showCloseIcon: true,
          duration: const Duration(seconds: 6),
        ));
    });

    final nav = widget.navigationShell;
    return Scaffold(
      body: nav,
      bottomNavigationBar: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const OfflineBanner(),
          NavigationBar(
            selectedIndex: nav.currentIndex,
            onDestinationSelected: (i) => nav.goBranch(i, initialLocation: i == nav.currentIndex),
            destinations: [
              for (final t in widget.tabs)
                NavigationDestination(icon: Icon(t.icon), selectedIcon: Icon(t.selectedIcon), label: t.label),
            ],
          ),
        ],
      ),
    );
  }
}

/// Slim banner shown above the navigation bar while the device is unreachable.
class OfflineBanner extends ConsumerWidget {
  const OfflineBanner({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final connection = ref.watch(connectionProvider);
    final show = connection.status != ConnectionStatus.online && (connection.isOffline || connection.health != null);
    final scheme = Theme.of(context).colorScheme;
    final retrying = connection.status == ConnectionStatus.connecting;
    return AnimatedSize(
      duration: const Duration(milliseconds: 200),
      alignment: Alignment.bottomCenter,
      child: show
          ? Material(
              key: const Key('offline-banner'),
              color: scheme.errorContainer,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 4, 8, 4),
                child: Row(
                  children: [
                    Icon(Icons.cloud_off_rounded, size: 18, color: scheme.onErrorContainer),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        retrying ? 'Reconnecting...' : 'Display offline',
                        maxLines: 2,
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: scheme.onErrorContainer),
                      ),
                    ),
                    TextButton(
                      key: const Key('banner-retry'),
                      onPressed: retrying ? null : () => ref.read(connectionProvider.notifier).check(),
                      style: TextButton.styleFrom(minimumSize: const Size(48, 48), padding: const EdgeInsets.symmetric(horizontal: 12)),
                      child: const Text('Retry'),
                    ),
                    TextButton(
                      onPressed: () => context.go('/settings'),
                      style: TextButton.styleFrom(minimumSize: const Size(48, 48), padding: const EdgeInsets.symmetric(horizontal: 12)),
                      child: const Text('Settings'),
                    ),
                  ],
                ),
              ),
            )
          : const SizedBox(width: double.infinity),
    );
  }
}
