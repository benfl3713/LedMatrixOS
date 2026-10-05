import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:led_matrix_controller/api/media_models.dart';
import 'package:led_matrix_controller/api/result.dart';
import 'package:led_matrix_controller/core/providers.dart';
import 'package:led_matrix_controller/features/media/media_provider.dart';
import 'package:led_matrix_controller/main.dart';

import 'fake_api.dart';

class FakePicker implements MediaPicker {
  FakePicker(this.file);

  PickedMedia? file;
  bool? allowedVideo;

  @override
  Future<PickedMedia?> pick({required bool allowVideo}) async {
    allowedVideo = allowVideo;
    return file;
  }
}

PickedMedia _file(String name, int length) =>
    PickedMedia(name: name, length: length, open: () => Stream.value(List.filled(length, 1)));

Future<void> _open(WidgetTester tester, FakeApi api, {FakePicker? picker}) async {
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(ProviderScope(
    retry: (_, __) => null,
    overrides: [
      apiProvider.overrideWithValue(api),
      pollIntervalProvider.overrideWithValue(null),
      previewFeedProvider.overrideWith((ref) => Stream.value(const PreviewUpdate(connected: true))),
      if (picker != null) mediaPickerProvider.overrideWithValue(picker),
    ],
    child: const LedMatrixApp(),
  ));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Apps'));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('media-entry')));
  await tester.pumpAndSettle();
}

void main() {
  test('friendly upload errors', () {
    String msg(int code) => friendlyUploadError(ApiError(ApiErrorKind.http, 'x', statusCode: code), maxBytes: 5 * 1024 * 1024);
    expect(msg(400), contains('not a supported'));
    expect(msg(413), contains('5.0 MB'));
    expect(msg(503), contains('ffmpeg'));
    expect(msg(507), contains('full'));
  });

  testWidgets('gallery shows items with kind badges', (tester) async {
    await _open(tester, FakeApi());
    expect(find.byKey(const Key('media-tile-cat')), findsOneWidget);
    expect(find.byKey(const Key('media-tile-dance')), findsOneWidget);
    expect(find.text('Cat'), findsOneWidget);
    expect(find.text('256x64'), findsOneWidget);
    expect(find.text('GIF'), findsOneWidget);
    expect(find.byKey(const Key('media-video-hint')), findsNothing);
  });

  testWidgets('empty state', (tester) async {
    await _open(tester, FakeApi()..media = []);
    expect(find.text('No media yet'), findsOneWidget);
    expect(find.byKey(const Key('media-play-all')), findsOneWidget);
  });

  testWidgets('video hint shows when the device has no ffmpeg', (tester) async {
    final api = FakeApi()..mediaCaps = const MediaCapabilities(video: false, maxBytes: 1024 * 1024);
    final picker = FakePicker(null);
    await _open(tester, api, picker: picker);
    expect(find.byKey(const Key('media-video-hint')), findsOneWidget);
    await tester.tap(find.byKey(const Key('media-upload')));
    await tester.pumpAndSettle();
    expect(picker.allowedVideo, isFalse);
  });

  testWidgets('delete asks for confirmation', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.byKey(const Key('media-tile-cat')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('media-delete')));
    await tester.pumpAndSettle();
    expect(find.text('Delete this item?'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(api.calls.where((c) => c.startsWith('deleteMedia')), isEmpty);

    await tester.tap(find.byKey(const Key('media-tile-cat')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('media-delete')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('media-delete-confirm')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('deleteMedia:cat'));
    expect(find.byKey(const Key('media-tile-cat')), findsNothing);
  });

  testWidgets('upload adds the item', (tester) async {
    final api = FakeApi();
    await _open(tester, api, picker: FakePicker(_file('sun.png', 100)));
    await tester.tap(find.byKey(const Key('media-upload')));
    await tester.pumpAndSettle();
    expect(api.uploaded, ['sun.png']);
    expect(find.byKey(const Key('media-tile-new2')), findsOneWidget);
  });

  testWidgets('server errors are shown as friendly messages', (tester) async {
    final api = FakeApi()..uploadError = const ApiError(ApiErrorKind.http, 'HTTP 507', statusCode: 507);
    await _open(tester, api, picker: FakePicker(_file('big.gif', 100)));
    await tester.tap(find.byKey(const Key('media-upload')));
    await tester.pumpAndSettle();
    expect(find.textContaining('library is full'), findsOneWidget);
  });

  testWidgets('oversize files are rejected before upload', (tester) async {
    final api = FakeApi();
    await _open(tester, api, picker: FakePicker(_file('huge.gif', 2 * 1024 * 1024)));
    await tester.tap(find.byKey(const Key('media-upload')));
    await tester.pumpAndSettle();
    expect(find.textContaining('over the 1.0 MB limit'), findsOneWidget);
    expect(api.calls.where((c) => c.startsWith('upload')), isEmpty);
  });

  testWidgets('play actions activate the media app', (tester) async {
    final api = FakeApi();
    await _open(tester, api);
    await tester.tap(find.byKey(const Key('media-play-all')));
    await tester.pumpAndSettle();
    expect(api.calls, contains('activate:media'));
    expect(api.settingUpdates, isEmpty);

    await tester.tap(find.byKey(const Key('media-tile-dance')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('media-play-one')));
    await tester.pumpAndSettle();
    expect(api.settingUpdates.last, {'items': 'dance'});
    expect(api.active, 'media');
  });
}
