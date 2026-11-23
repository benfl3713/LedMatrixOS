import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/core/theme.dart';
import 'package:led_matrix_controller/core/widgets.dart';

import 'fake_api.dart';

Future<void> _open(WidgetTester tester, FakeApi api) async {
  tester.view.physicalSize = const Size(800, 2000);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('the app uses the dark night-display theme by default', (tester) async {
    await _open(tester, FakeApi());
    final theme = Theme.of(tester.element(find.byType(Scaffold).first));
    expect(theme.brightness, Brightness.dark);
    expect(theme.colorScheme.primary, const Color(0xFF22E5FF));
  });

  testWidgets('tapping the power tile (not just the switch) toggles power', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.byKey(const Key('power-tile')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('power:false'));
  });

  testWidgets('transition picker opens a sheet and selects', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.byKey(const Key('transition-picker')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Slide'));
    await tester.pumpAndSettle();
    expect(api.calls.any((c) => c.contains('slide')), isTrue);
  });

  testWidgets('loading placeholders are skeletons, not spinners', (tester) async {
    await tester.pumpWidget(MaterialApp(theme: buildTheme(Brightness.dark), home: const Scaffold(body: Skeleton())));
    expect(find.byType(Skeleton), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsNothing);
  });
}
