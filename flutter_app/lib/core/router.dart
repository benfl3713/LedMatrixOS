import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/apps/apps_page.dart';
import '../features/now/now_page.dart';
import '../features/settings/settings_page.dart';
import 'shell.dart';

/// One bottom-navigation destination. Adding a tab is a single registration in [appTabs].
class AppTab {
  const AppTab({
    required this.path,
    required this.label,
    required this.icon,
    required this.selectedIcon,
    required this.builder,
    this.enabled = true,
  });

  final String path;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
  final WidgetBuilder builder;

  /// Disabled tabs are neither routed nor shown.
  final bool enabled;
}

/// The tab registry. Order here is the order in the navigation bar.
final List<AppTab> appTabs = [
  AppTab(
    path: '/now',
    label: 'Now',
    icon: Icons.play_circle_outline_rounded,
    selectedIcon: Icons.play_circle_rounded,
    builder: (_) => const NowPage(),
  ),
  AppTab(
    path: '/apps',
    label: 'Apps',
    icon: Icons.grid_view_outlined,
    selectedIcon: Icons.grid_view_rounded,
    builder: (_) => const AppsPage(),
  ),
  // PLACEHOLDER: the Schedule tab is added by a later milestone. Replace the builder with the
  // real page and set enabled: true (or just delete `enabled`).
  AppTab(
    path: '/schedule',
    label: 'Schedule',
    icon: Icons.calendar_month_outlined,
    selectedIcon: Icons.calendar_month_rounded,
    builder: (_) => const SizedBox.shrink(),
    enabled: false,
  ),
  // PLACEHOLDER: the Notify tab is added by a later milestone (same recipe as Schedule).
  AppTab(
    path: '/notify',
    label: 'Notify',
    icon: Icons.notifications_none_rounded,
    selectedIcon: Icons.notifications_rounded,
    builder: (_) => const SizedBox.shrink(),
    enabled: false,
  ),
  AppTab(
    path: '/settings',
    label: 'Settings',
    icon: Icons.settings_outlined,
    selectedIcon: Icons.settings_rounded,
    builder: (_) => const SettingsPage(),
  ),
];

GoRouter buildRouter({List<AppTab>? tabs}) {
  final active = (tabs ?? appTabs).where((t) => t.enabled).toList();
  return GoRouter(
    initialLocation: active.first.path,
    routes: [
      StatefulShellRoute.indexedStack(
        builder: (context, state, shell) => AppShell(navigationShell: shell, tabs: active),
        branches: [
          for (final tab in active)
            StatefulShellBranch(
              routes: [
                GoRoute(path: tab.path, builder: (context, state) => tab.builder(context)),
              ],
            ),
        ],
      ),
    ],
  );
}

final routerProvider = Provider<GoRouter>((ref) {
  final router = buildRouter();
  ref.onDispose(router.dispose);
  return router;
});
