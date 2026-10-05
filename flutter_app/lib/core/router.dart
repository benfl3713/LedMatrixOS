import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/apps/apps_page.dart';
import '../features/notify/notify_page.dart';
import '../features/now/display_page.dart';
import '../features/now/now_page.dart';
import '../features/schedule/schedule_page.dart';
import '../features/screens/screen_editor_page.dart';
import '../features/screens/screens_page.dart';
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
    this.routes = const [],
  });

  final String path;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
  final WidgetBuilder builder;

  /// Disabled tabs are neither routed nor shown.
  final bool enabled;

  /// Pushed pages that live under this tab (the navigation bar stays visible).
  final List<RouteBase> routes;
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
    routes: [
      GoRoute(
        path: 'screens',
        builder: (context, state) => const ScreensPage(),
        routes: [
          GoRoute(
            path: ':id',
            builder: (context, state) => ScreenEditorPage(id: state.pathParameters['id']!),
          ),
        ],
      ),
    ],
  ),
  AppTab(
    path: '/schedule',
    label: 'Schedule',
    icon: Icons.calendar_month_outlined,
    selectedIcon: Icons.calendar_month_rounded,
    builder: (_) => const SchedulePage(),
  ),
  AppTab(
    path: '/notify',
    label: 'Notify',
    icon: Icons.notifications_none_rounded,
    selectedIcon: Icons.notifications_rounded,
    builder: (_) => const NotifyPage(),
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
                GoRoute(path: tab.path, builder: (context, state) => tab.builder(context), routes: tab.routes),
              ],
            ),
        ],
      ),
      // Outside the shell so the navigation bar is hidden.
      GoRoute(path: '/display', builder: (context, state) => const DisplayPage()),
    ],
  );
}

final routerProvider = Provider<GoRouter>((ref) {
  final router = buildRouter();
  ref.onDispose(router.dispose);
  return router;
});
