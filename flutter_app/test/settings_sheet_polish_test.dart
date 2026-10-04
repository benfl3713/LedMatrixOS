import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/apps/app_settings_sheet.dart';

import 'fake_api.dart';

Future<void> _open(WidgetTester tester, FakeApi api, List<AppSetting> settings) async {
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
      home: Scaffold(body: AppSettingsView(app: MatrixApp(id: 'clock', name: 'Clock', hasSettings: true))),
    ),
  ));
  await tester.pumpAndSettle();
}

/// Lets the 500 ms save debounce and the follow-up reload run.
Future<void> _settle(WidgetTester tester) async {
  await tester.pump(const Duration(milliseconds: 600));
  await tester.pumpAndSettle();
}

const _size = AppSetting(
    key: 'size', name: 'Size', description: 'How big', type: AppSettingType.integer, defaultValue: 2, currentValue: 4, minValue: 1, maxValue: 5);
const _label = AppSetting(key: 'label', name: 'Label', description: 'Text shown', type: AppSettingType.string, defaultValue: 'hi', currentValue: 'yo');
const _atDefault = AppSetting(key: 'flag', name: 'Flag', type: AppSettingType.boolean, defaultValue: true, currentValue: true);

void main() {
  test('isModified compares against the default', () {
    expect(_size.isModified, isTrue);
    expect(_atDefault.isModified, isFalse);
    expect(const AppSetting(key: 'k', name: 'K', type: AppSettingType.string, currentValue: 'x').isModified, isFalse); // no default
    expect(const AppSetting(key: 'k', name: 'K', type: AppSettingType.integer, defaultValue: 2, currentValue: 2.0).isModified, isFalse);
    expect(AppSetting.fromJson({'key': 'a', 'name': 'A', 'type': 0}).advanced, isFalse);
    expect(AppSetting.fromJson({'key': 'a', 'name': 'A', 'type': 0, 'advanced': true}).advanced, isTrue);
  });

  group('reset to default', () {
    testWidgets('single reset posts the default and the button goes away', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [_size, _label, _atDefault]);

      expect(find.byKey(const Key('reset-size')), findsOneWidget);
      expect(find.byKey(const Key('reset-label')), findsOneWidget);
      expect(find.byKey(const Key('reset-flag')), findsNothing);

      await tester.tap(find.byKey(const Key('reset-size')));
      await _settle(tester);

      expect(api.settingUpdates.single, {'size': 2});
      expect(find.byKey(const Key('reset-size')), findsNothing);
      expect(find.byKey(const Key('reset-label')), findsOneWidget);
    });

    testWidgets('reset all asks first, then resets every changed setting', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [_size, _label, _atDefault]);

      await tester.tap(find.byKey(const Key('reset-all')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();
      await _settle(tester);
      expect(api.settingUpdates, isEmpty);

      await tester.tap(find.byKey(const Key('reset-all')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('reset-all-confirm')));
      await _settle(tester);

      expect(api.settingUpdates.single, {'size': 2, 'label': 'hi'});
      expect(find.byKey(const Key('reset-all')), findsNothing);
    });
  });

  group('basic and advanced', () {
    const adv = AppSetting(
        key: 'filter', name: 'Platform Filter', description: 'Raw text', type: AppSettingType.string, defaultValue: '', currentValue: '', advanced: true);

    testWidgets('advanced settings sit under a collapsed Advanced section', (tester) async {
      await _open(tester, FakeApi(), [_atDefault, adv]);

      expect(find.text('Flag'), findsOneWidget);
      expect(find.byKey(const Key('advanced-section')), findsOneWidget);
      expect(find.text('Platform Filter'), findsNothing);

      await tester.tap(find.text('Advanced'));
      await tester.pumpAndSettle();
      expect(find.text('Platform Filter'), findsOneWidget);
    });

    testWidgets('auto-expands when an advanced value is not the default', (tester) async {
      await _open(tester, FakeApi(), [_atDefault, adv.copyWith(currentValue: 'Eastbound')]);
      expect(find.text('Platform Filter'), findsOneWidget);
      expect(find.byKey(const Key('reset-filter')), findsOneWidget);
    });

    testWidgets('no Advanced section without advanced settings', (tester) async {
      await _open(tester, FakeApi(), [_atDefault]);
      expect(find.byKey(const Key('advanced-section')), findsNothing);
    });
  });

  group('save indicator', () {
    testWidgets('saving, then saved', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [_label]);
      expect(find.byKey(const Key('save-idle')), findsOneWidget);

      await tester.enterText(find.byType(TextField), 'new');
      await tester.pump();
      expect(find.byKey(const Key('save-saving')), findsOneWidget);

      await _settle(tester);
      expect(find.byKey(const Key('save-saved')), findsOneWidget);
      expect(api.settingUpdates.single, {'label': 'new'});
    });

    testWidgets('failure shows the message and retry sends it again', (tester) async {
      final api = FakeApi()
        ..failUpdate = const ApiError(ApiErrorKind.http, 'HTTP 400: Unknown setting(s)', statusCode: 400, fieldErrors: [FieldError('settings.label', 'Too long')]);
      await _open(tester, api, [_label]);

      await tester.enterText(find.byType(TextField), 'new');
      await _settle(tester);

      expect(find.byKey(const Key('save-failed')), findsOneWidget);
      expect(find.byKey(const Key('error-label')), findsOneWidget);
      expect(find.text('Too long'), findsOneWidget);
      expect(find.text('new'), findsOneWidget); // the typed value stays

      api.failUpdate = null;
      await tester.tap(find.byKey(const Key('save-retry')));
      await _settle(tester);

      expect(api.settingUpdates.length, 2);
      expect(api.settingUpdates.last, {'label': 'new'});
      expect(find.byKey(const Key('save-saved')), findsOneWidget);
      expect(find.byKey(const Key('error-label')), findsNothing);
    });

    testWidgets('integer text fields validate min and max locally', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [
        const AppSetting(key: 'size', name: 'Size', type: AppSettingType.integer, defaultValue: 2, currentValue: 3, maxValue: 5),
      ]);
      // only an upper bound: a number field instead of a slider
      await tester.enterText(find.byType(TextField), '9');
      await _settle(tester);
      expect(find.text('Must be at most 5'), findsOneWidget);
      expect(api.settingUpdates, isEmpty);

      await tester.enterText(find.byType(TextField), '4');
      await _settle(tester);
      expect(find.text('Must be at most 5'), findsNothing);
      expect(api.settingUpdates.single, {'size': 4});
    });
  });

  group('select pickers', () {
    final many = [for (var i = 1; i <= 12; i++) 'Option$i'];

    testWidgets('a long Select opens a searchable list', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [
        AppSetting(key: 'mode', name: 'Mode', description: 'Which one', type: AppSettingType.select, defaultValue: 'Option1', currentValue: 'Option1', options: many),
      ]);

      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.text('Which one'), findsOneWidget);
      await tester.tap(find.byKey(const Key('select-mode')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('option-Option12')), findsOneWidget);

      await tester.enterText(find.byKey(const Key('option-search')), 'tion1');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('option-Option2')), findsNothing);
      expect(find.byKey(const Key('option-Option11')), findsOneWidget);

      await tester.tap(find.byKey(const Key('option-Option11')));
      await tester.pumpAndSettle();
      await _settle(tester);
      expect(api.settingUpdates.single, {'mode': 'Option11'});
    });

    testWidgets('a short Select stays a dropdown', (tester) async {
      await _open(tester, FakeApi(), [
        const AppSetting(key: 'mode', name: 'Mode', type: AppSettingType.select, currentValue: 'a', options: ['a', 'b']),
      ]);
      expect(find.byType(DropdownButtonFormField<String>), findsOneWidget);
    });

    testWidgets('a colour-name Select shows swatches and sends the name', (tester) async {
      final api = FakeApi();
      await _open(tester, api, [
        const AppSetting(
            key: 'timeColor',
            name: 'Time Color',
            description: 'Digits',
            type: AppSettingType.select,
            defaultValue: 'White',
            currentValue: 'White',
            options: ['Palette', 'White', 'Red', 'Green', 'Blue']),
      ]);

      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.byKey(const Key('colour-timeColor-Red')), findsOneWidget);
      expect(find.byKey(const Key('colour-timeColor-Palette')), findsOneWidget); // not a colour: labelled chip

      await tester.tap(find.byKey(const Key('colour-timeColor-Red')));
      await _settle(tester);
      expect(api.settingUpdates.single, {'timeColor': 'Red'});
      expect(find.byKey(const Key('reset-timeColor')), findsOneWidget);
    });
  });
}
