import 'package:flutter/material.dart';

/// Parses "#RRGGBB" / "RRGGBB" / "#RGB". Returns null when it is not a colour.
Color? parseHexColor(Object? value) {
  if (value is! String) return null;
  var s = value.trim();
  if (s.startsWith('#')) s = s.substring(1);
  if (s.length == 3) s = s.split('').map((c) => '$c$c').join();
  if (s.length != 6) return null;
  final v = int.tryParse(s, radix: 16);
  return v == null ? null : Color(0xFF000000 | v);
}

/// "#RRGGBB" (uppercase), the format the server expects.
String colorToHex(Color c) {
  String two(double channel) => (channel * 255).round().clamp(0, 255).toRadixString(16).padLeft(2, '0');
  return '#${two(c.r)}${two(c.g)}${two(c.b)}'.toUpperCase();
}

/// Small HSV colour picker dialog. Pops with the chosen colour, or null when cancelled.
Future<Color?> showColorPicker(BuildContext context, {required Color initial, String title = 'Pick a colour'}) =>
    showDialog<Color>(context: context, builder: (_) => ColorPickerDialog(initial: initial, title: title));

class ColorPickerDialog extends StatefulWidget {
  const ColorPickerDialog({super.key, required this.initial, required this.title});

  final Color initial;
  final String title;

  @override
  State<ColorPickerDialog> createState() => _ColorPickerDialogState();
}

class _ColorPickerDialogState extends State<ColorPickerDialog> {
  static const presets = <Color>[
    Color(0xFFFF0000), Color(0xFFFF7A00), Color(0xFFFFD500), Color(0xFF00C853),
    Color(0xFF00E5FF), Color(0xFF2962FF), Color(0xFFAA00FF), Color(0xFFFF4081),
    Color(0xFFFFFFFF), Color(0xFF9E9E9E), Color(0xFF000000),
  ];

  late HSVColor _hsv = HSVColor.fromColor(widget.initial);
  late final TextEditingController _hex = TextEditingController(text: colorToHex(widget.initial));

  Color get _color => _hsv.toColor();

  void _set(HSVColor hsv) {
    setState(() => _hsv = hsv);
    _hex.text = colorToHex(hsv.toColor());
  }

  @override
  void dispose() {
    _hex.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final hueColor = HSVColor.fromAHSV(1, _hsv.hue, 1, 1).toColor();
    return AlertDialog(
      title: Text(widget.title),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              height: 56,
              decoration: BoxDecoration(
                color: _color,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: Theme.of(context).colorScheme.outlineVariant),
              ),
            ),
            const SizedBox(height: 12),
            _gradientSlider(
              key: const Key('hue-slider'),
              label: 'Hue',
              value: _hsv.hue,
              max: 360,
              colors: [for (var h = 0; h <= 360; h += 60) HSVColor.fromAHSV(1, h.toDouble(), 1, 1).toColor()],
              onChanged: (v) => _set(_hsv.withHue(v)),
            ),
            _gradientSlider(
              key: const Key('saturation-slider'),
              label: 'Saturation',
              value: _hsv.saturation,
              max: 1,
              colors: [HSVColor.fromAHSV(1, _hsv.hue, 0, _hsv.value).toColor(), hueColor],
              onChanged: (v) => _set(_hsv.withSaturation(v)),
            ),
            _gradientSlider(
              key: const Key('value-slider'),
              label: 'Brightness',
              value: _hsv.value,
              max: 1,
              colors: [Colors.black, HSVColor.fromAHSV(1, _hsv.hue, _hsv.saturation, 1).toColor()],
              onChanged: (v) => _set(_hsv.withValue(v)),
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final c in presets)
                  GestureDetector(
                    onTap: () => _set(HSVColor.fromColor(c)),
                    child: Container(
                      width: 32,
                      height: 32,
                      decoration: BoxDecoration(
                        color: c,
                        shape: BoxShape.circle,
                        border: Border.all(color: Theme.of(context).colorScheme.outlineVariant),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('hex-field'),
              controller: _hex,
              decoration: const InputDecoration(labelText: 'Hex', border: OutlineInputBorder(), prefixText: ''),
              onChanged: (text) {
                final c = parseHexColor(text);
                if (c != null) setState(() => _hsv = HSVColor.fromColor(c));
              },
            ),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.pop(context, _color), child: const Text('Select')),
      ],
    );
  }

  Widget _gradientSlider({
    required Key key,
    required String label,
    required double value,
    required double max,
    required List<Color> colors,
    required ValueChanged<double> onChanged,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: Theme.of(context).textTheme.labelMedium),
        Stack(
          alignment: Alignment.center,
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              child: Container(
                height: 10,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(5),
                  gradient: LinearGradient(colors: colors),
                ),
              ),
            ),
            SliderTheme(
              data: SliderTheme.of(context).copyWith(
                trackHeight: 10,
                activeTrackColor: Colors.transparent,
                inactiveTrackColor: Colors.transparent,
              ),
              child: Slider(key: key, value: value.clamp(0, max), min: 0, max: max, onChanged: onChanged),
            ),
          ],
        ),
      ],
    );
  }
}
