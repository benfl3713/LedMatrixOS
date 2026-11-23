import 'package:flutter/material.dart';

/// Spacing, radii and brand colours shared across the app ("LED / night display" identity).
abstract final class Space {
  static const double xs = 4;
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
  static const double xl = 24;
}

abstract final class Radii {
  static const double sm = 10;
  static const double md = 16;
  static const double lg = 22;
  static const double bezel = 14;
}

abstract final class Brand {
  /// Electric cyan, the LED accent.
  static const cyan = Color(0xFF22E5FF);

  /// Warm amber, used for secondary highlights (tertiary).
  static const amber = Color(0xFFFFB84D);

  /// "Live" indicator.
  static const live = Color(0xFFFF4D5E);

  /// Near-black panel colour behind the LEDs.
  static const panel = Color(0xFF020304);
}

/// Tabular-figure style for numbers (percentages, resolution, fps) so digits do not jitter.
TextStyle numericStyle(TextStyle? base) => (base ?? const TextStyle()).copyWith(
      fontFeatures: const [FontFeature.tabularFigures()],
      letterSpacing: 0.2,
    );
