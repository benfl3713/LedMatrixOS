/// One item of the device's media library.
class MediaItem {
  const MediaItem({
    required this.id,
    required this.name,
    required this.kind,
    this.width = 0,
    this.height = 0,
    this.frames = 1,
    this.durationMs = 0,
  });

  final String id;
  final String name;

  /// `image`, `gif` or `video`.
  final String kind;
  final int width;
  final int height;
  final int frames;
  final int durationMs;

  bool get isGif => kind == 'gif';
  bool get isVideo => kind == 'video';

  factory MediaItem.fromJson(Map<String, dynamic> j) => MediaItem(
        id: '${j['id']}',
        name: (j['name'] as String?) ?? '${j['id']}',
        kind: (j['kind'] as String?) ?? 'image',
        width: (j['width'] as num?)?.toInt() ?? 0,
        height: (j['height'] as num?)?.toInt() ?? 0,
        frames: (j['frames'] as num?)?.toInt() ?? 1,
        durationMs: (j['durationMs'] as num?)?.toInt() ?? 0,
      );
}

/// What the device's media library accepts.
class MediaCapabilities {
  const MediaCapabilities({this.video = false, this.maxBytes = 25 * 1024 * 1024});

  final bool video;
  final int maxBytes;

  factory MediaCapabilities.fromJson(Map<String, dynamic> j) => MediaCapabilities(
        video: j['video'] == true,
        maxBytes: (j['maxBytes'] as num?)?.toInt() ?? 25 * 1024 * 1024,
      );
}

/// "1.4 MB" style size.
String formatBytes(int bytes) {
  if (bytes < 1024) return '$bytes B';
  if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}
