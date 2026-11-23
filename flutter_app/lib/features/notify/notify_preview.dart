import 'package:flutter/material.dart';

import '../../core/widgets.dart';
import 'notify_provider.dart';

/// Native size of the panel, used to scale badge geometry.
const _panelW = 256.0;
const _panelH = 64.0;

/// What the composer's overlay will roughly look like on the 256x64 panel. A calm clock-ish backdrop stands in for
/// whatever app is running underneath. Sizes follow the real overlay rules (toast strip, square badge, bordered alert).
class NotifyPreview extends StatelessWidget {
  const NotifyPreview({
    super.key,
    required this.mode,
    required this.text,
    required this.color,
    required this.background,
    this.badgeSize = 4,
    this.badgeX,
    this.badgeY,
    this.flashOnly = false,
  });

  final NotifyMode mode;
  final String text;
  final Color color;
  final Color background;
  final int badgeSize;
  final int? badgeX;
  final int? badgeY;
  final bool flashOnly;

  @override
  Widget build(BuildContext context) {
    final label = switch (mode) {
      NotifyMode.toast => 'Preview of a toast',
      NotifyMode.badge => 'Preview of a badge',
      NotifyMode.alert => flashOnly ? 'Preview of a flash alert' : 'Preview of an alert',
    };
    return Semantics(
      label: '$label${text.isEmpty ? '' : ': $text'}',
      image: true,
      child: ExcludeSemantics(
        child: GlowPreview(
          child: AspectRatio(
            aspectRatio: _panelW / _panelH,
            child: LayoutBuilder(builder: (context, c) {
              final scale = c.maxWidth / _panelW;
              return Stack(
                key: const Key('notify-preview'),
                children: [
                  _backdrop(c.maxHeight),
                  switch (mode) {
                    NotifyMode.toast => _toast(scale),
                    NotifyMode.badge => _badge(scale),
                    NotifyMode.alert => _alert(scale, c.maxHeight),
                  },
                ],
              );
            }),
          ),
        ),
      ),
    );
  }

  Widget _backdrop(double h) => Positioned.fill(
        child: Center(
          child: Text(
            '12:34',
            style: TextStyle(
              fontSize: h * 0.55,
              fontWeight: FontWeight.w800,
              letterSpacing: 2,
              color: Colors.white.withValues(alpha: 0.10),
              fontFeatures: const [FontFeature.tabularFigures()],
            ),
          ),
        ),
      );

  Widget _toast(double scale) {
    final height = (9 * scale).clamp(14.0, 40.0);
    return Positioned(
      left: 0,
      right: 0,
      top: 0,
      height: height,
      child: ColoredBox(
        key: const Key('preview-toast'),
        color: background,
        child: Center(
          child: Text(
            text.isEmpty ? 'Your message' : text,
            maxLines: 1,
            overflow: TextOverflow.clip,
            softWrap: false,
            style: TextStyle(
              fontSize: height * 0.62,
              fontWeight: FontWeight.w700,
              height: 1,
              color: text.isEmpty ? color.withValues(alpha: 0.6) : color,
            ),
          ),
        ),
      ),
    );
  }

  Widget _badge(double scale) {
    final size = (badgeSize.clamp(1, 32) * scale).clamp(4.0, 200.0);
    final x = badgeX == null ? (_panelW - badgeSize - 1) * scale : badgeX! * scale;
    final y = (badgeY ?? 1) * scale;
    return Positioned(
      left: x,
      top: y,
      width: size,
      height: size,
      child: DecoratedBox(
        key: const Key('preview-badge'),
        decoration: BoxDecoration(
          color: color,
          boxShadow: [BoxShadow(color: color.withValues(alpha: 0.6), blurRadius: size)],
        ),
      ),
    );
  }

  Widget _alert(double scale, double h) {
    const base = Color(0xFF080410);
    return Positioned.fill(
      child: DecoratedBox(
        key: const Key('preview-alert'),
        decoration: BoxDecoration(
          color: flashOnly ? color.withValues(alpha: 0.85) : base,
          border: Border.all(color: color, width: (2 * scale).clamp(2.0, 8.0)),
        ),
        child: flashOnly
            ? null
            : Center(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  child: Text(
                    text.isEmpty ? 'Your message' : text,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: h * 0.36,
                      fontWeight: FontWeight.w800,
                      height: 1,
                      color: text.isEmpty ? color.withValues(alpha: 0.6) : color,
                      shadows: [Shadow(color: color.withValues(alpha: 0.7), blurRadius: 8)],
                    ),
                  ),
                ),
              ),
      ),
    );
  }
}
