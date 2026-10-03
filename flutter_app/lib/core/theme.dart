import 'package:flutter/material.dart';

const seedColor = Color(0xFF00E5FF);

/// Material 3 themes. Pass the platform's dynamic schemes when available; the cyan seed is the fallback.
ThemeData buildTheme(Brightness brightness, {ColorScheme? dynamicScheme}) {
  final scheme = dynamicScheme ?? ColorScheme.fromSeed(seedColor: seedColor, brightness: brightness);
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    cardTheme: CardThemeData(
      elevation: 0,
      color: scheme.surfaceContainerLow,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      margin: EdgeInsets.zero,
    ),
    appBarTheme: const AppBarTheme(centerTitle: false),
    snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
  );
}
