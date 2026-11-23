import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/models.dart';

import 'fake_api.dart';

Future<void> _openApps(WidgetTester tester, FakeApi api) async {
  tester.view.physicalSize = const Size(800, 2000);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps').last);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('tapping a tile opens details, long press activates, active tile says On display', (tester) async {
    final api = FakeApi();
    await _openApps(tester, api);

    expect(find.text('On display'), findsOneWidget); // the active app (clock)
    expect(find.byKey(const Key('screens-entry')), findsOneWidget);

    await tester.longPress(find.byKey(const Key('app-tile-weather')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('activate:weather'));

    await tester.tap(find.byKey(const Key('app-tile-fire')));
    await tester.pumpAndSettle();
    // Inactive app: the sheet offers to show it, then activating shows the preview.
    expect(find.byKey(const Key('sheet-activate')), findsOneWidget);
    await tester.tap(find.byKey(const Key('sheet-activate')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('activate:fire'));
    expect(find.byKey(const Key('sheet-preview')), findsOneWidget);
    expect(find.byKey(const Key('sheet-activate')), findsNothing);
  });

  testWidgets('settings sheet sits above the navigation bar', (tester) async {
    final api = FakeApi();
    await _openApps(tester, api);
    await tester.tap(find.byKey(const Key('app-tile-clock')));
    await tester.pumpAndSettle();

    final navBottom = tester.getBottomLeft(find.byType(NavigationBar)).dy;
    final sheet = find.byType(BottomSheet);
    expect(sheet, findsOneWidget);
    expect(tester.getBottomLeft(sheet).dy, greaterThanOrEqualTo(navBottom));
    expect(find.byType(NavigationBar).hitTestable(), findsNothing);
  });

  testWidgets('a fixed option set renders as chips and saves the pick', (tester) async {
    final api = FakeApi();
    api.settings
      ..clear()
      ..add(const AppSetting(
          key: 'palette',
          name: 'Palette',
          type: AppSettingType.select,
          defaultValue: 'Sunset',
          currentValue: 'Sunset',
          options: ['Sunset', 'Ocean', 'Neon', 'Aurora', 'Ember', 'Mono', 'Lava', 'Cyber', 'Rainbow']));
    await _openApps(tester, api);
    await tester.tap(find.byKey(const Key('app-tile-clock')));
    await tester.pumpAndSettle();

    expect(find.byType(ChoiceChip), findsNWidgets(9));
    await tester.tap(find.byKey(const Key('chip-palette-Ocean')));
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();
    expect(api.settingUpdates.single, {'palette': 'Ocean'});
  });
}
