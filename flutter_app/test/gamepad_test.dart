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

Future<RecordingInput> _openPad(WidgetTester tester) async {
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final input = RecordingInput();
  await tester.pumpWidget(ProviderScope(
    retry: (_, __) => null,
    overrides: [
      apiProvider.overrideWithValue(FakeApi()),
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
}
