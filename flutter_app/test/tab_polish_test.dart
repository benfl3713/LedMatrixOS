import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/core/connection.dart';
import 'package:led_matrix_controller/core/theme.dart';
import 'package:led_matrix_controller/core/widgets.dart';

import 'fake_api.dart';

class _Offline extends ConnectionNotifier {
  @override
  DeviceConnection build() => const DeviceConnection(status: ConnectionStatus.offline, failures: 2);
}

Widget _host(Widget child, {bool offline = false, Brightness brightness = Brightness.dark}) => ProviderScope(
      overrides: [if (offline) connectionProvider.overrideWith(_Offline.new)],
      child: MaterialApp(theme: buildTheme(brightness), home: Scaffold(body: child)),
    );

Future<void> _openAppsSub(WidgetTester tester, FakeApi api, String entryKey) async {
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps'));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(Key(entryKey)));
  await tester.pumpAndSettle();
}

void main() {
  group('ErrorView', () {
    testWidgets('online: short message with the detail kept small', (tester) async {
      var retried = 0;
      await tester.pumpWidget(_host(ErrorView(error: 'boom', onRetry: () => retried++)));
      expect(find.text("Couldn't load this"), findsOneWidget);
      expect(find.text('boom'), findsOneWidget);
      await tester.tap(find.byKey(const Key('error-retry')));
      expect(retried, 1);
    });

    testWidgets('offline: calm empty state without the raw exception text', (tester) async {
      await tester.pumpWidget(_host(const ErrorView(error: 'SocketException: boom'), offline: true));
      expect(find.text('Display offline'), findsOneWidget);
      expect(find.textContaining('SocketException'), findsNothing);
      expect(find.byKey(const Key('error-retry')), findsOneWidget);
    });

    testWidgets('renders in the light theme', (tester) async {
      await tester.pumpWidget(_host(const ErrorView(error: 'x'), brightness: Brightness.light));
      expect(tester.takeException(), isNull);
      expect(find.byType(ErrorView), findsOneWidget);
    });
  });

  testWidgets('page titles use the large title style', (tester) async {
    await tester.pumpWidget(MaterialApp(
      theme: buildTheme(Brightness.dark),
      home: Scaffold(appBar: AppBar(title: const Text('Schedule'))),
    ));
    final style = DefaultTextStyle.of(tester.element(find.text('Schedule'))).style;
    expect(style.fontSize, 28);
  });

  group('empty states', () {
    testWidgets('screens: invites the first screen', (tester) async {
      final api = FakeApi();
      api.screens.clear();
      await _openAppsSub(tester, api, 'screens-entry');
      expect(find.text('Design your own screen'), findsOneWidget);
      await tester.tap(find.byKey(const Key('empty-new-screen')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('new-screen-name')), findsOneWidget);
      // The sheet is on the root navigator, so it sits above the bottom navigation bar.
      expect(find.byType(NavigationBar).hitTestable(), findsNothing);
    });

    testWidgets('media: invites the first upload and Play is disabled', (tester) async {
      final api = FakeApi();
      api.media = [];
      await _openAppsSub(tester, api, 'media-entry');
      expect(find.text('No media yet'), findsOneWidget);
      expect(find.byKey(const Key('media-empty-upload')), findsOneWidget);
      expect(tester.widget<FilledButton>(find.byKey(const Key('media-play-all'))).onPressed, isNull);
    });
  });
}
