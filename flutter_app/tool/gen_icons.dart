// Generates launcher icons: `dart run tool/gen_icons.dart` (from flutter_app/).
// Needs the `image` dev dependency. Writes Android mipmaps and the iOS AppIcon set.
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:image/image.dart' as img;

const bg = [8, 10, 14];
const cyan = [34, 229, 255];
const amber = [255, 184, 77];

/// Draws the LED grid. [safe] is the fraction of the canvas width the grid
/// occupies (smaller for adaptive-icon foregrounds, whose outer area is cropped).
img.Image render(int size, {required bool background, double safe = 0.62, bool mono = false}) {
  // Supersample 4x for smooth glows, then downscale.
  final s = size * 4;
  final im = img.Image(width: s, height: s, numChannels: 4);
  if (background) {
    img.fill(im, color: img.ColorRgba8(bg[0], bg[1], bg[2], 255));
  }
  const cols = 8, rows = 4;
  final gridW = s * safe;
  final pitch = gridW / cols;
  final x0 = (s - gridW) / 2 + pitch / 2;
  final y0 = (s - pitch * rows) / 2 + pitch / 2;
  // 1 = cyan, 2 = amber, 0 = dim. A stylised scrolling wave.
  const pattern = [
    [0, 1, 1, 0, 0, 1, 1, 0],
    [1, 1, 0, 0, 1, 1, 0, 2],
    [1, 0, 0, 1, 1, 0, 2, 2],
    [0, 0, 1, 1, 0, 0, 2, 0],
  ];
  final r = pitch * 0.36;
  for (var j = 0; j < rows; j++) {
    for (var i = 0; i < cols; i++) {
      final v = pattern[j][i];
      final cx = x0 + i * pitch, cy = y0 + j * pitch;
      final col = mono ? [255, 255, 255] : (v == 2 ? amber : cyan);
      if (v == 0) {
        if (!mono) _dot(im, cx, cy, r, [255, 255, 255], 0.10, 0);
        continue;
      }
      if (!mono) _dot(im, cx, cy, r, col, 0.0, r);
      _dot(im, cx, cy, r, col, 1.0, 0);
    }
  }
  return img.copyResize(im, width: size, height: size, interpolation: img.Interpolation.average);
}

void _dot(img.Image im, double cx, double cy, double r, List<int> c, double alpha, double glow) {
  final ext = (r + glow * 1.4).ceil() + 2;
  for (var y = (cy - ext).floor(); y <= (cy + ext).ceil(); y++) {
    for (var x = (cx - ext).floor(); x <= (cx + ext).ceil(); x++) {
      if (x < 0 || y < 0 || x >= im.width || y >= im.height) continue;
      final d = math.sqrt(math.pow(x - cx, 2) + math.pow(y - cy, 2));
      double a;
      if (glow > 0) {
        a = d <= r ? 0.0 : 0.32 * math.exp(-(d - r) / (glow * 0.55));
      } else {
        a = alpha * (d <= r - 1 ? 1.0 : d >= r + 1 ? 0.0 : (r + 1 - d) / 2);
      }
      if (a <= 0) continue;
      final p = im.getPixel(x, y);
      final pa = p.a / 255;
      final oa = a + pa * (1 - a);
      if (oa <= 0) continue;
      final nr = (c[0] * a + p.r * pa * (1 - a)) / oa;
      final ng = (c[1] * a + p.g * pa * (1 - a)) / oa;
      final nb = (c[2] * a + p.b * pa * (1 - a)) / oa;
      im.setPixelRgba(x, y, nr.round(), ng.round(), nb.round(), (oa * 255).round());
    }
  }
}

void save(String path, img.Image im) {
  File(path).createSync(recursive: true);
  File(path).writeAsBytesSync(img.encodePng(im));
}

void main() {
  const res = 'android/app/src/main/res';
  const dens = {'mdpi': 1.0, 'hdpi': 1.5, 'xhdpi': 2.0, 'xxhdpi': 3.0, 'xxxhdpi': 4.0};
  dens.forEach((d, f) {
    final dir = '$res/mipmap-$d';
    final fg = (108 * f).round();
    save('$dir/ic_launcher_foreground.png', render(fg, background: false, safe: 0.58));
    save('$dir/ic_launcher_monochrome.png', render(fg, background: false, safe: 0.58, mono: true));
    final solid = img.Image(width: fg, height: fg, numChannels: 4);
    img.fill(solid, color: img.ColorRgba8(bg[0], bg[1], bg[2], 255));
    save('$dir/ic_launcher_background.png', solid);
    save('$dir/ic_launcher.png', render((48 * f).round(), background: true, safe: 0.86));
  });

  const iosDir = 'ios/Runner/Assets.xcassets/AppIcon.appiconset';
  final contents = jsonDecode(File('$iosDir/Contents.json').readAsStringSync());
  for (final e in contents['images']) {
    final name = e['filename'] as String?;
    if (name == null) continue;
    final base = double.parse((e['size'] as String).split('x').first);
    final scale = double.parse((e['scale'] as String).replaceAll('x', ''));
    final px = (base * scale).round();
    // iOS icons must be opaque and square; the OS applies the mask.
    final im = render(px, background: true, safe: 0.86);
    final flat = img.Image(width: px, height: px, numChannels: 3);
    img.fill(flat, color: img.ColorRgb8(bg[0], bg[1], bg[2]));
    img.compositeImage(flat, im);
    save('$iosDir/$name', flat);
  }
  save('tool/icon_preview.png', render(512, background: true, safe: 0.86));
}

