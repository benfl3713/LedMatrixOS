import 'package:flutter/material.dart';
import '../api_service.dart';

/// Returns a subtle hue colour for an app's icon background based on its category.
Color _appAccentColor(String appId) {
  if (appId.startsWith('tube')) return const Color(0xFF1565C0); // tube blue
  if (appId == 'fire') return const Color(0xFFBF360C); // deep orange
  if (appId == 'weather') return const Color(0xFF0277BD); // sky blue
  if (appId == 'spotify') return const Color(0xFF1B5E20); // spotify green
  if (appId == 'equalizer') return const Color(0xFF4A148C); // deep purple
  if (appId == 'matrix-rain') return const Color(0xFF1B5E20); // matrix green
  if (appId == 'rainbow-spiral') return const Color(0xFF880E4F); // pink
  if (appId.contains('clock') ||
      appId.contains('timer') ||
      appId.contains('flip')) {
    return const Color(0xFF1A237E); // indigo
  }
  if (appId == 'bouncing-balls') return const Color(0xFFE65100); // orange
  if (appId == 'dvd-logo') return const Color(0xFF37474F); // blue-grey
  if (appId == 'solid_color') return const Color(0xFF4A148C); // purple
  if (appId == 'scrolling-text') return const Color(0xFF006064); // teal
  if (appId == 'geometric-patterns') return const Color(0xFF1A237E);
  return const Color(0xFF263238); // default dark teal
}

class AppCard extends StatelessWidget {
  final MatrixApp app;
  final bool isActive;
  final VoidCallback onTap;
  final IconData Function(String appId) getAppIcon;

  const AppCard({
    super.key,
    required this.app,
    required this.isActive,
    required this.onTap,
    required this.getAppIcon,
  });

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final accent = _appAccentColor(app.id);

    return AnimatedContainer(
      duration: const Duration(milliseconds: 220),
      curve: Curves.easeInOut,
      decoration: BoxDecoration(
        color: isActive
            ? Color.alphaBlend(accent.withOpacity(0.55), colorScheme.surface)
            : Color.alphaBlend(
                accent.withOpacity(0.22), colorScheme.surfaceContainer),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: isActive ? accent.withOpacity(0.85) : accent.withOpacity(0.25),
          width: isActive ? 1.5 : 1,
        ),
        boxShadow: isActive
            ? [
                BoxShadow(
                  color: accent.withOpacity(0.45),
                  blurRadius: 16,
                  spreadRadius: 0,
                ),
              ]
            : [],
      ),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(14),
          splashColor: accent.withOpacity(0.25),
          highlightColor: accent.withOpacity(0.1),
          child: Padding(
            padding: const EdgeInsets.all(11),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Icon in a tinted rounded box
                Container(
                  padding: const EdgeInsets.all(7),
                  decoration: BoxDecoration(
                    color: isActive
                        ? accent.withOpacity(0.45)
                        : accent.withOpacity(0.25),
                    borderRadius: BorderRadius.circular(9),
                    border: Border.all(
                      color: accent.withOpacity(isActive ? 0.7 : 0.35),
                      width: 1,
                    ),
                  ),
                  child: Icon(
                    getAppIcon(app.id),
                    size: 22,
                    color: isActive
                        ? Colors.white
                        : Colors.white.withOpacity(0.75),
                  ),
                ),
                const Spacer(),
                // App name bottom-left
                Text(
                  app.name,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: isActive ? FontWeight.w700 : FontWeight.w500,
                    color: isActive
                        ? Colors.white
                        : Colors.white.withOpacity(0.65),
                    letterSpacing: 0.1,
                    height: 1.2,
                  ),
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                ),
                if (app.hasSettings) ...[
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      Icon(
                        Icons.tune_rounded,
                        size: 9,
                        color: isActive
                            ? Colors.white.withOpacity(0.55)
                            : Colors.white.withOpacity(0.3),
                      ),
                      const SizedBox(width: 3),
                      Text(
                        'configurable',
                        style: TextStyle(
                          fontSize: 9,
                          color: isActive
                              ? Colors.white.withOpacity(0.55)
                              : Colors.white.withOpacity(0.3),
                        ),
                      ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
