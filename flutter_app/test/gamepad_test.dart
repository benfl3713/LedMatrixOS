import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/gamepad/gamepad_provider.dart';
import 'package:led_matrix_controller/features/gamepad/input_service.dart';
import 'package:led_matrix_controller/main.dart';

import 'fake_api.dart';

class RecordingInput extends InputService {
  RecordingInput() : super(baseUrl: 'http://localhost:5005');

  final List<String> events = [];

  @override
  Future<void> start() async {}

  @override
  Future<bool> send(int player, PadButton button, PadState state) async {
    events.add('$player:${button.name}:${state.name}');
    return true;
  }
}

Future<RecordingInput> _openPad(WidgetTester tester, {Size size = const Size(800, 2400), FakeApi? api}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final input = RecordingInput();
  await tester.pumpWidget(ProviderScope(
    retry: (_, __) => null,
    overrides: [
      deviceConfigProvider.overrideWith(() => FakeDeviceConfig('http://fake')),
      apiProvider.overrideWithValue(api ?? FakeApi()),
      pollIntervalProvider.overrideWithValue(null),
      previewFeedProvider.overrideWith((ref) => Stream.value(const PreviewUpdate(connected: true))),
      inputServiceProvider.overrideWithValue(input),
    ],
    child: const LedMatrixApp(),
  ));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps'));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('gamepad-entry')));
  await tester.pumpAndSettle();
  return input;
}

void main() {
  test('payload matches the backend contract', () {
    expect(InputService.payload(2, PadButton.start, PadState.press), {'player': 2, 'button': 'start', 'state': 'press'});
  });

  testWidgets('entry opens the gamepad', (tester) async {
    await _openPad(tester);
    expect(find.byKey(const Key('pad-up')), findsOneWidget);
    expect(find.byKey(const Key('pad-a')), findsOneWidget);
    expect(find.byKey(const Key('pad-select')), findsOneWidget);
  });

  testWidgets('sends down on touch and up on release', (tester) async {
    final input = await _openPad(tester);
    final g = await tester.startGesture(tester.getCenter(find.byKey(const Key('pad-a'))));
    await tester.pump();
    expect(input.events, ['0:a:down']);
    await g.up();
    await tester.pump();
    expect(input.events, ['0:a:down', '0:a:up']);
  });

  testWidgets('multi-touch holds d-pad and A together; player selector changes player', (tester) async {
    final input = await _openPad(tester);
    await tester.tap(find.text('P3'));
    await tester.pump();
    final left = await tester.startGesture(tester.getCenter(find.byKey(const Key('pad-left'))), pointer: 1);
    final a = await tester.startGesture(tester.getCenter(find.byKey(const Key('pad-a'))), pointer: 2);
    await tester.pump();
    await left.up();
    await a.up();
    await tester.pump();
    expect(input.events, ['2:left:down', '2:a:down', '2:left:up', '2:a:up']);
  });

  testWidgets('controls are thumb sized', (tester) async {
    await _openPad(tester, size: const Size(390, 844));
    expect(tester.getSize(find.byKey(const Key('pad-up'))).width, greaterThanOrEqualTo(56));
    expect(tester.getSize(find.byKey(const Key('pad-a'))).width, greaterThanOrEqualTo(72));
    final dpadWidth = tester.getTopRight(find.byKey(const Key('pad-right'))).dx - tester.getTopLeft(find.byKey(const Key('pad-left'))).dx;
    expect(dpadWidth, greaterThanOrEqualTo(160));
  });

  testWidgets('game chips activate the app and highlight the active one', (tester) async {
    final api = FakeApi();
    await _openPad(tester, size: const Size(390, 844), api: api);
    await tester.tap(find.byKey(const Key('game-snake')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('activate:snake'));
    expect(tester.widget<ChoiceChip>(find.byKey(const Key('game-snake'))).selected, isTrue);
    expect(tester.widget<ChoiceChip>(find.byKey(const Key('game-pong'))).selected, isFalse);
  });

  for (final size in const [Size(360, 740), Size(740, 360)]) {
    testWidgets('no overflow at ${size.width.toInt()}x${size.height.toInt()}', (tester) async {
      final input = await _openPad(tester, size: size);
      expect(tester.takeException(), isNull);
      for (final k in ['pad-up', 'pad-a', 'pad-start', 'game-tetris']) {
        final r = tester.getRect(find.byKey(Key(k)));
        expect(r.left, greaterThanOrEqualTo(0), reason: k);
        expect(r.right, lessThanOrEqualTo(size.width), reason: k);
        expect(r.bottom, lessThanOrEqualTo(size.height), reason: k);
      }
      final g = await tester.startGesture(tester.getCenter(find.byKey(const Key('pad-up'))));
      await g.up();
      await tester.pump();
      expect(input.events, ['0:up:down', '0:up:up']);
    });
  }
}
