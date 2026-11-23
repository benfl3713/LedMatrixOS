import 'package:flutter/material.dart';

import '../../core/tokens.dart';
import '../apps/color_picker.dart';

/// Colours that read well on an LED panel.
const swatchPalette = <(String, Color)>[
  ('White', Color(0xFFFFFFFF)),
  ('Black', Color(0xFF000000)),
  ('Red', Color(0xFFFF3C3C)),
  ('Orange', Color(0xFFFF9F1C)),
  ('Yellow', Color(0xFFFFE066)),
  ('Green', Color(0xFF3DDC84)),
  ('Cyan', Color(0xFF22E5FF)),
  ('Teal', Color(0xFF00677A)),
  ('Violet', Color(0xFF9600FF)),
  ('Pink', Color(0xFFFF4D9D)),
];

/// A labelled row of colour swatches plus a "custom" tile that opens the full picker. Replaces raw hex entry.
class ColorSwatchRow extends StatelessWidget {
  const ColorSwatchRow({super.key, required this.label, required this.color, required this.onChanged});

  final String label;
  final Color color;
  final ValueChanged<Color> onChanged;

  bool get _custom => !swatchPalette.any((s) => s.$2.toARGB32() == color.toARGB32());

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final text = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(child: Text(label, style: text.labelLarge)),
            Text(colorToHex(color), style: numericStyle(text.bodySmall?.copyWith(color: scheme.onSurfaceVariant))),
          ],
        ),
        const SizedBox(height: 2),
        Wrap(
          children: [
            for (final (name, c) in swatchPalette)
              _Swatch(
                semanticsLabel: '$label $name',
                color: c,
                selected: c.toARGB32() == color.toARGB32(),
                onTap: () => onChanged(c),
              ),
            _Swatch(
              key: Key('${label.toLowerCase().replaceAll(' ', '-')}-custom'),
              semanticsLabel: '$label custom colour',
              color: _custom ? color : null,
              selected: _custom,
              icon: Icons.colorize_rounded,
              onTap: () async {
                final picked = await showColorPicker(context, initial: color, title: label);
                if (picked != null) onChanged(picked);
              },
            ),
          ],
        ),
      ],
    );
  }
}

class _Swatch extends StatelessWidget {
  const _Swatch({super.key, required this.semanticsLabel, required this.selected, required this.onTap, this.color, this.icon});

  final String semanticsLabel;
  final Color? color;
  final bool selected;
  final IconData? icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final fill = color;
    final dark = (fill ?? scheme.surfaceContainerHigh).computeLuminance() < 0.4;
    return Semantics(
      button: true,
      selected: selected,
      label: semanticsLabel,
      child: InkResponse(
        onTap: onTap,
        radius: 26,
        child: SizedBox(
          width: 48,
          height: 48,
          child: Center(
            child: Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(
                color: fill ?? scheme.surfaceContainerHigh,
                shape: BoxShape.circle,
                border: Border.all(
                  color: selected ? scheme.primary : scheme.outline,
                  width: selected ? 3 : 1,
                ),
              ),
              child: ExcludeSemantics(
                child: selected && icon == null
                    ? Icon(Icons.check_rounded, size: 18, color: dark ? Colors.white : Colors.black)
                    : icon != null
                        ? Icon(icon, size: 16, color: fill == null ? scheme.onSurfaceVariant : (dark ? Colors.white : Colors.black))
                        : null,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
