import 'dart:typed_data';

/// One decoded frame from `/ws/preview`.
class PreviewFrame {
  const PreviewFrame(this.width, this.height, this.rgba);

  final int width;
  final int height;

  /// width * height * 4 bytes, alpha always 255.
  final Uint8List rgba;
}

/// Parses a binary preview message: u16 LE width, u16 LE height, then RGB triples.
/// Returns null for malformed messages.
PreviewFrame? parsePreviewFrame(List<int> message) {
  final bytes = message is Uint8List ? message : Uint8List.fromList(message);
  if (bytes.length < 4) return null;
  final width = bytes[0] | (bytes[1] << 8);
  final height = bytes[2] | (bytes[3] << 8);
  final pixels = width * height;
  if (pixels == 0 || bytes.length < 4 + pixels * 3) return null;

  final rgba = Uint8List(pixels * 4);
  for (int i = 0, src = 4, dst = 0; i < pixels; i++) {
    rgba[dst++] = bytes[src++];
    rgba[dst++] = bytes[src++];
    rgba[dst++] = bytes[src++];
    rgba[dst++] = 255;
  }
  return PreviewFrame(width, height, rgba);
}
