import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:led_matrix_controller/features/now/display_page.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fake_api.dart';

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  testWidgets('fullscreen button opens the display, snapped to whole pixels per LED, and closes again', (tester) async {
    tester.view.physicalSize = const Size(2532, 1170); // iPhone 14 landscape
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(testApp(FakeApi()));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('preview-fullscreen')));
    await tester.pumpAndSettle();

    expect(find.byType(DisplayPage), findsOneWidget);
    expect(find.byType(NavigationBar), findsNothing);

    // 2532 / 256 = 9.89 physical px per LED, floored to 9: 2304 x 576 physical, 768 x 192 logical.
    expect(tester.getSize(find.byKey(const Key('display-matrix'))), const Size(768, 192));

    // Controls fade out, and a tap brings them back.
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
    expect(tester.widget<AnimatedOpacity>(find.byType(AnimatedOpacity)).opacity, 0);
    await tester.tapAt(const Offset(100, 100));
    await tester.pumpAndSettle();
    expect(tester.widget<AnimatedOpacity>(find.byType(AnimatedOpacity)).opacity, 1);

    await tester.tap(find.byKey(const Key('display-led-dots')));
    await tester.pump();
    final prefs = await SharedPreferences.getInstance();
    expect(prefs.getBool('display_led_dots'), false);

    await tester.tap(find.byKey(const Key('display-close')));
    await tester.pumpAndSettle();
    expect(find.byType(DisplayPage), findsNothing);
    expect(find.byType(NavigationBar), findsOneWidget);
  });

  testWidgets('close falls back to Now when the display was opened directly', (tester) async {
    await tester.pumpWidget(testApp(FakeApi()));
    await tester.pumpAndSettle();
    GoRouter.of(tester.element(find.byType(NavigationBar))).go('/display');
    await tester.pumpAndSettle();
    expect(find.byType(DisplayPage), findsOneWidget);

    await tester.tap(find.byKey(const Key('display-close')));
    await tester.pumpAndSettle();
    expect(find.byType(DisplayPage), findsNothing);
    expect(find.byType(NavigationBar), findsOneWidget);
  });
}
