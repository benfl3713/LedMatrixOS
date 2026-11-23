import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/models.dart';
import 'package:led_matrix_controller/core/connection.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/core/router.dart';
import 'package:led_matrix_controller/features/onboarding/discovery.dart';
import 'package:led_matrix_controller/features/onboarding/onboarding_page.dart';
import 'package:led_matrix_controller/main.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

Widget _firstRunApp(FakeApi api, {DeviceScanner? scanner}) => ProviderScope(
      retry: (_, __) => null,
      overrides: [
        deviceConfigProvider.overrideWith(() => FakeDeviceConfig(null)),
        apiProvider.overrideWithValue(api),
        apiFactoryProvider.overrideWithValue((_) => api),
        pollIntervalProvider.overrideWithValue(null),
        previewFeedProvider.overrideWith((ref) => Stream.value(const PreviewUpdate(connected: true))),
        if (scanner != null) deviceScannerProvider.overrideWithValue(scanner),
      ],
      child: const LedMatrixApp(),
    );

void _phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(390, 1600);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  group('router gate', () {
    test('nothing is routed before the saved address has loaded', () {
      expect(deviceConfigRedirect(const DeviceConfig(), '/now'), '/loading');
      expect(deviceConfigRedirect(const DeviceConfig(), '/loading'), isNull);
    });

    test('a first run goes to the welcome flow and cannot leave it', () {
      const first = DeviceConfig(loaded: true);
      expect(deviceConfigRedirect(first, '/now'), '/welcome');
      expect(deviceConfigRedirect(first, '/display'), '/welcome');
      expect(deviceConfigRedirect(first, '/welcome'), isNull);
    });

    test('with a saved address onboarding and the splash lead to Now', () {
      const saved = DeviceConfig(loaded: true, url: 'http://pi:5005');
      expect(deviceConfigRedirect(saved, '/welcome'), '/now');
      expect(deviceConfigRedirect(saved, '/loading'), '/now');
      expect(deviceConfigRedirect(saved, '/apps'), isNull);
    });
  });

  group('onboarding', () {
    testWidgets('a saved address skips onboarding', (tester) async {
      _phone(tester);
      await tester.pumpWidget(testApp(FakeApi()));
      await tester.pumpAndSettle();
      expect(find.byType(OnboardingPage), findsNothing);
      expect(find.byType(NavigationBar), findsOneWidget);
    });

    testWidgets('a first run shows the welcome screen instead of Now, and no request is made', (tester) async {
      _phone(tester);
      final api = FakeApi();
      await tester.pumpWidget(_firstRunApp(api));
      await tester.pumpAndSettle();
      expect(find.text('Meet your display'), findsOneWidget);
      expect(find.byKey(const Key('find-display')), findsOneWidget);
      expect(find.byType(NavigationBar), findsNothing);
      expect(api.calls, isEmpty);
    });

    testWidgets('manual entry: invalid input is explained, a working address is tested, saved, and lands on Now', (tester) async {
      _phone(tester);
      await tester.pumpWidget(_firstRunApp(FakeApi()));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(const Key('manual-address')), 'ftp://x');
      await tester.tap(find.byKey(const Key('test-connection')));
      await tester.pumpAndSettle();
      expect(find.text('Only http:// and https:// addresses are supported'), findsOneWidget);
      expect(find.byKey(const Key('use-display')), findsNothing);

      await tester.enterText(find.byKey(const Key('manual-address')), '192.168.1.20');
      await tester.tap(find.byKey(const Key('test-connection')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('test-ok')), findsOneWidget);
      expect(find.text('Connected to http://192.168.1.20:5005'), findsOneWidget);

      await tester.ensureVisible(find.byKey(const Key('use-display')));
      await tester.tap(find.byKey(const Key('use-display')));
      await tester.pumpAndSettle();
      expect(find.byType(OnboardingPage), findsNothing);
      expect(find.byType(NavigationBar), findsOneWidget);
      expect((await SharedPreferences.getInstance()).getString('api_url'), 'http://192.168.1.20:5005');
    });

    testWidgets('a failed test shows the reason and offers no way forward', (tester) async {
      _phone(tester);
      final api = FakeApi()..failHealth = true;
      await tester.pumpWidget(_firstRunApp(api));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('manual-address')), 'ledmatrix.local');
      await tester.tap(find.byKey(const Key('test-connection')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('test-failed')), findsOneWidget);
      expect(find.byKey(const Key('use-display')), findsNothing);
    });

    testWidgets('discovery lists devices as they appear and connecting to one lands on Now', (tester) async {
      _phone(tester);
      final controller = StreamController<FoundDevice>();
      addTearDown(controller.close);
      await tester.pumpWidget(_firstRunApp(FakeApi(), scanner: () => controller.stream));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('find-display')));
      await tester.pump();
      expect(find.byKey(const Key('scanning')), findsOneWidget);

      controller.add(const FoundDevice(
        url: 'http://192.168.1.42:5005',
        health: Health(status: 'ok', activeApp: 'clock', isEnabled: true, width: 256, height: 64, uptimeSeconds: 5),
      ));
      await tester.pump();
      expect(find.text('192.168.1.42'), findsOneWidget);
      expect(find.text('256x64 - showing clock'), findsOneWidget);

      await tester.tap(find.byKey(const Key('found-192.168.1.42')));
      await tester.pumpAndSettle();
      expect(find.byType(NavigationBar), findsOneWidget);
      expect((await SharedPreferences.getInstance()).getString('api_url'), 'http://192.168.1.42:5005');
    });

    testWidgets('a scan that finds nothing says so and points to manual entry', (tester) async {
      _phone(tester);
      await tester.pumpWidget(_firstRunApp(FakeApi(), scanner: () => const Stream.empty()));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('find-display')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('scan-empty')), findsOneWidget);
      expect(find.byKey(const Key('manual-address')), findsOneWidget);
    });
  });

  group('scanner', () {
    test('tries the well-known names first, then the subnet with bounded concurrency, skipping this device', () async {
      final probed = <String>[];
      var inFlight = 0;
      var peak = 0;
      Future<Health?> probe(String url, Duration timeout) async {
        probed.add(url);
        inFlight++;
        if (inFlight > peak) peak = inFlight;
        await Future<void>.delayed(Duration.zero);
        inFlight--;
        if (url == 'http://10.0.0.7:5005') {
          return const Health(status: 'ok', isEnabled: true, width: 256, height: 64, uptimeSeconds: 1);
        }
        return null;
      }

      final found = await scanForDevices(
        probe: probe,
        localAddresses: () async => ['10.0.0.20'],
        priority: const ['http://ledmatrix.local:5005'],
        concurrency: 8,
      ).toList();

      expect(probed.first, 'http://ledmatrix.local:5005');
      expect(found.map((d) => d.url), ['http://10.0.0.7:5005']);
      expect(probed, isNot(contains('http://10.0.0.20:5005')));
      expect(probed.length, 1 + 253);
      expect(peak, lessThanOrEqualTo(8));
    });

    test('a device reachable by name and by address is reported once per host', () async {
      Future<Health?> probe(String url, Duration timeout) async =>
          const Health(status: 'ok', isEnabled: true, width: 256, height: 64, uptimeSeconds: 1);
      final found = await scanForDevices(
        probe: probe,
        localAddresses: () async => const [],
        priority: const ['http://a.local:5005', 'http://a.local:5005'],
      ).toList();
      expect(found, hasLength(1));
    });
  });

  group('offline', () {
    testWidgets('Now dims and disables its controls while the display is unreachable, and recovers by itself', (tester) async {
      _phone(tester);
      final api = FakeApi();
      await tester.pumpWidget(testApp(api));
      await tester.pumpAndSettle();
      final container = ProviderScope.containerOf(tester.element(find.byType(NavigationBar)));
      expect(find.byKey(const Key('stale-note')), findsNothing);

      api.failHealth = true;
      await container.read(connectionProvider.notifier).check();
      await tester.pumpAndSettle();
      expect(container.read(connectionProvider).isOffline, isTrue);

      // A calm panel, never an exception string.
      expect(find.byKey(const Key('offline-panel')), findsOneWidget);
      expect(find.text('Display offline'), findsWidgets);
      expect(find.textContaining('statusCode'), findsNothing);
      expect(find.textContaining('HTTP request failed'), findsNothing);
      expect(find.byKey(const Key('stale-note')), findsOneWidget);

      await tester.tap(find.byKey(const Key('power-tile')), warnIfMissed: false);
      await tester.pumpAndSettle();
      expect(api.calls, isNot(contains('power:false')));
      expect(tester.widget<Slider>(find.byType(Slider)).onChanged, isNull);
      expect(tester.widget<TextButton>(find.byKey(const Key('transition-picker'))).onPressed, isNull);

      api.failHealth = false;
      await container.read(connectionProvider.notifier).check();
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('stale-note')), findsNothing);
      expect(find.byKey(const Key('offline-panel')), findsNothing);
      expect(tester.widget<Slider>(find.byType(Slider)).onChanged, isNotNull);
      await tester.tap(find.byKey(const Key('power-tile')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('power:false'));
    });

    testWidgets('the Apps tab disables Show buttons while offline', (tester) async {
      _phone(tester);
      final api = FakeApi();
      await tester.pumpWidget(testApp(api));
      await tester.pumpAndSettle();
      final container = ProviderScope.containerOf(tester.element(find.byType(NavigationBar)));
      await tester.tap(find.text('Apps'));
      await tester.pumpAndSettle();
      expect(tester.widget<ButtonStyleButton>(find.byKey(const Key('activate-weather'))).onPressed, isNotNull);

      api.failHealth = true;
      await container.read(connectionProvider.notifier).check();
      await tester.pumpAndSettle();
      expect(tester.widget<ButtonStyleButton>(find.byKey(const Key('activate-weather'))).onPressed, isNull);
    });
  });

  testWidgets('the app settings sheet keeps its live preview pinned while the settings scroll', (tester) async {
    _phone(tester);
    final api = FakeApi();
    await tester.pumpWidget(testApp(api));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Apps'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('app-tile-clock')));
    await tester.pumpAndSettle();
    expect(tester.getSize(find.byKey(const Key('sheet-preview'))).height, lessThanOrEqualTo(100));
    // The first drag may grow the sheet; once it is full height the preview must stay put while the list scrolls.
    await tester.drag(find.byType(CustomScrollView).last, const Offset(0, -600));
    await tester.pumpAndSettle();
    final before = tester.getTopLeft(find.byKey(const Key('sheet-preview')));
    await tester.drag(find.byType(CustomScrollView).last, const Offset(0, -300));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('sheet-preview')), findsOneWidget);
    expect(tester.getTopLeft(find.byKey(const Key('sheet-preview'))).dy, before.dy);
  });

  testWidgets('Find display in Settings forgets the saved display and reopens setup', (tester) async {
    _phone(tester);
    await tester.pumpWidget(testApp(FakeApi()));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Settings'));
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(const Key('find-display')));
    await tester.tap(find.byKey(const Key('find-display')));
    await tester.pumpAndSettle();

    expect(find.byType(OnboardingPage), findsOneWidget);
    expect(find.byType(NavigationBar), findsNothing);
  });
}
