import 'package:flutter/material.dart';

import 'tokens.dart';

const seedColor = Brand.cyan;

const _darkScheme = ColorScheme(
  brightness: Brightness.dark,
  primary: Color(0xFF22E5FF),
  onPrimary: Color(0xFF00232A),
  primaryContainer: Color(0xFF08343D),
  onPrimaryContainer: Color(0xFFA6F3FF),
  secondary: Color(0xFF8FA8B5),
  onSecondary: Color(0xFF0B1A21),
  secondaryContainer: Color(0xFF1B2A33),
  onSecondaryContainer: Color(0xFFD3E6F0),
  tertiary: Color(0xFFFFB84D),
  onTertiary: Color(0xFF3A2400),
  tertiaryContainer: Color(0xFF4A3000),
  onTertiaryContainer: Color(0xFFFFDDAE),
  error: Color(0xFFFF7A85),
  onError: Color(0xFF3D0008),
  errorContainer: Color(0xFF5A1018),
  onErrorContainer: Color(0xFFFFDADD),
  surface: Color(0xFF080A0E),
  onSurface: Color(0xFFE8EDF2),
  onSurfaceVariant: Color(0xFF9FAAB6),
  outline: Color(0xFF3B4552),
  outlineVariant: Color(0xFF212831),
  shadow: Color(0xFF000000),
  scrim: Color(0xFF000000),
  inverseSurface: Color(0xFFE8EDF2),
  onInverseSurface: Color(0xFF12161B),
  inversePrimary: Color(0xFF006573),
  surfaceTint: Colors.transparent,
  surfaceContainerLowest: Color(0xFF050709),
  surfaceContainerLow: Color(0xFF0D1015),
  surfaceContainer: Color(0xFF12161C),
  surfaceContainerHigh: Color(0xFF181D24),
  surfaceContainerHighest: Color(0xFF20262E),
);

const _lightScheme = ColorScheme(
  brightness: Brightness.light,
  primary: Color(0xFF00677A),
  onPrimary: Color(0xFFFFFFFF),
  primaryContainer: Color(0xFFBDF1FF),
  onPrimaryContainer: Color(0xFF001F26),
  secondary: Color(0xFF4A6370),
  onSecondary: Color(0xFFFFFFFF),
  secondaryContainer: Color(0xFFDCE8EF),
  onSecondaryContainer: Color(0xFF05202B),
  tertiary: Color(0xFF8A5100),
  onTertiary: Color(0xFFFFFFFF),
  tertiaryContainer: Color(0xFFFFDDB4),
  onTertiaryContainer: Color(0xFF2C1700),
  error: Color(0xFFB3261E),
  onError: Color(0xFFFFFFFF),
  errorContainer: Color(0xFFFFDAD6),
  onErrorContainer: Color(0xFF410002),
  surface: Color(0xFFF5F8FA),
  onSurface: Color(0xFF12171C),
  onSurfaceVariant: Color(0xFF47525C),
  outline: Color(0xFF79838D),
  outlineVariant: Color(0xFFD3DAE0),
  shadow: Color(0xFF000000),
  scrim: Color(0xFF000000),
  inverseSurface: Color(0xFF273037),
  onInverseSurface: Color(0xFFEAF1F5),
  inversePrimary: Color(0xFF22E5FF),
  surfaceTint: Colors.transparent,
  surfaceContainerLowest: Color(0xFFFFFFFF),
  surfaceContainerLow: Color(0xFFEEF2F5),
  surfaceContainer: Color(0xFFE8EDF1),
  surfaceContainerHigh: Color(0xFFE1E7EC),
  surfaceContainerHighest: Color(0xFFD9E0E6),
);

TextTheme _textTheme(TextTheme base) {
  TextStyle s(TextStyle? b, FontWeight w, double ls) => (b ?? const TextStyle()).copyWith(fontWeight: w, letterSpacing: ls);
  return base.copyWith(
    displaySmall: s(base.displaySmall, FontWeight.w700, -0.5),
    headlineMedium: s(base.headlineMedium, FontWeight.w700, -0.4),
    headlineSmall: s(base.headlineSmall, FontWeight.w700, -0.3),
    titleLarge: s(base.titleLarge, FontWeight.w700, -0.2),
    titleMedium: s(base.titleMedium, FontWeight.w600, 0),
    titleSmall: s(base.titleSmall, FontWeight.w600, 0.1),
    bodyLarge: s(base.bodyLarge, FontWeight.w400, 0.1),
    bodyMedium: s(base.bodyMedium, FontWeight.w400, 0.1),
    bodySmall: s(base.bodySmall, FontWeight.w400, 0.2),
    labelLarge: s(base.labelLarge, FontWeight.w600, 0.3),
    labelMedium: s(base.labelMedium, FontWeight.w600, 0.5),
    labelSmall: s(base.labelSmall, FontWeight.w700, 0.8),
  );
}

/// Material 3 themes with the LED night-display identity. Dark is the primary design; light is a clean companion.
ThemeData buildTheme(Brightness brightness) {
  final scheme = brightness == Brightness.dark ? _darkScheme : _lightScheme;
  final base = ThemeData(brightness: brightness, useMaterial3: true, colorScheme: scheme);
  final text = _textTheme(base.textTheme).apply(bodyColor: scheme.onSurface, displayColor: scheme.onSurface);
  final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(Radii.md));
  const minSize = Size(48, 48);
  OutlineInputBorder border(Color c, [double w = 1]) =>
      OutlineInputBorder(borderRadius: BorderRadius.circular(Radii.md), borderSide: BorderSide(color: c, width: w));

  return base.copyWith(
    scaffoldBackgroundColor: scheme.surface,
    textTheme: text,
    appBarTheme: AppBarTheme(
      centerTitle: false,
      backgroundColor: scheme.surface,
      surfaceTintColor: Colors.transparent,
      scrolledUnderElevation: 0,
      titleTextStyle: text.titleLarge,
    ),
    cardTheme: CardThemeData(
      elevation: 0,
      color: scheme.surfaceContainerLow,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(Radii.lg),
        side: BorderSide(color: scheme.outlineVariant),
      ),
      margin: EdgeInsets.zero,
    ),
    dividerTheme: DividerThemeData(color: scheme.outlineVariant, space: 1, thickness: 1),
    sliderTheme: SliderThemeData(
      trackHeight: 14,
      activeTrackColor: scheme.primary,
      inactiveTrackColor: scheme.surfaceContainerHighest,
      thumbColor: scheme.primary,
      overlayColor: scheme.primary.withValues(alpha: 0.14),
      thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 12, elevation: 0, pressedElevation: 0),
      overlayShape: const RoundSliderOverlayShape(overlayRadius: 26),
      valueIndicatorColor: scheme.primaryContainer,
      valueIndicatorTextStyle: numericStyle(text.labelLarge?.copyWith(color: scheme.onPrimaryContainer)),
      tickMarkShape: SliderTickMarkShape.noTickMark,
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith(
          (s) => s.contains(WidgetState.selected) ? scheme.onPrimary : scheme.outline),
      trackColor: WidgetStateProperty.resolveWith(
          (s) => s.contains(WidgetState.selected) ? scheme.primary : scheme.surfaceContainerHighest),
      trackOutlineColor: WidgetStateProperty.resolveWith(
          (s) => s.contains(WidgetState.selected) ? Colors.transparent : scheme.outline),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: scheme.surfaceContainerLowest,
      surfaceTintColor: Colors.transparent,
      indicatorColor: scheme.primaryContainer,
      height: 68,
      labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
      iconTheme: WidgetStateProperty.resolveWith((s) =>
          IconThemeData(color: s.contains(WidgetState.selected) ? scheme.primary : scheme.onSurfaceVariant)),
      labelTextStyle: WidgetStateProperty.resolveWith((s) => text.labelMedium?.copyWith(
            color: s.contains(WidgetState.selected) ? scheme.primary : scheme.onSurfaceVariant,
          )),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(minimumSize: minSize, shape: shape, textStyle: text.labelLarge),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: minSize,
        shape: shape,
        side: BorderSide(color: scheme.outline),
        textStyle: text.labelLarge,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(minimumSize: minSize, shape: shape, textStyle: text.labelLarge),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: scheme.surfaceContainer,
      border: border(scheme.outlineVariant),
      enabledBorder: border(scheme.outlineVariant),
      focusedBorder: border(scheme.primary, 2),
    ),
    listTileTheme: ListTileThemeData(shape: shape, iconColor: scheme.onSurfaceVariant, minVerticalPadding: 8),
    bottomSheetTheme: BottomSheetThemeData(
      backgroundColor: scheme.surfaceContainer,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      dragHandleColor: scheme.outline,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(28))),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: scheme.surfaceContainerHigh,
      selectedColor: scheme.primaryContainer,
      side: BorderSide(color: scheme.outlineVariant),
      labelStyle: text.labelLarge,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(Radii.sm + 4)),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: scheme.surfaceContainerHighest,
      contentTextStyle: text.bodyMedium,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(Radii.md),
        side: BorderSide(color: scheme.outline),
      ),
    ),
    segmentedButtonTheme: SegmentedButtonThemeData(
      style: SegmentedButton.styleFrom(
        minimumSize: minSize,
        side: BorderSide(color: scheme.outline),
        selectedBackgroundColor: scheme.primaryContainer,
        selectedForegroundColor: scheme.onPrimaryContainer,
      ),
    ),
  );
}
