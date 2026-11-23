import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/core/connection.dart';
import 'package:led_matrix_controller/core/device_url.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/settings/settings_page.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

ProviderContainer containerFor(FakeApi api) {
  final c = ProviderContainer(retry: (a, b) => null, overrides: [
    apiProvider.overrideWithValue(api),
    pollIntervalProvider.overrideWithValue(null),
  ]);
  addTearDown(c.dispose);
  return c;
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  group('normalizeDeviceUrl', () {
    String? ok(String s) => normalizeDeviceUrl(s).url;

    test('adds scheme and default port', () {
      expect(ok('192.168.1.20'), 'http://192.168.1.20:5005');
      expect(ok('ledmatrix.local'), 'http://ledmatrix.local:5005');
      expect(ok('  ledmatrix.local:5005 '), 'http://ledmatrix.local:5005');
    });

    test('keeps explicit scheme and port, strips trailing slash', () {
      expect(ok('http://10.0.0.5:8080/'), 'http://10.0.0.5:8080');
      expect(ok('HTTP://Host:80'), 'http://host:80');
      expect(ok('https://matrix.example.com'), 'https://matrix.example.com');
      expect(ok('http://pi/api-proxy/'), 'http://pi:5005/api-proxy');
    });

    test('rejects bad input', () {
      expect(normalizeDeviceUrl('').error, isNotNull);
      expect(normalizeDeviceUrl('ftp://host').error, isNotNull);
      expect(normalizeDeviceUrl('http://').error, isNotNull);
      expect(normalizeDeviceUrl('host:99999').error, isNotNull);
      expect(normalizeDeviceUrl('a b').error, isNotNull);
      expect(normalizeDeviceUrl('bad_host!').error, isNotNull);
    });
  });

  test('formatUptime is human readable', () {
    expect(formatUptime(20), '20 seconds');
    expect(formatUptime(1), '1 second');
    expect(formatUptime(300), '5 minutes');
    expect(formatUptime(3700), '1 hour, 1 minute');
    expect(formatUptime(2 * 86400 + 3 * 3600), '2 days, 3 hours');
  });

  group('ConnectionNotifier', () {
    test('goes offline on first failure and recovers, refreshing data', () async {
      final api = FakeApi()..failHealth = true;
      final c = containerFor(api);
      await c.read(deviceSettingsProvider.future);
      expect(c.read(connectionProvider).status, ConnectionStatus.connecting);

      await c.read(connectionProvider.notifier).check();
      expect(c.read(connectionProvider).status, ConnectionStatus.offline);

      api.failHealth = false;
      api.brightness = 80;
      await c.read(connectionProvider.notifier).check();
      expect(c.read(connectionProvider).status, ConnectionStatus.online);
      await c.read(deviceSettingsProvider.future);
      expect(c.read(deviceSettingsProvider).value!.brightness, 80);
    });

    test('tolerates one dropped poll while online', () async {
      final api = FakeApi();
      final c = containerFor(api);
      await c.read(connectionProvider.notifier).check();
      api.failHealth = true;
      await c.read(connectionProvider.notifier).check();
      expect(c.read(connectionProvider).status, ConnectionStatus.online);
      await c.read(connectionProvider.notifier).check();
      expect(c.read(connectionProvider).status, ConnectionStatus.offline);
    });

    test('active app and power changed elsewhere are picked up', () async {
      final api = FakeApi();
      final c = containerFor(api);
      await c.read(appListProvider.future);
      await c.read(deviceSettingsProvider.future);
      await c.read(connectionProvider.notifier).check();

      api.active = 'fire';
      api.power = false;
      await c.read(connectionProvider.notifier).check();
      await Future<void>.delayed(Duration.zero);
      await Future<void>.delayed(Duration.zero);
      expect(c.read(appListProvider).value!.activeApp, 'fire');
      expect(c.read(deviceSettingsProvider).value!.isEnabled, false);
    });
  });

  group('device settings', () {
    test('failed power toggle rolls back only power, keeping a newer brightness', () async {
      final api = FakeApi()..failPower = true;
      final c = containerFor(api);
      await c.read(deviceSettingsProvider.future);
      final n = c.read(deviceSettingsProvider.notifier);
      final future = n.setPower(false);
      n.setBrightnessPercent(30);
      await future;
      final s = c.read(deviceSettingsProvider).value!;
      expect(s.isEnabled, true);
      expect(s.brightness, 30);
      expect(c.read(errorBusProvider), isNotNull);
    });

    test('rapid brightness changes send the last value', () async {
      final api = FakeApi();
      final c = containerFor(api);
      await c.read(deviceSettingsProvider.future);
      final n = c.read(deviceSettingsProvider.notifier);
      n.setBrightnessPercent(10);
      n.setBrightnessPercent(20);
      n.setBrightnessPercent(70);
      await Future<void>.delayed(const Duration(milliseconds: 400));
      expect(api.calls.where((x) => x.startsWith('brightness')).toList(), ['brightness:70']);
      expect(api.brightness, 70);
    });

    test('failed brightness reloads the real value', () async {
      final api = FakeApi()..failBrightness = true;
      final c = containerFor(api);
      await c.read(deviceSettingsProvider.future);
      c.read(deviceSettingsProvider.notifier).setBrightnessPercent(90);
      await Future<void>.delayed(const Duration(milliseconds: 400));
      expect(c.read(deviceSettingsProvider).value!.brightness, 50);
      expect(c.read(errorBusProvider), isNotNull);
    });
  });

  group('settings page', () {
    testWidgets('test connection reports success and failure, save normalises', (tester) async {
      final api = FakeApi();
      await tester.pumpWidget(testApp(api));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Settings'));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(const Key('device-url')), '192.168.1.20');
      await tester.pump();
      expect(find.text('Will connect to http://192.168.1.20:5005'), findsOneWidget);

      await tester.tap(find.byKey(const Key('test-connection')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('test-ok')), findsOneWidget);

      api.failHealth = true;
      await tester.tap(find.byKey(const Key('test-connection')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('test-failed')), findsOneWidget);

      await tester.tap(find.byKey(const Key('save-url')));
      await tester.pumpAndSettle();
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('api_url'), 'http://192.168.1.20:5005');
    });

    testWidgets('invalid address disables test and save', (tester) async {
      await tester.pumpWidget(testApp(FakeApi()));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Settings'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('device-url')), 'ftp://x');
      await tester.pump();
      expect(tester.widget<FilledButton>(find.byKey(const Key('save-url'))).onPressed, isNull);
      expect(find.text('Only http:// and https:// addresses are supported'), findsOneWidget);
    });
  });
}
