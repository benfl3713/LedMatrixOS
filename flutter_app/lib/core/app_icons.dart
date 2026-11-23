import 'package:flutter/material.dart';

/// Explicit icon per built-in app id. Anything not listed falls back to [iconForApp]'s keyword guess.
const Map<String, IconData> appIconById = {
  'home': Icons.space_dashboard_rounded,
  'clock': Icons.schedule_rounded,
  'sky-clock': Icons.wb_twilight_rounded,
  'countdown-timer': Icons.hourglass_bottom_rounded,
  'pomodoro': Icons.timer_rounded,
  'calendar': Icons.calendar_month_rounded,
  'bin-day': Icons.delete_rounded,
  'morning-briefing': Icons.coffee_rounded,
  'tube-status': Icons.directions_subway_rounded,
  'tube-line': Icons.linear_scale_rounded,
  'tube-departures': Icons.departure_board_rounded,
  'bus-arrivals': Icons.directions_bus_rounded,
  'rail-departures': Icons.train_rounded,
  'cycle-hub': Icons.pedal_bike_rounded,
  'road-disruptions': Icons.traffic_rounded,
  'journey': Icons.route_rounded,
  'commute': Icons.commute_rounded,
  'train-sim': Icons.tram_rounded,
  'plane-spotter': Icons.flight_rounded,
  'weather': Icons.wb_sunny_rounded,
  'air-quality': Icons.air_rounded,
  'tides': Icons.waves_rounded,
  'iss-tracker': Icons.satellite_alt_rounded,
  'stocks': Icons.show_chart_rounded,
  'rss-ticker': Icons.rss_feed_rounded,
  'ha-tiles': Icons.home_rounded,
  'spotify': Icons.library_music_rounded,
  'scrolling-text': Icons.text_fields_rounded,
  'qr': Icons.qr_code_2_rounded,
  'solid_color': Icons.palette_rounded,
  'rainbow-spiral': Icons.cyclone_rounded,
  'bouncing-balls': Icons.sports_basketball_rounded,
  'matrix-rain': Icons.terminal_rounded,
  'geometric-patterns': Icons.interests_rounded,
  'dvd-logo': Icons.album_rounded,
  'fire': Icons.local_fire_department_rounded,
  'life': Icons.grain_rounded,
  'alive': Icons.bubble_chart_rounded,
  'aquarium': Icons.set_meal_rounded,
  'demoscene': Icons.auto_awesome_rounded,
  'equalizer': Icons.equalizer_rounded,
  'party': Icons.celebration_rounded,
  'pet': Icons.pets_rounded,
  'snake': Icons.sports_esports_rounded,
  'breakout': Icons.border_all_rounded,
  'tetris': Icons.grid_view_rounded,
  'pong': Icons.sports_tennis_rounded,
  'media': Icons.perm_media_rounded,
  'input-test': Icons.gamepad_rounded,
  'widget-demo': Icons.widgets_rounded,
  'screen': Icons.dashboard_customize_rounded,
};

/// Picks an icon for an app: the explicit [appIconById] entry, then a keyword guess on the id so new server apps get
/// a sensible icon without a code change. Unknown ids get a generic apps icon.
IconData iconForApp(String appId) {
  final id = appId.toLowerCase();
  final exact = appIconById[id];
  if (exact != null) return exact;
  if (id.startsWith('screen:')) return Icons.dashboard_customize_rounded;
  const rules = <(List<String>, IconData)>[
    (['sky-clock'], Icons.wb_twilight_rounded),
    (['tides'], Icons.waves_rounded),
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
