import 'package:flutter/material.dart';

class AppIconHelper {
  static IconData getAppIcon(String appId) {
    switch (appId) {
      case 'clock':
      case 'animated-clock':
        return Icons.schedule;
      case 'solid_color':
        return Icons.palette;
      case 'rainbow-spiral':
        return Icons.gradient;
      case 'bouncing-balls':
        return Icons.sports_basketball;
      case 'matrix-rain':
        return Icons.code;
      case 'geometric-patterns':
        return Icons.category;
      case 'dvd-logo':
        return Icons.movie;
      case 'weather':
        return Icons.wb_sunny;
      case 'spotify':
        return Icons.music_note;
      default:
        return Icons.apps;
    }
  }
}
