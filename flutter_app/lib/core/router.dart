import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/apps/apps_page.dart';
import '../features/gamepad/gamepad_page.dart';
import '../features/media/media_page.dart';
import '../features/notify/notify_page.dart';
import '../features/now/display_page.dart';
import '../features/now/now_page.dart';
import '../features/onboarding/onboarding_page.dart';
import '../features/schedule/schedule_page.dart';
import '../features/screens/screen_editor_page.dart';
import '../features/screens/screens_page.dart';
import '../features/settings/settings_page.dart';
import 'providers.dart';
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
      GoRoute(path: 'media', builder: (context, state) => const MediaPage()),
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

/// Where the device-config gate sends [location], or null to stay. Nothing talks to the device before the saved
/// address has loaded; with none saved the user goes through onboarding; afterwards onboarding is unreachable.
String? deviceConfigRedirect(DeviceConfig config, String location) {
  if (!config.loaded) return location == '/loading' ? null : '/loading';
  if (config.isFirstRun) return location == '/welcome' ? null : '/welcome';
  if (location == '/loading' || location == '/welcome') return '/now';
  return null;
}

/// [config] and [refresh] wire in the device-config gate; without them (plain router tests) there is no gate.
GoRouter buildRouter({List<AppTab>? tabs, DeviceConfig Function()? config, Listenable? refresh}) {
  final active = (tabs ?? appTabs).where((t) => t.enabled).toList();
  return GoRouter(
    initialLocation: active.first.path,
    refreshListenable: refresh,
    redirect: config == null ? null : (context, state) => deviceConfigRedirect(config(), state.matchedLocation),
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
      GoRoute(path: '/loading', builder: (context, state) => const LoadingSplash()),
      GoRoute(path: '/welcome', builder: (context, state) => const OnboardingPage()),
      GoRoute(path: '/display', builder: (context, state) => const DisplayPage()),
      GoRoute(path: '/gamepad', builder: (context, state) => const GamepadPage()),
    ],
  );
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier<int>(0);
  ref.listen(deviceConfigProvider, (previous, next) => refresh.value++);
  final router = buildRouter(config: () => ref.read(deviceConfigProvider), refresh: refresh);
  ref.onDispose(() {
    router.dispose();
    refresh.dispose();
  });
  return router;
});
