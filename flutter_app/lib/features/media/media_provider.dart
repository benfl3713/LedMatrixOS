import 'package:file_picker/file_picker.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/media_models.dart';
import '../../api/result.dart';
import '../../core/providers.dart';

/// A file the user chose, readable as a stream so large videos are never held in memory.
class PickedMedia {
  const PickedMedia({required this.name, required this.length, required this.open});

  final String name;
  final int length;
  final Stream<List<int>> Function() open;
}

const imageExtensions = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp'];
const videoExtensions = ['mp4', 'mov', 'm4v', 'webm', 'mkv', 'avi'];

/// Opens the system file chooser. Tests override [mediaPickerProvider].
abstract class MediaPicker {
  /// Null when the user cancels. Video extensions are offered only when [allowVideo].
  Future<PickedMedia?> pick({required bool allowVideo});
}

class FilePickerMediaPicker implements MediaPicker {
  const FilePickerMediaPicker();

  @override
  Future<PickedMedia?> pick({required bool allowVideo}) async {
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: [...imageExtensions, if (allowVideo) ...videoExtensions],
    );
    if (file == null) return null;
    final length = await file.length() ?? 0;
    return PickedMedia(name: file.name, length: length, open: file.readAsByteStream);
  }
}

final mediaPickerProvider = Provider<MediaPicker>((ref) => const FilePickerMediaPicker());

/// What the server accepts. Falls back to images only if the endpoint is missing (older firmware).
final mediaCapabilitiesProvider = FutureProvider<MediaCapabilities>((ref) async {
  final result = await ref.watch(apiProvider).getMediaCapabilities();
  return result.valueOrNull ?? const MediaCapabilities();
});

/// A friendly sentence for a failed upload.
String friendlyUploadError(ApiError e, {int? maxBytes}) {
  switch (e.statusCode) {
    case 400:
      return 'That file is not a supported picture, GIF or video, or it is corrupt.';
    case 413:
      return maxBytes == null
          ? 'That file is too large for the device.'
          : 'That file is too large. The limit is ${formatBytes(maxBytes)}.';
    case 503:
      return 'This device cannot play video (ffmpeg is not installed). Pick a picture or GIF instead.';
    case 507:
      return 'The media library is full. Delete something and try again.';
  }
  return switch (e.kind) {
    ApiErrorKind.network => 'Could not reach the device to upload.',
    ApiErrorKind.timeout => 'The upload timed out.',
    _ => e.message,
  };
}

/// Upload in flight (null progress = starting).
class UploadState {
  const UploadState({required this.name, this.progress = 0});
  final String name;
  final double progress;
}

class UploadNotifier extends Notifier<UploadState?> {
  @override
  UploadState? build() => null;

  void start(String name) => state = UploadState(name: name);
  void progress(double p) {
    final s = state;
    if (s != null) state = UploadState(name: s.name, progress: p);
  }

  void finish() => state = null;
}

final uploadProvider = NotifierProvider<UploadNotifier, UploadState?>(UploadNotifier.new);

class MediaListNotifier extends AsyncNotifier<List<MediaItem>> {
  @override
  Future<List<MediaItem>> build() async => (await ref.watch(apiProvider).listMedia()).getOrThrow();

  Future<void> refresh() async {
    final result = await ref.read(apiProvider).listMedia();
    if (!ref.mounted) return;
    result.when(
      ok: (v) => state = AsyncData(v),
      err: (e) => ref.read(errorBusProvider.notifier).report(e),
    );
  }

  /// Uploads [file]; returns an error message to show, or null on success.
  Future<String?> upload(PickedMedia file, MediaCapabilities caps) async {
    if (file.length > caps.maxBytes) {
      return '${file.name} is ${formatBytes(file.length)}, over the ${formatBytes(caps.maxBytes)} limit.';
    }
    final uploads = ref.read(uploadProvider.notifier);
    uploads.start(file.name);
    final result = await ref.read(apiProvider).uploadMedia(
          filename: file.name,
          length: file.length,
          data: file.open(),
          onProgress: uploads.progress,
        );
    if (!ref.mounted) return null;
    uploads.finish();
    return result.when(
      ok: (item) {
        final current = state.value ?? const <MediaItem>[];
        state = AsyncData([...current.where((m) => m.id != item.id), item]);
        return null;
      },
      err: (e) => friendlyUploadError(e, maxBytes: caps.maxBytes),
    );
  }

  Future<bool> delete(String id) async {
    final result = await ref.read(apiProvider).deleteMedia(id);
    if (!ref.mounted) return false;
    return result.when(
      ok: (_) {
        state = AsyncData([for (final m in state.value ?? const <MediaItem>[]) if (m.id != id) m]);
        return true;
      },
      err: (e) {
        ref.read(errorBusProvider.notifier).report(e);
        return false;
      },
    );
  }

  /// Activates the media app with its current settings (whatever items it already has).
  Future<bool> playAll() => ref.read(appListProvider.notifier).activate('media');

  /// Restricts the media app to [id] (its `items` setting), then activates it.
  Future<bool> playOne(String id) async {
    final result = await ref.read(apiProvider).updateAppSettings('media', {'items': id});
    if (!ref.mounted) return false;
    final error = result.errorOrNull;
    if (error != null) {
      ref.read(errorBusProvider.notifier).report(error);
      return false;
    }
    return playAll();
  }
}

final mediaListProvider = AsyncNotifierProvider<MediaListNotifier, List<MediaItem>>(MediaListNotifier.new);
