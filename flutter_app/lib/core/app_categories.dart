import 'package:flutter/material.dart';

import '../api/models.dart';

/// Display style of one app category: label, icon and the tint behind its icon tiles.
class AppCategoryStyle {
  const AppCategoryStyle(this.key, this.label, this.icon, this.color);

  final String key;
  final String label;
  final IconData icon;
  final Color color;
}

/// Categories in display order. 'other' catches apps whose server does not report one.
const List<AppCategoryStyle> appCategories = [
  AppCategoryStyle('clocks', 'Clocks', Icons.schedule_rounded, Color(0xFF22E5FF)),
  AppCategoryStyle('travel', 'Travel', Icons.directions_transit_rounded, Color(0xFFFFB84D)),
  AppCategoryStyle('info', 'Info', Icons.info_rounded, Color(0xFF7AA2FF)),
  AppCategoryStyle('ambient', 'Ambient', Icons.auto_awesome_rounded, Color(0xFFB388FF)),
  AppCategoryStyle('games', 'Games', Icons.sports_esports_rounded, Color(0xFF5BE49B)),
  AppCategoryStyle('media', 'Media', Icons.perm_media_rounded, Color(0xFFFF7EB6)),
  AppCategoryStyle('screens', 'My screens', Icons.dashboard_customize_rounded, Color(0xFFFFD166)),
  AppCategoryStyle('developer', 'Developer', Icons.code_rounded, Color(0xFF90A4AE)),
  AppCategoryStyle('other', 'Other', Icons.apps_rounded, Color(0xFF90A4AE)),
];

AppCategoryStyle categoryStyle(String key) =>
    appCategories.firstWhere((c) => c.key == key, orElse: () => appCategories.last);

/// The style for an app's category; unknown category strings are treated as 'other'.
AppCategoryStyle categoryOf(MatrixApp app) => categoryStyle(app.category);

/// Icon colour that stays readable on the tinted tile in the given brightness.
Color tintForeground(Color tint, Brightness brightness) {
  if (brightness == Brightness.dark) return tint;
  final hsl = HSLColor.fromColor(tint);
  return hsl.withLightness((hsl.lightness * 0.55).clamp(0.0, 1.0)).toColor();
}
