import 'package:flutter/material.dart';

class AppIconHelper {
  static IconData getAppIcon(String appId) {
    switch (appId) {
      // Clocks
      case 'clock':
        return Icons.schedule;
      case 'animated-clock':
        return Icons.access_time_filled_rounded;
      case 'flip-clock':
        return Icons.flip_rounded;
      case 'countdown-timer':
        return Icons.timer_rounded;

      // Visuals
      case 'solid_color':
        return Icons.circle;
      case 'rainbow-spiral':
        return Icons.cyclone_rounded;
      case 'fire':
        return Icons.local_fire_department_rounded;
      case 'matrix-rain':
        return Icons.terminal_rounded;
      case 'geometric-patterns':
        return Icons.interests_rounded;
      case 'dvd-logo':
        return Icons.tv_rounded;
      case 'bouncing-balls':
        return Icons.sports_basketball_rounded;
      case 'scrolling-text':
        return Icons.text_fields_rounded;

      // Transport
      case 'tube-departures':
        return Icons.train_rounded;
      case 'tube-line':
        return Icons.linear_scale_rounded;
      case 'tube-status':
        return Icons.directions_subway_rounded;

      // Media & data
      case 'equalizer':
        return Icons.equalizer_rounded;
      case 'spotify':
        return Icons.library_music_rounded;
      case 'weather':
        return Icons.wb_sunny_rounded;

      // System
      case 'home':
        return Icons.grid_on_rounded;

      default:
        return Icons.apps_rounded;
    }
  }
}
