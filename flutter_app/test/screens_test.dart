import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'fake_api.dart';

Future<void> _open(WidgetTester tester, FakeApi api) async {
  tester.view.physicalSize = const Size(800, 2000);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('Now screen shows the app, brightness as a percentage and the quick-switch strip', (tester) async {
    final api = FakeApi();
    await _open(tester, api);

    expect(find.text('Now showing'), findsOneWidget);
    expect(find.text('Clock'), findsWidgets);
    expect(find.text('50%'), findsOneWidget); // 128 / 255
    expect(find.text('Quick switch'), findsOneWidget);
    expect(find.text('Weather'), findsOneWidget);
    // All five tabs are in the navigation bar.
    expect(find.text('Schedule'), findsOneWidget);
    expect(find.text('Notify'), findsOneWidget);
  });

  testWidgets('quick switch activates an app', (tester) async {
    final api = FakeApi();
    await _open(tester, api);

    await tester.tap(find.text('Weather'));
    await tester.pumpAndSettle();

    expect(api.calls, contains('activate:weather'));
  });

  testWidgets('failed activation surfaces a snackbar', (tester) async {
    final api = FakeApi(failActivate: true);
    await _open(tester, api);

    await tester.tap(find.text('Weather'));
    await tester.pumpAndSettle();

    expect(find.textContaining('HTTP 404'), findsOneWidget);
  });

  testWidgets('brightness slider sends 0-255 after the debounce', (tester) async {
    final api = FakeApi();
    await _open(tester, api);

    final slider = find.byType(Slider);
    await tester.drag(slider, const Offset(2000, 0)); // all the way right
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();

    expect(api.calls.where((c) => c.startsWith('brightness:')).last, 'brightness:255');
    expect(find.text('100%'), findsOneWidget);
  });

  testWidgets('power switch posts the new state', (tester) async {
    final api = FakeApi();
    await _open(tester, api);

    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();

    expect(api.calls, contains('power:false'));
  });

  testWidgets('a failing apps endpoint shows an inline error', (tester) async {
    final api = FakeApi(failApps: true);
    await _open(tester, api);

    expect(find.textContaining('Cannot reach the device'), findsWidgets);
    expect(find.text('Retry'), findsWidgets);
  });

  testWidgets('Apps screen lists, searches, activates and opens settings', (tester) async {
    final api = FakeApi();
    await _open(tester, api);

    await tester.tap(find.text('Apps').last);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('app-tile-clock')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-weather')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-fire')), findsOneWidget);

    await tester.enterText(find.byType(SearchBar), 'wea');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('app-tile-weather')), findsOneWidget);
    expect(find.byKey(const Key('app-tile-clock')), findsNothing);

    // Activate is separate from configure.
    await tester.tap(find.byKey(const Key('activate-weather')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('activate:weather'));

    // Tapping the tile opens settings for any app.
    await tester.tap(find.byKey(const Key('app-tile-weather')));
    await tester.pumpAndSettle();
    expect(find.text('Show seconds'), findsOneWidget);
    expect(find.text('Tint'), findsOneWidget);
    expect(find.text('#FF0000'), findsOneWidget);
  });

  testWidgets('settings edits are saved once, debounced', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.text('Apps').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('app-tile-clock')));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(Switch).last);
    await tester.pump(const Duration(milliseconds: 100));
    expect(api.settingUpdates, isEmpty);
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();

    expect(api.settingUpdates, [
      {'showSeconds': false},
    ]);
  });

  testWidgets('colour setting opens a picker and saves a hex value', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.text('Apps').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('app-tile-clock')));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Tint'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('hue-slider')), findsOneWidget);

    await tester.enterText(find.byKey(const Key('hex-field')), '#00FF00');
    await tester.tap(find.text('Select'));
    await tester.pumpAndSettle(const Duration(milliseconds: 700));

    expect(api.settingUpdates.last['tint'], '#00FF00');
  });

  testWidgets('Settings screen shows health and device URL', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.text('Settings').last);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('device-url')), findsOneWidget);
    expect(find.text('OK'), findsOneWidget);
    expect(find.text('1h 1m'), findsOneWidget);
  });
}
