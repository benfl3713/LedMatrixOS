import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/core/app_icons.dart';
import 'package:led_matrix_controller/core/app_prefs.dart';
import 'package:led_matrix_controller/features/apps/apps_logic.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

const _allIds = [
  'home',
  'clock',
  'sky-clock',
  'countdown-timer',
  'pomodoro',
  'calendar',
  'bin-day',
  'morning-briefing',
  'tube-status',
  'tube-line',
  'tube-departures',
  'bus-arrivals',
  'rail-departures',
  'cycle-hub',
  'road-disruptions',
  'journey',
  'commute',
  'train-sim',
  'plane-spotter',
  'weather',
  'air-quality',
  'tides',
  'iss-tracker',
  'stocks',
  'rss-ticker',
  'ha-tiles',
  'spotify',
  'scrolling-text',
  'qr',
  'solid_color',
  'rainbow-spiral',
  'bouncing-balls',
  'matrix-rain',
  'geometric-patterns',
  'dvd-logo',
  'fire',
  'life',
  'alive',
  'aquarium',
  'demoscene',
  'equalizer',
  'party',
  'pet',
  'snake',
  'breakout',
  'tetris',
  'pong',
  'media',
  'input-test',
  'widget-demo',
  'screen',
];

MatrixApp _app(String id, String name, String category, [String description = '']) =>
    MatrixApp(id: id, name: name, hasSettings: true, category: category, description: description);

final _apps = [
  _app('clock', 'Clock', 'clocks', 'Digital and flip clock'),
  _app('weather', 'Weather', 'info', 'Current conditions and forecast'),
  _app('tube-status', 'Tube Status', 'travel', 'Service status for every line'),
  _app('bus-arrivals', 'Bus Arrivals', 'travel', 'Live arrivals at your bus stop'),
  _app('fire', 'Fire', 'ambient', 'Flickering flames'),
  _app('snake', 'Snake', 'games', 'Classic snake'),
  _app('input-test', 'Input Test', 'developer', 'Check buttons'),
  const MatrixApp(id: 'screen:demo', name: 'Demo', hasSettings: true, category: 'screens', isScreen: true),
];

Future<FakeApi> _openApps(WidgetTester tester, {List<MatrixApp>? apps}) async {
  tester.view.physicalSize = const Size(800, 3000);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final api = FakeApi()..apps = apps ?? _apps;
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps').last);
  await tester.pumpAndSettle();
  return api;
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  test('every known app id has an explicit, non-default icon', () {
    for (final id in _allIds) {
      expect(appIconById.containsKey(id), isTrue, reason: id);
      expect(iconForApp(id), isNot(Icons.apps_rounded), reason: id);
    }
    expect(iconForApp('some-new-app'), Icons.apps_rounded);
    expect(iconForApp('my-clock-thing'), Icons.schedule_rounded); // keyword fallback
  });

  test('MatrixApp tolerates a missing category and description', () {
    final a = MatrixApp.fromJson({'id': 'x', 'name': 'X', 'hasSettings': false});
    expect(a.category, 'other');
    expect(a.description, '');
    final s = MatrixApp.fromJson({'id': 'screen:a', 'name': 'A', 'isScreen': true});
    expect(s.category, 'screens');
    expect(MatrixApp.fromJson({'id': 'y', 'name': 'Y', 'category': 'Travel'}).category, 'travel');
  });

  test('apps group by category in display order, skipping empty ones', () {
    final groups = groupByCategory(_apps);
    expect(groups.map((g) => g.$1.key), ['clocks', 'travel', 'info', 'ambient', 'games', 'screens', 'developer']);
    expect(groups[1].$2.map((a) => a.id), ['tube-status', 'bus-arrivals']);
  });

  test('search ranks name prefix, then name contains, then description, then category', () {
    final apps = [
      _app('a', 'Daily Bus', 'info'), // name contains
      _app('b', 'Timetable', 'info', 'Shows the next bus'), // description
      _app('c', 'Bus Arrivals', 'travel'), // prefix
      _app('d', 'Commute', 'bus-ish'), // category
    ];
    expect(searchApps(apps, 'BUS').map((a) => a.id), ['c', 'a', 'b', 'd']);
    expect(searchApps(apps, 'zzz'), isEmpty);
    expect(searchApps(apps, '  ').length, 4);
  });

  test('quick switch order is favourites, recents, then the first apps, capped at 8', () {
    final many = [for (var i = 0; i < 20; i++) _app('a$i', 'App $i', 'info')];
    final result = quickSwitchApps(many, ['a5'], ['a3', 'a5', 'a9'], limit: 8).map((a) => a.id).toList();
    expect(result, ['a5', 'a3', 'a9', 'a0', 'a1', 'a2', 'a4', 'a6']);
    expect(quickSwitchApps(many, ['gone'], [], limit: 8).first.id, 'a0');
  });

  testWidgets('the Apps page groups apps under category headers and collapses Developer', (tester) async {
    await _openApps(tester);

    expect(find.text('CLOCKS  1'), findsOneWidget);
    expect(find.text('TRAVEL  2'), findsOneWidget);
    expect(find.text('MY SCREENS  1'), findsOneWidget);
    expect(find.byKey(const Key('app-tile-tube-status')), findsOneWidget);
    expect(find.text('Service status for every line'), findsOneWidget);
    // Developer starts collapsed; opening it reveals its apps.
    expect(find.byKey(const Key('app-tile-input-test')), findsNothing);
    await tester.tap(find.byKey(const Key('developer-section')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-input-test')), findsOneWidget);
    // The contextual links replace the big cards.
    expect(find.byKey(const Key('screens-manage')), findsOneWidget);
    expect(find.byKey(const Key('games-gamepad')), findsOneWidget);
  });

  testWidgets('a category chip narrows the list to that category', (tester) async {
    await _openApps(tester);

    await tester.tap(find.byKey(const Key('filter-travel')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-tube-status')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-bus-arrivals')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-clock')), findsNothing);
    expect(find.byKey(const Key('on-display-strip')), findsNothing);

    await tester.tap(find.byKey(const Key('filter-all')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-clock')), findsOneWidget);
  });

  testWidgets('search shows a ranked flat list, clears, and has a friendly empty state', (tester) async {
    await _openApps(tester);

    await tester.enterText(find.byKey(const Key('apps-search')), 'BUS');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-bus-arrivals')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-clock')), findsNothing);
    expect(find.text('CLOCKS  1'), findsNothing);

    // Matches the description and the category too.
    await tester.enterText(find.byKey(const Key('apps-search')), 'flicker');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-fire')), findsOneWidget);
    await tester.enterText(find.byKey(const Key('apps-search')), 'games');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-snake')), findsOneWidget);

    await tester.enterText(find.byKey(const Key('apps-search')), 'nothing-like-this');
    await tester.pumpAndSettle();
    expect(find.textContaining('No apps match'), findsOneWidget);

    await tester.tap(find.byKey(const Key('apps-search-clear')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-clock')), findsOneWidget);
  });

  testWidgets('favourites: star in the sheet pins to the row, persists, and leads the quick switch', (tester) async {
    await _openApps(tester);

    expect(find.byKey(const Key('favourites-row')), findsNothing);
    await tester.tap(find.byKey(const Key('app-tile-snake')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('sheet-favourite')));
    await tester.pumpAndSettle();
    final prefs = await SharedPreferences.getInstance();
    expect(prefs.getStringList('app_favourites'), ['snake']);

    await tester.tapAt(const Offset(400, 20)); // dismiss the sheet via the barrier
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('fav-snake')), findsOneWidget);

    // The favourites chip lists only favourites.
    await tester.tap(find.byKey(const Key('filter-favourites')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-snake')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-clock')), findsNothing);

    // Long-press menu toggles it off again.
    await tester.longPress(find.byKey(const Key('app-tile-snake')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('menu-favourite')));
    await tester.pumpAndSettle();
    expect(prefs.getStringList('app_favourites'), isEmpty);
    expect(find.text('No favourites yet'), findsOneWidget);
  });

  testWidgets('quick switch shows saved favourites first, then recents', (tester) async {
    SharedPreferences.setMockInitialValues({
      'app_favourites': ['snake'],
      'app_recents': ['fire', 'snake'],
    });
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = FakeApi()..apps = _apps;
    await tester.pumpWidget(testApp(api));
    await tester.pumpAndSettle();

    final strip = find.byKey(const Key('quick-switch'));
    final names = ['Snake', 'Fire', 'Clock'];
    final xs = [for (final n in names) tester.getTopLeft(find.descendant(of: strip, matching: find.text(n))).dx];
    // Two rows of tiles: the first column holds the two leading apps, so check the order by column then row.
    final order = [for (final n in names) tester.getTopLeft(find.descendant(of: strip, matching: find.text(n)))];
    expect(order[0].dx, lessThanOrEqualTo(order[1].dx));
    expect(order[0].dy, lessThan(order[1].dy)); // Snake above Fire in the same first column
    expect(xs[1], lessThan(xs[2]));
    expect(find.descendant(of: strip, matching: find.text('Weather')), findsOneWidget);
  });

  testWidgets('activating an app records it in recents (newest first, max 8)', (tester) async {
    await _openApps(tester);
    await tester.tap(find.byKey(const Key('activate-weather')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('activate-fire')));
    await tester.pumpAndSettle();
    final prefs = await SharedPreferences.getInstance();
    expect(prefs.getStringList('app_recents'), ['fire', 'weather']);
  });

  testWidgets('phone width: no overflow with the full layout', (tester) async {
    tester.view.physicalSize = const Size(360, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = FakeApi()..apps = _apps;
    await tester.pumpWidget(testApp(api));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Apps').last);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });

  testWidgets('search and filter chips stay pinned while the list scrolls', (tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(testApp(FakeApi()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Apps'));
    await tester.pumpAndSettle();

    await tester.drag(find.byKey(const Key('apps-scroll')), const Offset(0, -2000));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('apps-search')).hitTestable(), findsOneWidget);
    expect(find.byKey(const Key('filter-all')).hitTestable(), findsOneWidget);
  });

  test('using an app before stored prefs have loaded keeps the saved favourites and recents', () async {
    SharedPreferences.setMockInitialValues({
      'flutter.app_favourites': ['tube-status'],
      'flutter.app_recents': ['clock'],
    });
    final container = ProviderContainer();
    addTearDown(container.dispose);

    // The first read creates the provider; the change lands before the async load completes.
    container.read(appPrefsProvider.notifier).recordUse('snake');
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);

    final prefs = container.read(appPrefsProvider);
    expect(prefs.favourites, ['tube-status']);
    expect(prefs.recents, ['snake', 'clock']);
    final stored = await SharedPreferences.getInstance();
    expect(stored.getStringList('app_favourites'), ['tube-status']);
    expect(stored.getStringList('app_recents'), ['snake', 'clock']);
  });
}
