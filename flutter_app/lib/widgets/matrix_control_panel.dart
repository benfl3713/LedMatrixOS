import 'package:flutter/material.dart';
import '../api_service.dart';

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

    return Container(
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerHighest,
        border: Border(
          bottom: BorderSide(
            color: colorScheme.primary.withValues(alpha: 0.25),
            width: 1,
          ),
        ),
        boxShadow: [
          BoxShadow(
            color: colorScheme.primary.withValues(alpha: 0.08),
            blurRadius: 24,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: SafeArea(
        bottom: false,
        child: CustomPaint(
          painter: _DotGridPainter(
            dotColor: colorScheme.onSurface.withValues(alpha: 0.05),
          ),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 10, 8, 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // ── Title row ──────────────────────────────────────────
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.all(7),
                      decoration: BoxDecoration(
                        color: colorScheme.primary.withValues(alpha: 0.12),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(
                          color: colorScheme.primary.withValues(alpha: 0.35),
                          width: 1,
                        ),
                      ),
                      child: Icon(
                        Icons.grid_on_rounded,
                        color: colorScheme.primary,
                        size: 15,
                      ),
                    ),
                    const SizedBox(width: 10),
                    Text(
                      'LED MATRIX',
                      style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w900,
                        color: colorScheme.onSurface,
                        letterSpacing: 2,
                      ),
                    ),
                    const Spacer(),
                    IconButton(
                      icon: Icon(Icons.settings_outlined,
                          size: 20, color: colorScheme.onSurfaceVariant),
                      onPressed: onSettings,
                      visualDensity: VisualDensity.compact,
                    ),
                    IconButton(
                      icon: Icon(Icons.refresh_rounded,
                          size: 20, color: colorScheme.onSurfaceVariant),
                      onPressed: onRefresh,
                      visualDensity: VisualDensity.compact,
                    ),
                  ],
                ),

                const SizedBox(height: 10),

                // ── Status row ─────────────────────────────────────────
                Row(
                  children: [
                    // Online indicator
                    Container(
                      width: 7,
                      height: 7,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: isOnline
                            ? colorScheme.primary
                            : colorScheme.error,
                        boxShadow: isOnline
                            ? [
                                BoxShadow(
                                  color: colorScheme.primary.withValues(alpha: 0.7),
                                  blurRadius: 8,
                                  spreadRadius: 1,
                                ),
                              ]
                            : null,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      isOnline ? 'ONLINE' : 'OFFLINE',
                      style: TextStyle(
                        fontSize: 10,
                        fontWeight: FontWeight.w800,
                        color: isOnline
                            ? colorScheme.primary
                            : colorScheme.error,
                        letterSpacing: 1.5,
                      ),
                    ),
                    if (settings != null) ...[
                      _dot(colorScheme),
                      Text(
                        '${settings!.width}×${settings!.height}',
                        style: TextStyle(
                          fontSize: 11,
                          color: colorScheme.onSurfaceVariant,
                          fontWeight: FontWeight.w500,
                          fontFeatures: const [FontFeature.tabularFigures()],
                        ),
                      ),
                    ],
                    const Spacer(),
                    // Power toggle pill
                    if (settings != null)
                      GestureDetector(
                        onTap: onPowerToggle,
                        child: AnimatedContainer(
                          duration: const Duration(milliseconds: 250),
                          padding: const EdgeInsets.symmetric(
                              horizontal: 12, vertical: 7),
                          decoration: BoxDecoration(
                            color: isEnabled
                                ? colorScheme.primaryContainer
                                : colorScheme.surfaceContainer,
                            borderRadius: BorderRadius.circular(20),
                            border: Border.all(
                              color: isEnabled
                                  ? colorScheme.primary.withValues(alpha: 0.5)
                                  : colorScheme.outline.withValues(alpha: 0.25),
                              width: 1,
                            ),
                            boxShadow: isEnabled
                                ? [
                                    BoxShadow(
                                      color:
                                          colorScheme.primary.withValues(alpha: 0.25),
                                      blurRadius: 10,
                                      spreadRadius: 1,
                                    ),
                                  ]
                                : [],
                          ),
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(
                                Icons.power_settings_new_rounded,
                                size: 13,
                                color: isEnabled
                                    ? colorScheme.onPrimaryContainer
                                    : colorScheme.onSurfaceVariant,
                              ),
                              const SizedBox(width: 5),
                              Text(
                                isEnabled ? 'ON' : 'OFF',
                                style: TextStyle(
                                  fontSize: 11,
                                  fontWeight: FontWeight.w800,
                                  letterSpacing: 0.8,
                                  color: isEnabled
                                      ? colorScheme.onPrimaryContainer
                                      : colorScheme.onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                  ],
                ),

                // ── Brightness row ─────────────────────────────────────
                if (settings != null) ...[
                  const SizedBox(height: 6),
                  Row(
                    children: [
                      Icon(
                        Icons.brightness_3_rounded,
                        size: 13,
                        color: colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
                      ),
                      Expanded(
                        child: SliderTheme(
                          data: SliderTheme.of(context).copyWith(
                            trackHeight: 3,
                            thumbShape: const RoundSliderThumbShape(
                                enabledThumbRadius: 6),
                            overlayShape: const RoundSliderOverlayShape(
                                overlayRadius: 14),
                            activeTrackColor: colorScheme.primary,
                            inactiveTrackColor:
                                colorScheme.surfaceContainerHigh,
                            thumbColor: colorScheme.primary,
                            overlayColor:
                                colorScheme.primary.withValues(alpha: 0.15),
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
                      Icon(
                        Icons.brightness_7_rounded,
                        size: 13,
                        color: colorScheme.primary,
                      ),
                      const SizedBox(width: 6),
                      SizedBox(
                        width: 32,
                        child: Text(
                          '${settings!.brightness}%',
                          style: TextStyle(
                            fontSize: 11,
                            color: colorScheme.onSurfaceVariant,
                            fontWeight: FontWeight.w600,
                            fontFeatures: const [FontFeature.tabularFigures()],
                          ),
                          textAlign: TextAlign.right,
                        ),
                      ),
                    ],
                  ),
                ],

                // ── Now playing row ────────────────────────────────────
                if (_activeApp != null) ...[
                  const SizedBox(height: 10),
                  Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 12, vertical: 9),
                    decoration: BoxDecoration(
                      color: colorScheme.primary.withValues(alpha: 0.08),
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(
                        color: colorScheme.primary.withValues(alpha: 0.22),
                        width: 1,
                      ),
                    ),
                    child: Row(
                      children: [
                        Icon(
                          Icons.play_arrow_rounded,
                          size: 11,
                          color: colorScheme.primary,
                        ),
                        const SizedBox(width: 4),
                        Text(
                          'NOW ON MATRIX',
                          style: TextStyle(
                            fontSize: 9,
                            fontWeight: FontWeight.w800,
                            color: colorScheme.primary,
                            letterSpacing: 1.5,
                          ),
                        ),
                        const Spacer(),
                        Icon(
                          getAppIcon(_activeApp!.id),
                          size: 15,
                          color: colorScheme.primary,
                        ),
                        const SizedBox(width: 7),
                        Text(
                          _activeApp!.name,
                          style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: colorScheme.onSurface,
                          ),
                        ),
                        if (_activeApp!.hasSettings &&
                            onConfigureActive != null) ...[
                          const SizedBox(width: 10),
                          GestureDetector(
                            onTap: onConfigureActive,
                            child: Container(
                              padding: const EdgeInsets.symmetric(
                                  horizontal: 9, vertical: 5),
                              decoration: BoxDecoration(
                                color: colorScheme.primary.withValues(alpha: 0.15),
                                borderRadius: BorderRadius.circular(7),
                                border: Border.all(
                                  color: colorScheme.primary.withValues(alpha: 0.3),
                                  width: 1,
                                ),
                              ),
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Icon(Icons.tune_rounded,
                                      size: 11,
                                      color: colorScheme.primary),
                                  const SizedBox(width: 4),
                                  Text(
                                    'CONFIG',
                                    style: TextStyle(
                                      fontSize: 9,
                                      fontWeight: FontWeight.w800,
                                      color: colorScheme.primary,
                                      letterSpacing: 0.5,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ],
              ],
            ),
          ),
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
