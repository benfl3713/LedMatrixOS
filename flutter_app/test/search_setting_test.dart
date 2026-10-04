import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/led_api.dart' show optionsQuery;
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/apps/app_settings_provider.dart';
import 'package:led_matrix_controller/features/apps/app_settings_sheet.dart';

import 'fake_api.dart';

const _single = AppSetting(
  key: 'station',
  name: 'Station',
  type: AppSettingType.search,
  currentValue: 'KGX',
  currentLabel: 'London Kings Cross',
);

AppSetting _multi({String ids = 'a,b', List<String> labels = const ['Alpha', 'Beta'], num max = 3}) => AppSetting(
      key: 'stops',
      name: 'Stops',
      type: AppSettingType.multiSearch,
      currentValue: ids,
      currentLabels: labels,
      maxValue: max,
    );

Future<ProviderContainer> _open(WidgetTester tester, FakeApi api, List<AppSetting> settings) async {
  api.settings
    ..clear()
    ..addAll(settings);
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final container = ProviderContainer(overrides: [
    apiProvider.overrideWithValue(api),
    pollIntervalProvider.overrideWithValue(null),
  ]);
  addTearDown(container.dispose);
  await tester.pumpWidget(UncontrolledProviderScope(
    container: container,
    child: const MaterialApp(
      home: Scaffold(body: AppSettingsView(app: MatrixApp(id: 'commute', name: 'Commute', hasSettings: true))),
    ),
  ));
  await tester.pumpAndSettle();
  return container;
}

Future<void> _type(WidgetTester tester, String key, String text) async {
  await tester.enterText(find.byKey(ValueKey('search-field-$key')), text);
  await tester.pump(const Duration(milliseconds: 350));
  await tester.pump();
}

/// Lets the 500 ms save debounce and the follow-up reload run.
Future<void> _settle(WidgetTester tester) async {
  await tester.pump(const Duration(milliseconds: 600));
  await tester.pumpAndSettle();
}

void main() {
  group('model', () {
    test('parses search types, labels and options', () {
      final s = AppSetting.fromJson({
        'key': 'a',
        'name': 'A',
        'type': 5,
        'currentValue': 'x',
        'currentLabel': 'Ex',
      });
      expect(s.type, AppSettingType.search);
      expect(s.currentLabel, 'Ex');

      final m = AppSetting.fromJson({
        'key': 'b',
        'name': 'B',
        'type': 6,
        'currentValue': 'a, b',
        'currentLabels': ['Alpha', 'Beta'],
        'maxValue': 4,
      });
      expect(m.type, AppSettingType.multiSearch);
      expect(m.currentIds, ['a', 'b']);
      expect(m.currentLabels, ['Alpha', 'Beta']);
      expect(m.maxValue, 4);

      final o = SettingOption.fromJson({'value': 'v', 'label': 'L', 'subtitle': 'sub'});
      expect((o.value, o.label, o.subtitle), ('v', 'L', 'sub'));
      expect(SettingOption.fromJson({'value': 'v', 'label': 'L'}).subtitle, isNull);
    });
  });

  group('Search setting', () {
    testWidgets('renders the existing label, not the id', (tester) async {
      await _open(tester, FakeApi(), [_single]);
      expect(find.text('London Kings Cross'), findsOneWidget);
      expect(find.text('KGX'), findsNothing);
    });

    testWidgets('typing debounces, shows subtitles, and a pick saves once', (tester) async {
      final api = FakeApi()
        ..knownLabels['EUS'] = 'London Euston'
        ..optionsHandler = (key, q) async =>
            const Ok([SettingOption(value: 'EUS', label: 'London Euston', subtitle: 'EUS rail')]);
      await _open(tester, api, [_single]);

      await tester.enterText(find.byKey(const ValueKey('search-field-station')), 'eu');
      await tester.enterText(find.byKey(const ValueKey('search-field-station')), 'eus');
      await tester.pump(const Duration(milliseconds: 100));
      expect(api.optionQueries, isEmpty); // still debouncing
      await tester.pump(const Duration(milliseconds: 250));
      await tester.pump();
      expect(api.optionQueries, ['eus']);
      expect(find.text('EUS rail'), findsOneWidget);

      await tester.tap(find.byKey(const ValueKey('option-EUS')));
      await tester.pump();
      expect(find.text('London Euston'), findsOneWidget); // optimistic label
      await _settle(tester);
      expect(api.settingUpdates, [
        {'station': 'EUS'}
      ]);
      expect(find.text('London Euston'), findsOneWidget);
      expect(find.byKey(const ValueKey('option-EUS')), findsNothing);
    });

    testWidgets('queries under two characters are not sent', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [_single]);
      await _type(tester, 'station', 'e');
      expect(api.optionQueries, isEmpty);
    });

    testWidgets('clear writes an empty id', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [_single]);
      await tester.tap(find.byKey(const ValueKey('search-clear')));
      await _settle(tester);
      expect(api.settingUpdates.single, {'station': ''});
      expect(find.byKey(const ValueKey('search-current')), findsNothing);
    });

    testWidgets('a stale response never overwrites a newer query', (tester) async {
      final slow = Completer<Result<List<SettingOption>>>();
      final api = FakeApi()
        ..optionsHandler = (key, q) {
          if (q == 'ab') return slow.future;
          return Future.value(const Ok([SettingOption(value: 'new', label: 'Newer result')]));
        };
      await _open(tester, api, [_single]);

      await _type(tester, 'station', 'ab');
      await _type(tester, 'station', 'abc');
      expect(find.text('Newer result'), findsOneWidget);

      slow.complete(const Ok([SettingOption(value: 'old', label: 'Older result')]));
      await tester.pump();
      expect(find.text('Older result'), findsNothing);
      expect(find.text('Newer result'), findsOneWidget);
    });

    testWidgets('an options error is reported on the error bus', (tester) async {
      final api = FakeApi()
        ..optionsHandler = (key, q) async => const Err(ApiError(ApiErrorKind.network, 'offline'));
      final container = await _open(tester, api, [_single]);
      await _type(tester, 'station', 'abc');
      expect(container.read(errorBusProvider), isNotNull);
    });
  });

  group('MultiSearch setting', () {
    testWidgets('renders one chip per label', (tester) async {
      await _open(tester, FakeApi(), [_multi()]);
      expect(find.text('Alpha'), findsOneWidget);
      expect(find.text('Beta'), findsOneWidget);
      expect(find.text('a'), findsNothing);
    });

    testWidgets('add appends the id and removing a chip drops it', (tester) async {
      final api = FakeApi()
        ..knownLabels['c'] = 'Gamma'
        ..optionsHandler = (key, q) async => const Ok([
              SettingOption(value: 'a', label: 'Alpha'), // already picked, hidden
              SettingOption(value: 'c', label: 'Gamma'),
            ]);
      await _open(tester, api, [_multi(max: 4)]);

      await _type(tester, 'stops', 'gam');
      expect(find.byKey(const ValueKey('option-a')), findsNothing);
      await tester.tap(find.byKey(const ValueKey('option-c')));
      await _settle(tester);
      expect(api.settingUpdates.last, {'stops': 'a,b,c'});
      expect(find.text('Gamma'), findsOneWidget);

      await tester.tap(find.descendant(of: find.byKey(const ValueKey('chip-a')), matching: find.byIcon(Icons.clear)));
      await _settle(tester);
      expect(api.settingUpdates.last, {'stops': 'b,c'});
      expect(find.byKey(const ValueKey('chip-a')), findsNothing);
    });

    testWidgets('the maximum hides the add field until one is removed', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [_multi(ids: 'a,b', max: 2)]);
      expect(find.byKey(const ValueKey('search-field-stops')), findsNothing);
      expect(find.textContaining('Maximum of 2'), findsOneWidget);

      await tester.tap(find.descendant(of: find.byKey(const ValueKey('chip-b')), matching: find.byIcon(Icons.clear)));
      await _settle(tester);
      expect(find.byKey(const ValueKey('search-field-stops')), findsOneWidget);
    });
  });

  group('Browse setting (routes of a station)', () {
    const station = AppSetting(
      key: 'stationId',
      name: 'Station',
      type: AppSettingType.search,
      currentValue: 'BST',
      currentLabel: 'Baker Street',
    );
    AppSetting routes({String ids = '', List<String> labels = const []}) => AppSetting(
          key: 'routes',
          name: 'Routes',
          type: AppSettingType.multiSearch,
          currentValue: ids,
          currentLabels: labels,
          browse: true,
        );

    FakeApi api() => FakeApi()
      ..knownLabels['met|Eastbound'] = 'Metropolitan Eastbound'
      ..optionsHandler = (key, q) async => key == 'routes'
          ? const Ok([
              SettingOption(value: 'met|Eastbound', label: 'Metropolitan Eastbound', subtitle: 'Metropolitan line, platforms 5, 6'),
              SettingOption(value: 'jub|Southbound', label: 'Jubilee Southbound', subtitle: 'Jubilee line, platform 1'),
            ])
          : const Ok(<SettingOption>[SettingOption(value: 'EUS', label: 'London Euston')]);

    test('model parses browse, and the options request carries the context', () {
      expect(AppSetting.fromJson({'key': 'r', 'name': 'R', 'type': 6, 'browse': true}).browse, isTrue);
      expect(AppSetting.fromJson({'key': 'r', 'name': 'R', 'type': 6}).browse, isFalse);
      expect(routes().copyWith(currentValue: 'x').browse, isTrue);

      expect(optionsQuery('met', {'stationId': 'HUBBAK', 'x': 'a b&c'}), 'q=met&ctx.stationId=HUBBAK&ctx.x=a+b%26c');
      expect(optionsQuery('', const {}), 'q=');
    });

    testWidgets('lists the options straight away, with the other settings as context', (tester) async {
      final fake = api();
      await _open(tester, fake, [station, routes()]);

      expect(fake.optionQueries, ['']); // one browse request, no typing needed
      expect(fake.optionContexts.single, {'stationId': 'BST'});
      expect(find.text('Metropolitan Eastbound'), findsOneWidget);
      expect(find.text('Metropolitan line, platforms 5, 6'), findsOneWidget);
      expect(find.text('Jubilee Southbound'), findsOneWidget);

      await tester.tap(find.byKey(const ValueKey('option-met|Eastbound')));
      await _settle(tester);
      expect(fake.settingUpdates.last, {'routes': 'met|Eastbound'});
      expect(find.byKey(const ValueKey('chip-met|Eastbound')), findsOneWidget);
      expect(find.byKey(const ValueKey('option-met|Eastbound')), findsNothing); // picked, so no longer offered
      expect(find.byKey(const ValueKey('option-jub|Southbound')), findsOneWidget); // the list stays after a pick
    });

    testWidgets('typing filters through the same request, even for one character', (tester) async {
      final fake = api();
      await _open(tester, fake, [station, routes()]);

      await tester.enterText(find.byKey(const ValueKey('search-field-routes')), 'j');
      await tester.pump(const Duration(milliseconds: 350));
      await tester.pump();
      expect(fake.optionQueries.last, 'j');
    });

    testWidgets('picking another station reloads the routes for it and clears the old selection', (tester) async {
      final fake = api();
      await _open(tester, fake, [station, routes(ids: 'met|Eastbound', labels: ['Metropolitan Eastbound'])]);
      expect(fake.optionContexts.last['stationId'], 'BST');

      await _type(tester, 'stationId', 'eus');
      await tester.tap(find.byKey(const ValueKey('option-EUS')));
      await tester.pump();
      await _settle(tester);

      // one save with both the new station and the emptied routes
      expect(fake.settingUpdates.last, {'stationId': 'EUS', 'routes': ''});
      expect(fake.optionContexts.last['stationId'], 'EUS'); // the routes list was asked again for the new station
      expect(find.byKey(const ValueKey('chip-met|Eastbound')), findsNothing);
    });

    testWidgets('says so when the station has no routes yet', (tester) async {
      final fake = FakeApi()..optionsHandler = (key, q) async => const Ok(<SettingOption>[]);
      await _open(tester, fake, [routes()]);
      expect(find.byKey(const ValueKey('browse-empty')), findsOneWidget);
    });
  });

  testWidgets('select dropdown survives its options changing', (tester) async {
    final api = FakeApi();
    await _open(tester, api, [
      const AppSetting(key: 'style', name: 'Style', type: AppSettingType.select, currentValue: 'a', options: ['a', 'b']),
    ]);
    api.settings[0] =
        const AppSetting(key: 'style', name: 'Style', type: AppSettingType.select, currentValue: 'z', options: ['x', 'y']);
    final c = ProviderScope.containerOf(tester.element(find.byType(AppSettingsView)));
    await c.read(appSettingsProvider('commute').notifier).reload();
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
