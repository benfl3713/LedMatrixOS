import 'package:flutter/material.dart';
import '../api_service.dart';
import 'glass_container.dart';

class MatrixControlPanel extends StatelessWidget {
  final MatrixSettings? settings;
  final String? activeAppId;
  final List<MatrixApp> apps;
  final VoidCallback onPowerToggle;
  final VoidCallback onSettings;
  final VoidCallback onRefresh;
  final Function(double) onBrightnessChanged;
  final IconData Function(String) getAppIcon;
  final VoidCallback? onConfigureActive;

  const MatrixControlPanel({
    super.key,
    required this.settings,
    required this.activeAppId,
    required this.apps,
    required this.onPowerToggle,
    required this.onSettings,
    required this.onRefresh,
    required this.onBrightnessChanged,
    required this.getAppIcon,
    this.onConfigureActive,
  });

  MatrixApp? get _activeApp {
    if (activeAppId == null) return null;
    final matches = apps.where((a) => a.id == activeAppId);
    return matches.isEmpty ? null : matches.first;
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isOnline = settings != null;
    final isEnabled = settings?.isEnabled ?? false;

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
      child: GlassContainer(
        borderRadius: 32,
        padding: const EdgeInsets.fromLTRB(20, 16, 12, 0),
        opacity: 0.25,
        blur: 20,
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.1),
            blurRadius: 30,
            offset: const Offset(0, 10),
          ),
        ],
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // ── Title row ──────────────────────────────────────────
            Row(
              children: [
                Container(
                  width: 8,
                  height: 8,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: isOnline ? colorScheme.primary : colorScheme.error,
                    boxShadow: isOnline
                        ? [
                            BoxShadow(
                              color: colorScheme.primary.withValues(alpha: 0.5),
                              blurRadius: 10,
                              spreadRadius: 2,
                            ),
                          ]
                        : null,
                  ),
                ),
                const SizedBox(width: 8),
                Text(
                  isOnline ? 'ONLINE' : 'OFFLINE',
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w800,
                    color: isOnline ? colorScheme.onSurface : colorScheme.error,
                    letterSpacing: 1.0,
                  ),
                ),
                if (settings != null) ...[
                  _dot(colorScheme),
                  Text(
                    '${settings!.width}×${settings!.height}',
                    style: TextStyle(
                      fontSize: 12,
                      color: colorScheme.onSurfaceVariant,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
                const Spacer(),
                IconButton.filledTonal(
                  icon: Icon(Icons.settings_outlined,
                      size: 20, color: colorScheme.onSurfaceVariant),
                  onPressed: onSettings,
                ),
                const SizedBox(width: 8),
                IconButton.filledTonal(
                  icon: Icon(Icons.refresh_rounded,
                      size: 20, color: colorScheme.onSurfaceVariant),
                  onPressed: onRefresh,
                ),
              ],
            ),

            const SizedBox(height: 16),

            if (settings != null)
              GestureDetector(
                onTap: onPowerToggle,
                child: AnimatedContainer(
                  duration: const Duration(milliseconds: 300),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  decoration: BoxDecoration(
                    color: isEnabled
                        ? colorScheme.primary
                        : colorScheme.surfaceContainerHighest
                            .withValues(alpha: 0.5),
                    borderRadius: BorderRadius.circular(24),
                    boxShadow: isEnabled
                        ? [
                            BoxShadow(
                              color: colorScheme.primary.withValues(alpha: 0.4),
                              blurRadius: 15,
                              offset: const Offset(0, 4),
                            ),
                          ]
                        : [],
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.max,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(
                        Icons.power_settings_new_rounded,
                        size: 20,
                        color: isEnabled
                            ? colorScheme.onPrimary
                            : colorScheme.onSurfaceVariant,
                      ),
                      const SizedBox(width: 6),
                      Text(
                        isEnabled ? 'ON' : 'OFF',
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w900,
                          color: isEnabled
                              ? colorScheme.onPrimary
                              : colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
              ),

            // ── Brightness row ─────────────────────────────────────
            if (settings != null) ...[
              const SizedBox(height: 12),
              Row(
                children: [
                  Icon(
                    Icons.brightness_medium_rounded,
                    size: 18,
                    color: colorScheme.onSurfaceVariant,
                  ),
                  Expanded(
                    child: SliderTheme(
                      data: SliderTheme.of(context).copyWith(
                        trackHeight: 6,
                        thumbShape: const RoundSliderThumbShape(
                            enabledThumbRadius: 10),
                        overlayShape: const RoundSliderOverlayShape(
                            overlayRadius: 20),
                        activeTrackColor: colorScheme.primary,
                        inactiveTrackColor:
                            colorScheme.onSurface.withValues(alpha: 0.1),
                        thumbColor: Colors.white,
                      ),
                      child: Slider(
                        value: settings!.brightness.toDouble(),
                        min: 0,
                        max: 100,
                        divisions: 100,
                        onChanged: onBrightnessChanged,
                      ),
                    ),
                  ),
                  Text(
                    '${settings!.brightness}%',
                    style: TextStyle(
                      fontSize: 12,
                      color: colorScheme.onSurface,
                      fontWeight: FontWeight.w700,
                      fontFeatures: const [FontFeature.tabularFigures()],
                    ),
                  ),
                ],
              ),
            ],

            // ── Now playing row ────────────────────────────────────
            if (_activeApp != null) ...[
              const SizedBox(height: 16),
              GestureDetector(
                onTap: onConfigureActive,
                child: GlassContainer(
                  borderRadius: 20,
                  opacity: 0.1,
                  blur: 5,
                  padding: const EdgeInsets.symmetric(
                      horizontal: 16, vertical: 12),
                  border: Border.all(
                    color: colorScheme.primary.withValues(alpha: 0.2),
                    width: 1,
                  ),
                  child: Row(
                    children: [
                      Container(
                        padding: const EdgeInsets.all(8),
                        decoration: BoxDecoration(
                          color: colorScheme.primary.withValues(alpha: 0.2),
                          shape: BoxShape.circle,
                        ),
                        child: Icon(
                          getAppIcon(_activeApp!.id),
                          size: 20,
                          color: colorScheme.primary,
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'NOW ACTIVE',
                              style: TextStyle(
                                fontSize: 10,
                                fontWeight: FontWeight.w900,
                                color: colorScheme.primary,
                                letterSpacing: 1.2,
                              ),
                            ),
                            Text(
                              _activeApp!.name,
                              style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w700,
                                color: colorScheme.onSurface,
                              ),
                            ),
                          ],
                        ),
                      ),
                      if (_activeApp!.hasSettings &&
                          onConfigureActive != null) ...[
                        Icon(
                          Icons.chevron_right_rounded,
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ],
                    ],
                  ),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _dot(ColorScheme colorScheme) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 7),
      child: Container(
        width: 3,
        height: 3,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: colorScheme.onSurfaceVariant.withValues(alpha: 0.35),
        ),
      ),
    );
  }
}

/// Paints a subtle dot grid to evoke a physical LED matrix panel.
class _DotGridPainter extends CustomPainter {
  final Color dotColor;

  _DotGridPainter({required this.dotColor});

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = dotColor
      ..style = PaintingStyle.fill;

    const spacing = 16.0;
    const radius = 1.5;

    for (double x = spacing; x < size.width; x += spacing) {
      for (double y = spacing; y < size.height; y += spacing) {
        canvas.drawCircle(Offset(x, y), radius, paint);
      }
    }
  }

  @override
  bool shouldRepaint(_DotGridPainter old) => old.dotColor != dotColor;
}
