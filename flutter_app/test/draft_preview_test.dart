import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/features/screens/draft_preview.dart';

import 'fake_api.dart';

Future<void> _openEditor(WidgetTester tester, FakeApi api) async {
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(testApp(api));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps'));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('screens-entry')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('screen-tile-demo')));
  await tester.pumpAndSettle();
}

int _previewCalls(FakeApi api) => api.calls.where((c) => c.startsWith('previewScreen')).length;

void main() {
  testWidgets('shows the rendered draft and the unsaved-preview copy', (tester) async {
    final api = FakeApi();
    await _openEditor(tester, api);

    expect(_previewCalls(api), 1);
    final image = tester.widget<Image>(find.byKey(const Key('draft-preview-image')));
    expect(image.filterQuality, FilterQuality.none);
    expect(find.text('Preview of your unsaved screen'), findsOneWidget);
  });

  testWidgets('edits are debounced into a single preview request', (tester) async {
    final api = FakeApi();
    await _openEditor(tester, api);
    expect(_previewCalls(api), 1);

    await tester.enterText(find.byKey(const Key('screen-name')), 'A');
    await tester.pump(const Duration(milliseconds: 200));
    await tester.enterText(find.byKey(const Key('screen-name')), 'AB');
    await tester.pump(const Duration(milliseconds: 200));
    await tester.enterText(find.byKey(const Key('screen-name')), 'ABC');
    // Still inside the debounce window of the last edit: nothing sent yet, spinner shows over the old image.
    await tester.pump(const Duration(milliseconds: 399) - const Duration(milliseconds: 100));
    expect(_previewCalls(api), 1);
    expect(find.byKey(const Key('draft-preview-spinner')), findsOneWidget);
    expect(find.byKey(const Key('draft-preview-image')), findsOneWidget);

    await tester.pump(draftPreviewDebounce);
    await tester.pumpAndSettle();
    expect(_previewCalls(api), 2);
    expect(api.previews.last.name, 'ABC');
    expect(find.byKey(const Key('draft-preview-spinner')), findsNothing);
  });

  testWidgets('a 400 replaces the image with the validation errors, then recovers', (tester) async {
    final api = FakeApi();
    await _openEditor(tester, api);

    api.previewErrors = [const FieldError('root.children[0].text', 'text is required')];
    await tester.enterText(find.byKey(const Key('screen-name')), 'Broken');
    await tester.pump(draftPreviewDebounce);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('draft-preview-image')), findsNothing);
    expect(find.byKey(const Key('draft-preview-errors')), findsOneWidget);
    expect(find.textContaining('text is required'), findsOneWidget);

    await tester.enterText(find.byKey(const Key('screen-name')), 'Fixed');
    await tester.pump(draftPreviewDebounce);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('draft-preview-errors')), findsNothing);
    expect(find.byKey(const Key('draft-preview-image')), findsOneWidget);
  });
}
