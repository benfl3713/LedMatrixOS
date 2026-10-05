import 'package:flutter/material.dart';

/// Colours the server accepts by name in Select settings (case-insensitive), approximated for display.
const namedColors = <String, Color>{
  'white': Color(0xFFFFFFFF),
  'black': Color(0xFF000000),
  'red': Color(0xFFFF0000),
  'green': Color(0xFF00C800),
  'blue': Color(0xFF0050FF),
  'yellow': Color(0xFFFFE600),
  'cyan': Color(0xFF00E5FF),
  'magenta': Color(0xFFFF00FF),
  'orange': Color(0xFFFF8C00),
  'amber': Color(0xFFFFBF00),
  'gold': Color(0xFFFFD700),
  'purple': Color(0xFF9B30FF),
  'pink': Color(0xFFFF69B4),
  'navy': Color(0xFF000080),
  'darkblue': Color(0xFF162250),
  'darkgray': Color(0xFF404040),
  'gray': Color(0xFF808080),
};

/// True when [options] is mostly colour names (a few extras such as "Palette" or "Rainbow" are fine).
bool looksLikeColourSelect(List<String> options) {
  if (options.length < 3) return false;
  final known = options.where((o) => namedColors.containsKey(o.toLowerCase())).length;
  return known * 2 >= options.length;
}
