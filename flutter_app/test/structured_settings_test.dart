import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/apps/app_settings_sheet.dart';
import 'package:led_matrix_controller/features/apps/syntax_codecs.dart';

import 'fake_api.dart';

// Sample strings from the C# tests (HomeAssistantTilesTests, BinScheduleTests) in their canonical form
const _haSample = 'sensor.lounge_temp|Lounge|spark, light.lamp|Lamp|icon, sensor.power|Power|spark, lock.front|Lock|icon, light.plain|Plain, switch.fan';
const _binsSample = 'Black|#3a3a3a|Mon|2|2026-10-05; Recycling|#1e90ff|Mon|2|2026-10-12; Garden|#2ea043|Wed|1|2026-10-07|2026-12-30';
const _remindersSample = 'Take pills|08:00|20:00|Mon,Tue,Wed,Thu,Fri,Sat,Sun; Water plants|18:00|19:00; Night|22:00|02:00|Fri';

AppSetting _setting(String key, String editor, String value) =>
    AppSetting(key: key, name: key, type: AppSettingType.string, defaultValue: '', currentValue: value, editor: editor);

void main() {
  group('codecs', () {
    test('HA entities round-trip', () {
      expect(serializeHaEntities(parseHaEntities(_haSample)), _haSample);
      expect(serializeHaEntities(parseHaEntities('sensor.a|A|icon+spark')), 'sensor.a|A|icon+spark');
      expect(serializeHaEntities(parseHaEntities('')), '');
    });

    test('HA entities read the same grammar as the server', () {
      final rows = parseHaEntities(' sensor.temp|Lounge, light.kitchen ;bogus, switch.fan| \n climate.hall|Hall ');
      expect(rows.map((r) => r.id), ['sensor.temp', 'light.kitchen', 'switch.fan', 'climate.hall']);
      expect(rows.map((r) => r.label), ['Lounge', '', '', 'Hall']);

      final flags = parseHaEntities('sensor.t|Living|spark, light.lamp|Lamp|icon, switch.x||ICON, sensor.y|Y|spark+bogus, sensor.z|Z|bogus, light.plain|Plain');
      expect(flags.map((r) => (r.icon, r.spark)), [(false, true), (true, false), (true, false), (false, true), (false, false), (false, false)]);
      expect(flags.map((r) => r.label), ['Living', 'Lamp', '', 'Y', 'Z', 'Plain']);
    });

    test('HA entities: edit then serialise', () {
      final rows = parseHaEntities(_haSample);
      rows[1] = rows[1].copyWith(label: 'Lamp, big|one', spark: true);
      expect(serializeHaEntities(rows).split(', ')[1], 'light.lamp|Lamp  big one|icon+spark');
    });

    test('bins round-trip, mixed separators read the same', () {
      expect(serializeBins(parseBins(_binsSample)), _binsSample);
      expect(serializeBins(parseBins('A|#123|Fri')), 'A|#123|Fri');
      expect(parseBins('Black|#3a3a3a|Mon|2|2026-10-05; Recycling|#1e90ff|Mon|2|2026-10-12\nGarden|#2ea043|Wed|1|2026-10-07|2026-12-30;'),
          parseBins(_binsSample));
      expect(parseBins('  ;\n '), isEmpty);
    });

    test('bins: validation matches the server messages', () {
      expect(parseBins(_binsSample).map(validateBin), everyElement(isNull));
      expect(validateBin(parseBins('A|#123|Fri').single), isNull);
      String? v(String s) => validateBin(parseBins(s).single);
      expect(v('Bad|#zzz|Mon|1|'), 'Bad: pick a colour');
      expect(v('NoAnchor|#fff|Tue|2|'), 'NoAnchor: needs an anchor date');
      expect(v('WrongDay|#fff|Tue|1|2026-10-05'), 'WrongDay: anchor date is not a Tue');
      expect(v('Weeks|#fff|Tue|9|'), 'Weeks: weeks must be 1-4');
      expect(v('Skip|#fff|Mon|1|2026-10-05|nope'), 'Skip: skip dates must be yyyy-MM-dd');
      expect(v('Day|#fff|Moo'), 'Day: pick a day');
      expect(validateBin(const BinRow(colour: '#fff')), isNotNull); // no name
    });

    test('reminders round-trip', () {
      expect(serializeReminders(parseReminders(_remindersSample)), _remindersSample);
      expect(parseReminders(_remindersSample)[1].daySet, {0, 1, 2, 3, 4, 5, 6});
      expect(parseReminders(_remindersSample)[2].daySet, {4});
    });

    test('reminders: validation and day edits', () {
      expect(parseReminders(_remindersSample).map(validateReminder), everyElement(isNull));
      String? v(String s) => validateReminder(parseReminders(s).single);
      expect(v('Bad|25:00|19:00|Mon'), 'Bad: times must be HH:mm');
      expect(v('Same|08:00|08:00'), 'Same: the window is empty');
      expect(v('Days|08:00|09:00|Moo'), 'Days: pick at least one day');

      final all = parseReminders(_remindersSample).first;
      final noSunday = all.withDays(all.daySet..remove(6));
      expect(serializeReminders([noSunday]), 'Take pills|08:00|20:00|Mon,Tue,Wed,Thu,Fri,Sat');
      expect(serializeReminders([noSunday.withDays({0, 1, 2, 3, 4, 5, 6})]), 'Take pills|08:00|20:00'); // all seven = daily
    });
  });

  test('AppSetting reads the editor hint, null when absent', () {
    expect(AppSetting.fromJson({'key': 'a', 'name': 'A', 'type': 2}).editor, isNull);
    expect(AppSetting.fromJson({'key': 'a', 'name': 'A', 'type': 2, 'editor': null}).editor, isNull);
    expect(AppSetting.fromJson({'key': 'a', 'name': 'A', 'type': 2, 'editor': 'bins'}).editor, 'bins');
    expect(_setting('a', 'bins', 'x').copyWith(currentValue: 'y').editor, 'bins');
  });

  group('editors in the settings sheet', () {
    Future<void> open(WidgetTester tester, FakeApi api, List<AppSetting> settings) async {
      api.settings
        ..clear()
        ..addAll(settings);
      tester.view.physicalSize = const Size(800, 4000);
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
          home: Scaffold(body: AppSettingsView(app: MatrixApp(id: 'bin-day', name: 'Bin Day', hasSettings: true))),
        ),
      ));
      await tester.pumpAndSettle();
    }

    Future<void> settle(WidgetTester tester) async {
      await tester.pump(const Duration(milliseconds: 600));
      await tester.pumpAndSettle();
    }

    testWidgets('unknown editor falls back to the text field', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('bins', 'mystery', 'x')]);
      expect(find.byType(TextField), findsOneWidget);
      expect(find.byKey(const Key('add-bin')), findsNothing);
    });

    testWidgets('bins: shows rows, edits the name and saves the exact syntax', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('bins', 'bins', _binsSample)]);

      expect(find.byKey(const Key('bin-name-0')), findsOneWidget);
      expect(find.byKey(const Key('bin-name-2')), findsOneWidget);
      expect(api.settingUpdates, isEmpty); // viewing never saves

      await tester.enterText(find.byKey(const Key('bin-name-0')), 'Grey');
      await settle(tester);
      expect(api.settingUpdates.last['bins'], _binsSample.replaceFirst('Black', 'Grey'));
    });

    testWidgets('bins: swatch sets the colour, reorder and remove', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('bins', 'bins', _binsSample)]);

      await tester.tap(find.byKey(const Key('bin-colour-0-red')));
      await settle(tester);
      expect(api.settingUpdates.last['bins'], startsWith('Black|#FF0000|Mon|2|2026-10-05; Recycling'));

      await tester.tap(find.byKey(const Key('row-down-0')));
      await settle(tester);
      expect(api.settingUpdates.last['bins'], startsWith('Recycling|#1e90ff|Mon|2|2026-10-12; Black|#FF0000'));

      await tester.tap(find.byKey(const Key('row-remove-0')));
      await settle(tester);
      expect(api.settingUpdates.last['bins'], 'Black|#FF0000|Mon|2|2026-10-05; Garden|#2ea043|Wed|1|2026-10-07|2026-12-30');
    });

    testWidgets('bins: invalid row shows the parse error and is not saved until fixed', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('bins', 'bins', 'Black|#3a3a3a|Mon|2|2026-10-05')]);

      await tester.enterText(find.byKey(const Key('bin-anchor-0')), '2026-10-06');
      await settle(tester);
      expect(find.text('Black: anchor date is not a Mon'), findsOneWidget);
      expect(api.settingUpdates, isEmpty);

      await tester.enterText(find.byKey(const Key('bin-anchor-0')), '2026-10-12');
      await settle(tester);
      expect(find.byKey(const Key('row-error-0')), findsNothing);
      expect(api.settingUpdates.last['bins'], 'Black|#3a3a3a|Mon|2|2026-10-12');
    });

    testWidgets('bins: add bin starts with a name error', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('bins', 'bins', '')]);
      expect(find.byKey(const Key('rows-empty-bins')), findsOneWidget);

      await tester.tap(find.byKey(const Key('add-bin')));
      await tester.pumpAndSettle();
      expect(find.text('Give the bin a name'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('bin-name-0')), 'Glass');
      await settle(tester);
      expect(api.settingUpdates.last['bins'], 'Glass|#808080|Mon');
    });

    testWidgets('reminders: edit text and days, saves the exact syntax', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('reminders', 'reminders', _remindersSample)]);

      await tester.enterText(find.byKey(const Key('rem-text-1')), 'Water');
      await settle(tester);
      expect(api.settingUpdates.last['reminders'], _remindersSample.replaceFirst('Water plants', 'Water'));

      // Row 1 is daily: turning Sunday off lists the days
      await tester.tap(find.byKey(const Key('rem-day-1-Sun')));
      await settle(tester);
      expect(api.settingUpdates.last['reminders'], contains('Water|18:00|19:00|Mon,Tue,Wed,Thu,Fri,Sat'));

      await tester.tap(find.byKey(const Key('rem-day-1-Sun')));
      await settle(tester);
      expect(api.settingUpdates.last['reminders'], contains('Water|18:00|19:00;'));
    });

    testWidgets('reminders: bad time shows the error and is not saved', (tester) async {
      final api = FakeApi();
      await open(tester, api, [_setting('reminders', 'reminders', 'Pills|08:00|20:00')]);

      await tester.enterText(find.byKey(const Key('rem-end-0')), '25:00');
      await settle(tester);
      expect(find.text('Pills: times must be HH:mm'), findsOneWidget);
      expect(api.settingUpdates, isEmpty);

      await tester.enterText(find.byKey(const Key('rem-end-0')), '21:30');
      await settle(tester);
      expect(api.settingUpdates.last['reminders'], 'Pills|08:00|21:30');
    });

    testWidgets('ha entities: pick from the options, edit label and flags, reorder', (tester) async {
      final api = FakeApi()
        ..optionsHandler = (key, q) async {
          expect(key, 'entities');
          final all = [
            const SettingOption(value: 'light.kitchen', label: 'Kitchen Light', subtitle: 'light.kitchen'),
            const SettingOption(value: 'sensor.lounge_temp', label: 'Lounge Temperature', subtitle: 'sensor.lounge_temp'),
          ];
          return Ok([for (final o in all) if (q.isEmpty || o.value.contains(q)) o]);
        };
      await open(tester, api, [_setting('entities', 'ha_entities', 'switch.fan|Fan|icon')]);

      expect(find.byKey(const Key('ha-title-0')), findsOneWidget);
      // Browse: the list is there before typing
      expect(find.byKey(const Key('ha-option-light.kitchen')), findsOneWidget);

      await tester.tap(find.byKey(const Key('ha-option-light.kitchen')));
      await settle(tester);
      expect(api.settingUpdates.last['entities'], 'switch.fan|Fan|icon, light.kitchen');
      expect(find.text('Kitchen Light'), findsWidgets);
      expect(find.byKey(const Key('ha-option-light.kitchen')), findsNothing); // already added

      await tester.enterText(find.byKey(const Key('ha-label-1')), 'Kitchen');
      await tester.pump(); // enterText does not build the frame it schedules
      await tester.tap(find.byKey(const Key('ha-spark-1')));
      await settle(tester);
      expect(api.settingUpdates.last['entities'], 'switch.fan|Fan|icon, light.kitchen|Kitchen|spark');

      await tester.tap(find.byKey(const Key('row-up-1')));
      await settle(tester);
      expect(api.settingUpdates.last['entities'], 'light.kitchen|Kitchen|spark, switch.fan|Fan|icon');

      await tester.tap(find.byKey(const Key('row-remove-0')));
      await settle(tester);
      expect(api.settingUpdates.last['entities'], 'switch.fan|Fan|icon');

      await tester.enterText(find.byKey(const Key('ha-entity-search')), 'lounge');
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pumpAndSettle();
      expect(api.optionQueries, contains('lounge'));
      expect(find.byKey(const Key('ha-option-sensor.lounge_temp')), findsOneWidget);
    });

    testWidgets('ha entities: not configured shows the explanation, not a pick', (tester) async {
      final api = FakeApi()
        ..optionsHandler = (key, q) async => const Ok([SettingOption(value: '', label: 'Home Assistant is not configured', subtitle: 'Set HomeAssistant:BaseUrl')]);
      await open(tester, api, [_setting('entities', 'ha_entities', '')]);

      expect(find.text('Home Assistant is not configured'), findsOneWidget);
      await tester.tap(find.byKey(const Key('ha-option-info')));
      await settle(tester);
      expect(api.settingUpdates, isEmpty);
    });
  });
}
