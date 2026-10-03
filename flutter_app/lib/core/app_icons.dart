import 'package:flutter/material.dart';

/// Picks an icon for an app from keywords in its id, so new server apps get a sensible icon
/// without a code change. Unknown ids get a generic apps icon.
IconData iconForApp(String appId) {
  final id = appId.toLowerCase();
  const rules = <(List<String>, IconData)>[
    (['flip'], Icons.flip_rounded),
    (['countdown', 'timer'], Icons.timer_rounded),
    (['clock', 'time'], Icons.schedule_rounded),
    (['commute'], Icons.directions_walk_rounded),
    (['ha-', 'home-assistant', 'tiles'], Icons.home_rounded),
    (['solid', 'color'], Icons.circle),
    (['rainbow', 'spiral'], Icons.cyclone_rounded),
    (['fire'], Icons.local_fire_department_rounded),
    (['matrix', 'rain'], Icons.terminal_rounded),
    (['pattern', 'geometric'], Icons.interests_rounded),
    (['dvd'], Icons.tv_rounded),
    (['ball'], Icons.sports_basketball_rounded),
    (['text', 'scroll'], Icons.text_fields_rounded),
    (['subway', 'status'], Icons.directions_subway_rounded),
    (['tube', 'train', 'rail'], Icons.train_rounded),
    (['bus'], Icons.directions_bus_rounded),
    (['equalizer', 'audio', 'visual', 'spectrum'], Icons.equalizer_rounded),
    (['spotify', 'music'], Icons.library_music_rounded),
    (['weather'], Icons.wb_sunny_rounded),
    (['calendar'], Icons.calendar_month_rounded),
    (['home'], Icons.grid_on_rounded),
  ];
  for (final (keywords, icon) in rules) {
    if (keywords.any(id.contains)) return icon;
  }
  return Icons.apps_rounded;
}
